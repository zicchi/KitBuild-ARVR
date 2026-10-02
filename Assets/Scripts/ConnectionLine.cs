using UnityEngine;

/// <summary>
/// Renders one directed connection between two nodes using a LineRenderer.
/// Source edge (concept→link): gray line + dot at the concept end.
/// Target edge (link→concept): gray line + arrowhead at the concept end.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class ConnectionLine : MonoBehaviour
{
    public enum ConnectionType { Source, Target }

    // Warna versi web: garis abu-abu netral utk source & target — beda
    // dibedakan lewat bentuk ujung (titik bulat utk source, panah utk target).
    static readonly Color SOURCE_COLOR = new Color(0.58f, 0.58f, 0.6f, 0.95f);
    static readonly Color TARGET_COLOR = new Color(0.45f, 0.45f, 0.47f, 0.95f);

    private LineRenderer    lr;
    private Transform       from;
    private Transform       to;
    private ConnectionType  connType;

    // Arrow head (Target connections only)
    [SerializeField] private GameObject arrowHeadPrefab;
    private GameObject arrowHead;

    // Titik bulat di ujung koneksi Source (versi web)
    private GameObject endDot;

    // ──────────────────────────────────────────────
    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.useWorldSpace    = true;
        lr.positionCount    = 2;
        lr.numCapVertices   = 4;
        lr.numCornerVertices = 4;
    }

    public void Init(Transform fromT, Transform toT, ConnectionType type)
    {
        from     = fromT;
        to       = toT;
        connType = type;

        Color c = type == ConnectionType.Source ? SOURCE_COLOR : TARGET_COLOR;
        lr.startColor = c;
        lr.endColor   = c;
        lr.startWidth = 0.004f;
        lr.endWidth   = 0.004f;

        if (type == ConnectionType.Target)
        {
            arrowHead = arrowHeadPrefab != null
                ? Instantiate(arrowHeadPrefab, transform)
                : CreateArrowHead(c);
        }
        else
        {
            // Source: titik bulat abu-abu di ujung concept (versi web)
            endDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            endDot.name = "EndDot";
            endDot.transform.SetParent(transform, false);
            endDot.transform.localScale = Vector3.one * 0.018f;
            Destroy(endDot.GetComponent<Collider>());   // jangan ganggu raycast tap
            var mr = endDot.GetComponent<MeshRenderer>();
            mr.material = CreateSolidMaterial(c);
        }

        lr.material = CreateSolidMaterial(c);
    }

    // Panah segitiga sederhana (dipakai jika prefab tidak di-assign)
    GameObject CreateArrowHead(Color c)
    {
        var go = new GameObject("ArrowHead");
        go.transform.SetParent(transform, false);

        var mesh = new Mesh();
        // Segitiga menghadap +Z (arah garis), lebar 2.2cm panjang 3.5cm
        mesh.vertices  = new[] {
            new Vector3(0f, 0f, 0f),            // ujung depan
            new Vector3(-0.011f, 0f, -0.035f),  // sayap kiri
            new Vector3( 0.011f, 0f, -0.035f),  // sayap kanan
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 1 }; // dua sisi
        mesh.RecalculateNormals();

        go.AddComponent<MeshFilter>().mesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.material = CreateSolidMaterial(c);
        return go;
    }

    void Update()
    {
        if (from == null || to == null) { Destroy(gameObject); return; }

        Vector3 a = from.position;
        Vector3 b = to.position;

        // Slightly offset so lines don't overlap node centers
        Vector3 dir = (b - a).normalized;
        a += dir * 0.06f;
        b -= dir * 0.06f;

        lr.SetPosition(0, a);
        lr.SetPosition(1, b);

        // Panah di ujung target (menghadap kamera agar selalu terlihat)
        if (arrowHead != null)
        {
            arrowHead.transform.position = b;
            arrowHead.transform.rotation =
                Quaternion.LookRotation(dir, -Camera.main.transform.forward);
        }

        // Titik bulat di ujung concept (koneksi source)
        if (endDot != null)
            endDot.transform.position = a;
    }

    // ── Material helpers ──────────────────────────
    static Material CreateSolidMaterial(Color c)
    {
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = c;
        return mat;
    }
}
