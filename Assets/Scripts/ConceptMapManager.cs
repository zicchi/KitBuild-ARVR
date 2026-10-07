using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TMPro;
using Newtonsoft.Json;

/// <summary>
/// Master manager for the AR Concept Map.
/// Handles: JSON load/save, node spawning, AR placement, drag-to-connect.
/// </summary>
public class ConceptMapManager : MonoBehaviour
{
    // ─── AR Foundation ────────────────────────────
    [Header("AR")]
    public ARRaycastManager  arRaycast;
    public ARPlaneManager    arPlanes;
    public Camera            arCamera;

    // ─── Prefabs ──────────────────────────────────
    [Header("Prefabs")]
    public GameObject conceptNodePrefab;
    public GameObject linkNodePrefab;
    public GameObject connectionLinePrefab;
    public GameObject arrowHeadPrefab;
    public GameObject previewLinePrefab;   // simple LineRenderer for drag preview

    // ─── UI ───────────────────────────────────────
    [Header("UI Buttons")]
    public UnityEngine.UI.Button btnAddConcept;
    public UnityEngine.UI.Button btnAddLink;
    public UnityEngine.UI.Button btnReset;
    public UnityEngine.UI.Button btnSave;
    public TextMeshProUGUI       statusText;
    public TextMeshProUGUI       trackingText;

    // ─── Runtime state ────────────────────────────
    private ConceptMapFile mapFile;
    private Dictionary<string, ConceptNode> conceptObjects = new();
    private Dictionary<string, LinkNode>    linkObjects    = new();
    private List<ConnectionLine>            connections    = new();

    // Placement mode
    public enum Mode { None, PlaceConcept, PlaceLink }
    private Mode currentMode = Mode.None;
    private string pendingId;   // cid or lid being placed

    // Connection drag state
    private bool     isDragging;
    private MapNode  dragFromNode;
    private LineRenderer previewLR;
    private static readonly List<ARRaycastHit> arHits = new();

    // Counter for new node IDs
    private int cCounter = 0;
    private int lCounter = 0;

    // Pinch-to-scale
    private float  nodeScale     = 1.0f;
    private float  lastPinchDist = -1f;
    private const float MIN_SCALE = 0.3f;
    private const float MAX_SCALE = 3.0f;

    private const float XR_MAP_DIST     = 1.4f;
    private const float XR_MAP_WIDTH    = 1.4f;
    private const float XR_MAP_HEIGHT   = 0.8f;
    private const float XR_PLACE_DIST   = 1.2f;
    private const float XR_PICK_DEGREES = 6f;

    // ─── Undo / Redo ──────────────────────────────
    private class UndoRecord
    {
        public string description;
        public Action undo;
        public Action redo;
    }
    private readonly Stack<UndoRecord> undoStack = new();
    private readonly Stack<UndoRecord> redoStack = new();

    void PushUndo(string description, Action undo, Action redo)
    {
        undoStack.Push(new UndoRecord { description = description, undo = undo, redo = redo });
        redoStack.Clear(); // aksi baru menghapus jalur redo
    }

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;

    public void Undo()
    {
        if (undoStack.Count == 0) { ShowStatus("[!] Tidak ada yang bisa di-undo"); return; }
        var rec = undoStack.Pop();
        rec.undo();
        redoStack.Push(rec);
        ShowStatus($"[Undo] {rec.description}");
    }

    public void Redo()
    {
        if (redoStack.Count == 0) { ShowStatus("[!] Tidak ada yang bisa di-redo"); return; }
        var rec = redoStack.Pop();
        rec.redo();
        undoStack.Push(rec);
        ShowStatus($"[Redo] {rec.description}");
    }

    // ──────────────────────────────────────────────
    void Start()
    {
        // Background putih polos — matikan passthrough kamera AR (spt versi web).
        // AR plane detection & tracking tetap jalan di belakang layar, hanya
        // gambar kamera yang tak dirender.
        if (arCamera != null)
        {
            var camBg = arCamera.GetComponent<ARCameraBackground>();
            if (camBg != null) camBg.enabled = false;
            arCamera.clearFlags       = CameraClearFlags.SolidColor;
            arCamera.backgroundColor  = Color.white;
        }

        // Button listeners
        btnAddConcept.onClick.AddListener(StartAddConcept);
        btnAddLink   .onClick.AddListener(StartAddLink);
        btnReset     .onClick.AddListener(ResetMap);
        btnSave      .onClick.AddListener(SaveToFile);

        // Preview line
        if (previewLinePrefab != null)
            previewLR = Instantiate(previewLinePrefab).GetComponent<LineRenderer>();
        if (previewLR == null)
            previewLR = new GameObject("PreviewLine").AddComponent<LineRenderer>();

        // PAKSA setting di kode — prefab bisa punya width default 1m (kotak raksasa!)
        previewLR.useWorldSpace  = true;
        previewLR.positionCount  = 2;
        previewLR.startWidth     = 0.008f;
        previewLR.endWidth       = 0.008f;
        previewLR.numCapVertices = 4;
        previewLR.material       = new Material(Shader.Find("Sprites/Default"));
        var green = new Color(0.42f, 0.76f, 0.58f, 0.95f);
        previewLR.startColor = green;
        previewLR.endColor   = green;
        previewLR.enabled    = false;

        // Sembunyikan visualisasi AR plane
        if (arPlanes != null)
        {
            arPlanes.planesChanged += OnPlanesChanged;
            // Hide planes yang sudah ada
            foreach (var plane in arPlanes.trackables)
                SetPlaneVisible(plane, false);
        }

        // Data loading is handled by ApiLoader → ApplyMapJson()
        mapFile = new ConceptMapFile { canvas = new CanvasData(),
                                       map    = new MapInfo { direction = "multi" } };
    }

    // Sembunyikan mesh AR plane saat terdeteksi
    void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        foreach (var plane in args.added)
            SetPlaneVisible(plane, false);
        foreach (var plane in args.updated)
            SetPlaneVisible(plane, false);
    }

    void SetPlaneVisible(ARPlane plane, bool visible)
    {
        foreach (var mr in plane.GetComponentsInChildren<MeshRenderer>())
            mr.enabled = visible;
        foreach (var lr in plane.GetComponentsInChildren<LineRenderer>())
            lr.enabled = visible;
    }

    void Update()
    {
        UpdateTrackingStatus();
        HandlePlacementTap();
        HandlePinchZoom();
    }

    // ── Pinch to scale semua node ─────────────────
    void HandlePinchZoom()
    {
        float axis = PointerInput.ScaleAxis;
        if (Mathf.Abs(axis) > 0.2f)
        {
            nodeScale = Mathf.Clamp(nodeScale + axis * Time.deltaTime, MIN_SCALE, MAX_SCALE);
            ApplyScaleToAll();
        }

        if (Input.touchCount != 2) { lastPinchDist = -1f; return; }

        Touch t0 = Input.GetTouch(0);
        Touch t1 = Input.GetTouch(1);
        float dist = Vector2.Distance(t0.position, t1.position);

        if (lastPinchDist < 0f) { lastPinchDist = dist; return; }

        float delta = dist - lastPinchDist;
        lastPinchDist = dist;

        nodeScale = Mathf.Clamp(nodeScale + delta * 0.001f, MIN_SCALE, MAX_SCALE);
        ApplyScaleToAll();
    }

    void ApplyScaleToAll()
    {
        // Kalikan basis auto-size tiap node (bukan skala fix) agar teks tetap pas
        foreach (var node in MapNode.All)
            if (node) node.transform.localScale = node.AutoBaseScale * nodeScale;
    }

    // ── Tracking status label ─────────────────────
    // Canvas dibiarkan bersih (tanpa teks status) — hanya tombol yang tampil.
    void UpdateTrackingStatus()
    {
        if (trackingText != null && trackingText.gameObject.activeSelf)
            trackingText.gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────
    //  JSON LOAD
    // ─────────────────────────────────────────────
    public void ApplyMapJson(string json)
    {
        if (PointerInput.IsXR && !XRSupport.Instance.HeadTracked)
        {
            StartCoroutine(ApplyMapJsonWhenTracked(json));
            return;
        }
        ApplyMapJsonNow(json);
    }

    IEnumerator ApplyMapJsonWhenTracked(string json)
    {
        float waited = 0f;
        while (!XRSupport.Instance.HeadTracked && waited < 3f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        ApplyMapJsonNow(json);
    }

    void ApplyMapJsonNow(string json)
    {
        try
        {
            mapFile = JsonConvert.DeserializeObject<ConceptMapFile>(json);
            RebuildScene();
            ShowStatus($"[OK] Dimuat — {mapFile.canvas.concepts.Count} konsep, {mapFile.canvas.links.Count} relasi");
            undoStack.Clear(); redoStack.Clear();
        }
        catch (Exception e)
        {
            ShowStatus("[ERROR] JSON error: " + e.Message);
        }
    }

    void RebuildScene()
    {
        ClearScene();

        var canvas = mapFile.canvas;

        // Auto-fit coordinates to a 2m × 1.2m AR plane in front of camera
        var allNodes = new List<(float x, float y)>();
        canvas.concepts.ForEach(c => allNodes.Add((c.x, c.y)));
        canvas.links   .ForEach(l => allNodes.Add((l.x, l.y)));

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var (x, y) in allNodes)
        {
            minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
            minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
        }
        float rangeX = Mathf.Max(maxX - minX, 1f);
        float rangeY = Mathf.Max(maxY - minY, 1f);

        // Fit ke FOV kamera: seluruh peta langsung terlihat tanpa pinch-zoom.
        // Dihitung dari sudut pandang kamera → otomatis benar di portrait & landscape.
        float DIST = 1.0f;                             // jarak peta dari kamera
        float vFov   = arCamera.fieldOfView * Mathf.Deg2Rad;
        float worldH = 2f * DIST * Mathf.Tan(vFov * 0.5f) * 0.65f;   // 65% tinggi layar
        float worldW = worldH * arCamera.aspect * 0.9f;               // 90% lebar layar
        if (PointerInput.IsXR)
        {
            DIST   = XR_MAP_DIST;
            worldH = XR_MAP_HEIGHT;
            worldW = XR_MAP_WIDTH;
        }

        float scaleX = worldW / rangeX, scaleY = worldH / rangeY;
        float scale  = Mathf.Min(scaleX, scaleY);

        GetViewBasis(out Vector3 camPos, out Vector3 fwd, out Vector3 right, out Vector3 up);
        Vector3 origin = camPos
                       + fwd   * DIST
                       - right * worldW * 0.5f
                       - up    * worldH * 0.5f;

        Vector3 WorldPos(float x, float y)
        {
            float nx = (x - minX) * scale;
            float ny = (y - minY) * scale;
            // Flip Y (screen Y down, Unity Y up)
            return origin + right * nx + up * (worldH - ny);
        }

        // Spawn concepts
        foreach (var c in canvas.concepts)
        {
            cCounter = Mathf.Max(cCounter, ParseIdNum(c.cid));
            SpawnConcept(c, WorldPos(c.x, c.y));
        }

        // Spawn links
        foreach (var l in canvas.links)
        {
            lCounter = Mathf.Max(lCounter, ParseIdNum(l.lid));
            SpawnLink(l, WorldPos(l.x, l.y));
        }

        // Draw connections
        DrawAllConnections();
    }

    static int ParseIdNum(string id)
    {
        var digits = System.Text.RegularExpressions.Regex.Replace(id ?? "", @"\D", "");
        return int.TryParse(digits, out int n) ? n : 0;
    }

    // ─────────────────────────────────────────────
    //  SPAWN NODES
    // ─────────────────────────────────────────────
    void SpawnConcept(ConceptData data, Vector3 worldPos)
    {
        var go   = Instantiate(conceptNodePrefab, worldPos, Quaternion.identity);
        var node = go.GetComponent<ConceptNode>();
        node.Setup(data);
        conceptObjects[data.cid] = node;
    }

    void SpawnLink(LinkData data, Vector3 worldPos)
    {
        var go   = Instantiate(linkNodePrefab, worldPos, Quaternion.identity);
        var node = go.GetComponent<LinkNode>();
        node.Setup(data);
        linkObjects[data.lid] = node;
    }

    // ─────────────────────────────────────────────
    //  DRAW CONNECTIONS
    // ─────────────────────────────────────────────
    void DrawAllConnections()
    {
        // Clear old
        connections.ForEach(c => { if (c) Destroy(c.gameObject); });
        connections.Clear();

        foreach (var lk in mapFile.canvas.links)
        {
            // Source edge: concept → link
            if (!string.IsNullOrEmpty(lk.source_cid)
                && conceptObjects.TryGetValue(lk.source_cid, out var srcNode)
                && linkObjects.TryGetValue(lk.lid, out var lkNode))
            {
                AddConnectionLine(srcNode.transform, lkNode.transform, ConnectionLine.ConnectionType.Source);
            }
        }

        // Target edges: link → concept(s)
        foreach (var lt in mapFile.canvas.linktargets)
        {
            if (linkObjects.TryGetValue(lt.lid, out var lkNode)
                && conceptObjects.TryGetValue(lt.target_cid, out var tgtNode))
            {
                AddConnectionLine(lkNode.transform, tgtNode.transform, ConnectionLine.ConnectionType.Target);
            }
        }
    }

    void AddConnectionLine(Transform from, Transform to, ConnectionLine.ConnectionType type)
    {
        var go  = Instantiate(connectionLinePrefab);
        var cl  = go.GetComponent<ConnectionLine>();
        cl.Init(from, to, type);
        connections.Add(cl);
    }

    // ─────────────────────────────────────────────
    //  ADD CONCEPT / LINK (toolbar buttons)
    // ─────────────────────────────────────────────
    void StartAddConcept()
    {
        currentMode = Mode.PlaceConcept;
        pendingId   = "c" + (++cCounter);
        ShowStatus("Tap permukaan untuk letakkan Konsep");
    }

    void StartAddLink()
    {
        currentMode = Mode.PlaceLink;
        pendingId   = "l" + (++lCounter);
        ShowStatus("Tap permukaan untuk letakkan Relasi");
    }

    void HandlePlacementTap()
    {
        if (currentMode == Mode.None) return;

        foreach (var p in PointerInput.Samples)
        {
            if (p.phase != TouchPhase.Ended) continue;

            // Don't place if touch is on UI
            if (p.overUI) continue;

            Vector3 worldPos;

            // Try AR raycast against detected planes
            if (arRaycast.Raycast(p.ray, arHits, TrackableType.PlaneWithinPolygon))
            {
                worldPos = arHits[0].pose.position;
            }
            else
            {
                // Fallback: 1.5m in front of camera
                worldPos = p.xr
                    ? p.ray.GetPoint(XR_PLACE_DIST)
                    : arCamera.transform.position + arCamera.transform.forward * 1.5f;
            }

            PlaceNodeAt(worldPos);
            return;
        }
    }

    void PlaceNodeAt(Vector3 worldPos)
    {
        if (currentMode == Mode.PlaceConcept)
        {
            var data = new ConceptData
            {
                cid   = pendingId,
                label = "Konsep " + cCounter,
                x     = 0, y = 0,
                data  = "{\"color\":\"#000000\",\"background-color\":\"#FFBF40\",\"width\":70,\"height\":22,\"type\":\"concept\"}"
            };
            mapFile.canvas.concepts.Add(data);
            SpawnConcept(data, worldPos);
            ShowStatus($"[OK] Konsep '{data.label}' ditambahkan");
        }
        else if (currentMode == Mode.PlaceLink)
        {
            var data = new LinkData
            {
                lid        = pendingId,
                label      = "relasi " + lCounter,
                x          = 0, y = 0,
                source_cid = null,
                data       = "{\"color\":\"#000000\",\"background-color\":\"#DEDEDE\",\"width\":60,\"height\":22,\"type\":\"link\",\"limit\":9}"
            };
            mapFile.canvas.links.Add(data);
            SpawnLink(data, worldPos);
            ShowStatus($"[OK] Relasi '{data.label}' ditambahkan");
        }

        currentMode = Mode.None;
        pendingId   = null;
    }

    // ─────────────────────────────────────────────
    //  DRAG-TO-CONNECT  (called by MapNode)
    // ─────────────────────────────────────────────
    public void BeginConnectionDrag(MapNode fromNode, PointerSample pointer)
    {
        isDragging   = true;
        dragFromNode = fromNode;

        // Highlight valid targets
        if (fromNode is ConceptNode)
            foreach (var kv in linkObjects)    kv.Value.SetHighlight(true);
        else
            foreach (var kv in conceptObjects) kv.Value.SetHighlight(true);

        if (previewLR != null) previewLR.enabled = true;
        ShowStatus(fromNode is ConceptNode
            ? "Drag ke node Relasi untuk set source"
            : "Drag ke node Konsep untuk set target");
    }

    public void UpdateConnectionDrag(PointerSample pointer)
    {
        if (!isDragging || previewLR == null) return;

        Vector3 from = dragFromNode.transform.position;

        // Proyeksikan jari ke bidang sejajar kamera di kedalaman node,
        // supaya ujung garis selalu tepat di bawah jari
        Plane p = new Plane(arCamera.transform.forward, from);
        Ray   r = pointer.ray;
        Vector3 to = p.Raycast(r, out float d)
            ? r.GetPoint(d)
            : r.origin + r.direction * 1.5f;

        previewLR.SetPosition(0, from);
        previewLR.SetPosition(1, to);
    }

    public void EndConnectionDrag(PointerSample pointer, MapNode fromNode, bool backward = false)
    {
        if (!isDragging) return;
        isDragging = false;

        ClearHighlights();
        if (previewLR != null) previewLR.enabled = false;

        // 1) Raycast langsung ke node
        MapNode target = null;
        Ray ray = pointer.ray;
        if (Physics.Raycast(ray, out RaycastHit hit, 10f))
        {
            var hitNode = hit.transform.GetComponentInParent<MapNode>();
            if (hitNode != null && hitNode != fromNode) target = hitNode;
        }

        // 2) Sensing lebar: kalau raycast meleset, ambil node terdekat
        //    dalam radius layar (10% tinggi layar) dari titik lepas
        if (target == null && pointer.xr)
        {
            float best = XR_PICK_DEGREES;
            foreach (var n in MapNode.All)
            {
                if (n == null || n == fromNode) continue;
                float a = Vector3.Angle(ray.direction, n.transform.position - ray.origin);
                if (a < best) { best = a; target = n; }
            }
        }
        else if (target == null)
        {
            float best = Screen.height * 0.10f;
            foreach (var n in MapNode.All)
            {
                if (n == null || n == fromNode) continue;
                Vector3 sp = arCamera.WorldToScreenPoint(n.transform.position);
                if (sp.z < 0f) continue;
                float d = Vector2.Distance((Vector2)sp, pointer.screenPos);
                if (d < best) { best = d; target = n; }
            }
        }

        if (target != null)
        {
            // backward (tombol "←" LinkNode): node tujuan jadi SOURCE relasi
            if (backward) TryConnect(target, fromNode);
            else          TryConnect(fromNode, target);
        }
        else ShowStatus("[!] Lepaskan di dekat node tujuan");
    }

    public void TryConnect(MapNode from, MapNode to)
    {
        // Concept → Link: set source
        if (from is ConceptNode cNode && to is LinkNode lNode)
        {
            var lData = mapFile.canvas.links.Find(l => l.lid == lNode.NodeId);
            if (lData != null)
            {
                string oldSource = lData.source_cid;
                string newSource = cNode.NodeId;
                lData.source_cid = newSource;
                DrawAllConnections();
                ShowStatus($"[OK] Source: {cNode.Label} → {lNode.Label}");
                PushUndo($"source {cNode.Label} → {lNode.Label}",
                    undo: () => { lData.source_cid = oldSource; DrawAllConnections(); },
                    redo: () => { lData.source_cid = newSource; DrawAllConnections(); });
            }
        }
        // Link → Concept: add target
        else if (from is LinkNode lNode2 && to is ConceptNode cNode2)
        {
            var lData = mapFile.canvas.links.Find(l => l.lid == lNode2.NodeId);
            if (lData == null) return;
            if (lData.source_cid == cNode2.NodeId)
            { ShowStatus("[!] Source dan target tidak boleh sama"); return; }

            bool exists = mapFile.canvas.linktargets
                .Exists(t => t.lid == lNode2.NodeId && t.target_cid == cNode2.NodeId);
            if (exists) { ShowStatus("[!] Target sudah terhubung"); return; }

            var newTarget = new LinkTargetData
            {
                lid         = lNode2.NodeId,
                target_cid  = cNode2.NodeId,
                target_data = JsonConvert.SerializeObject(new { source = lNode2.NodeId, target = cNode2.NodeId, type = "right" })
            };
            mapFile.canvas.linktargets.Add(newTarget);
            DrawAllConnections();
            ShowStatus($"[OK] Target: {lNode2.Label} → {cNode2.Label}");
            PushUndo($"target {lNode2.Label} → {cNode2.Label}",
                undo: () => { mapFile.canvas.linktargets.Remove(newTarget); DrawAllConnections(); },
                redo: () => { mapFile.canvas.linktargets.Add(newTarget); DrawAllConnections(); });
        }
        else
        {
            ShowStatus("[!] Drag Konsep→Relasi atau Relasi→Konsep");
        }
    }

    void ClearHighlights()
    {
        foreach (var kv in conceptObjects) kv.Value.SetHighlight(false);
        foreach (var kv in linkObjects)    kv.Value.SetHighlight(false);
    }

    // ─────────────────────────────────────────────
    //  AUTO LAYOUT — rapikan semua node di depan kamera
    // ─────────────────────────────────────────────
    /// Rapikan LEMBUT: node di-snap ke grid terdekat dari posisi sekarang.
    /// Susunan relatif & koneksi tidak berubah — hanya diratakan (align)
    /// dan dijamin tidak bertumpuk. Node bergeser seminimal mungkin.
    public void AutoLayoutNodes()
    {
        var nodes = new List<MapNode>();
        foreach (var n in MapNode.All) if (n != null) nodes.Add(n);
        if (nodes.Count == 0) return;

        GetViewBasis(out Vector3 camPos, out Vector3 fwd, out Vector3 right, out Vector3 up);

        // Bidang layout = jarak rata-rata node dari kamera (bukan reset ke 0.7m)
        float dist = 0f;
        foreach (var n in nodes) dist += Vector3.Dot(n.transform.position - camPos, fwd);
        dist = Mathf.Max(dist / nodes.Count, 0.35f);
        Vector3 origin = camPos + fwd * dist;

        // Ukuran sel grid dari node terbesar + jarak antar
        float cellW = 0.12f, cellH = 0.06f;
        foreach (var n in nodes)
        {
            cellW = Mathf.Max(cellW, n.transform.localScale.x);
            cellH = Mathf.Max(cellH, n.transform.localScale.y);
        }
        cellW *= 1.3f;
        cellH *= 1.9f;

        // Urutkan dari kiri-atas agar hasil deterministik
        nodes.Sort((a, b) =>
        {
            Vector3 pa = a.transform.position - origin, pb = b.transform.position - origin;
            float ya = Vector3.Dot(pa, up), yb = Vector3.Dot(pb, up);
            if (Mathf.Abs(ya - yb) > 0.01f) return yb.CompareTo(ya);
            return Vector3.Dot(pa, right).CompareTo(Vector3.Dot(pb, right));
        });

        var used = new HashSet<(int, int)>();
        foreach (var n in nodes)
        {
            Vector3 rel = n.transform.position - origin;
            int gx = Mathf.RoundToInt(Vector3.Dot(rel, right) / cellW);
            int gy = Mathf.RoundToInt(Vector3.Dot(rel, up)    / cellH);

            (int fx, int fy) = FindFreeCell(gx, gy, used);
            used.Add((fx, fy));
            n.transform.position = origin + right * (fx * cellW) + up * (fy * cellH);
        }

        ShowStatus("[OK] Layout dirapikan");
    }

    /// Centerize: pindahkan SELURUH peta ke depan kamera sebagai satu grup.
    /// Susunan antar-node dipertahankan (offset relatif terhadap pusat grup
    /// di-remap ke basis kamera sekarang), koneksi tidak berubah.
    public void CenterNodes()
    {
        var nodes = new List<MapNode>();
        foreach (var n in MapNode.All) if (n != null) nodes.Add(n);
        if (nodes.Count == 0) return;

        // Pusat grup saat ini
        Vector3 centroid = Vector3.zero;
        foreach (var n in nodes) centroid += n.transform.position;
        centroid /= nodes.Count;

        GetViewBasis(out Vector3 camPos, out Vector3 fwd, out Vector3 right, out Vector3 up);

        // Simpan offset tiap node pada basis kamera (kedalaman diratakan)
        var offsets = new List<(MapNode n, float x, float y)>();
        foreach (var n in nodes)
        {
            Vector3 rel = n.transform.position - centroid;
            offsets.Add((n, Vector3.Dot(rel, right), Vector3.Dot(rel, up)));
        }

        // Tempatkan pusat grup di depan kamera
        Vector3 newCenter = camPos + fwd * (PointerInput.IsXR ? XR_MAP_DIST : 1.0f);
        foreach (var (n, x, y) in offsets)
            n.transform.position = newCenter + right * x + up * y;

        ShowStatus("[OK] Peta dipusatkan ke depan kamera");
    }

    void GetViewBasis(out Vector3 pos, out Vector3 fwd, out Vector3 right, out Vector3 up)
    {
        var cam = arCamera.transform;
        pos = cam.position;
        if (!PointerInput.IsXR)
        {
            fwd = cam.forward; right = cam.right; up = cam.up;
            return;
        }

        fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(cam.up, Vector3.up);
        fwd.Normalize();
        up    = Vector3.up;
        right = Vector3.Cross(up, fwd);
    }

    // Cari sel grid kosong terdekat (spiral keluar dari sel asal)
    static (int, int) FindFreeCell(int gx, int gy, HashSet<(int, int)> used)
    {
        if (!used.Contains((gx, gy))) return (gx, gy);
        for (int r = 1; r <= 8; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    if (!used.Contains((gx + dx, gy + dy))) return (gx + dx, gy + dy);
                }
        return (gx, gy); // penuh — biarkan (tidak mungkin dalam praktik)
    }

    // ─────────────────────────────────────────────
    //  CLEAR LINK CONNECTIONS (dipanggil dari ✕ button)
    // ─────────────────────────────────────────────
    public void ClearLinkConnections(string lid)
    {
        var lData = mapFile?.canvas.links.Find(l => l.lid == lid);
        if (lData == null) return;

        string oldSource   = lData.source_cid;
        var    oldTargets  = mapFile.canvas.linktargets.FindAll(lt => lt.lid == lid);

        lData.source_cid = null;
        mapFile.canvas.linktargets.RemoveAll(lt => lt.lid == lid);

        DrawAllConnections();

        var lNode = linkObjects.ContainsKey(lid) ? linkObjects[lid] : null;
        string lbl = lNode != null ? lNode.Label : lid;
        ShowStatus($"[OK] Semua koneksi '{lbl}' dihapus");
        PushUndo($"hapus koneksi {lbl}",
            undo: () => {
                lData.source_cid = oldSource;
                mapFile.canvas.linktargets.AddRange(oldTargets);
                DrawAllConnections();
            },
            redo: () => {
                lData.source_cid = null;
                mapFile.canvas.linktargets.RemoveAll(lt => lt.lid == lid);
                DrawAllConnections();
            });
    }

    // ─────────────────────────────────────────────
    //  SAVE
    // ─────────────────────────────────────────────
    public string GetCurrentJson()
    {
        if (mapFile == null) return "{}";
        SyncPositionsToData();
        return JsonConvert.SerializeObject(mapFile, Formatting.Indented);
    }

    void SyncPositionsToData()
    {
        foreach (var kv in conceptObjects)
        {
            var data = mapFile.canvas.concepts.Find(c => c.cid == kv.Key);
            if (data == null) continue;
            data.x = kv.Value.transform.position.x * 100f;
            data.y = kv.Value.transform.position.y * 100f;
        }
        foreach (var kv in linkObjects)
        {
            var data = mapFile.canvas.links.Find(l => l.lid == kv.Key);
            if (data == null) continue;
            data.x = kv.Value.transform.position.x * 100f;
            data.y = kv.Value.transform.position.y * 100f;
        }
    }

    public void SaveToFile()
    {
        if (mapFile == null) { ShowStatus("Tidak ada data untuk disimpan"); return; }

        string json = GetCurrentJson();
        string path = Path.Combine(Application.persistentDataPath,
            $"concept-map-{DateTime.Now:yyyyMMdd_HHmm}.json");
        File.WriteAllText(path, json);
        ShowStatus($"[Saved] Tersimpan:\n{path}");
    }

    // ─────────────────────────────────────────────
    //  RESET
    // ─────────────────────────────────────────────
    void ResetMap()
    {
        ClearScene();
        if (mapFile != null)
        {
            mapFile.canvas.concepts.Clear();
            mapFile.canvas.links.Clear();
            mapFile.canvas.linktargets.Clear();
        }
        cCounter = 0; lCounter = 0;
        ShowStatus("Reset selesai");
    }

    void ClearScene()
    {
        foreach (var kv in conceptObjects) if (kv.Value) Destroy(kv.Value.gameObject);
        foreach (var kv in linkObjects)    if (kv.Value) Destroy(kv.Value.gameObject);
        connections.ForEach(c => { if (c) Destroy(c.gameObject); });
        conceptObjects.Clear();
        linkObjects   .Clear();
        connections   .Clear();
    }

    // ─────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────
    public void ShowStatus(string msg)
    {
        // Canvas dijaga bersih — status hanya ke log (untuk debugging)
        Debug.Log($"[Status] {msg}");
        if (statusText != null && statusText.gameObject.activeSelf)
            statusText.gameObject.SetActive(false);
    }
}
