using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pulse awareness: titik berdenyut di tepi layar saat ada node
/// di luar pandangan (kiri/kanan/atas/bawah/di belakang kamera).
/// Warna mengikuti jenis node: kuning = konsep, abu-abu = relasi.
/// Self-bootstrap — tidak perlu ditambahkan ke scene manual.
/// Adaptif portrait & landscape (dihitung dari Screen tiap frame).
/// </summary>
public class OffscreenIndicator : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("OffscreenIndicator");
        DontDestroyOnLoad(go);
        go.AddComponent<OffscreenIndicator>();
    }

    static readonly Color CONCEPT_COLOR = new Color(0.96f, 0.73f, 0.23f); // kuning node konsep
    static readonly Color LINK_COLOR    = new Color(0.72f, 0.72f, 0.72f); // abu-abu node relasi

    const float MARGIN     = 52f;   // jarak indikator dari tepi layar (px @ scale 1)
    const float DOT_SIZE   = 72f;   // diameter titik (px) — besar agar jelas terlihat
    const float PULSE_HZ   = 2.2f;  // kecepatan denyut

    Camera cam;
    readonly List<Image> pool = new();
    Transform poolParent;
    Sprite dotSprite;

    void Start()
    {
        cam = Camera.main;

        var cgo = new GameObject("OffscreenCanvas");
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;   // di bawah toolbar (99), di atas AR
        poolParent = cgo.transform;

        dotSprite = MakeCircleSprite();
    }

    void LateUpdate()
    {
        if (PointerInput.IsXR)
        {
            if (poolParent != null && poolParent.gameObject.activeSelf) poolParent.gameObject.SetActive(false);
            return;
        }
        if (cam == null) { cam = Camera.main; if (cam == null) return; }

        int   used = 0;
        float t    = Time.time;
        float w    = Screen.width, h = Screen.height;
        Vector2 center = new Vector2(w, h) * 0.5f;

        foreach (var node in MapNode.All)
        {
            if (node == null) continue;

            Vector3 vp = cam.WorldToViewportPoint(node.transform.position);
            bool behind = vp.z < 0f;
            bool off    = behind || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f;
            if (!off) continue;

            // Node di belakang kamera → proyeksi terbalik, flip biar arah benar
            if (behind) { vp.x = 1f - vp.x; vp.y = 1f - vp.y; }

            // Arah dari tengah layar ke posisi node
            Vector2 screenPos = new Vector2(vp.x * w, vp.y * h);
            Vector2 dir       = screenPos - center;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector2.right;

            // Skala vektor supaya tepat menempel tepi layar (dikurangi margin)
            float sx = (w * 0.5f - MARGIN) / Mathf.Max(Mathf.Abs(dir.x), 1e-5f);
            float sy = (h * 0.5f - MARGIN) / Mathf.Max(Mathf.Abs(dir.y), 1e-5f);
            Vector2 pos = center + dir * Mathf.Min(sx, sy);

            var img = GetIndicator(used);
            img.gameObject.SetActive(true);
            img.rectTransform.position = pos;

            // Pulse: ukuran berdenyut, warna SOLID (alpha penuh) agar jelas
            float phase = t * PULSE_HZ * Mathf.PI * 2f + used * 0.7f;
            float pulse = 0.85f + 0.3f * Mathf.Sin(phase);
            img.rectTransform.localScale = Vector3.one * pulse;

            img.color = node is LinkNode ? LINK_COLOR : CONCEPT_COLOR;

            used++;
        }

        // Matikan indikator sisa
        for (int i = used; i < pool.Count; i++)
            if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
    }

    Image GetIndicator(int i)
    {
        while (pool.Count <= i)
        {
            var go = new GameObject($"Dot{pool.Count}");
            go.transform.SetParent(poolParent, false);
            var img = go.AddComponent<Image>();
            img.sprite        = dotSprite;
            img.raycastTarget = false;   // jangan blokir touch
            img.rectTransform.sizeDelta = Vector2.one * DOT_SIZE;
            pool.Add(img);
        }
        return pool[i];
    }

    // Sprite lingkaran SOLID bertepi gelap (kontras di background apapun)
    static Sprite MakeCircleSprite()
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        float r = S * 0.5f;
        var edge = new Color(0.12f, 0.12f, 0.12f, 1f);   // cincin outline gelap
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
            Color c;
            if      (d > 1f)     c = new Color(0, 0, 0, Mathf.Clamp01((1.03f - d) * 20f)); // AA luar
            else if (d > 0.82f)  c = edge;                 // outline
            else                 c = Color.white;          // isi solid (di-tint warna node)
            tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
    }
}
