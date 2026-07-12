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

    public string currentPropId;
    public PlayerDisguiseState currentState = PlayerDisguiseState.Human;

    private StarterAssetsInputs _input;
    private FirstPersonController _firstPersonController;
    private GameObject _currentPropVisual;
    private PropTarget _lastSeenProp;

    private void Awake()
    {
        _input = GetComponent<StarterAssetsInputs>();
        _firstPersonController = GetComponent<FirstPersonController>();

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
            if (currentState != PlayerDisguiseState.Human)
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
    }

    public bool IsSpectatorActive()
    {
        return currentState == PlayerDisguiseState.Spectator;
    }

    private void BecomeProp(PropTarget prop, GameObject sourceVisual)
    {
        if (prop == null)
        {
            return;
        }

        ClearPropVisual();

        GameObject visualSource = sourceVisual != null ? sourceVisual : prop.visualPrefab;
        Vector3 originalPositionBeforeCopy = visualSource != null ? visualSource.transform.position : Vector3.zero;

        Transform cloneParent = propVisualRoot != null ? propVisualRoot : transform;
        SetPropVisualActive(true);

        _currentPropVisual = CreateRenderOnlyClone(visualSource, cloneParent);
        _currentPropVisual.transform.localPosition = prop.visualOffset;
        _currentPropVisual.transform.localRotation = Quaternion.Euler(prop.visualRotationOffset);
        _currentPropVisual.transform.localScale = Vector3.one * Mathf.Max(0.01f, prop.visualScale);
        _currentPropVisual.name = visualSource != null ? $"{visualSource.name}_DisguiseClone" : $"{prop.displayName}_DisguiseClone";
        SetLayerRecursively(_currentPropVisual, 0);
        ActivateRenderers(_currentPropVisual);
        StripNonVisualComponents(_currentPropVisual);
        AlignCloneBottomToPlayerFeet(_currentPropVisual);

        SetHumanVisualActive(false);

        currentPropId = prop.propId;
        currentState = PlayerDisguiseState.Disguised;
        SetFirstPersonControllerEnabled(true);
        SetCamera(PlayerCameraMode.PropTPS);

        if (visualSource != null)
        {
            Debug.Log($"PropTransformSystem: Copy source: {visualSource.name}, target prop: {prop.displayName}.");
            Debug.Log($"PropTransformSystem: Original before copy: {originalPositionBeforeCopy}, after copy: {visualSource.transform.position}.");
            Debug.Log($"PropTransformSystem: Created clone under PropVisualRoot: {_currentPropVisual.name}.");
        }

        LogCloneVisibilityDiagnostics(visualSource, _currentPropVisual);
        Debug.Log($"PropTransformSystem: transformed into prop '{prop.displayName}' ({currentPropId}).");
    }

    private void BecomeHuman(bool log)
    {
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
        if (currentState == PlayerDisguiseState.Human)
        {
            return;
        }

        if (currentState == PlayerDisguiseState.Spectator)
        {
            currentState = PlayerDisguiseState.Disguised;
            SetFirstPersonControllerEnabled(true);
            SetCamera(PlayerCameraMode.PropTPS);
            Debug.Log("PropTransformSystem: switched from spectator camera to prop TPS camera.");
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

    private void ClearPropVisual()
    {
        if (_currentPropVisual != null)
        {
            Destroy(_currentPropVisual);
            _currentPropVisual = null;
        }
    }

    private static GameObject CreateRenderOnlyClone(GameObject sourceRoot, Transform parent)
    {
        if (sourceRoot == null)
        {
            GameObject cloneRoot = new GameObject("PropVisualClone");
            cloneRoot.transform.SetParent(parent, false);
            return cloneRoot;
        }

        GameObject clone = Instantiate(sourceRoot);
        clone.transform.SetParent(parent, false);
        clone.SetActive(true);
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;
        return clone;
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

        return prop.visualPrefab;
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
            EnsureRendererMaterialsVisible(renderer);
        }

        clone.SetActive(true);
    }

    private static void StripNonVisualComponents(GameObject clone)
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

    private void LogCloneVisibilityDiagnostics(GameObject visualSource, GameObject clone)
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

        Debug.Log($"PropTransformSystem: Source visual: {(visualSource != null ? visualSource.name : "null")}");
        Debug.Log($"PropTransformSystem: Clone created: {clone.name}");
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
            Debug.LogError($"PropTransformSystem: clone '{clone.name}' has no Renderer. Source visual was '{(visualSource != null ? visualSource.name : "null")}'.");
            return;
        }

        foreach (Renderer renderer in renderers)
        {
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            SkinnedMeshRenderer skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
            bool hasMesh = (meshFilter != null && meshFilter.sharedMesh != null) || (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null);
            bool hasMaterial = renderer.sharedMaterial != null;

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

    private static void EnsureRendererMaterialsVisible(Renderer renderer)
    {
        Material[] materials = renderer.materials;
        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material == null)
            {
                continue;
            }

            if (material.HasProperty("_BaseColor"))
            {
                Color color = material.GetColor("_BaseColor");
                if (color.a < 0.99f)
                {
                    color.a = 1f;
                    material.SetColor("_BaseColor", color);
                }
            }

            if (material.HasProperty("_Color"))
            {
                Color color = material.GetColor("_Color");
                if (color.a < 0.99f)
                {
                    color.a = 1f;
                    material.SetColor("_Color", color);
                }
            }
        }
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
