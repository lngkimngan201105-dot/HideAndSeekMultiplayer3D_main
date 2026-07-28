using UnityEngine;

[DisallowMultipleComponent]
public sealed class HiderPerceptionSignature : MonoBehaviour
{
    [SerializeField] private PropTransformSystem transformSystem;
    [SerializeField] private HiderRevealController revealController;
    [SerializeField, Min(0f)] private float movementThreshold = 0.15f;
    [SerializeField, Min(0f)] private float recentChangeDuration = 2.5f;

    private Vector3 previousPosition;
    private float lastVisualChangeAt = float.NegativeInfinity;

    public Vector3 Velocity { get; private set; }
    public bool IsMoving => new Vector2(Velocity.x, Velocity.z).magnitude >= movementThreshold;
    public bool IsHuman => transformSystem == null || !transformSystem.IsDisguised;
    public bool IsDisguised => transformSystem != null && transformSystem.IsDisguised;
    public bool IsRevealed => revealController != null && revealController.IsRevealed;
    public bool ChangedRecently => Time.time - lastVisualChangeAt <= recentChangeDuration;
    public Transform VisualRoot => transformSystem != null && transformSystem.CurrentVisualRoot != null
        ? transformSystem.CurrentVisualRoot
        : transform;

    private void Awake()
    {
        ResolveReferences();
        previousPosition = transform.position;
    }

    private void OnEnable()
    {
        ResolveReferences();
        previousPosition = transform.position;
        if (transformSystem != null)
        {
            transformSystem.VisualChanged -= HandleVisualChanged;
            transformSystem.VisualChanged += HandleVisualChanged;
        }
    }

    private void OnDisable()
    {
        if (transformSystem != null)
        {
            transformSystem.VisualChanged -= HandleVisualChanged;
        }
    }

    private void Update()
    {
        float delta = Mathf.Max(Time.deltaTime, 0.0001f);
        Velocity = (transform.position - previousPosition) / delta;
        previousPosition = transform.position;
    }

    public void Configure(PropTransformSystem configuredTransformSystem, HiderRevealController configuredReveal)
    {
        if (isActiveAndEnabled && transformSystem != null)
        {
            transformSystem.VisualChanged -= HandleVisualChanged;
        }

        transformSystem = configuredTransformSystem;
        revealController = configuredReveal;
        ResolveReferences();

        if (isActiveAndEnabled && transformSystem != null)
        {
            transformSystem.VisualChanged += HandleVisualChanged;
        }
    }

    public Vector3 GetAimPoint()
    {
        Renderer[] renderers = VisualRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return transform.position + Vector3.up;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }

        return bounds.center;
    }

    private void HandleVisualChanged()
    {
        lastVisualChangeAt = Time.time;
    }

    private void ResolveReferences()
    {
        if (transformSystem == null) transformSystem = GetComponent<PropTransformSystem>();
        if (revealController == null) revealController = GetComponent<HiderRevealController>();
    }
}
