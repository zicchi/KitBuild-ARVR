using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Memuat peta konsep awal saat aplikasi dibuka.
/// - useSampleMap = true  → baca StreamingAssets/map.json
/// - useSampleMap = false → GET {apiBaseUrl}/maps/{mapId}, fallback ke map.json bila gagal
/// </summary>
public class ApiLoader : MonoBehaviour
{
    const string SAMPLE_FILE = "map.json";

    [Header("API Config")]
    [Tooltip("Base URL API, contoh: https://api.example.com")]
    public string apiBaseUrl = "https://api.example.com";

    [Tooltip("ID peta konsep yang di-load")]
    public string mapId = "sample";

    [Tooltip("Pakai StreamingAssets/map.json tanpa memanggil API")]
    public bool useSampleMap = true;

    [Header("References")]
    public ConceptMapManager mapManager;

    public event Action<string> OnLoadError;

    void Start()
    {
        StartCoroutine(LoadMap());
    }

    public IEnumerator LoadMap()
    {
        yield return null;   // satu frame delay biar scene siap

        if (!useSampleMap)
        {
            string url = $"{apiBaseUrl.TrimEnd('/')}/maps/{mapId}";
            Debug.Log($"[ApiLoader] GET {url}");

            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Accept", "application/json");
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                mapManager.ApplyMapJson(req.downloadHandler.text);
                yield break;
            }

            Debug.LogWarning($"[ApiLoader] API gagal: {req.error} — fallback ke {SAMPLE_FILE}");
            OnLoadError?.Invoke(req.error);
        }

        yield return LoadSampleMap();
    }

    // StreamingAssets di Android ada di dalam APK, jadi harus dibaca via UnityWebRequest.
    IEnumerator LoadSampleMap()
    {
        string path = Path.Combine(Application.streamingAssetsPath, SAMPLE_FILE);
        if (!path.Contains("://")) path = "file://" + path;

        using var req = UnityWebRequest.Get(path);
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            mapManager.ApplyMapJson(req.downloadHandler.text);
        else
        {
            Debug.LogError($"[ApiLoader] Gagal baca {SAMPLE_FILE}: {req.error}");
            OnLoadError?.Invoke(req.error);
        }
    }
}
