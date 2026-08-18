using System;
using System.Collections.Generic;
using UnityEngine;

public enum SeekerTeamRole
{
    PrimaryPursuer,
    SupportFlanker
}

[Serializable]
public struct SeekerTeamSightingSnapshot
{
    public Vector3 ApproximatePosition;
    public float Timestamp;
    [Range(0f, 1f)] public float Confidence;
    public float UncertaintyRadius;
    public Vector3 ObservedVelocity;
    public SeekerTeamRole SourceRole;
}

[DisallowMultipleComponent]
public sealed class SeekerTeamCoordinator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PropHuntRoundManager roundManager;
    [SerializeField] private HiderAntiCampSystem antiCamp;
    [SerializeField] private Transform secondarySpawn;
    [SerializeField] private SeekerAIController primarySeeker;
    [SerializeField] private SeekerAIController secondarySeeker;

    [Header("Fair activation")]
    [SerializeField, Min(0f)] private float activationRemainingSeconds = 45f;
    [SerializeField, Min(0f)] private float secondaryInitialFireLock = 1f;

    [Header("Coordination")]
    [SerializeField] private Vector2 snapshotErrorRange = new Vector2(1.5f, 3f);
    [SerializeField] private Vector2 flankOffsetRange = new Vector2(6f, 10f);
    [SerializeField] private Vector2 preferredTeamSpacing = new Vector2(5f, 7f);
    [SerializeField, Min(0.3f)] private float firePermitDuration = 0.35f;
    [SerializeField, Min(0.1f)] private float sightingBroadcastInterval = 0.45f;

    private SeekerAIController permitOwner;
    private float permitExpiresAt;
    private float nextTeamShotAt;
    private int lastGrantedFrame = -1;
    private float lastBroadcastAt = float.NegativeInfinity;
    private float nextSpacingCheckAt;
    private bool secondaryActivated;
    private readonly Dictionary<SeekerAIController, int> patrolClaims =
        new Dictionary<SeekerAIController, int>();
    private readonly Dictionary<SeekerAIController, Vector3> searchClaims =
        new Dictionary<SeekerAIController, Vector3>();

    public int AliveSeekerCount
    {
        get
        {
            int count = 0;
            if (primarySeeker != null && primarySeeker.IsAlive) count++;
            if (secondarySeeker != null && secondarySeeker.IsAlive) count++;
            return count;
        }
    }

    public bool SecondaryActivated => secondaryActivated;
    public float ActivationRemainingSeconds => activationRemainingSeconds;
    public float FirePermitDuration => firePermitDuration;
    public event Action<int> AliveSeekerCountChanged;

    private void Awake()
    {
        ResolveReferences();
        BindControllers();
    }

    private void OnEnable()
    {
        ResolveReferences();
        BindControllers();
        Subscribe();
        ApplyRoundState(roundManager != null
            ? roundManager.CurrentState
            : PropHuntRoundState.Waiting);
    }

    private void OnDisable()
    {
        Unsubscribe();
        ReleaseAllPermits();
    }

    private void Update()
    {
        if (permitOwner != null && Time.time >= permitExpiresAt)
        {
            permitOwner = null;
        }

        if (roundManager == null ||
            roundManager.CurrentState != PropHuntRoundState.Hunting)
        {
            return;
        }

        if (!secondaryActivated &&
            (roundManager.RemainingTime <= activationRemainingSeconds ||
             primarySeeker == null || !primarySeeker.IsAlive))
        {
            ActivateSecondary();
        }

        if (secondaryActivated && Time.time >= nextSpacingCheckAt)
        {
            nextSpacingCheckAt = Time.time + 1f;
            MaintainTeamSpacing();
        }
    }

    public void Configure(
        PropHuntRoundManager configuredRoundManager,
        HiderAntiCampSystem configuredAntiCamp,
        Transform configuredSecondarySpawn,
        SeekerAIController configuredPrimary,
        SeekerAIController configuredSecondary)
    {
        Unsubscribe();
        roundManager = configuredRoundManager;
        antiCamp = configuredAntiCamp;
        secondarySpawn = configuredSecondarySpawn;
        primarySeeker = configuredPrimary;
        secondarySeeker = configuredSecondary;
        BindControllers();
        if (isActiveAndEnabled) Subscribe();
    }

    public bool TryAcquireFirePermit(SeekerAIController requester)
    {
        if (requester == null || !requester.IsOperational ||
            roundManager == null ||
            roundManager.CurrentState != PropHuntRoundState.Hunting ||
            Time.time < requester.FireAllowedAt ||
            Time.time < nextTeamShotAt ||
            lastGrantedFrame == Time.frameCount)
        {
            return false;
        }

        if (permitOwner != null && permitOwner != requester &&
            Time.time < permitExpiresAt)
        {
            return false;
        }

        permitOwner = requester;
        permitExpiresAt = Time.time + firePermitDuration;
        lastGrantedFrame = Time.frameCount;
        return true;
    }

    public void CompleteFirePermit(SeekerAIController requester, bool fired)
    {
        if (requester != permitOwner) return;
        if (fired)
        {
            nextTeamShotAt = Mathf.Max(
                nextTeamShotAt,
                Time.time + firePermitDuration);
        }
        permitOwner = null;
    }

    public void ClaimPatrolRegion(SeekerAIController requester, int regionIndex)
    {
        if (requester != null) patrolClaims[requester] = regionIndex;
    }

    public bool IsPatrolRegionClaimedByOther(
        SeekerAIController requester,
        int regionIndex)
    {
        foreach (KeyValuePair<SeekerAIController, int> claim in patrolClaims)
        {
            if (claim.Key != null && claim.Key != requester &&
                claim.Key.IsOperational && claim.Value == regionIndex)
            {
                return true;
            }
        }
        return false;
    }

    public bool TryClaimSearchArea(
        SeekerAIController requester,
        Vector3 position,
        float separation)
    {
        float minimumSqr = separation * separation;
        foreach (KeyValuePair<SeekerAIController, Vector3> claim in searchClaims)
        {
            if (claim.Key != null && claim.Key != requester &&
                claim.Key.IsOperational &&
                (claim.Value - position).sqrMagnitude < minimumSqr)
            {
                return false;
            }
        }
        if (requester != null) searchClaims[requester] = position;
        return true;
    }

    public void ReportSighting(SeekerAIController source, Vector3 exactPosition)
    {
        if (source == null || Time.time - lastBroadcastAt < sightingBroadcastInterval)
            return;

        SeekerAIController receiver = source == primarySeeker
            ? secondarySeeker
            : primarySeeker;
        if (receiver == null || !receiver.IsOperational) return;

        lastBroadcastAt = Time.time;
        Vector2 noise = UnityEngine.Random.insideUnitCircle.normalized *
                        UnityEngine.Random.Range(
                            Mathf.Min(snapshotErrorRange.x, snapshotErrorRange.y),
                            Mathf.Max(snapshotErrorRange.x, snapshotErrorRange.y));
        SeekerTeamSightingSnapshot snapshot = new SeekerTeamSightingSnapshot
        {
            ApproximatePosition = exactPosition +
                                  source.LastObservedVelocity * 0.8f +
                                  new Vector3(noise.x, 0f, noise.y),
            Timestamp = Time.time,
            Confidence = 0.78f,
            UncertaintyRadius = noise.magnitude,
            ObservedVelocity = source.LastObservedVelocity,
            SourceRole = source.TeamRole
        };

        Vector3 flank = ResolveFlankDestination(
            receiver,
            snapshot.ApproximatePosition,
            source.transform.position);
        receiver.ReceiveTeamSnapshot(snapshot, flank);
    }

    public void NotifySeekerEliminated(SeekerAIController eliminated)
    {
        if (permitOwner == eliminated) permitOwner = null;
        if (eliminated == primarySeeker && !secondaryActivated)
            ActivateSecondary();

        int alive = AliveSeekerCount;
        AliveSeekerCountChanged?.Invoke(alive);
        roundManager?.RefreshPlayerCounts();
        if (alive <= 0)
        {
            roundManager?.EndRound(
                RoundOutcome.HiderWin,
                RoundEndReason.AllSeekersEliminated);
        }
    }

    public void StopTeamForRoundEnd()
    {
        ReleaseAllPermits();
        primarySeeker?.StopForRoundEnd();
        secondarySeeker?.StopForRoundEnd();
    }

    private void ActivateSecondary()
    {
        if (secondaryActivated || secondarySeeker == null ||
            !secondarySeeker.IsAlive)
        {
            return;
        }

        secondaryActivated = true;
        Vector3 position = secondarySpawn != null
            ? secondarySpawn.position
            : secondarySeeker.transform.position;
        Quaternion rotation = secondarySpawn != null
            ? secondarySpawn.rotation
            : secondarySeeker.transform.rotation;
        secondarySeeker.ActivateFromDormant(
            position,
            rotation,
            secondaryInitialFireLock);
        AliveSeekerCountChanged?.Invoke(AliveSeekerCount);
    }

    private void HandleAntiCampAlert(HiderAntiCampAlertData alert)
    {
        SeekerAIController selected = SelectNearestOperational(alert.AlertPosition);
        if (selected == null) return;

        Vector2 offset = UnityEngine.Random.insideUnitCircle.normalized *
                         UnityEngine.Random.Range(2f, 4f);
        selected.ReceiveTeamInvestigation(
            alert.AlertPosition + new Vector3(offset.x, 0f, offset.y),
            Mathf.Max(2f, alert.AlertRadius));
    }

    private SeekerAIController SelectNearestOperational(Vector3 point)
    {
        SeekerAIController best = null;
        float bestDistance = float.PositiveInfinity;
        SeekerAIController[] candidates = { primarySeeker, secondarySeeker };
        foreach (SeekerAIController candidate in candidates)
        {
            if (candidate == null || !candidate.IsOperational) continue;
            float distance = (candidate.transform.position - point).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = candidate;
        }
        return best;
    }

    private void MaintainTeamSpacing()
    {
        if (primarySeeker == null || secondarySeeker == null ||
            !primarySeeker.IsOperational || !secondarySeeker.IsOperational ||
            primarySeeker.CurrentState == SeekerAIState.Attack ||
            secondarySeeker.CurrentState == SeekerAIState.Attack)
        {
            return;
        }

        Vector3 separation =
            secondarySeeker.transform.position - primarySeeker.transform.position;
        separation.y = 0f;
        float minimumSpacing = Mathf.Min(
            preferredTeamSpacing.x,
            preferredTeamSpacing.y);
        if (separation.sqrMagnitude >= minimumSpacing * minimumSpacing) return;
        if (separation.sqrMagnitude < 0.01f)
            separation = secondarySeeker.transform.right;
        Vector3 requested = secondarySeeker.transform.position +
                            separation.normalized *
                            Mathf.Max(minimumSpacing, 5f);
        secondarySeeker.ReceiveSpacingCorrection(requested);
    }

    private Vector3 ResolveFlankDestination(
        SeekerAIController receiver,
        Vector3 snapshot,
        Vector3 sourcePosition)
    {
        if (receiver.TeamRole == SeekerTeamRole.PrimaryPursuer)
        {
            return receiver.TrySampleReachable(snapshot, 4f, out Vector3 pursuit)
                ? pursuit
                : snapshot;
        }

        Vector3 sourceDirection = sourcePosition - snapshot;
        sourceDirection.y = 0f;
        if (sourceDirection.sqrMagnitude < 0.01f) sourceDirection = Vector3.forward;
        Vector3 lateral = Vector3.Cross(Vector3.up, sourceDirection.normalized);
        float side = receiver.TeamRole == SeekerTeamRole.SupportFlanker ? 1f : -1f;
        float distance = UnityEngine.Random.Range(
            Mathf.Min(flankOffsetRange.x, flankOffsetRange.y),
            Mathf.Max(flankOffsetRange.x, flankOffsetRange.y));
        Vector3 requested = snapshot + lateral * side * distance;
        if (receiver.TrySampleReachable(requested, 4f, out Vector3 sampled))
            return sampled;

        float spacing = UnityEngine.Random.Range(
            Mathf.Min(preferredTeamSpacing.x, preferredTeamSpacing.y),
            Mathf.Max(preferredTeamSpacing.x, preferredTeamSpacing.y));
        requested = snapshot - sourceDirection.normalized * spacing;
        return receiver.TrySampleReachable(requested, 4f, out sampled)
            ? sampled
            : snapshot;
    }

    private void HandleRoundStateChanged(PropHuntRoundState state)
    {
        ApplyRoundState(state);
    }

    private void ApplyRoundState(PropHuntRoundState state)
    {
        if (state != PropHuntRoundState.Hunting)
        {
            patrolClaims.Clear();
            searchClaims.Clear();
        }
        if (state == PropHuntRoundState.Preparation)
        {
            secondaryActivated = false;
            ReleaseAllPermits();
            primarySeeker?.SetDormant(false);
            secondarySeeker?.SetDormant(true);
            AliveSeekerCountChanged?.Invoke(AliveSeekerCount);
        }
        else if (state == PropHuntRoundState.Ended)
        {
            StopTeamForRoundEnd();
        }
    }

    private void BindControllers()
    {
        primarySeeker?.ConfigureTeam(this, SeekerTeamRole.PrimaryPursuer, false);
        secondarySeeker?.ConfigureTeam(this, SeekerTeamRole.SupportFlanker, true);
        roundManager?.ConfigureSeekerTeam(this);
    }

    private void ResolveReferences()
    {
        if (roundManager == null)
            roundManager = FindObjectOfType<PropHuntRoundManager>(true);
        if (antiCamp == null)
            antiCamp = FindObjectOfType<HiderAntiCampSystem>(true);
    }

    private void Subscribe()
    {
        if (roundManager != null)
        {
            roundManager.RoundStateChanged -= HandleRoundStateChanged;
            roundManager.RoundStateChanged += HandleRoundStateChanged;
        }
        if (antiCamp != null)
        {
            antiCamp.AntiCampAlertTriggered -= HandleAntiCampAlert;
            antiCamp.AntiCampAlertTriggered += HandleAntiCampAlert;
        }
    }

    private void Unsubscribe()
    {
        if (roundManager != null)
            roundManager.RoundStateChanged -= HandleRoundStateChanged;
        if (antiCamp != null)
            antiCamp.AntiCampAlertTriggered -= HandleAntiCampAlert;
    }

    private void ReleaseAllPermits()
    {
        permitOwner = null;
        permitExpiresAt = 0f;
        nextTeamShotAt = 0f;
        lastGrantedFrame = -1;
    }
}
