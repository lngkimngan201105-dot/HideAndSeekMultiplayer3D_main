using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public enum PropHuntZonePhase
{
    Inactive,
    FullMap,
    WarningFirstShrink,
    ShrinkingToSeventyPercent,
    HoldingSeventyPercent,
    WarningSecondShrink,
    ShrinkingToFortyTwoPercent,
    HoldingFortyTwoPercent,
    WarningFinalShrink,
    ShrinkingToFinal,
    FinalHold,
    Finished
}

public class PropHuntShrinkingZone : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PropHuntRoundManager roundManager;
    [SerializeField] private HiderPlayableAreaBounds playableArea;
    [SerializeField] private LineRenderer boundaryVisual;
    [SerializeField] private PropHuntZoneDomeVisual domeVisual;
    [SerializeField] private List<PropHuntZoneAnchor> zoneAnchors = new List<PropHuntZoneAnchor>();

    [Header("Radius")]
    [SerializeField, Range(0.1f, 0.95f)] private float seventyPercentMultiplier = 0.70f;
    [SerializeField, Range(0.1f, 0.95f)] private float mediumZoneMultiplier = 0.425f;
    [SerializeField, Min(0.5f)] private float finalZoneRadius = 15f;
    [SerializeField, Min(0f)] private float initialCoverageMargin = 3f;
    [SerializeField, Min(0f)] private float boundaryTolerance = 0.15f;
    [SerializeField] private Bounds fallbackPlayableBounds = new Bounds(
        new Vector3(34.7f, 8.5f, -23.8f),
        new Vector3(151.1f, 24f, 162.5f));

    [Header("Boundary Visual")]
    [SerializeField, Range(24, 256)] private int visualSegments = 96;
    [SerializeField, Range(0.05f, 0.5f)] private float visualWidth = 0.10f;
    [SerializeField] private float visualHeightOffset = 0.12f;
    [SerializeField] private Color visualColor = new Color(0.08f, 0.88f, 1f, 0.9f);

    [Header("Debug")]
    [SerializeField] private bool showZoneDebugGizmos = true;
    [SerializeField] private bool showZoneDebugLogs;
#if UNITY_EDITOR
    [SerializeField] private bool debugOverrideHuntingTime;
    [SerializeField, Range(0f, 180f)] private float debugHuntingTimeRemaining = 180f;
#endif

    private Material _runtimeBoundaryMaterial;
    private bool _huntingInitialized;
    private float _lastVisualRadius = -1f;
    private Vector3 _lastVisualCenter = new Vector3(float.PositiveInfinity, 0f, 0f);
    private bool _missingReferenceWarningLogged;

    public PropHuntZonePhase CurrentPhase { get; private set; } = PropHuntZonePhase.Inactive;
    public Vector3 CurrentCenter { get; private set; }
    public float CurrentRadius { get; private set; }
    public float InitialRadius { get; private set; }
    public float SeventyPercentRadius { get; private set; }
    public float MediumRadius { get; private set; }
    public float FinalRadius { get; private set; } = 15f;
    public PropHuntZoneAnchor SelectedAnchor { get; private set; }
    public float BoundaryTolerance => boundaryTolerance;
    public bool IsZoneActive => _huntingInitialized &&
                                roundManager != null &&
                                roundManager.CurrentState == PropHuntRoundState.Hunting &&
                                CurrentPhase != PropHuntZonePhase.Inactive &&
                                CurrentPhase != PropHuntZonePhase.Finished;
    public bool IsWarningPhase => CurrentPhase == PropHuntZonePhase.WarningFirstShrink ||
                                  CurrentPhase == PropHuntZonePhase.WarningSecondShrink ||
                                  CurrentPhase == PropHuntZonePhase.WarningFinalShrink;
    public float WarningSecondsRemaining => GetWarningSecondsRemaining(GetAuthoritativeHuntingTime());

    public event Action<PropHuntZonePhase, PropHuntZonePhase> PhaseChanged;
    public event Action ZoneReset;

    private void Awake()
    {
        ResolveReferences();
        ConfigureBoundaryVisual();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToRound();
        SynchronizeWithRoundState();
    }

    private void OnDisable()
    {
        UnsubscribeFromRound();
        SetBoundaryVisible(false);
    }

    private void OnDestroy()
    {
        if (_runtimeBoundaryMaterial != null)
        {
            Destroy(_runtimeBoundaryMaterial);
        }
    }

    private void Update()
    {
        if (roundManager == null)
        {
            ResolveReferences();
            if (roundManager == null)
            {
                WarnMissingReferencesOnce();
                return;
            }
        }

        if (roundManager.CurrentState != PropHuntRoundState.Hunting)
        {
            return;
        }

        if (!_huntingInitialized)
        {
            BeginHuntingZone();
        }

        UpdateZoneFromRemainingTime(GetAuthoritativeHuntingTime());
    }

    public void Configure(
        PropHuntRoundManager configuredRoundManager,
        HiderPlayableAreaBounds configuredPlayableArea,
        LineRenderer configuredBoundaryVisual,
        PropHuntZoneDomeVisual configuredDomeVisual,
        IEnumerable<PropHuntZoneAnchor> configuredAnchors,
        Bounds configuredFallbackBounds)
    {
        bool wasActive = isActiveAndEnabled;
        if (wasActive)
        {
            UnsubscribeFromRound();
        }

        roundManager = configuredRoundManager;
        playableArea = configuredPlayableArea;
        boundaryVisual = configuredBoundaryVisual;
        domeVisual = configuredDomeVisual;
        seventyPercentMultiplier = 0.70f;
        mediumZoneMultiplier = 0.425f;
        finalZoneRadius = 15f;
        initialCoverageMargin = 3f;
        visualSegments = 96;
        visualWidth = 0.10f;
        visualHeightOffset = 0.12f;
        visualColor = new Color(0.08f, 0.88f, 1f, 0.9f);
        fallbackPlayableBounds = configuredFallbackBounds;
        zoneAnchors = configuredAnchors != null
            ? configuredAnchors.Where(anchor => anchor != null).Distinct().ToList()
            : new List<PropHuntZoneAnchor>();

#if UNITY_EDITOR
        debugOverrideHuntingTime = false;
        debugHuntingTimeRemaining = 180f;
#endif
        ConfigureBoundaryVisual();
        ResetForPreparation();

        if (wasActive)
        {
            SubscribeToRound();
            SynchronizeWithRoundState();
        }
    }

    public bool IsPositionInsideZone(Vector3 worldPosition, float tolerance = 0f)
    {
        Vector2 point = new Vector2(worldPosition.x, worldPosition.z);
        Vector2 center = new Vector2(CurrentCenter.x, CurrentCenter.z);
        return Vector2.Distance(point, center) <= CurrentRadius + tolerance;
    }

    public float DistanceFromCenterXZ(Vector3 worldPosition)
    {
        return Vector2.Distance(
            new Vector2(worldPosition.x, worldPosition.z),
            new Vector2(CurrentCenter.x, CurrentCenter.z));
    }

    private void SubscribeToRound()
    {
        if (roundManager == null)
        {
            return;
        }

        roundManager.RoundStateChanged -= HandleRoundStateChanged;
        roundManager.RoundStateChanged += HandleRoundStateChanged;
        roundManager.RoundStarted -= HandleRoundStarted;
        roundManager.RoundStarted += HandleRoundStarted;
    }

    private void UnsubscribeFromRound()
    {
        if (roundManager == null)
        {
            return;
        }

        roundManager.RoundStateChanged -= HandleRoundStateChanged;
        roundManager.RoundStarted -= HandleRoundStarted;
    }

    private void HandleRoundStarted()
    {
        ResetForPreparation();
    }

    private void HandleRoundStateChanged(PropHuntRoundState state)
    {
        switch (state)
        {
            case PropHuntRoundState.Hunting:
                BeginHuntingZone();
                break;
            case PropHuntRoundState.Ended:
                FinishZone();
                break;
            default:
                ResetForPreparation();
                break;
        }
    }

    private void SynchronizeWithRoundState()
    {
        if (roundManager == null)
        {
            SetBoundaryVisible(false);
            return;
        }

        HandleRoundStateChanged(roundManager.CurrentState);
    }

    private void BeginHuntingZone()
    {
        ClearSelectedAnchor();
        SelectRoundAnchor();
        Bounds bounds = GetPlayableBounds();
        if (SelectedAnchor != null)
        {
            CurrentCenter = SelectedAnchor.transform.position;
        }
        else
        {
            CurrentCenter = new Vector3(bounds.center.x, bounds.min.y + 0.12f, bounds.center.z);
        }

        CalculateRadii(bounds);
        CurrentRadius = InitialRadius;
        _huntingInitialized = true;
        _lastVisualRadius = -1f;
        SetBoundaryVisible(true);
        SetPhase(PropHuntZonePhase.FullMap);
        UpdateBoundaryVisual(true);

        if (showZoneDebugLogs)
        {
            Debug.Log(
                $"Prop Hunt Zone:\nSelected anchor={(SelectedAnchor != null ? SelectedAnchor.name : "FallbackCenter")}\n" +
                $"InitialRadius={InitialRadius:F2}\nFinalRadius={FinalRadius:F2}");
        }
    }

    private void ResetForPreparation()
    {
        _huntingInitialized = false;
        ClearSelectedAnchor();
        CurrentRadius = 0f;
        SetPhase(PropHuntZonePhase.Inactive);
        SetBoundaryVisible(false);
        ZoneReset?.Invoke();
    }

    private void FinishZone()
    {
        _huntingInitialized = false;
        SetPhase(PropHuntZonePhase.Finished);
        SetBoundaryVisible(false);
        ZoneReset?.Invoke();
    }

    private void SelectRoundAnchor()
    {
        List<PropHuntZoneAnchor> validAnchors = zoneAnchors
            .Where(anchor => anchor != null && anchor.IsEnabledAnchor && anchor.ValidateAnchor(out _))
            .ToList();
        if (validAnchors.Count == 0)
        {
            Debug.LogWarning("PropHuntShrinkingZone: no valid ZoneAnchor was found; PlayableArea center will be used.");
            SelectedAnchor = null;
            return;
        }

        SelectedAnchor = validAnchors[UnityEngine.Random.Range(0, validAnchors.Count)];
        SelectedAnchor.SetSelected(true);
    }

    private void ClearSelectedAnchor()
    {
        foreach (PropHuntZoneAnchor anchor in zoneAnchors)
        {
            if (anchor != null)
            {
                anchor.SetSelected(false);
            }
        }

        SelectedAnchor = null;
    }

    private Bounds GetPlayableBounds()
    {
        if (playableArea != null && playableArea.TryGetBounds(out Bounds bounds))
        {
            return bounds;
        }

        if (!_missingReferenceWarningLogged)
        {
            _missingReferenceWarningLogged = true;
            Debug.LogWarning("PropHuntShrinkingZone: PlayableArea bounds missing; serialized fallback bounds will be used.");
        }

        return fallbackPlayableBounds;
    }

    private void CalculateRadii(Bounds bounds)
    {
        Vector2 center = new Vector2(CurrentCenter.x, CurrentCenter.z);
        Vector2[] corners =
        {
            new Vector2(bounds.min.x, bounds.min.z),
            new Vector2(bounds.min.x, bounds.max.z),
            new Vector2(bounds.max.x, bounds.min.z),
            new Vector2(bounds.max.x, bounds.max.z)
        };

        InitialRadius = corners.Max(corner => Vector2.Distance(center, corner)) + initialCoverageMargin;
        FinalRadius = Mathf.Min(finalZoneRadius, Mathf.Max(0.5f, InitialRadius - 1.5f));
        MediumRadius = Mathf.Clamp(
            InitialRadius * mediumZoneMultiplier,
            FinalRadius + 0.5f,
            Mathf.Max(FinalRadius + 0.5f, InitialRadius - 1f));
        SeventyPercentRadius = Mathf.Clamp(
            InitialRadius * seventyPercentMultiplier,
            MediumRadius + 0.5f,
            Mathf.Max(MediumRadius + 0.5f, InitialRadius - 0.5f));

        if (!(InitialRadius > SeventyPercentRadius &&
              SeventyPercentRadius > MediumRadius &&
              MediumRadius > FinalRadius))
        {
            Debug.LogWarning("PropHuntShrinkingZone: map bounds are too small for configured radii; safe decreasing radii were clamped.");
        }
    }

    private void UpdateZoneFromRemainingTime(float remainingTime)
    {
        remainingTime = Mathf.Clamp(remainingTime, 0f, 180f);
        PropHuntZonePhase phase;
        float radius;

        if (remainingTime <= 0f)
        {
            SetPhase(PropHuntZonePhase.Finished);
            SetRadius(FinalRadius);
            SetBoundaryVisible(false);
            return;
        }

        if (remainingTime > 160f)
        {
            phase = PropHuntZonePhase.FullMap;
            radius = InitialRadius;
        }
        else if (remainingTime > 150f)
        {
            phase = PropHuntZonePhase.WarningFirstShrink;
            radius = InitialRadius;
        }
        else if (remainingTime > 125f)
        {
            phase = PropHuntZonePhase.ShrinkingToSeventyPercent;
            radius = LinearRadius(InitialRadius, SeventyPercentRadius, 150f, 125f, remainingTime);
        }
        else if (remainingTime > 110f)
        {
            phase = PropHuntZonePhase.HoldingSeventyPercent;
            radius = SeventyPercentRadius;
        }
        else if (remainingTime > 100f)
        {
            phase = PropHuntZonePhase.WarningSecondShrink;
            radius = SeventyPercentRadius;
        }
        else if (remainingTime > 75f)
        {
            phase = PropHuntZonePhase.ShrinkingToFortyTwoPercent;
            radius = LinearRadius(SeventyPercentRadius, MediumRadius, 100f, 75f, remainingTime);
        }
        else if (remainingTime > 60f)
        {
            phase = PropHuntZonePhase.HoldingFortyTwoPercent;
            radius = MediumRadius;
        }
        else if (remainingTime > 50f)
        {
            phase = PropHuntZonePhase.WarningFinalShrink;
            radius = MediumRadius;
        }
        else if (remainingTime > 20f)
        {
            phase = PropHuntZonePhase.ShrinkingToFinal;
            radius = LinearRadius(MediumRadius, FinalRadius, 50f, 20f, remainingTime);
        }
        else
        {
            phase = PropHuntZonePhase.FinalHold;
            radius = FinalRadius;
        }

        SetPhase(phase);
        SetRadius(radius);
    }

    private void SetPhase(PropHuntZonePhase nextPhase)
    {
        if (CurrentPhase == nextPhase)
        {
            return;
        }

        PropHuntZonePhase previous = CurrentPhase;
        CurrentPhase = nextPhase;
        PhaseChanged?.Invoke(previous, nextPhase);
        if (showZoneDebugLogs)
        {
            Debug.Log($"Prop Hunt Zone: Phase {previous} -> {nextPhase}");
        }
    }

    private void SetRadius(float radius)
    {
        float clampedRadius = Mathf.Max(0f, radius);
        if (Mathf.Abs(CurrentRadius - clampedRadius) <= 0.001f)
        {
            return;
        }

        CurrentRadius = clampedRadius;
        UpdateBoundaryVisual(false);
    }

    private static float LinearRadius(float startRadius, float targetRadius, float startRemaining, float endRemaining, float remaining)
    {
        float progress = Mathf.Clamp01((startRemaining - remaining) / (startRemaining - endRemaining));
        return Mathf.Lerp(startRadius, targetRadius, progress);
    }

    private static bool IsShrinkingPhase(PropHuntZonePhase phase)
    {
        return phase == PropHuntZonePhase.ShrinkingToSeventyPercent ||
               phase == PropHuntZonePhase.ShrinkingToFortyTwoPercent ||
               phase == PropHuntZonePhase.ShrinkingToFinal;
    }

    private float GetAuthoritativeHuntingTime()
    {
#if UNITY_EDITOR
        if (debugOverrideHuntingTime)
        {
            return debugHuntingTimeRemaining;
        }
#endif
        return roundManager != null ? roundManager.RemainingTime : 0f;
    }

    private float GetWarningSecondsRemaining(float remainingTime)
    {
        switch (CurrentPhase)
        {
            case PropHuntZonePhase.WarningFirstShrink:
                return Mathf.Clamp(remainingTime - 150f, 0f, 10f);
            case PropHuntZonePhase.WarningSecondShrink:
                return Mathf.Clamp(remainingTime - 100f, 0f, 10f);
            case PropHuntZonePhase.WarningFinalShrink:
                return Mathf.Clamp(remainingTime - 50f, 0f, 10f);
            default:
                return 0f;
        }
    }

    private void ResolveReferences()
    {
        if (roundManager == null) roundManager = FindObjectOfType<PropHuntRoundManager>();
        if (playableArea == null) playableArea = GetComponent<HiderPlayableAreaBounds>();
        if (boundaryVisual == null) boundaryVisual = GetComponentInChildren<LineRenderer>(true);
        if (domeVisual == null) domeVisual = GetComponentInChildren<PropHuntZoneDomeVisual>(true);
        if (zoneAnchors == null || zoneAnchors.Count == 0)
        {
            zoneAnchors = GetComponentsInChildren<PropHuntZoneAnchor>(true).ToList();
        }
    }

    private void WarnMissingReferencesOnce()
    {
        if (_missingReferenceWarningLogged)
        {
            return;
        }

        _missingReferenceWarningLogged = true;
        Debug.LogWarning("PropHuntShrinkingZone: RoundManager is missing; zone remains inactive.");
    }

    private void ConfigureBoundaryVisual()
    {
        if (boundaryVisual == null)
        {
            return;
        }

        boundaryVisual.useWorldSpace = true;
        boundaryVisual.loop = true;
        boundaryVisual.positionCount = Mathf.Max(24, visualSegments);
        boundaryVisual.startWidth = visualWidth;
        boundaryVisual.endWidth = visualWidth;
        boundaryVisual.startColor = visualColor;
        boundaryVisual.endColor = visualColor;
        boundaryVisual.shadowCastingMode = ShadowCastingMode.Off;
        boundaryVisual.receiveShadows = false;
        boundaryVisual.textureMode = LineTextureMode.Stretch;
        boundaryVisual.alignment = LineAlignment.View;

        if (_runtimeBoundaryMaterial == null && Application.isPlaying)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                _runtimeBoundaryMaterial = new Material(shader)
                {
                    name = "PropHuntZoneBoundary_Runtime",
                    color = visualColor
                };
                boundaryVisual.material = _runtimeBoundaryMaterial;
            }
        }
    }

    private void SetBoundaryVisible(bool visible)
    {
        if (boundaryVisual != null && boundaryVisual.gameObject.activeSelf != visible)
        {
            boundaryVisual.gameObject.SetActive(visible);
        }

        if (domeVisual != null)
        {
            domeVisual.SetVisible(visible);
        }
    }

    private void UpdateBoundaryVisual(bool force)
    {
        bool hasGroundRing = boundaryVisual != null && boundaryVisual.gameObject.activeInHierarchy;
        bool hasDome = domeVisual != null && domeVisual.gameObject.activeInHierarchy;
        if (!hasGroundRing && !hasDome)
        {
            return;
        }

        if (!force && Mathf.Abs(_lastVisualRadius - CurrentRadius) < 0.005f &&
            (_lastVisualCenter - CurrentCenter).sqrMagnitude < 0.0001f)
        {
            return;
        }

        if (hasGroundRing)
        {
            ConfigureBoundaryVisual();
            int segments = boundaryVisual.positionCount;
            for (int index = 0; index < segments; index++)
            {
                float angle = index / (float)segments * Mathf.PI * 2f;
                boundaryVisual.SetPosition(index, CurrentCenter + new Vector3(
                    Mathf.Cos(angle) * CurrentRadius,
                    visualHeightOffset,
                    Mathf.Sin(angle) * CurrentRadius));
            }
        }

        if (hasDome)
        {
            domeVisual.SetZone(CurrentCenter, CurrentRadius, IsShrinkingPhase(CurrentPhase));
        }

        _lastVisualRadius = CurrentRadius;
        _lastVisualCenter = CurrentCenter;
    }

#if UNITY_EDITOR
    public void DebugSetHuntingTime(float remainingTime)
    {
        debugOverrideHuntingTime = true;
        debugHuntingTimeRemaining = Mathf.Clamp(remainingTime, 0f, 180f);
        if (!_huntingInitialized)
        {
            BeginHuntingZone();
        }

        UpdateZoneFromRemainingTime(debugHuntingTimeRemaining);
    }

    public void DebugClearHuntingTimeOverride()
    {
        debugOverrideHuntingTime = false;
    }

    [ContextMenu("Debug/Set Zone Time 180")]
    private void DebugTime180() => DebugSetHuntingTime(180f);
    [ContextMenu("Debug/Set Zone Time 160")]
    private void DebugTime160() => DebugSetHuntingTime(160f);
    [ContextMenu("Debug/Set Zone Time 150")]
    private void DebugTime150() => DebugSetHuntingTime(150f);
    [ContextMenu("Debug/Set Zone Time 125")]
    private void DebugTime125() => DebugSetHuntingTime(125f);
    [ContextMenu("Debug/Set Zone Time 110")]
    private void DebugTime110() => DebugSetHuntingTime(110f);
    [ContextMenu("Debug/Set Zone Time 100")]
    private void DebugTime100() => DebugSetHuntingTime(100f);
    [ContextMenu("Debug/Set Zone Time 75")]
    private void DebugTime75() => DebugSetHuntingTime(75f);
    [ContextMenu("Debug/Set Zone Time 60")]
    private void DebugTime60() => DebugSetHuntingTime(60f);
    [ContextMenu("Debug/Set Zone Time 50")]
    private void DebugTime50() => DebugSetHuntingTime(50f);
    [ContextMenu("Debug/Set Zone Time 20")]
    private void DebugTime20() => DebugSetHuntingTime(20f);
#endif

    private void OnDrawGizmosSelected()
    {
        if (!showZoneDebugGizmos || InitialRadius <= 0f)
        {
            return;
        }

        DrawDebugCircle(InitialRadius, new Color(0.2f, 0.8f, 1f, 0.35f));
        DrawDebugCircle(SeventyPercentRadius, new Color(0.25f, 1f, 0.4f, 0.45f));
        DrawDebugCircle(MediumRadius, new Color(1f, 0.75f, 0.1f, 0.55f));
        DrawDebugCircle(FinalRadius, new Color(1f, 0.2f, 0.15f, 0.75f));
        DrawDebugCircle(CurrentRadius, Color.white);
    }

    private void DrawDebugCircle(float radius, Color color)
    {
        Gizmos.color = color;
        Vector3 previous = CurrentCenter + Vector3.right * radius;
        for (int index = 1; index <= 96; index++)
        {
            float angle = index / 96f * Mathf.PI * 2f;
            Vector3 next = CurrentCenter + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
