using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class HiderSpectatorController : MonoBehaviour
{
    private const string FollowingStatus = "ĐÃ BỊ LOẠI — ĐANG THEO DÕI";
    private const string NoTargetStatus = "KHÔNG CÒN HIDER ĐỂ THEO DÕI";

    [Header("References")]
    [SerializeField] private HiderEliminationController owner;
    [SerializeField] private HiderRosterManager rosterManager;
    [SerializeField] private PlayerCameraModeManager cameraModeManager;
    [SerializeField] private Camera spectatorCamera;
    [SerializeField] private SpectatorCameraController legacyFreeCameraController;

    [Header("Follow Camera")]
    [SerializeField] private float targetHeight = 1.8f;
    [SerializeField] private float minimumDistance = 3f;
    [SerializeField] private float maximumDistance = 8f;
    [SerializeField] private float cameraDistance = 5f;
    [SerializeField] private float lookSensitivity = 0.12f;
    [SerializeField] private float zoomSensitivity = 0.004f;
    [SerializeField] private float collisionRadius = 0.25f;
    [SerializeField] private float collisionPadding = 0.12f;
    [SerializeField] private LayerMask collisionMask = Physics.DefaultRaycastLayers;

    private readonly RaycastHit[] collisionHits = new RaycastHit[24];
    private HiderEliminationController currentTarget;
    private int targetIndex = -1;
    private float yaw;
    private float pitch = 15f;
    private bool isSpectating;
    private Vector3 safeDeathViewPosition;
    private Quaternion safeDeathViewRotation = Quaternion.identity;
    private bool hasPreparedDeathView;

    public bool IsSpectating => isSpectating;
    public HiderEliminationController CurrentTarget => currentTarget;
    public string CurrentStatusText => currentTarget != null ? FollowingStatus : NoTargetStatus;
    public event Action<HiderEliminationController> TargetChanged;
    public event Action<string> StatusTextChanged;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToRoster();
    }

    private void OnDisable()
    {
        UnsubscribeFromRoster();
        if (isSpectating)
        {
            ExitSpectator();
        }
    }

    public void Configure(
        HiderEliminationController configuredOwner,
        HiderRosterManager configuredRoster,
        PlayerCameraModeManager configuredCameraModeManager,
        Camera configuredSpectatorCamera,
        SpectatorCameraController configuredLegacyController)
    {
        if (isActiveAndEnabled)
        {
            UnsubscribeFromRoster();
        }

        owner = configuredOwner;
        rosterManager = configuredRoster;
        cameraModeManager = configuredCameraModeManager;
        spectatorCamera = configuredSpectatorCamera;
        legacyFreeCameraController = configuredLegacyController;

        if (isActiveAndEnabled)
        {
            SubscribeToRoster();
        }
    }

    public void EnterSpectator(Vector3 eliminatedPosition)
    {
        ResolveReferences();
        if (!hasPreparedDeathView)
        {
            CaptureDeathView(eliminatedPosition);
        }
        hasPreparedDeathView = false;
        isSpectating = true;
        if (legacyFreeCameraController != null)
        {
            legacyFreeCameraController.enabled = false;
        }

        cameraModeManager?.SetMode(PlayerCameraMode.Spectator);
        SelectClosestAliveTarget(eliminatedPosition);
        ApplyCameraImmediately();
    }

    public void PrepareDeathView(Vector3 eliminatedPosition)
    {
        ResolveReferences();
        CaptureDeathView(eliminatedPosition);
        hasPreparedDeathView = true;
    }

    public void ExitSpectator()
    {
        if (!isSpectating && currentTarget == null)
        {
            return;
        }

        isSpectating = false;
        SetTarget(null, -1);
        if (legacyFreeCameraController != null)
        {
            legacyFreeCameraController.enabled = false;
        }

        cameraModeManager?.SetMode(PlayerCameraMode.HumanFPS);
    }

    private void LateUpdate()
    {
        if (!isSpectating)
        {
            return;
        }

        if (!IsValidAliveTarget(currentTarget))
        {
            SelectClosestAliveTarget(safeDeathViewPosition);
        }

        HandleTargetCycleInput();
        HandleOrbitAndZoomInput();
        UpdateCameraPose();
    }

    private void HandleRosterChanged(int aliveCount, int totalCount)
    {
        if (!isSpectating)
        {
            return;
        }

        if (!IsValidAliveTarget(currentTarget))
        {
            SelectClosestAliveTarget(safeDeathViewPosition);
        }
        else
        {
            StatusTextChanged?.Invoke(CurrentStatusText);
        }
    }

    private void SelectClosestAliveTarget(Vector3 fromPosition)
    {
        HiderEliminationController closest = null;
        int closestIndex = -1;
        float closestDistance = float.PositiveInfinity;
        if (rosterManager != null)
        {
            IReadOnlyList<HiderEliminationController> alive = rosterManager.AliveHiders;
            for (int i = 0; i < alive.Count; i++)
            {
                HiderEliminationController candidate = alive[i];
                if (!IsValidAliveTarget(candidate))
                {
                    continue;
                }

                float distance = (candidate.transform.position - fromPosition).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closest = candidate;
                    closestIndex = i;
                    closestDistance = distance;
                }
            }
        }

        SetTarget(closest, closestIndex);
    }

    private void CycleTarget(int direction)
    {
        if (rosterManager == null || rosterManager.AliveHiders.Count == 0)
        {
            SetTarget(null, -1);
            return;
        }

        int count = rosterManager.AliveHiders.Count;
        int startIndex = targetIndex < 0 ? 0 : targetIndex;
        for (int step = 1; step <= count; step++)
        {
            int candidateIndex = (startIndex + direction * step + count * 2) % count;
            HiderEliminationController candidate = rosterManager.AliveHiders[candidateIndex];
            if (IsValidAliveTarget(candidate))
            {
                SetTarget(candidate, candidateIndex);
                return;
            }
        }

        SetTarget(null, -1);
    }

    private void SetTarget(HiderEliminationController target, int index)
    {
        bool changed = currentTarget != target;
        currentTarget = target;
        targetIndex = index;
        if (changed)
        {
            TargetChanged?.Invoke(currentTarget);
        }

        StatusTextChanged?.Invoke(CurrentStatusText);
    }

    private bool IsValidAliveTarget(HiderEliminationController target)
    {
        return target != null && target != owner && target.Health != null && target.Health.IsAlive &&
               target.TransformSystem != null && target.TransformSystem.playerRole == PlayerRole.Hider;
    }

    private void CaptureDeathView(Vector3 fallbackPosition)
    {
        Camera source = null;
        if (cameraModeManager != null)
        {
            if (cameraModeManager.tpsCamera != null && cameraModeManager.tpsCamera.gameObject.activeInHierarchy)
                source = cameraModeManager.tpsCamera;
            else if (cameraModeManager.fpsCamera != null && cameraModeManager.fpsCamera.gameObject.activeInHierarchy)
                source = cameraModeManager.fpsCamera;
        }

        if (source != null)
        {
            safeDeathViewPosition = source.transform.position;
            safeDeathViewRotation = source.transform.rotation;
        }
        else
        {
            safeDeathViewPosition = fallbackPosition + Vector3.up * targetHeight - transform.forward * cameraDistance;
            safeDeathViewRotation = Quaternion.LookRotation(fallbackPosition + Vector3.up * targetHeight - safeDeathViewPosition);
        }

        Vector3 angles = safeDeathViewRotation.eulerAngles;
        yaw = angles.y;
        pitch = angles.x > 180f ? angles.x - 360f : angles.x;
    }

    private void ApplyCameraImmediately()
    {
        if (spectatorCamera == null)
        {
            return;
        }

        spectatorCamera.transform.SetPositionAndRotation(safeDeathViewPosition, safeDeathViewRotation);
        UpdateCameraPose();
    }

    private void UpdateCameraPose()
    {
        if (spectatorCamera == null)
        {
            return;
        }

        if (currentTarget == null)
        {
            spectatorCamera.transform.SetPositionAndRotation(safeDeathViewPosition, safeDeathViewRotation);
            return;
        }

        Vector3 focus = currentTarget.GetSpectatorFocusPosition(targetHeight);
        Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desiredPosition = focus - orbitRotation * Vector3.forward * cameraDistance;
        Vector3 ray = desiredPosition - focus;
        float rayDistance = ray.magnitude;
        if (rayDistance > 0.001f)
        {
            int hitCount = Physics.SphereCastNonAlloc(
                focus,
                collisionRadius,
                ray / rayDistance,
                collisionHits,
                rayDistance,
                collisionMask,
                QueryTriggerInteraction.Ignore);
            float safeDistance = rayDistance;
            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = collisionHits[i].collider;
                if (hitCollider == null || hitCollider.transform.IsChildOf(currentTarget.transform))
                {
                    continue;
                }

                safeDistance = Mathf.Min(safeDistance, Mathf.Max(0.2f,
                    collisionHits[i].distance - collisionPadding));
            }

            desiredPosition = focus + ray.normalized * safeDistance;
        }

        spectatorCamera.transform.position = desiredPosition;
        Vector3 lookDirection = focus - desiredPosition;
        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            spectatorCamera.transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
        }
    }

    private void HandleOrbitAndZoomInput()
    {
        Vector2 look = Vector2.zero;
        float scroll = 0f;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            look = Mouse.current.delta.ReadValue();
            scroll = Mouse.current.scroll.ReadValue().y;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (look == Vector2.zero)
        {
            look = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 8f;
        }
        if (Mathf.Approximately(scroll, 0f)) scroll = Input.mouseScrollDelta.y * 120f;
#endif
        yaw += look.x * lookSensitivity;
        pitch = Mathf.Clamp(pitch - look.y * lookSensitivity, -25f, 75f);
        cameraDistance = Mathf.Clamp(cameraDistance - scroll * zoomSensitivity, minimumDistance, maximumDistance);
    }

    private void HandleTargetCycleInput()
    {
        bool previous = false;
        bool next = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            previous = Keyboard.current.qKey.wasPressedThisFrame;
            next = Keyboard.current.eKey.wasPressedThisFrame;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        previous |= Input.GetKeyDown(KeyCode.Q);
        next |= Input.GetKeyDown(KeyCode.E);
#endif
        if (previous) CycleTarget(-1);
        else if (next) CycleTarget(1);
    }

    private void SubscribeToRoster()
    {
        if (rosterManager == null)
        {
            return;
        }

        rosterManager.AliveCountChanged -= HandleRosterChanged;
        rosterManager.AliveCountChanged += HandleRosterChanged;
    }

    private void UnsubscribeFromRoster()
    {
        if (rosterManager != null)
        {
            rosterManager.AliveCountChanged -= HandleRosterChanged;
        }
    }

    private void ResolveReferences()
    {
        if (owner == null) owner = GetComponent<HiderEliminationController>();
        if (rosterManager == null) rosterManager = FindObjectOfType<HiderRosterManager>();
        if (cameraModeManager == null) cameraModeManager = GetComponent<PlayerCameraModeManager>();
        if (spectatorCamera == null && cameraModeManager != null)
            spectatorCamera = cameraModeManager.spectatorCamera;
        if (legacyFreeCameraController == null && spectatorCamera != null)
            legacyFreeCameraController = spectatorCamera.GetComponent<SpectatorCameraController>();
    }
}
