using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum PlayerCameraMode
{
    HumanFPS,
    PropTPS,
    GhostCamera,
    Spectator
}

public enum AdaptivePropCameraMode
{
    GroundTPS,
    WallTPS
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

    [Header("Adaptive Prop Distance")]
    [SerializeField] private float minimumPropCameraDistance = 3.2f;
    [SerializeField] private float maximumPropCameraDistance = 7f;
    [SerializeField] private float propDistanceMultiplier = 1.6f;
    [SerializeField] private float propDistancePadding = 0.6f;

    [Header("Adaptive Prop Height")]
    [SerializeField] private float minimumPropCameraHeight = 1.4f;
    [SerializeField] private float maximumPropCameraHeight = 4.5f;
    [SerializeField] private float propHeightMultiplier = 0.45f;
    [SerializeField] private float lookTargetVerticalOffset = 0.1f;

    [Header("Prop Zoom")]
    [SerializeField] private float zoomScrollSensitivity = 1.5f;
    [SerializeField] private float minimumUserZoomOffset = -1.2f;
    [SerializeField] private float maximumUserZoomOffset = 1.5f;

    [Header("Prop Orbit")]
    [SerializeField] private float orbitLookSensitivity = 1f;
    [SerializeField] private float minimumPitch = -25f;
    [SerializeField] private float maximumPitch = 70f;

    [Header("Wall TPS")]
    [SerializeField] private float minimumCameraWallSideDistance = 0.3f;
    [SerializeField] private float wallNormalSmoothingSpeed = 12f;

    [Header("TPS Collision")]
    public LayerMask cameraCollisionMask = Physics.DefaultRaycastLayers;
    [SerializeField] private float cameraCollisionRadius = 0.25f;
    [SerializeField] private float cameraCollisionPadding = 0.15f;
    [SerializeField] private float minimumCollisionDistance = 0.45f;
    [SerializeField] private float minimumDistanceFromPropSurface = 0.35f;

    [Header("TPS Smoothing")]
    [SerializeField] private float positionSmoothTime = 0.08f;
    [SerializeField] private float distanceSmoothTime = 0.15f;
    [SerializeField] private float heightSmoothTime = 0.15f;
    [SerializeField] private float rotationSmoothSpeed = 15f;

    [Header("Local Prop Transparency Fallback")]
    [SerializeField] private float fadeStartDistance = 1.2f;
    [SerializeField] private float fadeFullDistance = 0.45f;
    [SerializeField, Range(0f, 1f)] private float minimumLocalPropAlpha = 0.3f;

    [Header("Legacy Prop Camera Compatibility")]
    public float cameraDistance = 4f;
    public float cameraHeight = 2.5f;
    public float cameraFollowSpeed = 10f;
    public float wallPadding = 0.2f;
    public float nearCameraDistance = 4f;
    public float nearCameraHeight = 2.5f;
    public float farCameraDistance = 7f;
    public float farCameraHeight = 3.5f;

    [Header("Ghost Camera")]
    [SerializeField] private float ghostMoveSpeed = 5f;
    [SerializeField] private float ghostLookSensitivity = 1f;
    [SerializeField] private float ghostMaxDistance = 10f;
    [SerializeField] private float ghostCollisionRadius = 0.2f;
    [SerializeField] private LayerMask ghostCollisionMask = ~0;

    public PlayerCameraMode CurrentMode { get; private set; } = PlayerCameraMode.HumanFPS;
    public AdaptivePropCameraMode CurrentAdaptiveMode { get; private set; } =
        AdaptivePropCameraMode.GroundTPS;
    public bool IsPropCameraFar { get; private set; }
    public Vector3 GhostAnchorPosition { get; private set; }

    private readonly List<Renderer> _cachedPropRenderers = new List<Renderer>();
    private readonly List<FadeRendererState> _fadeRendererStates =
        new List<FadeRendererState>();

    private Transform _propTarget;
    private Transform _ghostPlayerRoot;
    private PropTransformSystem _propTransformSystem;
    private StarterAssetsInputs _input;
    private FirstPersonController _firstPersonController;
    private float _ghostYaw;
    private float _ghostPitch;
    private float _orbitYaw;
    private float _orbitPitch = 10f;
    private float _userZoomOffset;
    private float _currentDistance = 4f;
    private float _currentHeight = 2.5f;
    private float _distanceVelocity;
    private float _heightVelocity;
    private Vector3 _positionVelocity;
    private Vector3 _smoothedWallNormal;
    private bool _orbitInitialized;
    private bool _forceSafeNextLateUpdate;
    private bool _fadeUnavailableWarningLogged;
    private bool _cameraForcedOutLoggedForCurrentProp;
    private bool _cameraSystemEnabled = true;

    public bool IsCameraSystemEnabled => _cameraSystemEnabled;

    private sealed class FadeRendererState
    {
        public Renderer Renderer;
        public int ColorPropertyId;
        public Color OriginalColor;
        public MaterialPropertyBlock PropertyBlock;
    }

    private void Awake()
    {
        _propTransformSystem = GetComponent<PropTransformSystem>();
        _input = GetComponent<StarterAssetsInputs>();
        _firstPersonController = GetComponent<FirstPersonController>();
        ResolveMissingCameras();
        SetMode(PlayerCameraMode.HumanFPS);
    }

    private void OnDisable()
    {
        ResetAdaptiveCamera();
        if (_firstPersonController != null)
        {
            _firstPersonController.SetCameraLookLocked(false);
        }
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
        PlayerCameraMode previousMode = CurrentMode;
        CurrentMode = mode;

        SetCameraActive(fpsCamera, _cameraSystemEnabled && mode == PlayerCameraMode.HumanFPS);
        SetCameraActive(
            tpsCamera,
            _cameraSystemEnabled &&
            (mode == PlayerCameraMode.PropTPS || mode == PlayerCameraMode.GhostCamera)
        );
        SetCameraActive(spectatorCamera, _cameraSystemEnabled && mode == PlayerCameraMode.Spectator);

        if (_firstPersonController != null)
        {
            _firstPersonController.SetCameraLookLocked(mode != PlayerCameraMode.HumanFPS);
        }

        if (mode != PlayerCameraMode.GhostCamera)
        {
            _ghostPlayerRoot = null;
        }

        if (mode == PlayerCameraMode.PropTPS)
        {
            if (!_orbitInitialized)
            {
                _orbitYaw = transform.eulerAngles.y;
                _orbitPitch = 10f;
                _orbitInitialized = true;
            }

            if (_propTarget != null && _cachedPropRenderers.Count == 0)
            {
                RefreshCurrentPropCamera();
            }

            _forceSafeNextLateUpdate = true;
        }
        else
        {
            RestorePropAlpha();
            if (mode == PlayerCameraMode.HumanFPS || mode == PlayerCameraMode.Spectator)
            {
                ResetAdaptiveCamera();
            }
        }

        if (previousMode != mode)
        {
            Debug.Log($"PlayerCameraModeManager: switched camera to {mode}.");
        }
    }

    public void SetCameraSystemEnabled(bool enabled)
    {
        _cameraSystemEnabled = enabled;
        if (!enabled)
        {
            SetCameraActive(fpsCamera, false);
            SetCameraActive(tpsCamera, false);
            SetCameraActive(spectatorCamera, false);
            if (_firstPersonController != null)
            {
                _firstPersonController.SetCameraLookLocked(true);
            }
            return;
        }

        SetMode(CurrentMode);
    }

    public bool BeginGhostCamera(Vector3 anchorPosition, Transform playerRoot)
    {
        if (tpsCamera == null || playerRoot == null)
        {
            return false;
        }

        RestorePropAlpha();
        GhostAnchorPosition = anchorPosition;
        _ghostPlayerRoot = playerRoot;

        Vector3 initialOffset = tpsCamera.transform.position - GhostAnchorPosition;
        if (initialOffset.magnitude > ghostMaxDistance)
        {
            tpsCamera.transform.position =
                GhostAnchorPosition + initialOffset.normalized * ghostMaxDistance;
        }

        Vector3 angles = tpsCamera.transform.eulerAngles;
        _ghostYaw = angles.y;
        _ghostPitch = angles.x > 180f ? angles.x - 360f : angles.x;
        _ghostPitch = Mathf.Clamp(_ghostPitch, -80f, 80f);

        SetMode(PlayerCameraMode.GhostCamera);
        return true;
    }

    public void SetPropTarget(Transform target)
    {
        if (_propTarget == target && target != null)
        {
            RefreshCurrentPropCamera();
            return;
        }

        RestorePropAlpha();
        _propTarget = target;
        RefreshCurrentPropCamera();
    }

    public void ClearPropTarget()
    {
        RestorePropAlpha();
        _propTarget = null;
        ResetAdaptiveCamera();
    }

    public void TogglePropCameraDistance()
    {
        SetPropCameraFar(!IsPropCameraFar);
    }

    public void SetPropCameraFar(bool useFarPreset)
    {
        IsPropCameraFar = useFarPreset;
        RefreshCurrentPropCamera();
    }

    public bool TryGetCurrentPropWorldBounds(out Bounds combinedBounds)
    {
        combinedBounds = default;
        bool initialized = false;
        foreach (Renderer renderer in _cachedPropRenderers)
        {
            if (!IsValidPropRenderer(renderer))
            {
                continue;
            }

            if (!initialized)
            {
                combinedBounds = renderer.bounds;
                initialized = true;
            }
            else
            {
                combinedBounds.Encapsulate(renderer.bounds);
            }
        }

        return initialized;
    }

    public void RefreshCurrentPropCamera()
    {
        RestorePropAlpha();
        _cachedPropRenderers.Clear();
        _fadeRendererStates.Clear();
        _fadeUnavailableWarningLogged = false;
        _cameraForcedOutLoggedForCurrentProp = false;

        if (_propTarget == null)
        {
            return;
        }

        bool allRenderersSupportFade = true;
        foreach (Renderer renderer in _propTarget.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsValidPropRenderer(renderer))
            {
                continue;
            }

            _cachedPropRenderers.Add(renderer);
            allRenderersSupportFade &= TryCacheFadeRenderer(renderer);
        }

        if (!allRenderersSupportFade)
        {
            _fadeRendererStates.Clear();
        }

        _forceSafeNextLateUpdate = true;
        if (TryGetCurrentPropWorldBounds(out Bounds bounds))
        {
            CalculateDesiredDistanceAndHeight(
                bounds,
                out float desiredDistance,
                out float desiredHeight
            );
            Debug.Log(
                $"Adaptive TPS: Prop changed.\n" +
                $"Bounds size={bounds.size}.\n" +
                $"Desired distance={desiredDistance:0.00}.\n" +
                $"Desired height={desiredHeight:0.00}."
            );
        }
    }

    public void ForceCameraToSafePosition()
    {
        if (CurrentMode != PlayerCameraMode.PropTPS ||
            tpsCamera == null ||
            !TryGetCurrentPropWorldBounds(out Bounds bounds))
        {
            return;
        }

        UpdateAdaptiveMode(true);
        CalculateDesiredDistanceAndHeight(bounds, out _currentDistance, out _currentHeight);
        _distanceVelocity = 0f;
        _heightVelocity = 0f;
        _positionVelocity = Vector3.zero;

        Vector3 lookTarget = GetLookTarget(bounds);
        Vector3 safePosition = CalculateCollisionSafePosition(
            bounds,
            lookTarget,
            _currentDistance,
            _currentHeight
        );
        tpsCamera.transform.position = safePosition;
        Vector3 lookDirection = lookTarget - safePosition;
        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            tpsCamera.transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
        }

        UpdatePropFade(bounds, safePosition);
        _forceSafeNextLateUpdate = false;
    }

    public void ResetAdaptiveCamera()
    {
        RestorePropAlpha();
        _cachedPropRenderers.Clear();
        _fadeRendererStates.Clear();
        _distanceVelocity = 0f;
        _heightVelocity = 0f;
        _positionVelocity = Vector3.zero;
        _smoothedWallNormal = Vector3.zero;
        _forceSafeNextLateUpdate = false;
        _fadeUnavailableWarningLogged = false;
        _cameraForcedOutLoggedForCurrentProp = false;
        _orbitInitialized = false;
        _userZoomOffset = 0f;
        IsPropCameraFar = false;
        _propTarget = null;
    }

    private void LateUpdate()
    {
        if (!_cameraSystemEnabled)
        {
            RestorePropAlpha();
            return;
        }

        if (CurrentMode == PlayerCameraMode.GhostCamera)
        {
            UpdateGhostCamera();
            return;
        }

        if (CurrentMode != PlayerCameraMode.PropTPS ||
            _propTarget == null ||
            tpsCamera == null ||
            _propTransformSystem == null ||
            !_propTransformSystem.IsDisguised ||
            _propTransformSystem.IsGhostCameraActive ||
            _propTransformSystem.IsEliminated)
        {
            RestorePropAlpha();
            return;
        }

        if (!TryGetCurrentPropWorldBounds(out Bounds bounds))
        {
            return;
        }

        UpdateOrbitInput();
        UpdateAdaptiveMode(true);
        CalculateDesiredDistanceAndHeight(
            bounds,
            out float desiredDistance,
            out float desiredHeight
        );

        _currentDistance = Mathf.SmoothDamp(
            _currentDistance,
            desiredDistance,
            ref _distanceVelocity,
            distanceSmoothTime
        );
        _currentHeight = Mathf.SmoothDamp(
            _currentHeight,
            desiredHeight,
            ref _heightVelocity,
            heightSmoothTime
        );
        cameraDistance = _currentDistance;
        cameraHeight = _currentHeight;

        if (_forceSafeNextLateUpdate)
        {
            ForceCameraToSafePosition();
            return;
        }

        Vector3 lookTarget = GetLookTarget(bounds);
        Vector3 safePosition = CalculateCollisionSafePosition(
            bounds,
            lookTarget,
            _currentDistance,
            _currentHeight
        );
        Transform cameraTransform = tpsCamera.transform;
        bool currentPositionUnsafe =
            IsCameraInsideOrTooCloseToProp(cameraTransform.position, bounds) ||
            IsCameraOverlappingEnvironment(cameraTransform.position);
        Vector3 nextPosition = currentPositionUnsafe
            ? safePosition
            : Vector3.SmoothDamp(
                cameraTransform.position,
                safePosition,
                ref _positionVelocity,
                positionSmoothTime
            );

        nextPosition = EnforceWallHalfSpace(nextPosition);
        nextPosition = ResolveCameraCollision(lookTarget, nextPosition);
        if (IsCameraInsideOrTooCloseToProp(nextPosition, bounds) &&
            !IsCameraInsideOrTooCloseToProp(safePosition, bounds))
        {
            nextPosition = safePosition;
            _positionVelocity = Vector3.zero;
        }

        cameraTransform.position = nextPosition;
        Vector3 lookDirection = lookTarget - nextPosition;
        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            cameraTransform.rotation = Quaternion.Slerp(
                cameraTransform.rotation,
                desiredRotation,
                Mathf.Clamp01(rotationSmoothSpeed * Time.deltaTime)
            );
        }

        UpdatePropFade(bounds, nextPosition);
    }

    private void UpdateAdaptiveMode(bool logChange)
    {
        AdaptivePropCameraMode nextMode =
            _propTransformSystem != null && _propTransformSystem.IsWallAttached
                ? AdaptivePropCameraMode.WallTPS
                : AdaptivePropCameraMode.GroundTPS;
        if (nextMode == CurrentAdaptiveMode)
        {
            if (nextMode == AdaptivePropCameraMode.WallTPS &&
                _propTransformSystem.WallNormal.sqrMagnitude > 0.5f)
            {
                _smoothedWallNormal = _smoothedWallNormal.sqrMagnitude > 0.5f
                    ? Vector3.Slerp(
                        _smoothedWallNormal,
                        _propTransformSystem.WallNormal.normalized,
                        Mathf.Clamp01(wallNormalSmoothingSpeed * Time.deltaTime)
                    ).normalized
                    : _propTransformSystem.WallNormal.normalized;
            }

            return;
        }

        AdaptivePropCameraMode previousMode = CurrentAdaptiveMode;
        CurrentAdaptiveMode = nextMode;
        _positionVelocity = Vector3.zero;
        if (nextMode == AdaptivePropCameraMode.WallTPS)
        {
            _smoothedWallNormal = _propTransformSystem.WallNormal.normalized;
        }
        else
        {
            _smoothedWallNormal = Vector3.zero;
        }

        if (logChange)
        {
            Debug.Log($"Adaptive TPS: Mode changed: {previousMode} -> {nextMode}.");
        }
    }

    private void UpdateOrbitInput()
    {
        if (_input == null)
        {
            return;
        }

        Vector2 lookInput = _input.look;
        _orbitYaw += lookInput.x * orbitLookSensitivity;
        _orbitPitch -= lookInput.y * orbitLookSensitivity;
        _orbitPitch = Mathf.Clamp(_orbitPitch, minimumPitch, maximumPitch);

        float normalizedScroll = ReadNormalizedZoomScroll();
        if (Mathf.Abs(normalizedScroll) > 0.01f)
        {
            _userZoomOffset -= normalizedScroll * zoomScrollSensitivity * 0.25f;
            _userZoomOffset = Mathf.Clamp(
                _userZoomOffset,
                minimumUserZoomOffset,
                maximumUserZoomOffset
            );
        }
    }

    private void CalculateDesiredDistanceAndHeight(
        Bounds bounds,
        out float desiredDistance,
        out float desiredHeight)
    {
        float horizontalExtent = Mathf.Max(bounds.extents.x, bounds.extents.z);
        desiredDistance = Mathf.Clamp(
            horizontalExtent * propDistanceMultiplier +
            propDistancePadding +
            _userZoomOffset,
            minimumPropCameraDistance,
            maximumPropCameraDistance
        );
        desiredHeight = Mathf.Clamp(
            bounds.size.y * propHeightMultiplier + 0.6f,
            minimumPropCameraHeight,
            maximumPropCameraHeight
        );
    }

    private static float ReadNormalizedZoomScroll()
    {
        float scroll = 0f;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            scroll = Mouse.current.scroll.ReadValue().y;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        scroll = Input.mouseScrollDelta.y;
#endif
        return Mathf.Abs(scroll) > 0.01f ? Mathf.Sign(scroll) : 0f;
    }

    private Vector3 GetLookTarget(Bounds bounds)
    {
        return bounds.center + Vector3.up * lookTargetVerticalOffset;
    }

    private Vector3 CalculateCollisionSafePosition(
        Bounds bounds,
        Vector3 lookTarget,
        float distance,
        float height)
    {
        Quaternion orbitRotation = Quaternion.Euler(_orbitPitch, _orbitYaw, 0f);
        Vector3 orbitDirection = orbitRotation * Vector3.back;
        Vector3 desiredPosition = lookTarget + orbitDirection * distance + Vector3.up * height;
        desiredPosition = EnforceWallHalfSpace(desiredPosition);

        Vector3 collisionSafePosition = ResolveCameraCollision(lookTarget, desiredPosition);
        collisionSafePosition = TryPushCameraOutsideProp(
            collisionSafePosition,
            bounds,
            lookTarget,
            orbitDirection
        );
        collisionSafePosition = EnforceWallHalfSpace(collisionSafePosition);
        collisionSafePosition = ResolveCameraCollision(lookTarget, collisionSafePosition);

        bool needsRaisedFallback =
            IsCameraInsideOrTooCloseToProp(collisionSafePosition, bounds) ||
            Vector3.Distance(lookTarget, collisionSafePosition) < minimumCollisionDistance;
        if (needsRaisedFallback)
        {
            Vector3 raisedDirection = (orbitDirection + Vector3.up * 0.35f).normalized;
            Vector3 raisedCandidate = lookTarget + raisedDirection * distance + Vector3.up * height;
            raisedCandidate = EnforceWallHalfSpace(raisedCandidate);
            raisedCandidate = ResolveCameraCollision(lookTarget, raisedCandidate);
            raisedCandidate = TryPushCameraOutsideProp(
                raisedCandidate,
                bounds,
                lookTarget,
                raisedDirection
            );
            raisedCandidate = EnforceWallHalfSpace(raisedCandidate);
            raisedCandidate = ResolveCameraCollision(lookTarget, raisedCandidate);
            bool raisedCandidateIsSafe =
                !IsCameraInsideOrTooCloseToProp(raisedCandidate, bounds) &&
                !IsCameraOverlappingEnvironment(raisedCandidate);
            bool raisedCandidateGivesMoreRoom =
                Vector3.Distance(lookTarget, raisedCandidate) >
                Vector3.Distance(lookTarget, collisionSafePosition);
            if (raisedCandidateIsSafe &&
                (IsCameraInsideOrTooCloseToProp(collisionSafePosition, bounds) ||
                 raisedCandidateGivesMoreRoom))
            {
                collisionSafePosition = raisedCandidate;
            }
        }

        return collisionSafePosition;
    }

    private Vector3 EnforceWallHalfSpace(Vector3 candidatePosition)
    {
        if (CurrentAdaptiveMode != AdaptivePropCameraMode.WallTPS ||
            _propTransformSystem == null ||
            !_propTransformSystem.IsWallAttached ||
            _smoothedWallNormal.sqrMagnitude < 0.5f)
        {
            return candidatePosition;
        }

        Vector3 wallNormal = _smoothedWallNormal.normalized;
        Vector3 wallPoint = _propTransformSystem.WallHitPoint;
        float sideDistance = Vector3.Dot(candidatePosition - wallPoint, wallNormal);
        if (sideDistance < minimumCameraWallSideDistance)
        {
            candidatePosition +=
                wallNormal * (minimumCameraWallSideDistance - sideDistance);
        }

        return candidatePosition;
    }

    private Vector3 ResolveCameraCollision(Vector3 targetPoint, Vector3 desiredPosition)
    {
        Vector3 cast = desiredPosition - targetPoint;
        float castDistance = cast.magnitude;
        if (castDistance <= 0.0001f)
        {
            return desiredPosition;
        }

        RaycastHit[] hits = Physics.SphereCastAll(
            targetPoint,
            cameraCollisionRadius,
            cast / castDistance,
            castDistance,
            cameraCollisionMask,
            QueryTriggerInteraction.Ignore
        );
        float closestDistance = castDistance;
        foreach (RaycastHit hit in hits)
        {
            if (ShouldIgnoreCameraCollider(hit.collider))
            {
                continue;
            }

            closestDistance = Mathf.Min(closestDistance, hit.distance);
        }

        if (closestDistance >= castDistance)
        {
            return desiredPosition;
        }

        float maximumSafeDistance =
            Mathf.Max(0f, closestDistance - cameraCollisionPadding);
        float collisionDistance = Mathf.Max(
            maximumSafeDistance,
            minimumCollisionDistance
        );
        // If the minimum does not physically fit, penetration safety wins and the
        // caller tries the raised fallback instead.
        collisionDistance = Mathf.Min(collisionDistance, maximumSafeDistance);

        return targetPoint + cast.normalized * collisionDistance;
    }

    private bool ShouldIgnoreCameraCollider(Collider collider)
    {
        if (collider == null || collider.isTrigger)
        {
            return true;
        }

        Transform hitTransform = collider.transform;
        return hitTransform == transform ||
               hitTransform.IsChildOf(transform) ||
               (_propTransformSystem != null &&
                collider.GetComponentInParent<PropTransformSystem>() == _propTransformSystem);
    }

    private bool IsCameraOverlappingEnvironment(Vector3 cameraPosition)
    {
        foreach (Collider overlap in Physics.OverlapSphere(
                     cameraPosition,
                     cameraCollisionRadius,
                     cameraCollisionMask,
                     QueryTriggerInteraction.Ignore))
        {
            if (!ShouldIgnoreCameraCollider(overlap))
            {
                return true;
            }
        }

        return false;
    }

    private Vector3 TryPushCameraOutsideProp(
        Vector3 cameraPosition,
        Bounds propBounds,
        Vector3 lookTarget,
        Vector3 fallbackDirection)
    {
        if (!IsCameraInsideOrTooCloseToProp(cameraPosition, propBounds))
        {
            return cameraPosition;
        }

        Vector3 direction = cameraPosition - propBounds.center;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = fallbackDirection;
        }

        direction.Normalize();
        float exitDistance = GetBoundsExitDistance(propBounds, direction);
        Vector3 pushedPosition =
            propBounds.center + direction * (exitDistance + minimumDistanceFromPropSurface);
        pushedPosition = EnforceWallHalfSpace(pushedPosition);
        pushedPosition = ResolveCameraCollision(lookTarget, pushedPosition);
        if (!IsCameraInsideOrTooCloseToProp(pushedPosition, propBounds) &&
            !_cameraForcedOutLoggedForCurrentProp)
        {
            Debug.Log("Adaptive TPS: Camera forced out of prop bounds.");
            _cameraForcedOutLoggedForCurrentProp = true;
        }

        return pushedPosition;
    }

    private static float GetBoundsExitDistance(Bounds bounds, Vector3 direction)
    {
        Vector3 extents = bounds.extents;
        float distance = float.PositiveInfinity;
        if (Mathf.Abs(direction.x) > 0.0001f)
        {
            distance = Mathf.Min(distance, extents.x / Mathf.Abs(direction.x));
        }

        if (Mathf.Abs(direction.y) > 0.0001f)
        {
            distance = Mathf.Min(distance, extents.y / Mathf.Abs(direction.y));
        }

        if (Mathf.Abs(direction.z) > 0.0001f)
        {
            distance = Mathf.Min(distance, extents.z / Mathf.Abs(direction.z));
        }

        return float.IsInfinity(distance) ? 0f : distance;
    }

    private bool IsCameraInsideOrTooCloseToProp(Vector3 cameraPosition, Bounds propBounds)
    {
        if (propBounds.Contains(cameraPosition))
        {
            return true;
        }

        Vector3 closestPoint = propBounds.ClosestPoint(cameraPosition);
        return Vector3.Distance(cameraPosition, closestPoint) < minimumDistanceFromPropSurface;
    }

    private bool TryCacheFadeRenderer(Renderer renderer)
    {
        Material[] materials = renderer.sharedMaterials;
        if (materials == null || materials.Length != 1 || materials[0] == null)
        {
            return false;
        }

        Material material = materials[0];
        string renderType = material.GetTag("RenderType", false, string.Empty);
        bool supportsTransparency =
            material.renderQueue >= 3000 ||
            renderType.Contains("Transparent") ||
            renderType.Contains("Fade") ||
            (material.shader != null &&
             (material.shader.name.Contains("Transparent") ||
              material.shader.name.Contains("Fade")));
        if (!supportsTransparency)
        {
            return false;
        }

        int colorPropertyId;
        if (material.HasProperty("_BaseColor"))
        {
            colorPropertyId = Shader.PropertyToID("_BaseColor");
        }
        else if (material.HasProperty("_Color"))
        {
            colorPropertyId = Shader.PropertyToID("_Color");
        }
        else
        {
            return false;
        }

        _fadeRendererStates.Add(new FadeRendererState
        {
            Renderer = renderer,
            ColorPropertyId = colorPropertyId,
            OriginalColor = material.GetColor(colorPropertyId),
            PropertyBlock = new MaterialPropertyBlock()
        });
        return true;
    }

    private void UpdatePropFade(Bounds bounds, Vector3 cameraPosition)
    {
        Vector3 closestPoint = bounds.ClosestPoint(cameraPosition);
        float surfaceDistance = bounds.Contains(cameraPosition)
            ? 0f
            : Vector3.Distance(cameraPosition, closestPoint);
        float alphaFactor = Mathf.InverseLerp(
            fadeFullDistance,
            fadeStartDistance,
            surfaceDistance
        );
        float alpha = Mathf.Lerp(minimumLocalPropAlpha, 1f, alphaFactor);
        if (alpha >= 0.999f)
        {
            RestorePropAlpha();
            return;
        }

        if (_fadeRendererStates.Count == 0)
        {
            if (!_fadeUnavailableWarningLogged)
            {
                Debug.LogWarning(
                    "Adaptive TPS: Transparency fallback unavailable for current shader."
                );
                _fadeUnavailableWarningLogged = true;
            }

            return;
        }

        foreach (FadeRendererState state in _fadeRendererStates)
        {
            if (state.Renderer == null)
            {
                continue;
            }

            state.Renderer.GetPropertyBlock(state.PropertyBlock);
            Color fadedColor = state.OriginalColor;
            fadedColor.a *= alpha;
            state.PropertyBlock.SetColor(state.ColorPropertyId, fadedColor);
            state.Renderer.SetPropertyBlock(state.PropertyBlock);
        }
    }

    private void RestorePropAlpha()
    {
        foreach (FadeRendererState state in _fadeRendererStates)
        {
            if (state.Renderer == null)
            {
                continue;
            }

            state.Renderer.GetPropertyBlock(state.PropertyBlock);
            state.PropertyBlock.SetColor(state.ColorPropertyId, state.OriginalColor);
            state.Renderer.SetPropertyBlock(state.PropertyBlock);
        }
    }

    private static bool IsValidPropRenderer(Renderer renderer)
    {
        return renderer != null &&
               renderer.enabled &&
               renderer.gameObject.activeInHierarchy &&
               !(renderer is ParticleSystemRenderer) &&
               !(renderer is TrailRenderer) &&
               !(renderer is LineRenderer) &&
               !renderer.name.Contains("Debug");
    }

    private void UpdateGhostCamera()
    {
        if (tpsCamera == null)
        {
            return;
        }

        Transform cameraTransform = tpsCamera.transform;
        Vector2 lookInput = ReadGhostLookInput();
        _ghostYaw += lookInput.x * ghostLookSensitivity;
        _ghostPitch -= lookInput.y * ghostLookSensitivity;
        _ghostPitch = Mathf.Clamp(_ghostPitch, -80f, 80f);
        cameraTransform.rotation = Quaternion.Euler(_ghostPitch, _ghostYaw, 0f);

        Vector2 moveInput = ReadGhostMoveInput();
        float verticalInput = ReadGhostVerticalInput();
        Vector3 planarForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        Vector3 planarRight = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        Vector3 movement =
            planarForward * moveInput.y +
            planarRight * moveInput.x +
            Vector3.up * verticalInput;

        if (movement.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        movement = Vector3.ClampMagnitude(movement, 1f);
        Vector3 desiredPosition =
            cameraTransform.position + movement * (ghostMoveSpeed * Time.deltaTime);
        Vector3 offset = desiredPosition - GhostAnchorPosition;
        if (offset.magnitude > ghostMaxDistance)
        {
            desiredPosition =
                GhostAnchorPosition + offset.normalized * ghostMaxDistance;
        }

        cameraTransform.position = ResolveGhostCollision(cameraTransform.position, desiredPosition);
    }

    private Vector3 ResolveGhostCollision(Vector3 currentPosition, Vector3 desiredPosition)
    {
        Vector3 displacement = desiredPosition - currentPosition;
        float distance = displacement.magnitude;
        if (distance <= 0.0001f)
        {
            return desiredPosition;
        }

        RaycastHit[] hits = Physics.SphereCastAll(
            currentPosition,
            ghostCollisionRadius,
            displacement / distance,
            distance,
            ghostCollisionMask,
            QueryTriggerInteraction.Ignore
        );

        float closestDistance = distance;
        bool foundObstacle = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == null ||
                (_ghostPlayerRoot != null &&
                 (hit.transform == _ghostPlayerRoot || hit.transform.IsChildOf(_ghostPlayerRoot))))
            {
                continue;
            }

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                foundObstacle = true;
            }
        }

        if (!foundObstacle)
        {
            return desiredPosition;
        }

        float safeDistance = Mathf.Max(0f, closestDistance - ghostCollisionRadius * 0.5f);
        return currentPosition + displacement.normalized * safeDistance;
    }

    private static Vector2 ReadGhostMoveInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            float horizontal =
                (Keyboard.current.dKey.isPressed ? 1f : 0f) -
                (Keyboard.current.aKey.isPressed ? 1f : 0f);
            float vertical =
                (Keyboard.current.wKey.isPressed ? 1f : 0f) -
                (Keyboard.current.sKey.isPressed ? 1f : 0f);
            return new Vector2(horizontal, vertical);
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#else
        return Vector2.zero;
#endif
    }

    private static Vector2 ReadGhostLookInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            return Mouse.current.delta.ReadValue() * 0.05f;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#else
        return Vector2.zero;
#endif
    }

    private static float ReadGhostVerticalInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            return
                (Keyboard.current.spaceKey.isPressed ? 1f : 0f) -
                (Keyboard.current.leftCtrlKey.isPressed ? 1f : 0f);
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return
            (Input.GetKey(KeyCode.Space) ? 1f : 0f) -
            (Input.GetKey(KeyCode.LeftControl) ? 1f : 0f);
#else
        return 0f;
#endif
    }

    private static void SetCameraActive(Camera targetCamera, bool active)
    {
        if (targetCamera == null)
        {
            return;
        }

        targetCamera.gameObject.tag = active ? "MainCamera" : "Untagged";
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
