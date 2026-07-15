using StarterAssets;
using UnityEngine;

public class PropTransformSystem : MonoBehaviour
{
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

    public Transform CurrentPropVisualTransform =>
        _currentPropVisual != null ? _currentPropVisual.transform : null;
    public bool IsEliminated { get; private set; }

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

    private void Start()
    {
        BecomeHuman(false);
    }

    private void Update()
    {
        if (playerRole != PlayerRole.Hider || _input == null)
        {
            return;
        }

        if (_input.cancelDisguise)
        {
            if (!IsEliminated && currentState != PlayerDisguiseState.Human)
            {
                BecomeHuman(true);
            }

            _input.cancelDisguise = false;
        }

        if (_input.spectatorToggle)
        {
            ToggleSpectator();
            _input.spectatorToggle = false;
        }

        if (_input.interact)
        {
            if (currentState == PlayerDisguiseState.Human && TryGetLookedAtProp(out PropTarget prop, out GameObject sourceVisual))
            {
                BecomeProp(prop, sourceVisual);
            }

            _input.interact = false;
        }

        if (currentState == PlayerDisguiseState.Human)
        {
            LogLookedAtProp();
        }

        if (currentState == PlayerDisguiseState.Disguised)
        {
            LogDisguisedMovement();
        }
    }

    public bool IsSpectatorActive()
    {
        return currentState == PlayerDisguiseState.Spectator;
    }

    public void SetEliminated(bool eliminated)
    {
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
            currentState = PlayerDisguiseState.Spectator;
            SetFirstPersonControllerEnabled(false);
            SetCamera(PlayerCameraMode.Spectator);
        }
        else if (currentState == PlayerDisguiseState.Spectator)
        {
            BecomeHuman(false);
        }

        if (roundManager != null)
        {
            roundManager.RefreshPlayerCounts();
        }
    }

    public bool ApplyPropDefinition(PropTarget propDefinition, bool keepCurrentCameraMode = true)
    {
        if (propDefinition == null ||
            currentState != PlayerDisguiseState.Disguised ||
            IsEliminated ||
            propVisualRoot == null ||
            !IsValidRandomPropDefinition(propDefinition))
        {
            return false;
        }

        Vector3 playerPosition = transform.position;
        GameObject previousVisual = _currentPropVisual;

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
            transform.position = playerPosition;
            return false;
        }

        transform.position = playerPosition;
        _currentPropVisual = candidatePivot;
        currentPropId = propDefinition.propId;

        if (previousVisual != null)
        {
            Destroy(previousVisual);
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
        if (definition.visualParts == null || definition.visualParts.Length == 0)
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
        if (prop == null)
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
        SetMovementComponentsEnabled(true);
        EnsureDisguisedMovementSettings();
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
        if (cameraModeManager != null)
        {
            cameraModeManager.ClearPropTarget();
        }

        ClearPropVisual();
        SetPropVisualActive(false);
        SetHumanVisualActive(true);

        currentPropId = string.Empty;
        currentState = PlayerDisguiseState.Human;
        SetFirstPersonControllerEnabled(true);
        SetCamera(PlayerCameraMode.HumanFPS);

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
        if (prop == null)
        {
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
