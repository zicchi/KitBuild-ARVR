using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Base class for ConceptNode and LinkNode.
/// - Tap         → select, muncul tombol "→" (dan "←" untuk LinkNode)
/// - Tap "→"     → connect forward (Concept→Link / Link→Concept target)
/// - Tap "←"     → connect backward (Link←Concept sebagai source)
/// - Tap node lain saat mode connect → sambungkan
/// - Drag        → pindahkan node
/// - Box auto-size menyesuaikan panjang teks + padding
/// </summary>
public abstract class MapNode : MonoBehaviour
{
    [Header("References")]
    public TextMeshPro    labelText;
    public Transform      connectorPort;
    public SpriteRenderer bgSprite;
    public SpriteRenderer portSprite;

    [HideInInspector] public string NodeId;
    [HideInInspector] public string Label;
    [HideInInspector] public Vector3 AutoBaseScale = Vector3.one;  // hasil auto-size (basis pinch-zoom)

    // Registry semua node aktif (dipakai OffscreenIndicator & pinch)
    public static readonly System.Collections.Generic.List<MapNode> All = new();

    protected ConceptMapManager manager;
    protected Camera            arCamera;
    private   MeshRenderer      meshRenderer;
    private   MaterialPropertyBlock propBlock;

    // Konstanta ukuran (dalam meter world-space)
    protected const float LABEL_WORLD_SCALE = 0.02f;  // 2cm per TMP unit → char 10cm
    protected const float TMP_FONT_SIZE     = 6.5f;
    protected const float MAX_NODE_WIDTH    = 0.6f;
    protected const float PAD_X             = 0.032f; // dipepetkan, teks isi penuh kotak
    protected const float PAD_Y             = 0.024f;
    protected const float BTN_SIZE          = 0.05f;  // tombol 5cm

    // Tombol runtime
    private GameObject       connectFwdBtn;   // "→"
    private GameObject       connectBwdBtn;   // "←" (hanya LinkNode)
    private GameObject       deleteConnBtn;   // "✕" (hanya LinkNode)
    private MeshRenderer     fwdBtnMR, bwdBtnMR;
    private MaterialPropertyBlock fwdBtnBlock, bwdBtnBlock;

    // Gesture
    private bool          isTracking;
    private int           trackedPointerId = -1;
    private PointerSample beganSample;
    private Vector3       dragOffset;
    private bool          hasDragged;

    // Mode koneksi
    private enum ConnectMode { None, Forward, Backward }
    private static MapNode      selectedNode  = null;
    private static ConnectMode  connectMode   = ConnectMode.None;
    private        bool         isSelected    = false;

    // Drag-from-arrow (drag tombol → node target, seperti versi web)
    private ConnectMode btnDragMode    = ConnectMode.None;
    private bool        connDragActive = false;

    // Skin rounded-rect (desain versi web)
    private Texture2D texNormal;    // sudut membulat polos
    private Texture2D texSelected;  // + border merah (node terpilih)
    private static Texture2D btnTexShared;  // rounded square untuk tombol
    static readonly Color SELECT_BORDER = new Color(0.90f, 0.42f, 0.45f, 1f); // merah muda web

    protected virtual bool HasBackButton   => false;
    protected virtual bool HasDeleteButton => false;

    // ──────────────────────────────────────────────
    void Awake()
    {
        arCamera     = Camera.main;
        manager      = FindFirstObjectByType<ConceptMapManager>();
        meshRenderer = GetComponent<MeshRenderer>();
        propBlock    = new MaterialPropertyBlock();
    }

    void OnEnable()  { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Update()
    {
        Billboard();
        HandleTouch();
    }

    // ── Billboard ─────────────────────────────────
    void Billboard()
    {
        if (arCamera == null) return;
        // Up = sumbu dunia (bukan kamera) → node tetap tegak/steady
        // walau HP diputar portrait ⇄ landscape
        Vector3 facing = PointerInput.IsXR
            ? transform.position - arCamera.transform.position
            : arCamera.transform.rotation * Vector3.forward;
        transform.LookAt(transform.position + facing, Vector3.up);
    }

    // ── Init ──────────────────────────────────────
    public virtual void Init(string id, string label, Color bg, Color textColor)
    {
        NodeId = id;
        Label  = label;

        SetMeshColor(bg);
        SetupLabel(label);
        CreateButtons();
        StartCoroutine(AutoSizeCo());
    }

    void SetupLabel(string label)
    {
        if (labelText == null) return;
        labelText.text           = label;
        labelText.color          = Color.black;
        labelText.fontSize       = TMP_FONT_SIZE;
        labelText.enableAutoSizing    = false;
        labelText.enableWordWrapping  = true;
        labelText.overflowMode        = TMPro.TextOverflowModes.Overflow;
        labelText.alignment           = TMPro.TextAlignmentOptions.Center;
        labelText.transform.localRotation = Quaternion.identity;
    }

    // ── Auto-size box sesuai teks ─────────────────
    IEnumerator AutoSizeCo()
    {
        yield return null; // tunggu 1 frame

        if (labelText == null) yield break;

        // Set node ke ukuran sementara (1,1,0.01) agar bisa hitung world scale
        transform.localScale = new Vector3(1f, 1f, 0.01f);
        ApplyLabelScale(1f, 1f);

        // Set lebar wrapping
        var rt = labelText.GetComponent<RectTransform>();
        float maxWidthTMP = MAX_NODE_WIDTH / LABEL_WORLD_SCALE;
        if (rt != null) rt.sizeDelta = new Vector2(maxWidthTMP, 10000f);

        labelText.ForceMeshUpdate();

        // Ukuran teks dalam TMP local → konversi ke world
        Vector2 tSize     = labelText.GetRenderedValues(false);
        float textWorldW  = tSize.x * LABEL_WORLD_SCALE;
        float textWorldH  = tSize.y * LABEL_WORLD_SCALE;

        // Ukuran node final
        float nodeW = Mathf.Clamp(textWorldW + PAD_X * 2f, 0.16f, MAX_NODE_WIDTH);
        float nodeH = Mathf.Max(textWorldH + PAD_Y * 2f, 0.075f);

        // Terapkan scale — teks dirender lebih besar dari kotak (FONT_BOOST),
        // kotak (nodeW/nodeH) sendiri TIDAK berubah dari hasil pengukuran di atas.
        transform.localScale = new Vector3(nodeW, nodeH, 0.01f);
        ApplyLabelScale(nodeW, nodeH, FONT_BOOST);

        // Update rect width sesuai lebar node final
        if (rt != null) rt.sizeDelta = new Vector2(nodeW / LABEL_WORLD_SCALE, 10000f);
        labelText.transform.localPosition = new Vector3(0f, 0f, -0.5f);

        // Update collider
        var col = GetComponent<BoxCollider>();
        if (col != null) col.size = new Vector3(1f, 1f, 0.5f);

        // Update posisi tombol
        UpdateButtonPositions(nodeW, nodeH);

        // Simpan basis skala untuk pinch-zoom
        AutoBaseScale = transform.localScale;

        // Terapkan skin rounded-rect sesuai aspek node (desain web)
        ApplyRoundedSkin(nodeW, nodeH);
    }

    // ── Skin rounded-rect ─────────────────────────
    void ApplyRoundedSkin(float nodeW, float nodeH)
    {
        if (meshRenderer == null) return;

        int tw = 256;
        int th = Mathf.Clamp(Mathf.RoundToInt(tw * nodeH / nodeW), 32, 512);
        // Radius sudut ±1.5cm world, konsisten berapapun ukuran node
        float radius = Mathf.Min(tw * (0.015f / nodeW), th * 0.5f - 1f);
        float border = Mathf.Min(tw * (0.012f / nodeW), th * 0.25f); // tebal border merah

        texNormal   = MakeRoundedTex(tw, th, radius, 0f,     SELECT_BORDER);
        texSelected = MakeRoundedTex(tw, th, radius, border, SELECT_BORDER);

        // Material flat transparan (seperti web, tanpa lighting)
        meshRenderer.material = new Material(Shader.Find("Sprites/Default"));
        SetMeshTexture(isSelected ? texSelected : texNormal);
        SetMeshColor(GetBaseColor());
    }

    void SetMeshTexture(Texture2D t)
    {
        if (meshRenderer == null || t == null) return;
        meshRenderer.GetPropertyBlock(propBlock);
        propBlock.SetTexture("_MainTex", t);
        meshRenderer.SetPropertyBlock(propBlock);
    }

    /// Rounded-rect putih (di-tint via _Color); borderPx>0 → cincin border berwarna.
    static Texture2D MakeRoundedTex(int w, int h, float radius, float borderPx, Color borderCol)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        float hw = w * 0.5f, hh = h * 0.5f;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            // Signed distance ke rounded rect (negatif = di dalam)
            float dx = Mathf.Max(Mathf.Abs(x + 0.5f - hw) - (hw - radius), 0f);
            float dy = Mathf.Max(Mathf.Abs(y + 0.5f - hh) - (hh - radius), 0f);
            float d  = Mathf.Sqrt(dx * dx + dy * dy) - radius;

            float alpha = Mathf.Clamp01(0.5f - d);          // anti-alias 1px
            Color c = (borderPx > 0f && d > -borderPx) ? borderCol : Color.white;
            c.a = alpha;
            tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return tex;
    }

    // FONT_BOOST: teks dirender lebih besar dari "pas" di kotak, TANPA
    // mengubah ukuran kotak — dipakai hanya saat render final (bukan saat
    // mengukur), jadi visualnya jadi padding lebih kecil, kotak tetap sama.
    protected const float FONT_BOOST = 1.3f;

    void ApplyLabelScale(float nodeW, float nodeH, float boost = 1f)
    {
        if (labelText == null) return;
        // Label world scale selalu LABEL_WORLD_SCALE, apapun ukuran node
        labelText.transform.localScale = new Vector3(
            boost * LABEL_WORLD_SCALE / nodeW,
            boost * LABEL_WORLD_SCALE / nodeH,
            1f);
    }

    // ── Tombol "→" dan "←" ───────────────────────
    // Warna handle disamakan dgn badge versi web: → biru, ← merah.
    static readonly Color BTN_BLUE   = new Color(0.25f, 0.55f, 0.95f, 1f);
    static readonly Color BTN_RED    = new Color(0.86f, 0.27f, 0.30f, 1f);
    static readonly Color BTN_ACTIVE = new Color(1f, 0.6f, 0f, 1f);

    void CreateButtons()
    {
        connectFwdBtn = CreateBtn("FwdBtn", "→", BTN_BLUE, Color.white);
        fwdBtnMR      = connectFwdBtn.GetComponent<MeshRenderer>();
        fwdBtnBlock   = new MaterialPropertyBlock();
        connectFwdBtn.SetActive(false);

        if (HasBackButton)
        {
            connectBwdBtn = CreateBtn("BwdBtn", "←", BTN_RED, Color.white);
            bwdBtnMR      = connectBwdBtn.GetComponent<MeshRenderer>();
            bwdBtnBlock   = new MaterialPropertyBlock();
            connectBwdBtn.SetActive(false);
        }

        if (HasDeleteButton)
        {
            deleteConnBtn = CreateBtn("DelBtn", "X", BTN_RED, Color.white);
            deleteConnBtn.SetActive(false);
        }
    }

    GameObject CreateBtn(string objName, string icon, Color color, Color iconColor)
    {
        var btn = GameObject.CreatePrimitive(PrimitiveType.Quad);
        btn.name = objName;
        btn.transform.SetParent(transform, false);

        // Rounded square + flat shader (desain web)
        if (btnTexShared == null)
            btnTexShared = MakeRoundedTex(64, 64, 18f, 0f, Color.white);
        var mr = btn.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Sprites/Default"));
        var block = new MaterialPropertyBlock();
        block.SetTexture("_MainTex", btnTexShared);
        block.SetColor("_Color", color);
        mr.SetPropertyBlock(block);

        // Teks icon
        var lbl = new GameObject("Lbl");
        lbl.transform.SetParent(btn.transform, false);
        var tmp       = lbl.AddComponent<TextMeshPro>();
        tmp.text      = icon;
        tmp.fontSize  = TMP_FONT_SIZE;
        tmp.color     = iconColor;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.enableAutoSizing = false;
        lbl.transform.localPosition = new Vector3(0f, 0f, -0.1f);
        // Dikompensasi thd kenaikan TMP_FONT_SIZE spy ikon tetap pas di badge kecil
        lbl.transform.localScale    = new Vector3(0.46f, 0.46f, 1f);

        return btn;
    }

    void UpdateButtonPositions(float nodeW, float nodeH)
    {
        // Handle disematkan sebagai badge di SUDUT node (tumpang-tindih
        // sedikit ke dalam), bukan melayang di tengah tepi dengan jarak —
        // meniru gaya versi web (badge kecil menempel di pojok node).
        float btnLocal    = BTN_SIZE / nodeW;
        float btnLocalTop = BTN_SIZE / nodeH;
        const float OVERLAP = 0.32f; // porsi badge yg masuk ke dalam node

        float cornerX      = 0.5f - btnLocal * OVERLAP;
        float cornerYBot   = -0.5f + btnLocalTop * OVERLAP;
        float cornerYTop   = 0.5f - btnLocalTop * OVERLAP;

        if (connectFwdBtn != null)
        {
            // "→" di pojok kanan-bawah
            connectFwdBtn.transform.localScale    = new Vector3(btnLocal, btnLocalTop, 1f);
            connectFwdBtn.transform.localPosition = new Vector3(cornerX, cornerYBot, -0.5f);
        }

        if (connectBwdBtn != null)
        {
            // "←" di pojok kiri-bawah
            connectBwdBtn.transform.localScale    = new Vector3(btnLocal, btnLocalTop, 1f);
            connectBwdBtn.transform.localPosition = new Vector3(-cornerX, cornerYBot, -0.5f);
        }

        if (deleteConnBtn != null)
        {
            // "X" di pojok kanan-atas
            deleteConnBtn.transform.localScale    = new Vector3(btnLocal, btnLocalTop, 1f);
            deleteConnBtn.transform.localPosition = new Vector3(cornerX, cornerYTop, -0.5f);
        }
    }

    // ── Touch ─────────────────────────────────────
    void HandleTouch()
    {
        foreach (var p in PointerInput.Samples)
        {
            if (p.phase == TouchPhase.Began && !isTracking)
            {
                if (p.overUI) continue;
                var hit = RaycastMe(p.ray);
                if (hit == null) continue;

                // Cek tombol — arrow bisa DI-DRAG ke node target (seperti versi web)
                if (IsBtn(hit.Value.transform, connectFwdBtn) ||
                    IsBtn(hit.Value.transform, connectBwdBtn))
                {
                    btnDragMode = IsBtn(hit.Value.transform, connectFwdBtn)
                        ? ConnectMode.Forward : ConnectMode.Backward;
                    connDragActive = false;
                    isTracking = true; trackedPointerId = p.id;
                    beganSample = p; hasDragged = false;
                    continue;
                }
                if (IsBtn(hit.Value.transform, deleteConnBtn))
                { OnDeleteBtn(); return; }

                isTracking = true; trackedPointerId = p.id;
                beganSample = p; hasDragged = false;
                ComputeDragOffset(p.ray);
            }
            else if (isTracking && p.id == trackedPointerId)
            {
                if (p.phase == TouchPhase.Moved)
                {
                    if (hasDragged || PointerInput.BeyondDragThreshold(beganSample, p))
                    {
                        hasDragged = true;
                        if (btnDragMode != ConnectMode.None)
                        {
                            // Drag garis koneksi dari arrow
                            if (!connDragActive)
                            { manager.BeginConnectionDrag(this, p); connDragActive = true; }
                            manager.UpdateConnectionDrag(p);
                        }
                        else MoveNode(p.ray);
                    }
                }
                else if (p.phase == TouchPhase.Ended || p.phase == TouchPhase.Canceled)
                {
                    if (btnDragMode != ConnectMode.None)
                    {
                        if (connDragActive)
                        {
                            // Lepas di node target → sambungkan
                            manager.EndConnectionDrag(p, this,
                                btnDragMode == ConnectMode.Backward);
                            DeselectAll();
                        }
                        else
                        {
                            // Tap singkat di arrow → mode tap lama (fallback)
                            if (btnDragMode == ConnectMode.Forward) OnFwdBtn();
                            else OnBwdBtn();
                        }
                        btnDragMode = ConnectMode.None; connDragActive = false;
                    }
                    else if (!hasDragged) OnNodeTapped();
                    isTracking = false; trackedPointerId = -1;
                }
            }
        }
    }

    RaycastHit? RaycastMe(Ray ray)
    {
        if (!Physics.Raycast(ray, out RaycastHit hit)) return null;
        return (hit.transform == transform || hit.transform.IsChildOf(transform)) ? hit : (RaycastHit?)null;
    }

    bool IsBtn(Transform t, GameObject btn) =>
        btn != null && btn.activeSelf && (t == btn.transform || t.IsChildOf(btn.transform));

    void ComputeDragOffset(Ray r)
    {
        Plane p = new Plane(arCamera.transform.forward, transform.position);
        if (p.Raycast(r, out float d)) dragOffset = transform.position - r.GetPoint(d);
    }

    void MoveNode(Ray r)
    {
        Plane p = new Plane(arCamera.transform.forward, transform.position);
        if (p.Raycast(r, out float d)) transform.position = r.GetPoint(d) + dragOffset;
    }

    // ── Tap logic ─────────────────────────────────
    void OnNodeTapped()
    {
        if (selectedNode != null && selectedNode != this && connectMode != ConnectMode.None)
        {
            // Connect!
            if (connectMode == ConnectMode.Backward)
                manager.TryConnect(this, selectedNode); // this=Concept, selected=Link
            else
                manager.TryConnect(selectedNode, this);
            DeselectAll();
            return;
        }
        if (isSelected) { DeselectAll(); return; }
        DeselectAll();
        SelectMe();
    }

    void OnFwdBtn()
    {
        connectMode = ConnectMode.Forward;
        SetBtnColor(fwdBtnMR, fwdBtnBlock, BTN_ACTIVE);
        manager.ShowStatus(this is ConceptNode
            ? "Tap node Relasi untuk sambungkan sebagai source"
            : "Tap node Konsep untuk sambungkan sebagai target");
    }

    void OnBwdBtn()
    {
        connectMode = ConnectMode.Backward;
        SetBtnColor(bwdBtnMR, bwdBtnBlock, BTN_ACTIVE);
        manager.ShowStatus("Tap node Konsep untuk set sebagai source relasi ini");
    }

    void OnDeleteBtn()
    {
        manager.ClearLinkConnections(NodeId);
        DeselectAll();
    }

    void SelectMe()
    {
        selectedNode = this; isSelected = true;
        connectMode  = ConnectMode.None;
        SetSelectedVisual(true);
        if (connectFwdBtn != null) connectFwdBtn.SetActive(true);
        if (connectBwdBtn != null) connectBwdBtn.SetActive(true);
        if (deleteConnBtn != null) deleteConnBtn.SetActive(true);
        manager.ShowStatus("Tap → / ← untuk connect | X hapus koneksi | drag pindah");
    }

    public static void DeselectAll()
    {
        if (selectedNode != null)
        {
            selectedNode.SetSelectedVisual(false);
            selectedNode.isSelected = false;
            if (selectedNode.connectFwdBtn != null) selectedNode.connectFwdBtn.SetActive(false);
            if (selectedNode.connectBwdBtn != null) selectedNode.connectBwdBtn.SetActive(false);
            if (selectedNode.deleteConnBtn != null) selectedNode.deleteConnBtn.SetActive(false);
            // Reset warna tombol ke warna resting masing-masing
            selectedNode.SetBtnColor(selectedNode.fwdBtnMR, selectedNode.fwdBtnBlock, BTN_BLUE);
            selectedNode.SetBtnColor(selectedNode.bwdBtnMR, selectedNode.bwdBtnBlock, BTN_RED);
            selectedNode = null;
        }
        connectMode = ConnectMode.None;
    }

    void SetSelectedVisual(bool on)
    {
        // Versi web: node terpilih dapat border merah, warna isi tetap
        if (texSelected != null) SetMeshTexture(on ? texSelected : texNormal);
        else SetMeshColor(on ? Color.Lerp(GetBaseColor(), Color.white, 0.45f) : GetBaseColor());
    }

    void SetBtnColor(MeshRenderer mr, MaterialPropertyBlock block, Color c)
    {
        if (mr == null || block == null) return;
        mr.GetPropertyBlock(block);          // pertahankan texture rounded
        block.SetColor("_Color", c);
        mr.SetPropertyBlock(block);
    }

    public void SetHighlight(bool on)
        // Highlight dimatikan — warna node tetap apa adanya
        => SetMeshColor(GetBaseColor());

    void SetMeshColor(Color c)
    {
        if (meshRenderer == null) return;
        meshRenderer.GetPropertyBlock(propBlock);
        propBlock.SetColor("_Color", c);
        meshRenderer.SetPropertyBlock(propBlock);
    }

    protected abstract Color GetBaseColor();
}
