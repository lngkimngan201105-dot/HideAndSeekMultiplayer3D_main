using System;
using StarterAssets;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum HiderControlState
{
    Human,
    DisguisedGrounded,
    DisguisedWallAttached,
    GhostCamera,
    Spectator
}

public class PropTransformSystem : MonoBehaviour
{
    public event Action VisualChanged;
    public event Action<bool> EliminationChanged;

    [Header("Role")]
    public PlayerRole playerRole = PlayerRole.Hider;

    [Header("Interaction")]
    public Camera mainCamera;
    public float interactionDistance = 3f;
    public LayerMask interactableLayers = ~0;

    [Header("Visual Roots")]
    public Transform humanVisualRoot;
    public Transform propVisualRoot;

    [Header("Camera")]
    public PlayerCameraModeManager cameraModeManager;

    [Header("Round")]
    public PropHuntRoundManager roundManager;

    [Header("Disguised Wall Traversal")]
    [SerializeField] private LayerMask attachSurfaceMask = Physics.DefaultRaycastLayers;
    [SerializeField, Range(0f, 1f)] private float maximumWallUpDot = 0.35f;
    [SerializeField] private float wallAttachMaxDistance = 1.2f;
    [SerializeField] private float wallSurfaceGap = 0.05f;
    [SerializeField, Range(0.1f, 1f)] private float wallMoveSpeedMultiplier = 0.5f;
    [SerializeField] private float propRotationSpeed = 90f;
    [SerializeField] private float wallJumpOutSpeed = 2.5f;
    [SerializeField] private float wallJumpUpSpeed = 3.5f;
    [SerializeField] private float wallSnapSpeed = 3f;
    [SerializeField] private float wallNormalSmoothingSpeed = 12f;
    [SerializeField] private HiderPlayableAreaBounds playableAreaBounds;

    public string currentPropId;
    public PlayerDisguiseState currentState = PlayerDisguiseState.Human;

    private StarterAssetsInputs _input;
    private FirstPersonController _firstPersonController;
    private CharacterController _characterController;
    private GameObject _currentPropVisual;
    private PropTarget _lastSeenProp;
    private Material _debugMaterial;
    private Vector3 _disguiseStartPlayerPosition;
    private float _nextMovementLogTime;
    private Vector3 _lockedHiderPosition;
    private Quaternion _lockedHiderRotation;
    private PlayerCameraMode _cameraModeBeforeGhost = PlayerCameraMode.PropTPS;
    private bool _firstPersonControllerWasEnabled;
    private bool _controllerWasAlreadyLocked;
    private CursorLockMode _cursorLockModeBeforeGhost;
    private bool _cursorVisibleBeforeGhost;
    private Quaternion _propVisualRotationOffset = Quaternion.identity;
    private Vector3 _wallHitPoint;
    private float _wallDistance;
    private Quaternion _baseWallAlignmentRotation = Quaternion.identity;
    private float _wallUserRotationDegrees;
    private Vector3 _detectedBackLocalDirection = Vector3.back;
    private float _detectedBackConfidence;
    private Bounds _localWallVisualBounds;
    private bool _hasLocalWallVisualBounds;
    private Vector3 _lastValidWallRight;
    private Vector3 _lastValidWallUp;
    private readonly System.Collections.Generic.HashSet<string> _backAnalysisWarnings =
        new System.Collections.Generic.HashSet<string>();
    private bool _gameplayInputLocked;

    public Transform CurrentPropVisualTransform =>
        _currentPropVisual != null ? _currentPropVisual.transform : null;
    public Transform CurrentVisualRoot => IsDisguised ? propVisualRoot : humanVisualRoot;
    public bool IsEliminated { get; private set; }
    public bool IsGhostCameraActive { get; private set; }
    public bool IsChangingModel { get; private set; }
    public bool IsGameplayInputLocked => _gameplayInputLocked;
    public bool IsDisguised => currentState == PlayerDisguiseState.Disguised;
    public bool IsWallAttached { get; private set; }
    public Vector3 WallNormal { get; private set; }
    public Collider AttachedWallCollider { get; private set; }
    public Vector3 WallHitPoint => _wallHitPoint;
    public HiderControlState CurrentControlState => IsGhostCameraActive
        ? HiderControlState.GhostCamera
        : currentState == PlayerDisguiseState.Spectator
            ? HiderControlState.Spectator
            : currentState == PlayerDisguiseState.Human
                ? HiderControlState.Human
                : IsWallAttached
                    ? HiderControlState.DisguisedWallAttached
                    : HiderControlState.DisguisedGrounded;

    private void Awake()
    {
        _input = GetComponent<StarterAssetsInputs>();
        _firstPersonController = GetComponent<FirstPersonController>();
        _characterController = GetComponent<CharacterController>();

        ResolveAndRepairVisualHierarchy();

        if (cameraModeManager == null)
        {
            cameraModeManager = GetComponent<PlayerCameraModeManager>();
        }

        if (mainCamera == null && cameraModeManager != null)
        {
            mainCamera = cameraModeManager.fpsCamera;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (roundManager == null)
        {
            roundManager = FindObjectOfType<PropHuntRoundManager>();
        }

        EnsureTpsCameraOutsideHumanVisualRoot();
    }

    private void OnEnable()
    {
        if (roundManager != null)
        {
            roundManager.RoundStateChanged += HandleRoundStateChanged;
        }
    }

    private void OnDisable()
    {
        if (roundManager != null)
        {
            roundManager.RoundStateChanged -= HandleRoundStateChanged;
        }

        ForceExitGhostCamera();
        ForceDetachFromWall();
    }

    private void Start()
    {
        if (playableAreaBounds == null)
        {
            playableAreaBounds = HiderPlayableAreaBounds.ResolveOrCreate(transform);
        }

        if (!IsEliminated)
        {
            BecomeHuman(false);
        }
    }

    private void Update()
    {
        if (playerRole != PlayerRole.Hider || _input == null)
        {
            return;
        }

        if (_gameplayInputLocked || IsEliminated)
        {
            ClearGameplayInputForGhostCamera();
            return;
        }

        if (IsGhostCameraActive)
        {
            if (!CanRemainInGhostCamera())
            {
                ForceExitGhostCamera();
                return;
            }

            if (_input.spectatorToggle)
            {
                _input.spectatorToggle = false;
                ForceExitGhostCamera();
                return;
            }

            KeepHiderLockedInPlace();
            ClearGameplayInputForGhostCamera();
            return;
        }

        if (_input.spectatorToggle)
        {
            _input.spectatorToggle = false;
            if (CanAttemptEnterGhostCamera())
            {
                TryEnterGhostCamera();
                return;
            }

            ToggleSpectator();
        }

        if (IsDisguised)
        {
            HandlePropRotationInput();
        }

        if (IsWallAttached && _input.jump)
        {
            _input.jump = false;
            DetachFromWall(true);
            return;
        }

        if (_input.cancelDisguise)
        {
            if (!IsWallAttached && !IsEliminated && currentState != PlayerDisguiseState.Human)
            {
                BecomeHuman(true);
            }

            _input.cancelDisguise = false;
        }

        if (_input.interact)
        {
            if (IsWallAttached)
            {
                DetachFromWall(false);
            }
            else if (currentState == PlayerDisguiseState.Disguised)
            {
                if (TryAttachToWall())
                {
                    _input.interact = false;
                    return;
                }
            }
            else if (currentState == PlayerDisguiseState.Human &&
                     TryGetLookedAtProp(out PropTarget prop, out GameObject sourceVisual))
            {
                BecomeProp(prop, sourceVisual);
            }

            _input.interact = false;
        }

        if (currentState == PlayerDisguiseState.Human)
        {
            LogLookedAtProp();
        }

        if (IsWallAttached)
        {
            UpdateWallTraversal();
        }
    }

    public bool IsSpectatorActive()
    {
        return currentState == PlayerDisguiseState.Spectator;
    }

    public void SetEliminated(bool eliminated)
    {
        HiderHealth health = GetComponent<HiderHealth>();
        if (health != null && health.IsEliminated != eliminated)
        {
            if (eliminated)
            {
                health.SetHealth(0);
            }
            else
            {
                health.ResetForRound();
            }

            return;
        }

        ApplyHealthEliminationState(eliminated);
    }

    public void ApplyHealthEliminationState(bool eliminated, bool enterSpectatorState = true)
    {
        if (eliminated)
        {
            ForceExitGhostCamera();
            ForceDetachForElimination();
        }

        if (IsEliminated == eliminated)
        {
            return;
        }

        IsEliminated = eliminated;
        if (eliminated)
        {
            ClearPropVisual();
            SetPropVisualActive(false);
            SetHumanVisualActive(false);
            SetFirstPersonControllerEnabled(false);
            if (enterSpectatorState)
            {
                currentState = PlayerDisguiseState.Spectator;
                SetCamera(PlayerCameraMode.Spectator);
            }
        }
        else
        {
            BecomeHuman(false);
        }

        EliminationChanged?.Invoke(eliminated);

        if (roundManager != null)
        {
            roundManager.RefreshPlayerCounts();
        }
    }

    public void SetGameplayInputLocked(bool locked)
    {
        _gameplayInputLocked = locked;
        if (_firstPersonController != null)
        {
            _firstPersonController.SetControlLocked(locked);
        }

        if (locked)
        {
            ClearGameplayInputForGhostCamera();
        }
    }

    public void ResetToHumanForRoleSelection()
    {
        if (playerRole != PlayerRole.Hider || IsEliminated)
        {
            return;
        }

        ForceExitGhostCamera();
        ForceDetachForElimination();
        BecomeHuman(false);
    }

    public bool ApplyPropDefinition(PropTarget propDefinition, bool keepCurrentCameraMode = true)
    {
        if (IsChangingModel)
        {
            return false;
        }

        bool applied;
        IsChangingModel = true;
        try
        {
            applied = ApplyPropDefinitionInternal(propDefinition, keepCurrentCameraMode);
        }
        finally
        {
            IsChangingModel = false;
        }

        if (applied)
        {
            VisualChanged?.Invoke();
        }

        return applied;
    }

    public bool TryBecomePropForTesting(PropTarget propDefinition)
    {
        if (propDefinition == null || !propDefinition.GameplayEnabled || IsEliminated || IsChangingModel)
        {
            return false;
        }

        BecomeProp(propDefinition, null);
        return IsDisguised && currentPropId == propDefinition.propId && CurrentPropVisualTransform != null;
    }

    private bool ApplyPropDefinitionInternal(PropTarget propDefinition, bool keepCurrentCameraMode)
    {
        if (propDefinition == null ||
            !propDefinition.GameplayEnabled ||
            currentState != PlayerDisguiseState.Disguised ||
            IsEliminated ||
            IsGhostCameraActive ||
            propVisualRoot == null ||
            !IsValidRandomPropDefinition(propDefinition))
        {
            return false;
        }

        Vector3 playerPosition = transform.position;
        GameObject previousVisual = _currentPropVisual;
        Quaternion savedVisualRotation = propVisualRoot.rotation;

        GameObject candidatePivot = new GameObject("DisguiseVisualPivot");
        Transform pivot = candidatePivot.transform;
        pivot.SetParent(propVisualRoot, false);
        pivot.localPosition = Vector3.zero;
        pivot.localRotation = Quaternion.identity;
        pivot.localScale = Vector3.one;

        GameObject model = CreatePropModelFromVisualParts(propDefinition, pivot);
        if (model == null)
        {
            Destroy(candidatePivot);
            return false;
        }

        StripPhysicsAndGameplayComponents(model);
        SetLayerRecursively(candidatePivot, 0);
        ActivateRenderers(model);
        model.SetActive(true);

        if (!ValidateOriginalPropModelBounds(model) || !CenterModelOnPlayer(model))
        {
            Destroy(candidatePivot);
            return false;
        }

        Vector3 safeWallPosition = playerPosition;
        float safeWallDistance = _wallDistance;
        PropWallGeometry candidateGeometry = default;
        Quaternion candidateBaseAlignment = _baseWallAlignmentRotation;
        Quaternion candidateVisualRotation = savedVisualRotation;
        if (IsWallAttached &&
            (!TryPrepareWallVisual(
                 candidatePivot.transform,
                 propDefinition.propId,
                 _baseWallAlignmentRotation,
                 WallNormal,
                 out candidateGeometry,
                 out candidateBaseAlignment,
                 out candidateVisualRotation) ||
             !TryCalculateWallPlacement(
                 playerPosition,
                 candidateVisualRotation,
                 candidateGeometry.LocalBounds,
                 AttachedWallCollider,
                 WallNormal,
                 _wallHitPoint,
                 out safeWallPosition,
                 out safeWallDistance)))
        {
            Destroy(candidatePivot);
            propVisualRoot.rotation = savedVisualRotation;
            Debug.Log(
                "HiderWallTraversal: Random prop rejected because wall placement was unsafe."
            );
            return false;
        }

        _currentPropVisual = candidatePivot;
        currentPropId = propDefinition.propId;

        if (previousVisual != null)
        {
            Destroy(previousVisual);
        }

        if (IsWallAttached)
        {
            _baseWallAlignmentRotation = candidateBaseAlignment;
            _detectedBackLocalDirection = candidateGeometry.DetectedBackLocalDirection;
            _detectedBackConfidence = candidateGeometry.Confidence;
            _localWallVisualBounds = candidateGeometry.LocalBounds;
            _hasLocalWallVisualBounds = true;
            propVisualRoot.rotation = candidateVisualRotation;
            _propVisualRotationOffset = candidateVisualRotation;
            _wallDistance = safeWallDistance;
            _characterController.Move(safeWallPosition - transform.position);
            Physics.SyncTransforms();
        }
        else
        {
            propVisualRoot.rotation = savedVisualRotation;
            _propVisualRotationOffset = savedVisualRotation;
        }

        if (cameraModeManager != null)
        {
            cameraModeManager.SetPropTarget(candidatePivot.transform);
            if (!keepCurrentCameraMode)
            {
                cameraModeManager.SetMode(PlayerCameraMode.PropTPS);
            }
        }

        Debug.Log($"PropTransformSystem: applied prop definition '{propDefinition.displayName}' without using scene mesh data.");
        return true;
    }

    private static bool IsValidRandomPropDefinition(PropTarget definition)
    {
        if (!definition.GameplayEnabled ||
            definition.visualParts == null ||
            definition.visualParts.Length == 0)
        {
            return false;
        }

        foreach (PropVisualPartData part in definition.visualParts)
        {
            if (part == null || part.mesh == null || IsCombinedMesh(part.mesh) ||
                part.materials == null || part.materials.Length == 0)
            {
                return false;
            }

            bool hasMaterial = false;
            foreach (Material material in part.materials)
            {
                hasMaterial |= material != null;
            }

            Vector3 scaledSize = Vector3.Scale(part.mesh.bounds.size, part.localScale);
            if (!hasMaterial ||
                Mathf.Abs(scaledSize.x) > 20f ||
                Mathf.Abs(scaledSize.y) > 20f ||
                Mathf.Abs(scaledSize.z) > 20f)
            {
                return false;
            }
        }

        return true;
    }

    private void BecomeProp(PropTarget prop, GameObject sceneSource)
    {
        if (IsChangingModel)
        {
            return;
        }

        IsChangingModel = true;
        try
        {
            BecomePropInternal(prop, sceneSource);
        }
        finally
        {
            IsChangingModel = false;
        }

        VisualChanged?.Invoke();
    }

    private void BecomePropInternal(PropTarget prop, GameObject sceneSource)
    {
        if (prop == null || !prop.GameplayEnabled)
        {
            return;
        }

        Vector3 playerBefore = transform.position;
        ClearPropVisual();

        ResolveAndRepairVisualHierarchy();

        SetHumanVisualActive(false);
        SetPropVisualActive(true);
        RepairInactivePropVisualRoot();

        if (prop.visualParts == null || prop.visualParts.Length == 0)
        {
            Debug.LogError(
                $"PropTransformSystem: '{prop.displayName}' has no visualParts. " +
                "Run Tools > Prop Hunt > Setup Hider Prop Hunt."
            );
            BecomeHuman(false);
            return;
        }

        if (propVisualRoot == null)
        {
            Debug.LogError("PropTransformSystem: cannot create prop clone because PropVisualRoot is missing.");
            BecomeHuman(false);
            return;
        }

        propVisualRoot.localPosition = Vector3.zero;
        propVisualRoot.localRotation = Quaternion.identity;
        propVisualRoot.localScale = Vector3.one;

        GameObject pivotObject = new GameObject("DisguiseVisualPivot");
        Transform pivot = pivotObject.transform;
        pivot.SetParent(propVisualRoot, false);
        pivot.localPosition = Vector3.zero;
        pivot.localRotation = Quaternion.identity;
        pivot.localScale = Vector3.one;

        GameObject model = CreatePropModelFromVisualParts(prop, pivot);
        if (model == null)
        {
            Debug.LogError($"PropTransformSystem: failed to create visualParts model for '{prop.displayName}'.");
            Destroy(pivotObject);
            BecomeHuman(false);
            return;
        }

        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        StripPhysicsAndGameplayComponents(model);
        SetLayerRecursively(pivotObject, 0);
        ActivateRenderers(model);
        model.SetActive(true);

        if (!ValidateOriginalPropModelBounds(model))
        {
            Destroy(pivotObject);
            BecomeHuman(false);
            return;
        }

        if (!CenterModelOnPlayer(model))
        {
            Destroy(pivotObject);
            BecomeHuman(false);
            return;
        }

        _currentPropVisual = pivotObject;

        currentPropId = prop.propId;
        currentState = PlayerDisguiseState.Disguised;
        IsWallAttached = false;
        WallNormal = Vector3.zero;
        AttachedWallCollider = null;
        _propVisualRotationOffset = propVisualRoot.rotation;
        SetMovementComponentsEnabled(true);
        EnsureDisguisedMovementSettings();
        if (_firstPersonController != null)
        {
            _firstPersonController.SetDisguisedCameraRelativeMovement(
                cameraModeManager != null ? cameraModeManager.tpsCamera : null,
                true
            );
        }
        _disguiseStartPlayerPosition = transform.position;
        _nextMovementLogTime = Time.time;

        if (cameraModeManager != null && cameraModeManager.tpsCamera != null)
        {
            cameraModeManager.tpsCamera.useOcclusionCulling = false;
        }

        if (cameraModeManager != null)
        {
            cameraModeManager.SetPropTarget(_currentPropVisual.transform);
        }

        SetCamera(PlayerCameraMode.PropTPS);

        Vector3 playerAfter = transform.position;
        Debug.Log($"Player before copy: {playerBefore}");
        Debug.Log($"Player after copy: {playerAfter}");
        Debug.Log($"Player movement during copy: {playerAfter - playerBefore}");
        LogMovementState();

        LogCloneVisibilityDiagnostics(prop, sceneSource, pivotObject);
        LogVisualHierarchyState();
        Debug.Log($"PropTransformSystem: transformed into prop '{prop.displayName}' ({currentPropId}).");
    }

    private void ResolveAndRepairVisualHierarchy()
    {
        if (humanVisualRoot == null)
        {
            humanVisualRoot = FindDescendantByName(transform, "HumanVisualRoot");
        }

        if (humanVisualRoot == transform)
        {
            Debug.LogError("PropTransformSystem: HumanVisualRoot cannot be PlayerCapsule itself.");
            humanVisualRoot = null;
        }
        else if (humanVisualRoot != null && humanVisualRoot.parent != transform)
        {
            Debug.LogWarning("PropTransformSystem: HumanVisualRoot is not a direct child of PlayerCapsule. Reparenting it.");
            humanVisualRoot.SetParent(transform, true);
            humanVisualRoot.localPosition = Vector3.zero;
            humanVisualRoot.localRotation = Quaternion.identity;
            humanVisualRoot.localScale = Vector3.one;
        }

        if (propVisualRoot == null)
        {
            propVisualRoot = FindDescendantByName(transform, "PropVisualRoot");
        }

        if (propVisualRoot == null)
        {
            Debug.LogError("PropTransformSystem: PropVisualRoot is missing from PlayerCapsule.");
            return;
        }

        if (propVisualRoot == transform)
        {
            Debug.LogError("PropTransformSystem: PropVisualRoot cannot be PlayerCapsule itself.");
            propVisualRoot = null;
            return;
        }

        if (humanVisualRoot != null && propVisualRoot.IsChildOf(humanVisualRoot))
        {
            Debug.LogWarning("PropVisualRoot is inside HumanVisualRoot. Reparenting to PlayerCapsule.");
            ReparentPropVisualRootToPlayer();
            return;
        }

        if (propVisualRoot.parent != transform)
        {
            Debug.LogWarning("PropTransformSystem: PropVisualRoot is not a direct child of PlayerCapsule. Reparenting it.");
            ReparentPropVisualRootToPlayer();
        }
    }

    private void RepairInactivePropVisualRoot()
    {
        if (propVisualRoot == null || !propVisualRoot.gameObject.activeSelf || propVisualRoot.gameObject.activeInHierarchy)
        {
            return;
        }

        Debug.LogWarning("PropTransformSystem: PropVisualRoot is activeSelf but inactiveInHierarchy. Reparenting to PlayerCapsule.");
        ReparentPropVisualRootToPlayer();
        propVisualRoot.gameObject.SetActive(true);
    }

    private void ReparentPropVisualRootToPlayer()
    {
        if (propVisualRoot == null || propVisualRoot == transform)
        {
            return;
        }

        propVisualRoot.SetParent(transform, true);
        propVisualRoot.localPosition = Vector3.zero;
        propVisualRoot.localRotation = Quaternion.identity;
        propVisualRoot.localScale = Vector3.one;
    }

    private void EnsureTpsCameraOutsideHumanVisualRoot()
    {
        if (humanVisualRoot == null || cameraModeManager == null || cameraModeManager.tpsCamera == null)
        {
            return;
        }

        Transform tpsCameraTransform = cameraModeManager.tpsCamera.transform;
        if (!tpsCameraTransform.IsChildOf(humanVisualRoot))
        {
            return;
        }

        Transform cameraRoot = cameraModeManager.tpsCameraRoot;
        Transform transformToMove = cameraRoot != null &&
                                    cameraRoot != humanVisualRoot &&
                                    cameraRoot != transform &&
                                    tpsCameraTransform.IsChildOf(cameraRoot)
            ? cameraRoot
            : tpsCameraTransform;

        Debug.LogWarning("PropTransformSystem: TPS Camera is inside HumanVisualRoot. Reparenting it to PlayerCapsule.");
        transformToMove.SetParent(transform, true);
    }

    private static Transform FindDescendantByName(Transform root, string objectName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child != root && child.name == objectName)
            {
                return child;
            }
        }

        return null;
    }

    private void LogVisualHierarchyState()
    {
        if (humanVisualRoot != null)
        {
            Debug.Log($"Human root active: {humanVisualRoot.gameObject.activeInHierarchy}");
        }

        if (propVisualRoot != null)
        {
            Debug.Log($"Prop root active self: {propVisualRoot.gameObject.activeSelf}");
            Debug.Log($"Prop root active hierarchy: {propVisualRoot.gameObject.activeInHierarchy}");
        }

        if (_currentPropVisual != null)
        {
            Debug.Log($"Clone active hierarchy: {_currentPropVisual.activeInHierarchy}");
        }
    }

    private void BecomeHuman(bool log)
    {
        ForceExitGhostCamera();
        ForceDetachFromWall();

        if (cameraModeManager != null)
        {
            cameraModeManager.ClearPropTarget();
        }

        if (_firstPersonController != null)
        {
            _firstPersonController.SetDisguisedCameraRelativeMovement(null, false);
        }
        ResetWallMovementBasis();

        ClearPropVisual();
        SetPropVisualActive(false);
        SetHumanVisualActive(true);

        currentPropId = string.Empty;
        currentState = PlayerDisguiseState.Human;
        SetFirstPersonControllerEnabled(true);
        SetCamera(PlayerCameraMode.HumanFPS);
        VisualChanged?.Invoke();

        if (log)
        {
            Debug.Log("PropTransformSystem: cancelled disguise and returned to human.");
        }
    }

    private void ToggleSpectator()
    {
        bool spectatorAllowed = IsEliminated ||
                                (roundManager != null && roundManager.CurrentState == PropHuntRoundState.Ended);

        if (currentState == PlayerDisguiseState.Disguised && !spectatorAllowed)
        {
            if (cameraModeManager != null)
            {
                cameraModeManager.TogglePropCameraDistance();
            }

            return;
        }

        if (currentState == PlayerDisguiseState.Spectator)
        {
            return;
        }

        if (!spectatorAllowed)
        {
            return;
        }

        currentState = PlayerDisguiseState.Spectator;
        SetFirstPersonControllerEnabled(false);
        SetCamera(PlayerCameraMode.Spectator);
        Debug.Log("PropTransformSystem: switched from prop TPS camera to spectator camera.");
    }

    public bool CanAttachToWall()
    {
        return IsDisguised &&
               !IsWallAttached &&
               !IsGhostCameraActive &&
               (_firstPersonController == null || _firstPersonController.enabled) &&
               TryFindAttachableWall(out _);
    }

    public bool TryAttachToWall()
    {
        if (!IsDisguised ||
            IsWallAttached ||
            IsGhostCameraActive ||
            !TryFindAttachableWall(out RaycastHit wallHit) ||
            propVisualRoot == null ||
            _characterController == null ||
            !_characterController.enabled ||
            _input == null ||
            (_firstPersonController != null && !_firstPersonController.enabled))
        {
            return false;
        }

        Quaternion previousVisualRotation = propVisualRoot.rotation;
        float previousUserRotation = _wallUserRotationDegrees;
        _wallUserRotationDegrees = 0f;
        Vector3 attachNormal = GetStableOutwardWallNormal(
            wallHit.normal,
            wallHit.point,
            Vector3.zero,
            transform.position
        );
        Transform analysisRoot = _currentPropVisual != null
            ? _currentPropVisual.transform
            : propVisualRoot;
        if (!TryPrepareWallVisual(
                analysisRoot,
                currentPropId,
                previousVisualRotation,
                attachNormal,
                out PropWallGeometry geometry,
                out Quaternion baseAlignment,
                out Quaternion desiredVisualRotation) ||
            !TryCalculateWallPlacement(
                transform.position,
                desiredVisualRotation,
                geometry.LocalBounds,
                wallHit.collider,
                attachNormal,
                wallHit.point,
                out Vector3 safePlayerPosition,
                out float safeDistance))
        {
            _wallUserRotationDegrees = previousUserRotation;
            propVisualRoot.rotation = previousVisualRotation;
            return false;
        }

        Vector3 originalPlayerPosition = transform.position;
        _characterController.Move(safePlayerPosition - transform.position);
        Physics.SyncTransforms();
        if (!CanUseWallPose(
                transform.position,
                desiredVisualRotation,
                geometry.LocalBounds,
                wallHit.collider,
                wallHit.point,
                attachNormal))
        {
            _characterController.Move(originalPlayerPosition - transform.position);
            _wallUserRotationDegrees = previousUserRotation;
            propVisualRoot.rotation = previousVisualRotation;
            Debug.Log("Wall Attach: Candidate pose rejected because of penetration.");
            return false;
        }

        AttachedWallCollider = wallHit.collider;
        WallNormal = attachNormal;
        _wallHitPoint = wallHit.point;
        _wallDistance = safeDistance;
        _baseWallAlignmentRotation = baseAlignment;
        _detectedBackLocalDirection = geometry.DetectedBackLocalDirection;
        _detectedBackConfidence = geometry.Confidence;
        _localWallVisualBounds = geometry.LocalBounds;
        _hasLocalWallVisualBounds = true;
        IsWallAttached = true;
        ResetWallMovementBasis();
        TryGetWallMovementBasis(WallNormal, out _, out _);
        propVisualRoot.rotation = desiredVisualRotation;
        _propVisualRotationOffset = desiredVisualRotation;

        if (_firstPersonController != null)
        {
            _firstPersonController.SetControlLocked(true);
        }

        _input.move = Vector2.zero;
        _input.jump = false;
        _input.sprint = false;
        Debug.Log(
            $"Wall Attach: Surface accepted: {AttachedWallCollider.name}\n" +
            $"Layer={LayerMask.LayerToName(AttachedWallCollider.gameObject.layer)}\n" +
            $"Collider={AttachedWallCollider.GetType().Name}\n" +
            $"Normal={WallNormal}."
        );
        if (cameraModeManager != null)
        {
            cameraModeManager.RefreshCurrentPropCamera();
            cameraModeManager.ForceCameraToSafePosition();
        }

        return true;
    }

    public void DetachFromWall(bool applyJumpImpulse = false)
    {
        DetachFromWallInternal(applyJumpImpulse, applyJumpImpulse ? "Jump" : "Manual");
    }

    public void ForceDetachFromWall()
    {
        DetachFromWallInternal(false, "Reset");
    }

    public void ForceDetachForElimination()
    {
        DetachFromWallInternal(false, "Elimination");
    }

    private void DetachFromWallInternal(bool applyJumpImpulse, string reason)
    {
        if (!IsWallAttached)
        {
            return;
        }

        Vector3 detachNormal = WallNormal;
        if (propVisualRoot != null)
        {
            _propVisualRotationOffset = propVisualRoot.rotation;
        }

        IsWallAttached = false;
        WallNormal = Vector3.zero;
        AttachedWallCollider = null;
        _wallHitPoint = Vector3.zero;
        _wallDistance = 0f;
        _wallUserRotationDegrees = 0f;
        _baseWallAlignmentRotation = Quaternion.identity;
        _detectedBackLocalDirection = Vector3.back;
        _detectedBackConfidence = 0f;
        _hasLocalWallVisualBounds = false;
        ResetWallMovementBasis();

        if (_firstPersonController != null)
        {
            _firstPersonController.SetControlLocked(false);
        }

        if (_characterController != null && _characterController.enabled)
        {
            _characterController.Move(detachNormal * 0.08f);
        }

        if (applyJumpImpulse && _firstPersonController != null)
        {
            _firstPersonController.ApplyExternalVelocity(
                detachNormal * wallJumpOutSpeed + Vector3.up * wallJumpUpSpeed
            );
        }

        if (_input != null)
        {
            _input.move = Vector2.zero;
            _input.jump = false;
            _input.sprint = false;
        }

        if (cameraModeManager != null)
        {
            cameraModeManager.RefreshCurrentPropCamera();
            cameraModeManager.ForceCameraToSafePosition();
        }

        Debug.Log($"HiderWallTraversal: Detached.\nReason={reason}.");
    }

    private void HandlePropRotationInput()
    {
        if (propVisualRoot == null || IsChangingModel)
        {
            return;
        }

        float rotationInput = ReadPropRotationInput();
        if (Mathf.Approximately(rotationInput, 0f))
        {
            return;
        }

        if (IsWallAttached)
        {
            if (!_hasLocalWallVisualBounds)
            {
                return;
            }

            float candidateUserRotation = _wallUserRotationDegrees +
                                          rotationInput * propRotationSpeed * Time.deltaTime;
            Quaternion candidateRotation =
                Quaternion.AngleAxis(candidateUserRotation, WallNormal) *
                _baseWallAlignmentRotation;
            if (!TryCalculateWallPlacement(
                    transform.position,
                    candidateRotation,
                    _localWallVisualBounds,
                    AttachedWallCollider,
                    WallNormal,
                    _wallHitPoint,
                    out Vector3 safePosition,
                    out float safeDistance))
            {
                return;
            }

            _wallUserRotationDegrees = candidateUserRotation;
            _wallDistance = safeDistance;
            _characterController.Move(safePosition - transform.position);
            propVisualRoot.rotation = candidateRotation;
            Physics.SyncTransforms();
        }
        else
        {
            propVisualRoot.Rotate(
                Vector3.up,
                rotationInput * propRotationSpeed * Time.deltaTime,
                Space.World
            );
        }

        _propVisualRotationOffset = propVisualRoot.rotation;
    }

    private void UpdateWallTraversal()
    {
        if (!IsWallAttached ||
            AttachedWallCollider == null ||
            !AttachedWallCollider.enabled ||
            _characterController == null ||
            !_characterController.enabled ||
            !_hasLocalWallVisualBounds ||
            (_firstPersonController != null && !_firstPersonController.enabled))
        {
            DetachFromWallInternal(false, "InvalidSurface");
            return;
        }

        if (!TryFindSupportingWall(
                transform.position,
                AttachedWallCollider,
                _wallHitPoint,
                WallNormal,
                out RaycastHit currentWallHit))
        {
            DetachFromWallInternal(false, "InvalidSurface");
            return;
        }

        Vector2 moveInput = Vector2.ClampMagnitude(_input.move, 1f);
        if (!TryGetWallMovementBasis(WallNormal, out Vector3 wallUp, out Vector3 wallRight))
        {
            MaintainWallAttachment(currentWallHit);
            return;
        }

        Vector3 wallMovement = wallRight * moveInput.x + wallUp * moveInput.y;

        if (wallMovement.sqrMagnitude > 0.0001f)
        {
            float normalMoveSpeed = _firstPersonController != null
                ? _firstPersonController.MoveSpeed
                : 4f;
            Vector3 movementDelta =
                wallMovement.normalized *
                (normalMoveSpeed * wallMoveSpeedMultiplier * Time.deltaTime);
            Vector3 desiredPosition = transform.position + movementDelta;

            if (IsInsidePlayableArea(desiredPosition) &&
                TryFindSupportingWall(
                    desiredPosition,
                    AttachedWallCollider,
                    _wallHitPoint,
                    WallNormal,
                    out RaycastHit desiredWallHit))
            {
                Vector3 desiredOutwardNormal = GetStableOutwardWallNormal(
                    desiredWallHit.normal,
                    desiredWallHit.point,
                    WallNormal,
                    desiredPosition
                );
                Vector3 candidateNormal = Vector3.Slerp(
                    WallNormal,
                    desiredOutwardNormal,
                    Mathf.Clamp01(wallNormalSmoothingSpeed * Time.deltaTime)
                ).normalized;
                Quaternion candidateBaseAlignment = AlignBackDirectionToWall(
                    _baseWallAlignmentRotation,
                    _detectedBackLocalDirection,
                    candidateNormal
                );
                Quaternion candidateVisualRotation =
                    Quaternion.AngleAxis(_wallUserRotationDegrees, candidateNormal) *
                    candidateBaseAlignment;

                if (TryCalculateWallPlacement(
                        desiredPosition,
                        candidateVisualRotation,
                        _localWallVisualBounds,
                        desiredWallHit.collider,
                        candidateNormal,
                        desiredWallHit.point,
                        out Vector3 safePosition,
                        out float safeDistance))
                {
                    _characterController.Move(safePosition - transform.position);
                    AttachedWallCollider = desiredWallHit.collider;
                    WallNormal = candidateNormal;
                    _wallHitPoint = desiredWallHit.point;
                    _wallDistance = safeDistance;
                    _baseWallAlignmentRotation = candidateBaseAlignment;
                    propVisualRoot.rotation = candidateVisualRotation;
                    _propVisualRotationOffset = candidateVisualRotation;
                    Physics.SyncTransforms();
                    return;
                }
            }
        }

        MaintainWallAttachment(currentWallHit);
    }

    private bool TryGetWallMovementBasis(
        Vector3 wallNormal,
        out Vector3 wallUp,
        out Vector3 wallRight)
    {
        wallUp = Vector3.zero;
        wallRight = Vector3.zero;
        if (wallNormal.sqrMagnitude < 0.5f)
        {
            return false;
        }

        Vector3 stableWallNormal = wallNormal.normalized;
        Vector3 projectedWorldUp = Vector3.ProjectOnPlane(Vector3.up, stableWallNormal);
        if (projectedWorldUp.sqrMagnitude > 0.0001f)
        {
            wallUp = projectedWorldUp.normalized;
            if (Vector3.Dot(wallUp, Vector3.up) < 0f)
            {
                wallUp = -wallUp;
            }
        }
        else if (_lastValidWallUp.sqrMagnitude > 0.5f)
        {
            wallUp = Vector3.ProjectOnPlane(_lastValidWallUp, stableWallNormal).normalized;
        }

        if (wallUp.sqrMagnitude < 0.5f)
        {
            return false;
        }

        Transform gameplayCamera = cameraModeManager != null && cameraModeManager.tpsCamera != null
            ? cameraModeManager.tpsCamera.transform
            : mainCamera != null
                ? mainCamera.transform
                : null;
        Vector3 cameraRight = gameplayCamera != null
            ? gameplayCamera.right
            : transform.right;
        Vector3 projectedCameraRight = Vector3.ProjectOnPlane(
            cameraRight,
            stableWallNormal
        );
        bool usedDirectCameraRight = projectedCameraRight.sqrMagnitude > 0.0001f;
        if (usedDirectCameraRight)
        {
            wallRight = projectedCameraRight.normalized;
        }
        else if (_lastValidWallRight.sqrMagnitude > 0.5f)
        {
            wallRight = Vector3.ProjectOnPlane(
                _lastValidWallRight,
                stableWallNormal
            ).normalized;
        }

        if (wallRight.sqrMagnitude < 0.5f)
        {
            wallRight = Vector3.Cross(stableWallNormal, wallUp).normalized;
            if (Vector3.Dot(wallRight, cameraRight) < 0f)
            {
                wallRight = -wallRight;
            }
        }

        if (!usedDirectCameraRight &&
            _lastValidWallRight.sqrMagnitude > 0.5f &&
            Vector3.Dot(wallRight, _lastValidWallRight) < 0f)
        {
            wallRight = -wallRight;
        }

        _lastValidWallUp = wallUp;
        _lastValidWallRight = wallRight;
        return true;
    }

    private void ResetWallMovementBasis()
    {
        _lastValidWallUp = Vector3.zero;
        _lastValidWallRight = Vector3.zero;
    }

    private static Vector3 GetStableOutwardWallNormal(
        Vector3 candidateNormal,
        Vector3 wallPoint,
        Vector3 currentWallNormal,
        Vector3 playerPosition)
    {
        if (candidateNormal.sqrMagnitude < 0.0001f)
        {
            return currentWallNormal.sqrMagnitude > 0.0001f
                ? currentWallNormal.normalized
                : Vector3.forward;
        }

        candidateNormal.Normalize();
        Vector3 wallToPlayer = playerPosition - wallPoint;
        if (wallToPlayer.sqrMagnitude > 0.0001f &&
            Vector3.Dot(candidateNormal, wallToPlayer) < 0f)
        {
            candidateNormal = -candidateNormal;
        }

        if (currentWallNormal.sqrMagnitude > 0.5f &&
            Vector3.Dot(candidateNormal, currentWallNormal) < 0f)
        {
            candidateNormal = -candidateNormal;
        }

        return candidateNormal;
    }

    private void MaintainWallAttachment(RaycastHit wallHit)
    {
        if (!IsWallAttached || _characterController == null)
        {
            return;
        }

        Vector3 outwardNormal = GetStableOutwardWallNormal(
            wallHit.normal,
            wallHit.point,
            WallNormal,
            transform.position
        );
        Vector3 candidateNormal = Vector3.Slerp(
            WallNormal,
            outwardNormal,
            Mathf.Clamp01(wallNormalSmoothingSpeed * Time.deltaTime)
        ).normalized;
        Quaternion candidateBaseAlignment = AlignBackDirectionToWall(
            _baseWallAlignmentRotation,
            _detectedBackLocalDirection,
            candidateNormal
        );
        Quaternion candidateRotation =
            Quaternion.AngleAxis(_wallUserRotationDegrees, candidateNormal) *
            candidateBaseAlignment;
        if (TryCalculateWallPlacement(
                transform.position,
                candidateRotation,
                _localWallVisualBounds,
                wallHit.collider,
                candidateNormal,
                wallHit.point,
                out Vector3 safePosition,
                out float safeDistance))
        {
            Vector3 correction = safePosition - transform.position;
            float maximumCorrection = wallSnapSpeed * Time.deltaTime;
            if (correction.magnitude > maximumCorrection)
            {
                correction = correction.normalized * maximumCorrection;
            }

            _characterController.Move(correction);
            AttachedWallCollider = wallHit.collider;
            WallNormal = candidateNormal;
            _wallHitPoint = wallHit.point;
            _wallDistance = safeDistance;
            _baseWallAlignmentRotation = candidateBaseAlignment;
            propVisualRoot.rotation = candidateRotation;
            _propVisualRotationOffset = candidateRotation;
            Physics.SyncTransforms();
        }
    }

    private bool TryFindAttachableWall(out RaycastHit wallHit)
    {
        wallHit = default;
        Camera rayCamera = cameraModeManager != null && cameraModeManager.tpsCamera != null
            ? cameraModeManager.tpsCamera
            : mainCamera != null
                ? mainCamera
                : Camera.main;
        if (rayCamera == null)
        {
            return false;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            rayCamera.transform.position,
            rayCamera.transform.forward,
            Mathf.Max(interactionDistance, wallAttachMaxDistance + 5f),
            attachSurfaceMask,
            QueryTriggerInteraction.Ignore
        );
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit hit in hits)
        {
            Vector3 distanceOrigin = _characterController != null
                ? transform.TransformPoint(_characterController.center)
                : transform.position;
            if (!IsValidAttachSurface(hit) ||
                Vector3.Distance(distanceOrigin, hit.point) > wallAttachMaxDistance ||
                (playableAreaBounds != null && !playableAreaBounds.Contains(hit.point)))
            {
                continue;
            }

            wallHit = hit;
            return true;
        }

        return false;
    }

    private bool IsValidAttachSurface(RaycastHit hit)
    {
        Collider candidate = hit.collider;
        if (candidate == null ||
            !candidate.enabled ||
            candidate.isTrigger ||
            candidate.transform == transform ||
            candidate.transform.IsChildOf(transform) ||
            candidate.GetComponentInParent<PropTransformSystem>() != null ||
            candidate.GetComponentInParent<CharacterController>() != null ||
            (attachSurfaceMask.value & (1 << candidate.gameObject.layer)) == 0)
        {
            return false;
        }

        Rigidbody body = candidate.attachedRigidbody;
        if (body != null && !body.isKinematic)
        {
            return false;
        }

        float upDot = Mathf.Abs(Vector3.Dot(hit.normal.normalized, Vector3.up));
        return upDot <= maximumWallUpDot && IsInsidePlayableArea(hit.point);
    }

    private bool TryPrepareWallVisual(
        Transform analysisRoot,
        string cacheKey,
        Quaternion seedRotation,
        Vector3 wallNormal,
        out PropWallGeometry geometry,
        out Quaternion baseAlignment,
        out Quaternion visualRotation)
    {
        geometry = default;
        baseAlignment = seedRotation;
        visualRotation = seedRotation;
        if (!PropWallGeometryAnalyzer.TryGetOrAnalyze(analysisRoot, cacheKey, out geometry))
        {
            return false;
        }

        Vector3 backLocalDirection = geometry.DetectedBackLocalDirection;
        if (!geometry.HasDetectedBack)
        {
            backLocalDirection = FindClosestFallbackBackDirection(seedRotation, -wallNormal);
            string warningKey = string.IsNullOrEmpty(cacheKey) ? analysisRoot.name : cacheKey;
            if (_backAnalysisWarnings.Add(warningKey))
            {
                Debug.LogWarning(
                    $"Wall Attach: Could not confidently detect the open back of '{warningKey}'. " +
                    "Using the nearest current horizontal face."
                );
            }

            geometry = new PropWallGeometry(
                geometry.LocalBounds,
                backLocalDirection,
                geometry.Confidence,
                false
            );
        }

        baseAlignment = AlignBackDirectionToWall(
            seedRotation,
            backLocalDirection,
            wallNormal
        );
        visualRotation =
            Quaternion.AngleAxis(_wallUserRotationDegrees, wallNormal) * baseAlignment;
        Debug.Log(
            $"Wall Attach: Detected prop back direction={backLocalDirection}\n" +
            $"Confidence={geometry.Confidence:0.000}."
        );
        return true;
    }

    private bool TryCalculateWallPlacement(
        Vector3 desiredPlayerPosition,
        Quaternion visualRotation,
        Bounds localVisualBounds,
        Collider wallCollider,
        Vector3 wallNormal,
        Vector3 wallPoint,
        out Vector3 safePlayerPosition,
        out float safeDistance)
    {
        safePlayerPosition = desiredPlayerPosition;
        safeDistance = 0f;
        if (wallCollider == null || _characterController == null)
        {
            return false;
        }

        float controllerClearance =
            _characterController.radius + _characterController.skinWidth + wallSurfaceGap;
        float playerDistance = Vector3.Dot(desiredPlayerPosition - wallPoint, wallNormal);
        float minimumVisualDistance = GetMinimumVisualPlaneDistance(
            desiredPlayerPosition,
            visualRotation,
            localVisualBounds,
            wallPoint,
            wallNormal
        );
        float correction = Mathf.Max(
            controllerClearance - playerDistance,
            wallSurfaceGap - minimumVisualDistance
        );
        safePlayerPosition = desiredPlayerPosition + wallNormal * correction;
        safeDistance = Vector3.Dot(safePlayerPosition - wallPoint, wallNormal);

        return CanUseWallPose(
            safePlayerPosition,
            visualRotation,
            localVisualBounds,
            wallCollider,
            wallPoint,
            wallNormal
        );
    }

    private bool CanUseWallPose(
        Vector3 playerPosition,
        Quaternion visualRotation,
        Bounds localVisualBounds,
        Collider attachedWall,
        Vector3 wallPoint,
        Vector3 wallNormal)
    {
        float playerClearance =
            _characterController.radius + _characterController.skinWidth + wallSurfaceGap;
        if (Vector3.Dot(playerPosition - wallPoint, wallNormal) < playerClearance - 0.002f ||
            GetMinimumVisualPlaneDistance(
                playerPosition,
                visualRotation,
                localVisualBounds,
                wallPoint,
                wallNormal) < wallSurfaceGap - 0.002f ||
            !AreVisualCornersInsidePlayableArea(
                playerPosition,
                visualRotation,
                localVisualBounds) ||
            !IsCapsulePositionClear(playerPosition, attachedWall, wallPoint, wallNormal) ||
            !IsVisualPoseClear(
                playerPosition,
                visualRotation,
                localVisualBounds,
                attachedWall,
                wallPoint,
                wallNormal) ||
            !TryFindSupportingWall(
                playerPosition,
                attachedWall,
                wallPoint,
                wallNormal,
                out _))
        {
            return false;
        }

        return true;
    }

    private bool TryFindSupportingWall(
        Vector3 playerPosition,
        Collider expectedWall,
        Vector3 expectedWallPoint,
        Vector3 expectedNormal,
        out RaycastHit wallHit)
    {
        wallHit = default;
        if (expectedWall == null || _characterController == null)
        {
            return false;
        }

        Vector3 capsuleCenterOffset =
            transform.rotation * _characterController.center;
        Vector3 origin = playerPosition + capsuleCenterOffset + expectedNormal * 0.2f;
        float playerPlaneDistance =
            Mathf.Abs(Vector3.Dot(playerPosition - expectedWallPoint, expectedNormal));
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            -expectedNormal,
            Mathf.Max(0.75f, playerPlaneDistance + 0.75f),
            attachSurfaceMask,
            QueryTriggerInteraction.Ignore
        );
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit hit in hits)
        {
            if (!IsValidAttachSurface(hit))
            {
                continue;
            }

            bool sameCollider = hit.collider == expectedWall;
            Vector3 candidateNormal = GetStableOutwardWallNormal(
                hit.normal,
                hit.point,
                expectedNormal,
                playerPosition
            );
            bool coplanarContinuation =
                Vector3.Dot(candidateNormal, expectedNormal) >= 0.94f &&
                Mathf.Abs(Vector3.Dot(
                    hit.point - expectedWallPoint,
                    expectedNormal)) <= 0.12f;
            if (sameCollider || coplanarContinuation)
            {
                wallHit = hit;
                return true;
            }
        }

        return false;
    }

    private bool IsCapsulePositionClear(
        Vector3 playerPosition,
        Collider ignoredWall,
        Vector3 wallPoint,
        Vector3 wallNormal)
    {
        Vector3 worldCenter =
            playerPosition + transform.rotation * _characterController.center;
        float radius = _characterController.radius;
        float halfLine = Mathf.Max(0f, _characterController.height * 0.5f - radius);
        Vector3 top = worldCenter + Vector3.up * halfLine;
        Vector3 bottom = worldCenter - Vector3.up * halfLine;
        float capsuleBottomY = worldCenter.y - _characterController.height * 0.5f;
        Collider[] overlaps = Physics.OverlapCapsule(
            top,
            bottom,
            radius,
            ~0,
            QueryTriggerInteraction.Ignore
        );

        foreach (Collider overlap in overlaps)
        {
            if (overlap == null ||
                overlap == ignoredWall ||
                overlap.transform == transform ||
                overlap.transform.IsChildOf(transform) ||
                IsColliderOnSupportingPlane(overlap, worldCenter, wallPoint, wallNormal, radius) ||
                overlap.bounds.max.y <= capsuleBottomY + 0.08f)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private bool IsVisualPoseClear(
        Vector3 playerPosition,
        Quaternion visualRotation,
        Bounds localVisualBounds,
        Collider ignoredWall,
        Vector3 wallPoint,
        Vector3 wallNormal)
    {
        Vector3 visualPosition = GetVisualPositionForPlayer(playerPosition);
        Vector3 worldCenter = visualPosition + visualRotation * localVisualBounds.center;
        Vector3 overlapExtents = Vector3.Max(localVisualBounds.extents - Vector3.one * 0.01f, Vector3.one * 0.01f);
        Collider[] overlaps = Physics.OverlapBox(
            worldCenter,
            overlapExtents,
            visualRotation,
            ~0,
            QueryTriggerInteraction.Ignore
        );
        Vector3[] worldCorners = GetVisualWorldCorners(
            playerPosition,
            visualRotation,
            localVisualBounds
        );
        float minimumY = worldCorners[0].y;
        foreach (Vector3 corner in worldCorners)
        {
            minimumY = Mathf.Min(minimumY, corner.y);
        }

        foreach (Collider overlap in overlaps)
        {
            if (overlap == null ||
                overlap == ignoredWall ||
                overlap.transform == transform ||
                overlap.transform.IsChildOf(transform) ||
                IsColliderOnSupportingPlane(
                    overlap,
                    worldCenter,
                    wallPoint,
                    wallNormal,
                    Vector3.Dot(overlapExtents, AbsVector(wallNormal))) ||
                overlap.bounds.max.y <= minimumY + 0.08f)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private bool IsColliderOnSupportingPlane(
        Collider candidate,
        Vector3 poseCenter,
        Vector3 wallPoint,
        Vector3 wallNormal,
        float castPadding)
    {
        if (candidate == null ||
            candidate.isTrigger ||
            candidate.GetComponentInParent<CharacterController>() != null ||
            (candidate.attachedRigidbody != null &&
             !candidate.attachedRigidbody.isKinematic))
        {
            return false;
        }

        float rayDistance = Mathf.Max(1f, castPadding * 2f + 1f);
        Ray ray = new Ray(
            poseCenter + wallNormal * (castPadding + 0.5f),
            -wallNormal
        );
        return candidate.Raycast(ray, out RaycastHit hit, rayDistance) &&
               Vector3.Dot(hit.normal.normalized, wallNormal) >= 0.94f &&
               Mathf.Abs(Vector3.Dot(hit.point - wallPoint, wallNormal)) <= 0.12f;
    }

    private float GetMinimumVisualPlaneDistance(
        Vector3 playerPosition,
        Quaternion visualRotation,
        Bounds localVisualBounds,
        Vector3 wallPoint,
        Vector3 wallNormal)
    {
        Vector3[] corners = GetVisualWorldCorners(
            playerPosition,
            visualRotation,
            localVisualBounds
        );
        float minimumDistance = float.PositiveInfinity;
        foreach (Vector3 corner in corners)
        {
            minimumDistance = Mathf.Min(
                minimumDistance,
                Vector3.Dot(corner - wallPoint, wallNormal)
            );
        }

        return minimumDistance;
    }

    private bool AreVisualCornersInsidePlayableArea(
        Vector3 playerPosition,
        Quaternion visualRotation,
        Bounds localVisualBounds)
    {
        if (!IsInsidePlayableArea(playerPosition))
        {
            return false;
        }

        if (playableAreaBounds == null)
        {
            return true;
        }

        foreach (Vector3 corner in GetVisualWorldCorners(
                     playerPosition,
                     visualRotation,
                     localVisualBounds))
        {
            if (!playableAreaBounds.Contains(corner))
            {
                return false;
            }
        }

        return true;
    }

    private Vector3[] GetVisualWorldCorners(
        Vector3 playerPosition,
        Quaternion visualRotation,
        Bounds localVisualBounds)
    {
        Vector3 minimum = localVisualBounds.min;
        Vector3 maximum = localVisualBounds.max;
        Vector3 visualPosition = GetVisualPositionForPlayer(playerPosition);
        Vector3[] corners = new Vector3[8];
        int index = 0;
        for (int x = 0; x <= 1; x++)
        {
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z <= 1; z++)
                {
                    Vector3 localCorner = new Vector3(
                        x == 0 ? minimum.x : maximum.x,
                        y == 0 ? minimum.y : maximum.y,
                        z == 0 ? minimum.z : maximum.z
                    );
                    corners[index++] = visualPosition + visualRotation * localCorner;
                }
            }
        }

        return corners;
    }

    private Vector3 GetVisualPositionForPlayer(Vector3 playerPosition)
    {
        return propVisualRoot != null
            ? playerPosition + (propVisualRoot.position - transform.position)
            : playerPosition;
    }

    private static Quaternion AlignBackDirectionToWall(
        Quaternion seedRotation,
        Vector3 backLocalDirection,
        Vector3 wallNormal)
    {
        Vector3 currentBackWorldDirection = seedRotation * backLocalDirection.normalized;
        Vector3 desiredBackWorldDirection = -wallNormal.normalized;
        return Quaternion.FromToRotation(
            currentBackWorldDirection,
            desiredBackWorldDirection
        ) * seedRotation;
    }

    private static Vector3 FindClosestFallbackBackDirection(
        Quaternion seedRotation,
        Vector3 desiredBackWorldDirection)
    {
        Vector3[] localDirections =
        {
            Vector3.forward,
            Vector3.back,
            Vector3.right,
            Vector3.left
        };
        Vector3 bestDirection = localDirections[0];
        float bestDot = Vector3.Dot(seedRotation * bestDirection, desiredBackWorldDirection);
        for (int index = 1; index < localDirections.Length; index++)
        {
            float dot = Vector3.Dot(
                seedRotation * localDirections[index],
                desiredBackWorldDirection
            );
            if (dot > bestDot)
            {
                bestDot = dot;
                bestDirection = localDirections[index];
            }
        }

        return bestDirection;
    }

    private static Vector3 AbsVector(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private bool IsInsidePlayableArea(Vector3 worldPosition)
    {
        return playableAreaBounds == null ||
               playableAreaBounds.Contains(
                   worldPosition,
                   _characterController != null ? _characterController.radius : 0f
               );
    }

    private static float ReadPropRotationInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            return
                (Keyboard.current.rightArrowKey.isPressed ? 1f : 0f) -
                (Keyboard.current.leftArrowKey.isPressed ? 1f : 0f);
        }
#endif
        return 0f;
    }

    private bool CanEnterGhostCamera()
    {
        if (!CanAttemptEnterGhostCamera() ||
            cameraModeManager == null ||
            cameraModeManager.tpsCamera == null)
        {
            return false;
        }

        return IsWallAttached || IsHiderGrounded();
    }

    private bool CanAttemptEnterGhostCamera()
    {
        return !IsGhostCameraActive &&
               playerRole == PlayerRole.Hider &&
               !IsEliminated &&
               !IsChangingModel &&
               currentState == PlayerDisguiseState.Disguised &&
               (roundManager == null || roundManager.IsAbilityPhaseActive());
    }

    private bool CanRemainInGhostCamera()
    {
        return !IsEliminated &&
               currentState == PlayerDisguiseState.Disguised &&
               (roundManager == null || roundManager.IsAbilityPhaseActive());
    }

    private bool IsHiderGrounded()
    {
        if (_characterController != null)
        {
            return _characterController.isGrounded;
        }

        return _firstPersonController != null && _firstPersonController.Grounded;
    }

    private void TryEnterGhostCamera()
    {
        if (!CanEnterGhostCamera())
        {
            if (currentState == PlayerDisguiseState.Disguised &&
                !IsWallAttached &&
                !IsHiderGrounded())
            {
                Debug.Log("GhostCamera: Cannot enter while airborne.");
            }

            return;
        }

        _lockedHiderPosition = transform.position;
        _lockedHiderRotation = transform.rotation;
        _cameraModeBeforeGhost = cameraModeManager.CurrentMode;
        _firstPersonControllerWasEnabled =
            _firstPersonController != null && _firstPersonController.enabled;
        _controllerWasAlreadyLocked =
            _firstPersonController != null && _firstPersonController.IsControlLocked;
        _cursorLockModeBeforeGhost = Cursor.lockState;
        _cursorVisibleBeforeGhost = Cursor.visible;

        if (!cameraModeManager.BeginGhostCamera(_lockedHiderPosition, transform))
        {
            return;
        }

        IsGhostCameraActive = true;
        if (_firstPersonController != null)
        {
            _firstPersonController.SetControlLocked(true);
        }

        ClearGameplayInputForGhostCamera();
        Debug.Log(
            $"GhostCamera: Entered. Anchor={_lockedHiderPosition}, Radius=10m."
        );
    }

    public void ForceExitGhostCamera()
    {
        if (!IsGhostCameraActive)
        {
            return;
        }

        IsGhostCameraActive = false;
        transform.SetPositionAndRotation(_lockedHiderPosition, _lockedHiderRotation);

        if (_firstPersonController != null)
        {
            _firstPersonController.enabled = _firstPersonControllerWasEnabled;
            _firstPersonController.SetControlLocked(_controllerWasAlreadyLocked);
        }

        ClearGameplayInputForGhostCamera();

        if (cameraModeManager != null)
        {
            cameraModeManager.SetMode(_cameraModeBeforeGhost);
            if (_cameraModeBeforeGhost == PlayerCameraMode.PropTPS)
            {
                cameraModeManager.RefreshCurrentPropCamera();
                cameraModeManager.ForceCameraToSafePosition();
            }
        }

        Cursor.lockState = _cursorLockModeBeforeGhost;
        Cursor.visible = _cursorVisibleBeforeGhost;
        Debug.Log("GhostCamera: Exited. Player controls restored.");
    }

    private void KeepHiderLockedInPlace()
    {
        if ((transform.position - _lockedHiderPosition).sqrMagnitude > 0.000001f ||
            Quaternion.Angle(transform.rotation, _lockedHiderRotation) > 0.001f)
        {
            transform.SetPositionAndRotation(_lockedHiderPosition, _lockedHiderRotation);
        }
    }

    private void ClearGameplayInputForGhostCamera()
    {
        if (_input == null)
        {
            return;
        }

        _input.move = Vector2.zero;
        _input.look = Vector2.zero;
        _input.jump = false;
        _input.sprint = false;
        _input.interact = false;
        _input.cancelDisguise = false;
        _input.spectatorToggle = false;
    }

    private void HandleRoundStateChanged(PropHuntRoundState state)
    {
        if (state == PropHuntRoundState.Waiting ||
            state == PropHuntRoundState.Preparation ||
            state == PropHuntRoundState.Ended)
        {
            ForceExitGhostCamera();
            ForceDetachFromWall();
            ResetWallMovementBasis();
            if (cameraModeManager != null)
            {
                cameraModeManager.ResetAdaptiveCamera();
            }
        }
    }

    public bool TryGetLookedAtProp(out PropTarget prop)
    {
        return TryGetLookedAtProp(out prop, out _);
    }

    public bool TryGetLookedAtProp(out PropTarget prop, out GameObject sourceVisual)
    {
        prop = null;
        sourceVisual = null;
        Camera rayCamera = mainCamera != null ? mainCamera : Camera.main;
        if (rayCamera == null)
        {
            return false;
        }

        Ray ray = new Ray(rayCamera.transform.position, rayCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, interactionDistance, interactableLayers, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        prop = hit.collider.GetComponentInParent<PropTarget>();
        if (prop == null || !prop.GameplayEnabled)
        {
            prop = null;
            sourceVisual = null;
            return false;
        }

        sourceVisual = ResolveSourceVisual(prop, hit);
        return true;
    }

    private void LogLookedAtProp()
    {
        if (!TryGetLookedAtProp(out PropTarget prop))
        {
            _lastSeenProp = null;
            return;
        }

        if (_lastSeenProp == prop)
        {
            return;
        }

        _lastSeenProp = prop;
        Debug.Log($"PropTransformSystem: looking at prop '{prop.displayName}' ({prop.propId}).");
    }

    private void SetCamera(PlayerCameraMode mode)
    {
        if (cameraModeManager != null)
        {
            cameraModeManager.SetMode(mode);
        }
    }

    private void SetHumanVisualActive(bool active)
    {
        if (humanVisualRoot != null)
        {
            humanVisualRoot.gameObject.SetActive(active);
        }
    }

    private void SetPropVisualActive(bool active)
    {
        if (propVisualRoot != null)
        {
            propVisualRoot.gameObject.SetActive(active);
        }
    }

    private void SetFirstPersonControllerEnabled(bool enabled)
    {
        if (_firstPersonController != null)
        {
            _firstPersonController.enabled = enabled;
        }
    }

    private void SetMovementComponentsEnabled(bool enabled)
    {
        SetFirstPersonControllerEnabled(enabled);

        if (_characterController != null)
        {
            _characterController.enabled = enabled;
        }

        if (_input != null)
        {
            _input.enabled = enabled;
        }
    }

    private void LogMovementState()
    {
        Debug.Log(
            $"Movement state: " +
            $"FirstPersonController={_firstPersonController != null && _firstPersonController.enabled}, " +
            $"CharacterController={_characterController != null && _characterController.enabled}, " +
            $"StarterAssetsInputs={_input != null && _input.enabled}, " +
            $"state={currentState}"
        );
    }

    private void EnsureDisguisedMovementSettings()
    {
        if (_firstPersonController != null)
        {
            _firstPersonController.MoveSpeed = Mathf.Max(_firstPersonController.MoveSpeed, 4f);
            _firstPersonController.SprintSpeed = Mathf.Max(_firstPersonController.SprintSpeed, 6f);
        }
    }

    private void LogDisguisedMovement()
    {
        if (Time.time < _nextMovementLogTime)
        {
            return;
        }

        _nextMovementLogTime = Time.time + 0.5f;
        Vector2 moveInput = _input != null ? _input.move : Vector2.zero;
        Vector3 velocity = _characterController != null
            ? _characterController.velocity
            : Vector3.zero;

        Debug.Log(
            $"Disguised movement test: " +
            $"input={moveInput}, " +
            $"velocity={velocity}, " +
            $"playerPosition={transform.position}, " +
            $"movedDistance={Vector3.Distance(_disguiseStartPlayerPosition, transform.position)}"
        );
    }

    private void ClearPropVisual()
    {
        if (_currentPropVisual != null)
        {
            Destroy(_currentPropVisual);
            _currentPropVisual = null;
        }

        if (_debugMaterial != null)
        {
            Destroy(_debugMaterial);
            _debugMaterial = null;
        }
    }

    public bool TryCreateDetachedVisualCopy(
        Transform parent,
        out GameObject detachedVisualRoot)
    {
        detachedVisualRoot = null;
        if (parent == null ||
            !IsDisguised ||
            IsChangingModel ||
            propVisualRoot == null ||
            _currentPropVisual == null ||
            !_currentPropVisual.activeInHierarchy)
        {
            return false;
        }

        GameObject root = new GameObject("CloneVisualRoot");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        GameObject visualCopy = Instantiate(_currentPropVisual, root.transform, false);
        visualCopy.name = "CapturedPropVisual";
        StripPhysicsAndGameplayComponents(visualCopy);
        ClearStaticFlagsRuntime(visualCopy);
        ActivateRenderers(visualCopy);

        Renderer[] renderers = visualCopy.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0 || ContainsCombinedMesh(visualCopy))
        {
            Destroy(root);
            return false;
        }

        root.SetActive(true);
        visualCopy.SetActive(true);
        detachedVisualRoot = root;
        return true;
    }

    private static bool ContainsCombinedMesh(GameObject root)
    {
        if (root == null)
        {
            return false;
        }

        foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (meshFilter != null && IsCombinedMesh(meshFilter.sharedMesh))
            {
                return true;
            }
        }

        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer != null && IsCombinedMesh(renderer.sharedMesh))
            {
                return true;
            }
        }

        return false;
    }

    private static GameObject CreatePropModelFromVisualParts(PropTarget prop, Transform parent)
    {
        if (prop == null || prop.visualParts == null || prop.visualParts.Length == 0 || parent == null)
        {
            return null;
        }

        GameObject model = new GameObject($"{prop.displayName}_Model");
        model.transform.SetParent(parent, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;
        model.name = $"{prop.displayName}_Model";

        int createdRendererCount = 0;
        for (int index = 0; index < prop.visualParts.Length; index++)
        {
            PropVisualPartData visualPart = prop.visualParts[index];
            if (visualPart == null || visualPart.mesh == null)
            {
                continue;
            }

            bool isCombinedMesh = IsCombinedMesh(visualPart.mesh);
            Debug.Log($"PropTransformSystem: visual part mesh: {visualPart.mesh.name}");
            Debug.Log($"PropTransformSystem: runtime mesh is Combined Mesh: {isCombinedMesh}");
            if (isCombinedMesh)
            {
                Debug.LogError(
                    $"PropTransformSystem: rejected visual part {index} for '{prop.displayName}' because mesh " +
                    $"'{visualPart.mesh.name}' is a Static Combined Mesh. Run the Prop Hunt setup tool again."
                );
                continue;
            }

            GameObject visualObject = new GameObject($"VisualPart_{index}_{visualPart.mesh.name}");
            Transform visualTransform = visualObject.transform;
            visualTransform.SetParent(model.transform, false);
            visualTransform.localPosition = visualPart.localPosition;
            visualTransform.localRotation = Quaternion.Euler(visualPart.localEulerAngles);
            visualTransform.localScale = visualPart.localScale;
            visualObject.isStatic = false;

            MeshFilter meshFilter = visualObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = visualPart.mesh;

            MeshRenderer meshRenderer = visualObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = visualPart.materials ?? new Material[0];
            meshRenderer.enabled = true;
            createdRendererCount++;
        }

        if (createdRendererCount == 0)
        {
            Debug.LogError($"PropTransformSystem: '{prop.displayName}' has no valid prefab visual parts.");
            Destroy(model);
            return null;
        }

        ClearStaticFlagsRuntime(model);
        model.SetActive(true);
        return model;
    }

    private static bool IsCombinedMesh(Mesh mesh)
    {
        return mesh != null &&
               mesh.name.IndexOf("Combined Mesh", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void ClearStaticFlagsRuntime(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.isStatic = false;
        }
    }

    private static bool ValidateOriginalPropModelBounds(GameObject model)
    {
        if (model == null)
        {
            return false;
        }

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogError($"PropTransformSystem: model '{model.name}' has no Renderer.");
            return false;
        }

        Bounds bounds = CalculateRendererBounds(renderers);
        Vector3 size = bounds.size;
        Debug.Log($"PropTransformSystem: original prop model bounds size: {size}");
        if (size.x > 20f || size.y > 20f || size.z > 20f)
        {
            Debug.LogError(
                $"PropTransformSystem: rejected '{model.name}' because bounds {size} exceed 20 metres. " +
                "The source is probably a Static Combined Mesh."
            );
            return false;
        }

        return true;
    }

    private static GameObject ResolveSourceVisual(PropTarget prop, RaycastHit hit)
    {
        Transform hitTransform = hit.collider.transform;

        if (HasRenderableRenderer(hitTransform.gameObject))
        {
            return hitTransform.gameObject;
        }

        Renderer childRenderer = hit.collider.GetComponentInChildren<Renderer>(true);
        if (childRenderer != null && HasRenderableRenderer(childRenderer.gameObject))
        {
            return childRenderer.gameObject;
        }

        Renderer parentRenderer = hit.collider.GetComponentInParent<Renderer>();
        if (parentRenderer != null && HasRenderableRenderer(parentRenderer.gameObject))
        {
            return parentRenderer.gameObject;
        }

        Transform closestChildRenderer = FindClosestRenderableChild(prop.transform, hit.point);
        if (closestChildRenderer != null)
        {
            return closestChildRenderer.gameObject;
        }

        return prop.gameObject;
    }

    private static Transform FindClosestRenderableChild(Transform root, Vector3 hitPoint)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Transform closest = null;
        float closestDistance = float.PositiveInfinity;

        foreach (Renderer renderer in renderers)
        {
            if (!HasRenderableRenderer(renderer.gameObject))
            {
                continue;
            }

            float distance = (renderer.bounds.ClosestPoint(hitPoint) - hitPoint).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = renderer.transform;
            }
        }

        return closest;
    }

    private static bool HasRenderableRenderer(GameObject gameObject)
    {
        MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = gameObject.GetComponent<MeshRenderer>();
        if (meshFilter != null && meshFilter.sharedMesh != null && meshRenderer != null && meshRenderer.sharedMaterial != null)
        {
            return true;
        }

        SkinnedMeshRenderer skinnedMeshRenderer = gameObject.GetComponent<SkinnedMeshRenderer>();
        return skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null && skinnedMeshRenderer.sharedMaterial != null;
    }

    private static void ActivateRenderers(GameObject clone)
    {
        if (clone == null)
        {
            return;
        }

        foreach (Transform child in clone.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.SetActive(true);
        }

        foreach (Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
            renderer.gameObject.SetActive(true);
            renderer.gameObject.isStatic = false;
        }

        foreach (LODGroup lodGroup in clone.GetComponentsInChildren<LODGroup>(true))
        {
            lodGroup.enabled = true;
            lodGroup.RecalculateBounds();
        }

        clone.SetActive(true);
    }

    private static void StripPhysicsAndGameplayComponents(GameObject clone)
    {
        if (clone == null)
        {
            return;
        }

        foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true))
        {
            Destroy(collider);
        }

        foreach (Rigidbody rigidbody in clone.GetComponentsInChildren<Rigidbody>(true))
        {
            Destroy(rigidbody);
        }

        foreach (Rigidbody2D rigidbody in clone.GetComponentsInChildren<Rigidbody2D>(true))
        {
            Destroy(rigidbody);
        }

        foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
            {
                continue;
            }

            behaviour.enabled = false;
            Destroy(behaviour);
        }
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (target == null)
        {
            return;
        }

        foreach (Transform child in target.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = layer;
        }
    }

    private void LogCloneVisibilityDiagnostics(PropTarget prop, GameObject sceneSource, GameObject clone)
    {
        if (clone == null)
        {
            Debug.LogError("PropTransformSystem: PropVisualClone is null after copy.");
            return;
        }

        MeshRenderer[] meshRenderers = clone.GetComponentsInChildren<MeshRenderer>(true);
        SkinnedMeshRenderer[] skinnedMeshRenderers = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = CalculateRendererBounds(renderers);

        Debug.Log($"Visual parts: {(prop != null && prop.visualParts != null ? prop.visualParts.Length : 0)}");
        Debug.Log($"Scene source: {(sceneSource != null ? sceneSource.name : "null")}");
        Debug.Log($"Clone: {clone.name}");
        Debug.Log($"Clone active: {clone.activeInHierarchy}");
        Debug.Log($"Clone static: {clone.isStatic}");
        Debug.Log($"Renderer count: {renderers.Length}");
        Debug.Log($"PropTransformSystem: MeshRenderers: {meshRenderers.Length}");
        Debug.Log($"PropTransformSystem: SkinnedMeshRenderers: {skinnedMeshRenderers.Length}");
        Debug.Log($"PropTransformSystem: Clone activeSelf: {clone.activeSelf}, activeInHierarchy: {clone.activeInHierarchy}");
        Debug.Log($"PropTransformSystem: Clone localPosition: {clone.transform.localPosition}, localRotation: {clone.transform.localRotation.eulerAngles}, localScale: {clone.transform.localScale}");
        Debug.Log($"PropTransformSystem: Clone bounds size: {bounds.size}");

        if (propVisualRoot != null)
        {
            Debug.Log($"PropTransformSystem: PropVisualRoot activeSelf: {propVisualRoot.gameObject.activeSelf}, activeInHierarchy: {propVisualRoot.gameObject.activeInHierarchy}");
        }

        if (renderers.Length == 0)
        {
            Debug.LogError($"PropTransformSystem: clone '{clone.name}' has no Renderer. Visual parts are missing or invalid.");
            return;
        }

        foreach (Renderer renderer in renderers)
        {
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            SkinnedMeshRenderer skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
            bool hasMesh = (meshFilter != null && meshFilter.sharedMesh != null) || (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null);
            bool hasMaterial = renderer.sharedMaterial != null;

            Debug.Log(
                $"Renderer={renderer.name}, " +
                $"enabled={renderer.enabled}, " +
                $"active={renderer.gameObject.activeInHierarchy}, " +
                $"static={renderer.gameObject.isStatic}, " +
                $"bounds={renderer.bounds}, " +
                $"material={renderer.sharedMaterial?.name}"
            );

            if (!renderer.enabled || !hasMesh || !hasMaterial)
            {
                Debug.LogWarning($"PropTransformSystem: renderer '{renderer.name}' visible check: enabled={renderer.enabled}, hasMesh={hasMesh}, hasMaterial={hasMaterial}, layer={renderer.gameObject.layer}.");
            }

            Camera tpsCamera = cameraModeManager != null ? cameraModeManager.tpsCamera : null;
            if (tpsCamera != null && (tpsCamera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
            {
                Debug.LogWarning($"PropTransformSystem: TPS Camera culling mask does not include clone layer {renderer.gameObject.layer} on '{renderer.name}'.");
            }
        }
    }

    private void AlignCloneBottomToPlayerFeet(GameObject clone)
    {
        Renderer[] renderers = clone != null ? clone.GetComponentsInChildren<Renderer>(true) : null;
        if (renderers == null || renderers.Length == 0)
        {
            return;
        }

        Bounds bounds = CalculateRendererBounds(renderers);
        float bottomOffsetFromPlayer = bounds.min.y - transform.position.y;
        if (bottomOffsetFromPlayer < -0.05f)
        {
            clone.transform.localPosition += Vector3.up * -bottomOffsetFromPlayer;
            Debug.Log($"PropTransformSystem: lifted clone by {-bottomOffsetFromPlayer} so renderer bounds bottom is near Player feet.");
        }
    }

    private bool CenterModelOnPlayer(GameObject model)
    {
        if (model == null)
        {
            return false;
        }

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogError("Disguise model has no Renderer.");
            return false;
        }

        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        Physics.SyncTransforms();

        Bounds initialBounds = CalculateRendererBounds(renderers);
        Vector3 modelPosition = model.transform.position;
        model.transform.position = new Vector3(
            modelPosition.x + transform.position.x - initialBounds.center.x,
            modelPosition.y,
            modelPosition.z + transform.position.z - initialBounds.center.z
        );

        Physics.SyncTransforms();

        Bounds boundsBeforeVerticalCorrection = CalculateRendererBounds(renderers);
        float expectedBottomY = GetPlayerFeetWorldY();
        float verticalCorrection = expectedBottomY - boundsBeforeVerticalCorrection.min.y;
        model.transform.position += Vector3.up * verticalCorrection;

        Physics.SyncTransforms();

        Bounds finalBounds = CalculateRendererBounds(renderers);
        if (Mathf.Abs(finalBounds.center.y - transform.position.y) > 3f)
        {
            Debug.LogWarning("PropTransformSystem: model bounds Y is abnormal. Applying safe Player-feet fallback.");
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.position = new Vector3(
                transform.position.x,
                expectedBottomY,
                transform.position.z
            );
            Physics.SyncTransforms();
            Bounds fallbackBounds = CalculateRendererBounds(renderers);
            model.transform.position = new Vector3(
                model.transform.position.x + transform.position.x - fallbackBounds.center.x,
                model.transform.position.y + expectedBottomY - fallbackBounds.min.y,
                model.transform.position.z + transform.position.z - fallbackBounds.center.z
            );
            Physics.SyncTransforms();
            finalBounds = CalculateRendererBounds(renderers);
        }

        float bottomError = Mathf.Abs(finalBounds.min.y - expectedBottomY);
        float centerXError = Mathf.Abs(finalBounds.center.x - transform.position.x);
        float centerZError = Mathf.Abs(finalBounds.center.z - transform.position.z);

        Debug.Log($"Player Y: {transform.position.y}");
        Debug.Log($"Player feet Y: {expectedBottomY}");
        Debug.Log($"Model world position: {model.transform.position}");
        Debug.Log($"Model localPosition: {model.transform.localPosition}");
        Debug.Log($"Bounds min Y before: {boundsBeforeVerticalCorrection.min.y}");
        Debug.Log($"Vertical correction: {verticalCorrection}");
        Debug.Log($"Bounds min Y after: {finalBounds.min.y}");
        Debug.Log($"Bounds center Y after: {finalBounds.center.y}");
        Debug.Log($"Final bounds center: {finalBounds.center}");
        Debug.Log($"Final bounds bottom: {finalBounds.min.y}");
        Debug.Log($"Expected player feet: {expectedBottomY}");
        Debug.Log($"Bottom Y error: {bottomError}");
        Debug.Log($"Center X error: {centerXError}");
        Debug.Log($"Center Z error: {centerZError}");

        bool centered =
            centerXError < 0.05f &&
            centerZError < 0.05f &&
            bottomError < 0.1f;
        if (!centered)
        {
            Debug.LogError("Clone centering failed, including vertical alignment. Camera target will not be assigned.");
        }

        return centered;
    }

    private float GetPlayerFeetWorldY()
    {
        CharacterController controller = _characterController != null
            ? _characterController
            : GetComponent<CharacterController>();
        if (controller != null)
        {
            Vector3 worldCenter = transform.TransformPoint(controller.center);
            return worldCenter.y - controller.height * 0.5f;
        }

        return transform.position.y;
    }

    [ContextMenu("Apply Red Debug Material To Current Clone")]
    public void ApplyDebugMaterialToCurrentClone()
    {
        if (_currentPropVisual == null)
        {
            Debug.LogWarning("PropTransformSystem: there is no current prop clone for material debugging.");
            return;
        }

        Shader shader = Shader.Find("Standard");
        if (shader == null)
        {
            Debug.LogError("PropTransformSystem: Standard shader was not found for the red debug material.");
            return;
        }

        if (_debugMaterial != null)
        {
            Destroy(_debugMaterial);
        }

        _debugMaterial = new Material(shader)
        {
            name = "PropClone_RedDebugMaterial",
            color = Color.red
        };

        foreach (Renderer renderer in _currentPropVisual.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterial = _debugMaterial;
        }

        Debug.Log("PropTransformSystem: applied red Standard debug material to the current prop clone.");
    }

    private static Bounds CalculateRendererBounds(Renderer[] renderers)
    {
        if (renderers == null || renderers.Length == 0)
        {
            return new Bounds(Vector3.zero, Vector3.zero);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }
}
