using System;
using UnityEngine;

public class HiderZoneStatusController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PropHuntShrinkingZone shrinkingZone;
    [SerializeField] private PropHuntRoundManager roundManager;
    [SerializeField] private HiderHealth hiderHealth;
    [SerializeField] private CharacterController playerCharacterController;
    [SerializeField] private Collider playerBodyCollider;
    [SerializeField] private Transform playerRoot;
    [SerializeField] private PropTransformSystem ghostCameraController;
    [SerializeField] private HiderAntiCampSystem antiCampController;

    [Header("Outside Zone Damage")]
    [SerializeField, Min(0f)] private float outsideGraceDuration = 3f;
    [SerializeField, Min(1)] private int outsideDamageAmount = 10;
    [SerializeField, Min(0.1f)] private float outsideDamageInterval = 2f;
    [SerializeField] private bool showZoneDebugLogs;

    private float _nextDamageAt;
    private bool _missingReferenceWarningLogged;
    private bool _eliminated;

    public bool IsOutsideZone { get; private set; }
    public float OutsideDuration { get; private set; }
    public float GraceTimeRemaining { get; private set; }
    public float TimeUntilNextDamage { get; private set; }
    public bool IsZoneDamageActive { get; private set; }
    public Vector3 TrackedWorldPosition => GetTrackedWorldPosition();
    public PropTransformSystem HiderTransformSystem => ghostCameraController;

    public event Action<bool> ZonePresenceChanged;
    public event Action<float> OutsideGraceUpdated;
    public event Action<int> ZoneDamageApplied;

    private void Awake()
    {
        ResolveReferences();
        ResetZoneState(false);
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToLifecycle();
    }

    private void OnDisable()
    {
        UnsubscribeFromLifecycle();
        ResetZoneState(false);
    }

    private void Update()
    {
        if (!CanEvaluateZone())
        {
            if (IsOutsideZone)
            {
                ResetZoneState(false);
            }

            return;
        }

        Vector3 trackedPosition = GetTrackedWorldPosition();
        float tolerance = IsOutsideZone
            ? -shrinkingZone.BoundaryTolerance
            : shrinkingZone.BoundaryTolerance;
        bool inside = shrinkingZone.IsPositionInsideZone(trackedPosition, tolerance);
        if (inside)
        {
            if (IsOutsideZone)
            {
                EnterSafeZone(trackedPosition);
            }

            return;
        }

        if (!IsOutsideZone)
        {
            EnterOutsideZone();
        }

        UpdateOutsideDamage();
    }

    public void Configure(
        PropHuntShrinkingZone configuredZone,
        PropHuntRoundManager configuredRoundManager,
        HiderHealth configuredHealth,
        CharacterController configuredCharacterController,
        Transform configuredPlayerRoot,
        PropTransformSystem configuredGhostCameraController,
        HiderAntiCampSystem configuredAntiCampController)
    {
        bool wasActive = isActiveAndEnabled;
        if (wasActive)
        {
            UnsubscribeFromLifecycle();
        }

        shrinkingZone = configuredZone;
        roundManager = configuredRoundManager;
        hiderHealth = configuredHealth;
        playerCharacterController = configuredCharacterController;
        playerBodyCollider = configuredCharacterController != null
            ? configuredCharacterController
            : configuredPlayerRoot != null
                ? configuredPlayerRoot.GetComponent<Collider>()
                : null;
        playerRoot = configuredPlayerRoot;
        ghostCameraController = configuredGhostCameraController;
        antiCampController = configuredAntiCampController;
        ResetZoneState(false);

        if (wasActive)
        {
            SubscribeToLifecycle();
        }
    }

    public void ResetForRound()
    {
        _eliminated = false;
        ResetZoneState(false);
    }

    public void SetEliminatedState(bool eliminated)
    {
        _eliminated = eliminated;
        ResetZoneState(false);
        if (eliminated)
        {
            antiCampController?.SetEliminatedState(true);
        }
    }

    private void SubscribeToLifecycle()
    {
        if (roundManager != null)
        {
            roundManager.RoundStarted -= HandleRoundStarted;
            roundManager.RoundStarted += HandleRoundStarted;
            roundManager.RoundStateChanged -= HandleRoundStateChanged;
            roundManager.RoundStateChanged += HandleRoundStateChanged;
        }

        if (shrinkingZone != null)
        {
            shrinkingZone.ZoneReset -= HandleZoneReset;
            shrinkingZone.ZoneReset += HandleZoneReset;
        }
    }

    private void UnsubscribeFromLifecycle()
    {
        if (roundManager != null)
        {
            roundManager.RoundStarted -= HandleRoundStarted;
            roundManager.RoundStateChanged -= HandleRoundStateChanged;
        }

        if (shrinkingZone != null)
        {
            shrinkingZone.ZoneReset -= HandleZoneReset;
        }
    }

    private void HandleRoundStarted()
    {
        ResetZoneState(false);
    }

    private void HandleRoundStateChanged(PropHuntRoundState state)
    {
        if (state != PropHuntRoundState.Hunting)
        {
            ResetZoneState(false);
        }
    }

    private void HandleZoneReset()
    {
        ResetZoneState(false);
    }

    private bool CanEvaluateZone()
    {
        if (shrinkingZone == null || roundManager == null || hiderHealth == null ||
            ghostCameraController == null || playerRoot == null)
        {
            WarnMissingReferencesOnce();
            return false;
        }

        return roundManager.CurrentState == PropHuntRoundState.Hunting &&
               !_eliminated &&
               shrinkingZone.IsZoneActive &&
               ghostCameraController.playerRole == PlayerRole.Hider &&
               !ghostCameraController.IsEliminated &&
               hiderHealth.IsAlive;
    }

    private Vector3 GetTrackedWorldPosition()
    {
        if (playerCharacterController != null && playerCharacterController.enabled)
        {
            return playerCharacterController.bounds.center;
        }

        if (playerBodyCollider != null && playerBodyCollider.enabled)
        {
            return playerBodyCollider.bounds.center;
        }

        return playerRoot != null ? playerRoot.position : transform.position;
    }

    private void EnterOutsideZone()
    {
        IsOutsideZone = true;
        OutsideDuration = 0f;
        GraceTimeRemaining = outsideGraceDuration;
        TimeUntilNextDamage = outsideGraceDuration;
        IsZoneDamageActive = false;
        _nextDamageAt = outsideGraceDuration;
        antiCampController?.SetSuppressedByZone(true);
        ZonePresenceChanged?.Invoke(true);
        OutsideGraceUpdated?.Invoke(GraceTimeRemaining);

        if (showZoneDebugLogs)
        {
            Debug.Log("Hider Zone: Player entered outside grace.");
        }
    }

    private void EnterSafeZone(Vector3 trackedPosition)
    {
        IsOutsideZone = false;
        OutsideDuration = 0f;
        GraceTimeRemaining = outsideGraceDuration;
        TimeUntilNextDamage = outsideGraceDuration;
        IsZoneDamageActive = false;
        _nextDamageAt = outsideGraceDuration;
        antiCampController?.ResumeFromZoneAt(playerRoot != null ? playerRoot.position : trackedPosition);
        ZonePresenceChanged?.Invoke(false);
        OutsideGraceUpdated?.Invoke(GraceTimeRemaining);

        if (showZoneDebugLogs)
        {
            Debug.Log("Hider Zone: Player returned to the safe zone; anti-camp origin reset.");
        }
    }

    private void UpdateOutsideDamage()
    {
        OutsideDuration += Time.deltaTime;
        GraceTimeRemaining = Mathf.Max(0f, outsideGraceDuration - OutsideDuration);
        TimeUntilNextDamage = Mathf.Max(0f, _nextDamageAt - OutsideDuration);
        OutsideGraceUpdated?.Invoke(GraceTimeRemaining);

        if (OutsideDuration < outsideGraceDuration)
        {
            return;
        }

        if (ghostCameraController != null && ghostCameraController.IsGhostCameraActive)
        {
            ghostCameraController.ForceExitGhostCamera();
            if (showZoneDebugLogs)
            {
                Debug.Log("Hider Zone: Ghost Camera forced to exit after outside grace expired.");
            }
        }

        IsZoneDamageActive = true;
        while (OutsideDuration >= _nextDamageAt && hiderHealth != null && !hiderHealth.IsDead)
        {
            ApplyZoneDamageTick();
            _nextDamageAt += outsideDamageInterval;
        }

        TimeUntilNextDamage = Mathf.Max(0f, _nextDamageAt - OutsideDuration);
    }

    private void ApplyZoneDamageTick()
    {
        if (hiderHealth == null || hiderHealth.IsDead)
        {
            return;
        }

        int previousHealth = hiderHealth.CurrentHealth;
        hiderHealth.TakeDamage(outsideDamageAmount, HiderDamageSource.Zone);
        int appliedDamage = previousHealth - hiderHealth.CurrentHealth;
        if (appliedDamage <= 0)
        {
            return;
        }

        ZoneDamageApplied?.Invoke(appliedDamage);
        if (showZoneDebugLogs)
        {
            Debug.Log($"Hider Zone: Zone damage {appliedDamage}. Health={hiderHealth.CurrentHealth}/{hiderHealth.MaxHealth}.");
        }
    }

    private void ResetZoneState(bool resumeAntiCampAtCurrentPosition)
    {
        bool wasOutside = IsOutsideZone;
        IsOutsideZone = false;
        OutsideDuration = 0f;
        GraceTimeRemaining = outsideGraceDuration;
        TimeUntilNextDamage = outsideGraceDuration;
        IsZoneDamageActive = false;
        _nextDamageAt = outsideGraceDuration;

        if (antiCampController != null)
        {
            if (resumeAntiCampAtCurrentPosition)
            {
                antiCampController.ResumeFromZoneAt(GetTrackedWorldPosition());
            }
            else
            {
                antiCampController.SetSuppressedByZone(false);
            }
        }

        if (wasOutside)
        {
            ZonePresenceChanged?.Invoke(false);
        }

        OutsideGraceUpdated?.Invoke(GraceTimeRemaining);
    }

    private void ResolveReferences()
    {
        if (playerRoot == null) playerRoot = transform;
        if (ghostCameraController == null) ghostCameraController = GetComponent<PropTransformSystem>();
        if (hiderHealth == null) hiderHealth = GetComponent<HiderHealth>();
        if (playerCharacterController == null) playerCharacterController = GetComponent<CharacterController>();
        if (playerBodyCollider == null) playerBodyCollider = GetComponent<Collider>();
        if (antiCampController == null) antiCampController = GetComponent<HiderAntiCampSystem>();
        if (roundManager == null) roundManager = FindObjectOfType<PropHuntRoundManager>();
        if (shrinkingZone == null) shrinkingZone = FindObjectOfType<PropHuntShrinkingZone>();
    }

    private void WarnMissingReferencesOnce()
    {
        if (_missingReferenceWarningLogged)
        {
            return;
        }

        _missingReferenceWarningLogged = true;
        Debug.LogWarning($"HiderZoneStatusController on '{name}' is missing a required reference; zone damage is disabled safely.");
    }

#if UNITY_EDITOR
    [ContextMenu("Debug/Force Outside State")]
    private void DebugForceOutsideState()
    {
        if (!IsOutsideZone) EnterOutsideZone();
    }

    [ContextMenu("Debug/Force Inside State")]
    private void DebugForceInsideState()
    {
        if (IsOutsideZone) EnterSafeZone(GetTrackedWorldPosition());
    }

    [ContextMenu("Debug/Apply One Zone Tick")]
    private void DebugApplyOneZoneTick()
    {
        ApplyZoneDamageTick();
    }
#endif

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsOutsideZone ? Color.red : Color.green;
        Gizmos.DrawWireSphere(GetTrackedWorldPosition(), 0.65f);
    }
}
