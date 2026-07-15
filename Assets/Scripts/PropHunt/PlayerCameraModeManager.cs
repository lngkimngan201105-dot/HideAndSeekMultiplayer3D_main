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

    [Header("Prop Third Person Camera")]
    public float cameraDistance = 4f;
    public float cameraHeight = 2.5f;
    [SerializeField] private float propLookHeight = 1f;
    public float cameraFollowSpeed = 10f;
    public float wallPadding = 0.2f;
    public LayerMask cameraCollisionMask = ~0;

    [Header("Prop Camera Presets")]
    public float nearCameraDistance = 4f;
    public float nearCameraHeight = 2.5f;
    public float farCameraDistance = 7f;
    public float farCameraHeight = 3.5f;

    public PlayerCameraMode CurrentMode { get; private set; } = PlayerCameraMode.HumanFPS;
    public bool IsPropCameraFar { get; private set; }

    private Transform _propTarget;
    private bool _logDiagnosticsNextLateUpdate;

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

    public void SetPropTarget(Transform target)
    {
        _propTarget = target;
        _logDiagnosticsNextLateUpdate = target != null;

        if (_propTarget == null || tpsCamera == null)
        {
            return;
        }

        Vector3 cloneCenter = GetPropBoundsCenter();
        Vector3 targetPoint = GetPlayerTargetPoint();
        Vector3 desiredPosition = GetDesiredPropCameraPosition();
        tpsCamera.transform.position = ClampCameraHeight(
            ResolveCameraCollision(targetPoint, desiredPosition)
        );

        Vector3 lookDirection = targetPoint - tpsCamera.transform.position;
        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            tpsCamera.transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
        }

        LogPropCameraDiagnostics(cloneCenter, targetPoint);
    }

    public void ClearPropTarget()
    {
        _propTarget = null;
        _logDiagnosticsNextLateUpdate = false;
    }

    public void TogglePropCameraDistance()
    {
        SetPropCameraFar(!IsPropCameraFar);
    }

    public void SetPropCameraFar(bool useFarPreset)
    {
        IsPropCameraFar = useFarPreset;
        cameraDistance = useFarPreset ? farCameraDistance : nearCameraDistance;
        cameraHeight = useFarPreset ? farCameraHeight : nearCameraHeight;
        _logDiagnosticsNextLateUpdate = _propTarget != null;
    }

    private void LateUpdate()
    {
        if (CurrentMode != PlayerCameraMode.PropTPS || _propTarget == null || tpsCamera == null)
        {
            return;
        }

        Vector3 cloneCenter = GetPropBoundsCenter();
        Vector3 targetPoint = GetPlayerTargetPoint();
        Vector3 desiredPosition = GetDesiredPropCameraPosition();

        desiredPosition = ResolveCameraCollision(targetPoint, desiredPosition);

        Transform cameraTransform = tpsCamera.transform;
        cameraTransform.position = Vector3.Lerp(
            cameraTransform.position,
            desiredPosition,
            Mathf.Clamp01(cameraFollowSpeed * Time.deltaTime)
        );
        cameraTransform.position = ClampCameraHeight(cameraTransform.position);

        Vector3 lookDirection = targetPoint - cameraTransform.position;
        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            cameraTransform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
        }

        if (_logDiagnosticsNextLateUpdate)
        {
            LogPropCameraDiagnostics(cloneCenter, targetPoint);
            _logDiagnosticsNextLateUpdate = false;
        }
    }

    private Vector3 GetPropBoundsCenter()
    {
        if (_propTarget == null)
        {
            return transform.position + Vector3.up;
        }

        Renderer[] renderers = _propTarget.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return transform.position + Vector3.up;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds.center;
    }

    private Vector3 GetPlayerTargetPoint()
    {
        return transform.position + Vector3.up * propLookHeight;
    }

    private Vector3 GetDesiredPropCameraPosition()
    {
        Vector3 desiredPosition =
            transform.position
            - transform.forward * cameraDistance
            + Vector3.up * cameraHeight;
        return ClampCameraHeight(desiredPosition);
    }

    private Vector3 ClampCameraHeight(Vector3 cameraPosition)
    {
        float minimumCameraY = transform.position.y + 1.2f;
        float maximumCameraY = transform.position.y + 4f;
        cameraPosition.y = Mathf.Clamp(cameraPosition.y, minimumCameraY, maximumCameraY);
        return cameraPosition;
    }

    private Vector3 ResolveCameraCollision(Vector3 targetPoint, Vector3 desiredPosition)
    {
        Vector3 ray = desiredPosition - targetPoint;
        float rayDistance = ray.magnitude;
        if (rayDistance <= 0.0001f)
        {
            return desiredPosition;
        }

        Vector3 rayDirection = ray / rayDistance;
        RaycastHit[] hits = Physics.RaycastAll(
            targetPoint,
            rayDirection,
            rayDistance,
            cameraCollisionMask,
            QueryTriggerInteraction.Ignore
        );

        float closestDistance = rayDistance;
        bool foundWall = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == null || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                foundWall = true;
            }
        }

        if (!foundWall)
        {
            return desiredPosition;
        }

        return targetPoint + rayDirection * Mathf.Max(0f, closestDistance - wallPadding);
    }

    private void LogPropCameraDiagnostics(Vector3 cloneCenter, Vector3 targetPoint)
    {
        Vector3 cameraPosition = tpsCamera.transform.position;
        Vector3 directionToTarget = targetPoint - cameraPosition;
        float distanceToTarget = directionToTarget.magnitude;
        Vector3 normalizedDirection = distanceToTarget > 0.0001f
            ? directionToTarget / distanceToTarget
            : tpsCamera.transform.forward;
        float facingDot = Vector3.Dot(tpsCamera.transform.forward, normalizedDirection);

        Debug.Log($"Player position: {transform.position}");
        Debug.Log($"Clone center: {cloneCenter}");
        Debug.Log($"TPS Camera position: {cameraPosition}");
        Debug.Log($"TPS Camera Y: {cameraPosition.y}");
        Debug.Log($"Camera target point: {targetPoint}");
        Debug.Log($"Camera target Y: {targetPoint.y}");
        Debug.Log($"TPS Camera forward: {tpsCamera.transform.forward}");
        Debug.Log($"Camera distance to target: {distanceToTarget}");
        Debug.Log($"Camera facing target dot: {facingDot}");
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
