using System;
using UnityEngine;

public enum HiderAntiCampState
{
    Safe,
    Warning,
    Revealed
}

public enum HiderAntiCampAlertType
{
    InitialReveal,
    RepeatedReveal
}

public readonly struct HiderAntiCampAlertData
{
    public HiderAntiCampAlertData(
        Vector3 alertPosition,
        float alertDuration,
        HiderAntiCampAlertType alertType,
        float timestamp,
        float alertRadius = 5f)
    {
        AlertPosition = alertPosition;
        AlertDuration = alertDuration;
        AlertType = alertType;
        Timestamp = timestamp;
        AlertRadius = Mathf.Max(0.1f, alertRadius);
    }

    public Vector3 AlertPosition { get; }
    public float AlertDuration { get; }
    public HiderAntiCampAlertType AlertType { get; }
    public float Timestamp { get; }
    public float AlertRadius { get; }
}

public class HiderAntiCampSystem : MonoBehaviour
{
    public const string DedicatedAudioObjectName = "HiderAntiCampAudioSource";

    [Header("References")]
    [SerializeField] private PropTransformSystem propTransformSystem;
    [SerializeField] private PropHuntRoundManager roundManager;

    [Header("Anti-camp")]
    [SerializeField, Min(0f)] private float allowedCampTime = 30f;
    [SerializeField, Min(0.1f)] private float escapeCountdownDuration = 5f;
    [SerializeField, Min(0.1f)] private float escapeRadius = 3f;
    [SerializeField, Min(0.1f)] private float repeatSoundInterval = 10f;

    public float CampTime { get; private set; }
    public float CountdownRemaining { get; private set; }
    public int CountdownDisplay => IsCountdownActive
        ? Mathf.Clamp(Mathf.CeilToInt(CountdownRemaining), 1, Mathf.CeilToInt(escapeCountdownDuration))
        : 0;
    public bool IsCountdownActive => CurrentState == HiderAntiCampState.Warning;
    public bool IsRevealed => CurrentState == HiderAntiCampState.Revealed;
    public bool IsSuppressedByZone => _suppressedByZone;
    public bool IsEliminated => _eliminated;
    public bool IsWarningActive => CurrentState == HiderAntiCampState.Warning;
    public bool AntiCampTriggered => _antiCampTriggered;
    public int AlertTriggerCount { get; private set; }
    public HiderAntiCampState CurrentState { get; private set; } = HiderAntiCampState.Safe;

    public event Action<HiderAntiCampAlertData> AntiCampAlertTriggered;
    public event Action AntiCampAlertCleared;

    private Vector3 _campOrigin;
    private float _nextRevealAt;
    private bool _hasCampOrigin;
    private bool _suppressedByZone;
    private bool _eliminated;
    private bool _antiCampTriggered;

    private void Awake()
    {
        ResolveReferences();
        ResetAntiCamp();
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
    }

    private void Update()
    {
        if (!ShouldTrackAntiCamp())
        {
            CountdownRemaining = 0f;
            CurrentState = HiderAntiCampState.Safe;
            return;
        }

        if (!_hasCampOrigin)
        {
            SetNewCampOrigin(transform.position);
        }

        Vector3 currentPosition = transform.position;
        float campDistance = GetCampDisplacement(currentPosition);
        if (campDistance >= escapeRadius)
        {
            SetNewCampOrigin(currentPosition);
            ClearAlertPresentation();
            return;
        }

        CampTime += Time.deltaTime;
        if (CampTime < allowedCampTime)
        {
            CountdownRemaining = 0f;
            CurrentState = HiderAntiCampState.Safe;
            return;
        }

        float warningElapsed = CampTime - allowedCampTime;
        if (warningElapsed < escapeCountdownDuration)
        {
            CountdownRemaining = escapeCountdownDuration - warningElapsed;
            CurrentState = HiderAntiCampState.Warning;
            return;
        }

        CountdownRemaining = 0f;
        CurrentState = HiderAntiCampState.Revealed;
        if (CampTime >= _nextRevealAt)
        {
            TriggerAlert();
            _nextRevealAt = CampTime + repeatSoundInterval;
        }
    }

    public void Configure(
        PropTransformSystem transformSystem,
        PropHuntRoundManager configuredRoundManager)
    {
        if (roundManager != null && isActiveAndEnabled)
        {
            roundManager.RoundStateChanged -= HandleRoundStateChanged;
        }

        propTransformSystem = transformSystem;
        roundManager = configuredRoundManager;

        if (roundManager != null && isActiveAndEnabled)
        {
            roundManager.RoundStateChanged += HandleRoundStateChanged;
        }

        ResetAntiCamp();
    }

    public void ResetAntiCamp()
    {
        _eliminated = false;
        _suppressedByZone = false;
        _hasCampOrigin = false;
        CampTime = 0f;
        CountdownRemaining = 0f;
        _nextRevealAt = allowedCampTime + escapeCountdownDuration;
        CurrentState = HiderAntiCampState.Safe;
        _antiCampTriggered = false;
        ClearAlertPresentation();
    }

    public void SetSuppressedByZone(bool suppressed)
    {
        if (_suppressedByZone == suppressed)
        {
            return;
        }

        _suppressedByZone = suppressed;
        CountdownRemaining = 0f;
        CurrentState = HiderAntiCampState.Safe;
        _antiCampTriggered = false;
        ClearAlertPresentation();
    }

    public void SetEliminatedState(bool eliminated)
    {
        _eliminated = eliminated;
        _suppressedByZone = eliminated;
        _hasCampOrigin = false;
        CampTime = 0f;
        CountdownRemaining = 0f;
        CurrentState = HiderAntiCampState.Safe;
        _antiCampTriggered = false;
        ClearAlertPresentation();
    }

    public void ResumeFromZoneAt(Vector3 newCampOrigin)
    {
        if (_eliminated)
        {
            CountdownRemaining = 0f;
            CurrentState = HiderAntiCampState.Safe;
            _antiCampTriggered = false;
            ClearAlertPresentation();
            return;
        }

        _suppressedByZone = false;
        SetNewCampOrigin(newCampOrigin);
        ClearAlertPresentation();
    }

    private void HandleRoundStateChanged(PropHuntRoundState state)
    {
        if (state == PropHuntRoundState.Hunting)
        {
            SetNewCampOrigin(transform.position);
        }
        else
        {
            ResetAntiCamp();
        }
    }

    private void SetNewCampOrigin(Vector3 position)
    {
        _campOrigin = position;
        _hasCampOrigin = true;
        CampTime = 0f;
        CountdownRemaining = 0f;
        _nextRevealAt = allowedCampTime + escapeCountdownDuration;
        CurrentState = HiderAntiCampState.Safe;
        _antiCampTriggered = false;
    }

    private bool ShouldTrackAntiCamp()
    {
        return propTransformSystem != null &&
               !_eliminated &&
               !_suppressedByZone &&
               propTransformSystem.playerRole == PlayerRole.Hider &&
               propTransformSystem.currentState == PlayerDisguiseState.Disguised &&
               !propTransformSystem.IsEliminated &&
               roundManager != null &&
               roundManager.CurrentState == PropHuntRoundState.Hunting;
    }

    private float GetCampDisplacement(Vector3 currentPosition)
    {
        Vector3 displacement = currentPosition - _campOrigin;
        if (propTransformSystem != null && propTransformSystem.IsWallAttached)
        {
            return Vector3.ProjectOnPlane(
                displacement,
                propTransformSystem.WallNormal
            ).magnitude;
        }

        return new Vector2(displacement.x, displacement.z).magnitude;
    }

    private void ResolveReferences()
    {
        if (propTransformSystem == null)
        {
            propTransformSystem = GetComponent<PropTransformSystem>();
        }

        if (roundManager == null)
        {
            roundManager = FindObjectOfType<PropHuntRoundManager>();
        }

    }

    private void TriggerAlert()
    {
        HiderAntiCampAlertType alertType = _antiCampTriggered
            ? HiderAntiCampAlertType.RepeatedReveal
            : HiderAntiCampAlertType.InitialReveal;
        _antiCampTriggered = true;
        AlertTriggerCount++;
        AntiCampAlertTriggered?.Invoke(new HiderAntiCampAlertData(
            transform.position,
            repeatSoundInterval,
            alertType,
            Time.unscaledTime));
    }

    private void ClearAlertPresentation()
    {
        AntiCampAlertCleared?.Invoke();
    }

#if UNITY_EDITOR
    public void TriggerAlertForValidation()
    {
        CurrentState = HiderAntiCampState.Revealed;
        CountdownRemaining = 0f;
        TriggerAlert();
    }
#endif

}
