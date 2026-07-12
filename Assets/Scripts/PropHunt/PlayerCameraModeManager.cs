using UnityEngine;

public enum PlayerCameraMode
{
    HumanFPS,
    PropTPS,
    Spectator
}

public class PlayerCameraModeManager : MonoBehaviour
{
    [Header("Cameras")]
    public Camera fpsCamera;
    public Camera tpsCamera;
    public Camera spectatorCamera;

    [Header("Roots")]
    public Transform tpsCameraRoot;
    public Transform spectatorCameraRoot;

    public PlayerCameraMode CurrentMode { get; private set; } = PlayerCameraMode.HumanFPS;

    private void Awake()
    {
        ResolveMissingCameras();
        SetMode(PlayerCameraMode.HumanFPS);
    }

    private void ResolveMissingCameras()
    {
        if (fpsCamera == null)
        {
            fpsCamera = FindNamedCameraInChildren("mainCamera");
        }

        if (fpsCamera == null)
        {
            fpsCamera = FindNamedCameraInChildren("MainCamera");
        }

        if (fpsCamera == null)
        {
            fpsCamera = Camera.main;
        }
    }

    public void SetMode(PlayerCameraMode mode)
    {
        CurrentMode = mode;

        SetCameraActive(fpsCamera, mode == PlayerCameraMode.HumanFPS);
        SetCameraActive(tpsCamera, mode == PlayerCameraMode.PropTPS);
        SetCameraActive(spectatorCamera, mode == PlayerCameraMode.Spectator);

        Debug.Log($"PlayerCameraModeManager: switched camera to {mode}.");
    }

    private static void SetCameraActive(Camera targetCamera, bool active)
    {
        if (targetCamera == null)
        {
            return;
        }

        targetCamera.gameObject.SetActive(active);

        AudioListener listener = targetCamera.GetComponent<AudioListener>();
        if (listener != null)
        {
            listener.enabled = active;
        }
    }

    private Camera FindNamedCameraInChildren(string cameraName)
    {
        foreach (Camera camera in GetComponentsInChildren<Camera>(true))
        {
            if (camera.name == cameraName)
            {
                return camera;
            }
        }

        return null;
    }
}
