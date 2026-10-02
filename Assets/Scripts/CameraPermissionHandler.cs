using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

#if UNITY_IOS
using UnityEngine.iOS;
#endif

/// <summary>
/// Memastikan izin kamera diberikan sebelum ARSession dimulai.
/// Attach ke GameObject yang sama dengan ARSession.
/// </summary>
[RequireComponent(typeof(ARSession))]
public class CameraPermissionHandler : MonoBehaviour
{
    private ARSession arSession;

    void Awake()
    {
        arSession = GetComponent<ARSession>();

        // Nonaktifkan ARSession dulu — aktifkan setelah izin diberikan
        arSession.enabled = false;

        StartCoroutine(RequestCameraAndStart());
    }

    IEnumerator RequestCameraAndStart()
    {
#if UNITY_IOS && !UNITY_EDITOR
        // Cek apakah izin kamera sudah diberikan
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            Debug.Log("[CameraPermission] Meminta izin kamera...");
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        }

        if (Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            Debug.Log("[CameraPermission] Izin kamera diberikan. Memulai ARSession...");
            arSession.enabled = true;
        }
        else
        {
            Debug.LogError("[CameraPermission] Izin kamera DITOLAK. AR tidak bisa berjalan.");
        }
#else
        // Di Editor atau Android, langsung aktifkan
        yield return null;
        arSession.enabled = true;
#endif
    }
}
