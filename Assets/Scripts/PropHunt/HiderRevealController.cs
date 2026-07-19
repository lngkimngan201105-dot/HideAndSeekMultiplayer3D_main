using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class HiderRevealController : MonoBehaviour
{
    private const string RevealLayerName = "SeekerReveal";
    public const float RevealHighlightAlpha = 0.05f;

    [Header("References")]
    [SerializeField] private PropTransformSystem propTransformSystem;
    [SerializeField] private Shader revealShader;

    [Header("Clone reveal")]
    [SerializeField, Min(0f)] private float cloneRevealDuration = 5f;
    [SerializeField] private Color revealColor = new Color(1f, 0.16f, 0.03f, RevealHighlightAlpha);
    [SerializeField, Min(0f)] private float outlineWidth = 0.025f;
    [SerializeField] private bool debugShowRevealToHiderCamera;

    public bool IsRevealed { get; private set; }
    public float RevealTimeRemaining { get; private set; }
    public Vector3 CurrentRevealPosition => transform.position;
    public float RevealDuration => cloneRevealDuration;
    public Color RevealColor => revealColor;
    public float HighlightAlpha => revealColor.a;
    public float OutlineWidth => outlineWidth;

    public event Action RevealStarted;
    public event Action RevealEnded;

    private readonly Dictionary<Camera, int> originalCameraMasks = new Dictionary<Camera, int>();
    private GameObject overlayRoot;
    private Material overlayMaterial;
    private int revealLayer = -1;
    private bool previousDebugVisibility;
    private bool missingLayerWarningLogged;

    private void Awake()
    {
        ClampVisualSettings();
        ResolveReferences();
        revealLayer = LayerMask.NameToLayer(RevealLayerName);
        previousDebugVisibility = debugShowRevealToHiderCamera;
    }

    private void OnValidate()
    {
        ClampVisualSettings();
        ApplyOverlayMaterialProperties();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (propTransformSystem != null)
        {
            propTransformSystem.VisualChanged += HandleVisualChanged;
        }

        CacheAndConfigureHiderCameras();
    }

    private void OnDisable()
    {
        if (propTransformSystem != null)
        {
            propTransformSystem.VisualChanged -= HandleVisualChanged;
        }

        StopReveal();
        RestoreHiderCameraMasks();
    }

    private void OnDestroy()
    {
        DestroyOverlay();
        if (overlayMaterial != null)
        {
            Destroy(overlayMaterial);
            overlayMaterial = null;
        }
    }

    private void Update()
    {
        if (debugShowRevealToHiderCamera != previousDebugVisibility)
        {
            previousDebugVisibility = debugShowRevealToHiderCamera;
            ApplyHiderCameraVisibility();
        }

        if (!IsRevealed)
        {
            return;
        }

        RevealTimeRemaining = Mathf.Max(0f, RevealTimeRemaining - Time.deltaTime);
        if (RevealTimeRemaining <= 0f)
        {
            StopReveal();
        }
    }

    public void Configure(PropTransformSystem transformSystem)
    {
        Configure(transformSystem, RevealHighlightAlpha);
    }

    public void Configure(PropTransformSystem transformSystem, float highlightAlpha)
    {
        if (isActiveAndEnabled && propTransformSystem != null)
        {
            propTransformSystem.VisualChanged -= HandleVisualChanged;
        }

        propTransformSystem = transformSystem;
        revealColor.a = Mathf.Clamp01(highlightAlpha);
        ApplyOverlayMaterialProperties();
        if (isActiveAndEnabled && propTransformSystem != null)
        {
            propTransformSystem.VisualChanged += HandleVisualChanged;
        }
    }

    public void RevealForSeconds(float duration)
    {
        float requestedDuration = duration > 0f ? duration : cloneRevealDuration;
        if (requestedDuration <= 0f)
        {
            return;
        }

        bool wasRevealed = IsRevealed;
        IsRevealed = true;
        RevealTimeRemaining = requestedDuration;

        if (!wasRevealed)
        {
            BuildOverlay();
            RevealStarted?.Invoke();
        }
        else if (overlayRoot == null)
        {
            BuildOverlay();
        }
    }

    public void StopReveal()
    {
        bool wasRevealed = IsRevealed;
        IsRevealed = false;
        RevealTimeRemaining = 0f;
        DestroyOverlay();
        if (wasRevealed)
        {
            RevealEnded?.Invoke();
            Debug.Log("Hider Reveal:\nReveal ended.");
        }
    }

    private void HandleVisualChanged()
    {
        if (IsRevealed)
        {
            BuildOverlay();
        }
    }

    private void BuildOverlay()
    {
        DestroyOverlay();
        revealLayer = LayerMask.NameToLayer(RevealLayerName);
        if (revealLayer < 0)
        {
            if (!missingLayerWarningLogged)
            {
                Debug.LogWarning(
                    "Hider Reveal: SeekerReveal layer is missing. IsRevealed remains available for AI, but the overlay cannot be rendered."
                );
                missingLayerWarningLogged = true;
            }

            return;
        }

        if (propTransformSystem == null)
        {
            return;
        }

        Transform sourceRoot = propTransformSystem.CurrentVisualRoot;
        if (sourceRoot == null || !sourceRoot.gameObject.activeInHierarchy)
        {
            return;
        }

        overlayRoot = new GameObject("HiderRevealOverlayRoot");
        overlayRoot.transform.SetParent(sourceRoot, false);
        overlayRoot.transform.localPosition = Vector3.zero;
        overlayRoot.transform.localRotation = Quaternion.identity;
        overlayRoot.transform.localScale = Vector3.one;

        GameObject visualCopy;
        if (propTransformSystem.IsDisguised)
        {
            if (!propTransformSystem.TryCreateDetachedVisualCopy(overlayRoot.transform, out visualCopy))
            {
                DestroyOverlay();
                return;
            }
        }
        else
        {
            visualCopy = Instantiate(sourceRoot.gameObject, overlayRoot.transform, false);
            visualCopy.name = "CapturedHumanVisual";
            StripNonVisualComponents(visualCopy);
        }

        Material material = GetOrCreateOverlayMaterial();
        if (material == null)
        {
            DestroyOverlay();
            return;
        }

        Renderer[] renderers = visualCopy.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            int materialCount = Mathf.Max(1, renderer.sharedMaterials.Length);
            Material[] materials = new Material[materialCount];
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = material;
            }

            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = true;
        }

        SetLayerRecursively(overlayRoot, revealLayer);
        ApplyHiderCameraVisibility();
    }

    private Material GetOrCreateOverlayMaterial()
    {
        if (overlayMaterial != null)
        {
            ApplyOverlayMaterialProperties();
            return overlayMaterial;
        }

        if (revealShader == null)
        {
            revealShader = Shader.Find("Hidden/PropHunt/SeekerRevealOutline");
        }

        if (revealShader == null)
        {
            Debug.LogWarning("Hider Reveal: reveal shader was not found.");
            return null;
        }

        overlayMaterial = new Material(revealShader)
        {
            name = "HiderRevealOverlay_Runtime",
            hideFlags = HideFlags.HideAndDontSave
        };
        ApplyOverlayMaterialProperties();
        return overlayMaterial;
    }

    private void ApplyOverlayMaterialProperties()
    {
        if (overlayMaterial == null)
        {
            return;
        }

        overlayMaterial.SetColor("_Color", revealColor);
        overlayMaterial.SetFloat("_OutlineWidth", outlineWidth);
    }

    private void ClampVisualSettings()
    {
        cloneRevealDuration = Mathf.Max(0f, cloneRevealDuration);
        revealColor.a = Mathf.Clamp01(revealColor.a);
        outlineWidth = Mathf.Max(0f, outlineWidth);
    }

    private void DestroyOverlay()
    {
        if (overlayRoot == null)
        {
            return;
        }

        overlayRoot.SetActive(false);
        overlayRoot.transform.SetParent(null, false);
        Destroy(overlayRoot);
        overlayRoot = null;
    }

    private void CacheAndConfigureHiderCameras()
    {
        originalCameraMasks.Clear();
        if (propTransformSystem == null)
        {
            return;
        }

        AddCamera(propTransformSystem.mainCamera);
        PlayerCameraModeManager manager = propTransformSystem.cameraModeManager;
        if (manager != null)
        {
            AddCamera(manager.fpsCamera);
            AddCamera(manager.tpsCamera);
            AddCamera(manager.spectatorCamera);
        }

        foreach (Camera camera in propTransformSystem.GetComponentsInChildren<Camera>(true))
        {
            AddCamera(camera);
        }

        ApplyHiderCameraVisibility();
    }

    private void AddCamera(Camera camera)
    {
        if (camera != null && !originalCameraMasks.ContainsKey(camera))
        {
            originalCameraMasks.Add(camera, camera.cullingMask);
        }
    }

    private void ApplyHiderCameraVisibility()
    {
        if (revealLayer < 0)
        {
            return;
        }

        int layerBit = 1 << revealLayer;
        foreach (KeyValuePair<Camera, int> entry in originalCameraMasks)
        {
            if (entry.Key == null)
            {
                continue;
            }

            entry.Key.cullingMask = debugShowRevealToHiderCamera
                ? entry.Value | layerBit
                : entry.Value & ~layerBit;
        }
    }

    private void RestoreHiderCameraMasks()
    {
        foreach (KeyValuePair<Camera, int> entry in originalCameraMasks)
        {
            if (entry.Key != null)
            {
                entry.Key.cullingMask = entry.Value;
            }
        }

        originalCameraMasks.Clear();
    }

    private static void StripNonVisualComponents(GameObject copy)
    {
        foreach (Collider collider in copy.GetComponentsInChildren<Collider>(true))
        {
            Destroy(collider);
        }

        foreach (Rigidbody body in copy.GetComponentsInChildren<Rigidbody>(true))
        {
            Destroy(body);
        }

        foreach (Camera camera in copy.GetComponentsInChildren<Camera>(true))
        {
            camera.enabled = false;
            Destroy(camera);
        }

        foreach (AudioListener listener in copy.GetComponentsInChildren<AudioListener>(true))
        {
            listener.enabled = false;
            Destroy(listener);
        }

        foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null)
            {
                behaviour.enabled = false;
                Destroy(behaviour);
            }
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = layer;
        }
    }

    private void ResolveReferences()
    {
        if (propTransformSystem == null)
        {
            propTransformSystem = GetComponent<PropTransformSystem>();
        }
    }
}
