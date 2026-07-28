using UnityEngine;
using UnityEngine.AI;

public enum SeekerAIState
{
    PreparationWait,
    Patrol,
    Observe,
    Investigate,
    Chase,
    Attack,
    SearchLastKnown,
    ReturnToPatrol,
    Reloading,
    Eliminated,
    RoundEnded
}

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class SeekerAIController : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private PropHuntRoundManager roundManager;
    [SerializeField] private HiderHealth hiderHealth;
    [SerializeField] private HiderAntiCampSystem antiCamp;
    [SerializeField] private SeekerHealth seekerHealth;
    [SerializeField] private SeekerRaycastWeapon weapon;
    [SerializeField] private SeekerWeaponEnergy energy;

    [Header("Modules")]
    [SerializeField] private SeekerAINavigation navigation;
    [SerializeField] private SeekerAIPerception perception;
    [SerializeField] private SeekerAICombat combat;
    [SerializeField] private SeekerAISuspicionSystem suspicion;

    [Header("Decision timing")]
    [SerializeField, Min(0f)] private float reactionTime = 0.6f;
    [SerializeField, Min(0f)] private float lostSightGrace = 0.4f;
    [SerializeField, Min(0f)] private float searchDuration = 8f;
    [SerializeField] private Vector2 preferredAttackRange = new Vector2(8f, 18f);
    [SerializeField] private Vector2 observeDurationRange = new Vector2(0.5f, 1.2f);

    private float visibleEvidenceTime;
    private float lostSightTime;
    private float stateEnteredAt;
    private float lastKnownUpdatedAt = float.NegativeInfinity;
    private Vector3 lastKnownPosition;
    private Vector3 searchOrigin;
    private Vector3 investigationSnapshot;
    private Collider suspicionTarget;
    private int searchPointsRemaining;
    private float searchPointReachedAt;
    private float suspicionObservedAt;
    private float observeDuration;
    private Quaternion observeCenterRotation;
    private SeekerAIState resumeAfterReload = SeekerAIState.Patrol;
    private bool hasLastKnownPosition;

    public SeekerAIState CurrentState { get; private set; } =
        SeekerAIState.PreparationWait;
    public Vector3 LastKnownPosition => lastKnownPosition;
    public Vector3 CurrentSearchOrigin => searchOrigin;
    public Vector3 InvestigationSnapshot => investigationSnapshot;
    public bool HasLastKnownPosition => hasLastKnownPosition;
    public float LastKnownUpdatedAt => lastKnownUpdatedAt;
    public float ReactionTime => reactionTime;
    public float LostSightGrace => lostSightGrace;
    public float SearchDuration => searchDuration;
    public Vector2 PreferredAttackRange => preferredAttackRange;

    public Vector3 ResolvePresentationAimPoint()
    {
        const float bodyAimHeight = 1.05f;
        switch (CurrentState)
        {
            case SeekerAIState.Attack:
            case SeekerAIState.Chase:
                if (perception != null && perception.CanIdentifyHider &&
                    perception.Hider != null)
                {
                    return perception.Hider.transform.position +
                           Vector3.up * bodyAimHeight;
                }
                return lastKnownPosition + Vector3.up * bodyAimHeight;

            case SeekerAIState.Investigate:
                if (suspicionTarget != null)
                    return suspicionTarget.bounds.center;
                return investigationSnapshot + Vector3.up * 0.75f;

            case SeekerAIState.SearchLastKnown:
            case SeekerAIState.Reloading:
                return searchOrigin + Vector3.up * 0.75f;

            default:
                return transform.position + transform.forward * 12f +
                       Vector3.up * 1.35f;
        }
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
        ApplyRoundState(roundManager != null
            ? roundManager.CurrentState
            : PropHuntRoundState.Waiting);
    }

    private void OnDisable()
    {
        Unsubscribe();
        navigation?.SetStopped(true);
    }

    private void Update()
    {
        if (roundManager == null || seekerHealth == null ||
            !seekerHealth.IsAlive)
        {
            if (seekerHealth != null && !seekerHealth.IsAlive)
                EliminateSeeker();
            return;
        }

        if (hiderHealth != null && !hiderHealth.IsAlive)
        {
            roundManager.EndRoundWithWinner(PropHuntRoundWinner.Seekers);
            return;
        }

        if (roundManager.CurrentState != PropHuntRoundState.Hunting)
        {
            return;
        }

        perception?.Observe();
        UpdateEvidence();
        if (CurrentState != SeekerAIState.Reloading &&
            visibleEvidenceTime >= reactionTime)
        {
            RecordVisibleHider();
            SelectCombatState();
        }

        TickNavigationRecovery();
        TickState();
    }

    public void Configure(
        PropHuntRoundManager configuredRoundManager,
        HiderHealth configuredHiderHealth,
        HiderAntiCampSystem configuredAntiCamp,
        SeekerHealth configuredSeekerHealth,
        SeekerRaycastWeapon configuredWeapon,
        SeekerWeaponEnergy configuredEnergy,
        SeekerAINavigation configuredNavigation,
        SeekerAIPerception configuredPerception,
        SeekerAICombat configuredCombat,
        SeekerAISuspicionSystem configuredSuspicion)
    {
        Unsubscribe();
        roundManager = configuredRoundManager;
        hiderHealth = configuredHiderHealth;
        antiCamp = configuredAntiCamp;
        seekerHealth = configuredSeekerHealth;
        weapon = configuredWeapon;
        energy = configuredEnergy;
        navigation = configuredNavigation;
        perception = configuredPerception;
        combat = configuredCombat;
        suspicion = configuredSuspicion;
        if (isActiveAndEnabled) Subscribe();
    }

    private void TickState()
    {
        if (CurrentState != SeekerAIState.Reloading &&
            energy != null && energy.IsReloading)
        {
            BeginReloadState(CurrentState);
            return;
        }

        switch (CurrentState)
        {
            case SeekerAIState.Patrol:
                TickPatrol();
                break;

            case SeekerAIState.Observe:
                TickObserve();
                break;

            case SeekerAIState.Investigate:
                TickInvestigate();
                break;

            case SeekerAIState.Chase:
                TickChase();
                break;

            case SeekerAIState.Attack:
                TickAttack();
                break;

            case SeekerAIState.SearchLastKnown:
                TickSearch();
                break;

            case SeekerAIState.ReturnToPatrol:
                ChangeState(SeekerAIState.Patrol);
                break;

            case SeekerAIState.Reloading:
                TickReloading();
                break;
        }
    }

    private void TickPatrol()
    {
        if (TryAcquireVisibleSuspicion()) return;
        if (combat != null && combat.TryEarlyReload(false))
        {
            BeginReloadState(SeekerAIState.Patrol);
            return;
        }

        if (navigation == null) return;
        if (navigation.HasArrived)
        {
            ChangeState(SeekerAIState.Observe);
        }
        else if (!navigation.Agent.pathPending && !navigation.Agent.hasPath)
        {
            navigation.MoveToRandomPatrolPoint();
        }
    }

    private void TickObserve()
    {
        if (TryAcquireVisibleSuspicion()) return;
        if (combat != null && combat.TryEarlyReload(false))
        {
            BeginReloadState(SeekerAIState.Observe);
            return;
        }

        float elapsed = Time.time - stateEnteredAt;
        float normalized = observeDuration > 0f
            ? Mathf.Clamp01(elapsed / observeDuration)
            : 1f;
        float sweep;
        if (normalized < 0.33f)
            sweep = Mathf.Lerp(0f, -42f, normalized / 0.33f);
        else if (normalized < 0.72f)
            sweep = Mathf.Lerp(-42f, 42f, (normalized - 0.33f) / 0.39f);
        else
            sweep = Mathf.Lerp(42f, 0f, (normalized - 0.72f) / 0.28f);
        Quaternion desired = observeCenterRotation *
                             Quaternion.Euler(0f, sweep, 0f);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            desired,
            180f * Time.deltaTime);
        if (elapsed >= observeDuration)
        {
            ChangeState(SeekerAIState.Patrol);
        }
    }

    private void TickChase()
    {
        if (perception != null && perception.CanIdentifyHider &&
            perception.Hider != null)
        {
            float distance = perception.DistanceToHider;
            if (distance >= preferredAttackRange.x &&
                distance <= preferredAttackRange.y)
            {
                ChangeState(SeekerAIState.Attack);
            }
            else if (distance < preferredAttackRange.x)
            {
                navigation?.TryMoveAwayFrom(
                    perception.Hider.transform.position,
                    preferredAttackRange.x - distance + 3f);
                Face(perception.LastVisiblePosition);
            }
            else
            {
                navigation?.MoveTo(
                    perception.Hider.transform.position,
                    true);
            }
        }
        else if (lostSightTime > lostSightGrace)
        {
            BeginLastKnownSearch();
        }
    }

    private void TickAttack()
    {
        if (perception == null || !perception.CanIdentifyHider ||
            perception.Hider == null)
        {
            if (lostSightTime > lostSightGrace)
                BeginLastKnownSearch();
            return;
        }

        float distance = perception.DistanceToHider;
        if (distance < preferredAttackRange.x ||
            distance > preferredAttackRange.y)
        {
            ChangeState(SeekerAIState.Chase);
            return;
        }

        navigation?.SetStopped(true);
        Face(perception.LastVisiblePosition);
        if (energy != null && energy.CurrentCharges <= 0)
        {
            if (!energy.IsReloading) energy.TryStartReloadFromAI();
            BeginReloadState(SeekerAIState.Attack);
            return;
        }

        combat?.TryFireAtHider(perception.Hider);
    }

    private void TickInvestigate()
    {
        if (navigation != null && !navigation.HasArrived)
        {
            if (!navigation.Agent.hasPath &&
                !navigation.MoveTo(investigationSnapshot, false))
            {
                searchOrigin = investigationSnapshot;
                ChangeState(SeekerAIState.SearchLastKnown);
            }
            return;
        }

        if (suspicionTarget == null && suspicion != null)
        {
            if (suspicion.TryTakeNext(out suspicionTarget))
            {
                suspicionObservedAt = Time.time;
            }
        }

        if (suspicionTarget != null)
        {
            navigation?.SetStopped(true);
            Face(suspicionTarget.bounds.center);
            if (Time.time - suspicionObservedAt < reactionTime)
            {
                return;
            }

            if (energy != null && energy.CurrentCharges <= 0)
            {
                if (!energy.IsReloading) energy.TryStartReloadFromAI();
                BeginReloadState(SeekerAIState.Investigate);
                return;
            }

            if (combat != null && combat.TryFireAtCollider(suspicionTarget))
            {
                suspicionTarget = null;
                suspicionObservedAt = 0f;
            }
            return;
        }

        searchOrigin = investigationSnapshot;
        ChangeState(SeekerAIState.SearchLastKnown);
    }

    private void TickSearch()
    {
        if (TryAcquireVisibleSuspicion()) return;
        if (combat != null && combat.TryEarlyReload(false))
        {
            BeginReloadState(SeekerAIState.SearchLastKnown);
            return;
        }

        if (Time.time - stateEnteredAt >= searchDuration ||
            searchPointsRemaining <= 0)
        {
            ChangeState(SeekerAIState.ReturnToPatrol);
            return;
        }

        if (navigation == null) return;
        if (!navigation.Agent.hasPath && !navigation.HasArrived)
        {
            MoveToNextSearchPoint();
            return;
        }

        if (!navigation.HasArrived) return;
        if (searchPointReachedAt <= 0f)
        {
            searchPointReachedAt = Time.time;
            observeCenterRotation = transform.rotation;
        }

        float observedFor = Time.time - searchPointReachedAt;
        float sweep = Mathf.Sin(observedFor * 3.5f) * 36f;
        Quaternion desired = observeCenterRotation *
                             Quaternion.Euler(0f, sweep, 0f);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            desired,
            170f * Time.deltaTime);
        if (observedFor < 0.7f) return;

        searchPointsRemaining--;
        searchPointReachedAt = 0f;
        if (searchPointsRemaining > 0) MoveToNextSearchPoint();
    }

    private void TickReloading()
    {
        navigation?.SetStopped(true);
        if (energy != null && energy.IsReloading) return;

        if (perception != null && perception.CanIdentifyHider)
        {
            SelectCombatState();
            return;
        }

        SeekerAIState resume = resumeAfterReload;
        if (resume == SeekerAIState.Attack ||
            resume == SeekerAIState.Chase)
        {
            BeginLastKnownSearch();
        }
        else
        {
            ChangeState(resume);
        }
    }

    private void UpdateEvidence()
    {
        if (perception != null && perception.HasLineOfSight &&
            perception.CanIdentifyHider)
        {
            visibleEvidenceTime += Time.deltaTime;
            lostSightTime = 0f;
            if (visibleEvidenceTime >= reactionTime)
                RecordVisibleHider();
        }
        else
        {
            visibleEvidenceTime = 0f;
            lostSightTime += Time.deltaTime;
        }
    }

    private void RecordVisibleHider()
    {
        if (perception == null || !perception.CanIdentifyHider) return;
        lastKnownPosition = perception.LastVisiblePosition;
        searchOrigin = lastKnownPosition;
        hasLastKnownPosition = true;
        lastKnownUpdatedAt = Time.time;
    }

    private void SelectCombatState()
    {
        if (perception == null || !perception.CanIdentifyHider) return;
        float distance = perception.DistanceToHider;
        ChangeState(distance >= preferredAttackRange.x &&
                    distance <= preferredAttackRange.y
            ? SeekerAIState.Attack
            : SeekerAIState.Chase);
    }

    private void BeginLastKnownSearch()
    {
        perception?.ForgetPriorSight();
        visibleEvidenceTime = 0f;
        if (hasLastKnownPosition) searchOrigin = lastKnownPosition;
        ChangeState(SeekerAIState.SearchLastKnown);
    }

    private bool TryAcquireVisibleSuspicion()
    {
        if (suspicion == null || perception == null ||
            !suspicion.TryFindVisibleHighSuspicion(
                perception.Eye,
                perception,
                perception.ViewDistance,
                perception.FieldOfView,
                out Collider target))
        {
            return false;
        }

        suspicionTarget = target;
        suspicionObservedAt = Time.time;
        investigationSnapshot = target.bounds.center;
        navigation?.MoveTo(investigationSnapshot, false);
        ChangeState(SeekerAIState.Investigate);
        return true;
    }

    private void MoveToNextSearchPoint()
    {
        if (navigation == null ||
            navigation.MoveToRandomPointNear(searchOrigin, 4f, 7f))
        {
            return;
        }

        searchPointsRemaining--;
    }

    private void TickNavigationRecovery()
    {
        if (navigation == null ||
            !navigation.TickStuckRecovery(out bool abandoned) ||
            !abandoned)
        {
            return;
        }

        switch (CurrentState)
        {
            case SeekerAIState.Patrol:
            case SeekerAIState.ReturnToPatrol:
                navigation.MoveToRandomPatrolPoint();
                break;
            case SeekerAIState.Chase:
                if (perception != null && perception.CanIdentifyHider)
                    navigation.MoveTo(perception.Hider.transform.position, true);
                else
                    BeginLastKnownSearch();
                break;
            case SeekerAIState.Investigate:
                if (!navigation.MoveToRandomPointNear(
                        investigationSnapshot, 2f, 4f))
                {
                    searchOrigin = investigationSnapshot;
                    ChangeState(SeekerAIState.SearchLastKnown);
                }
                break;
            case SeekerAIState.SearchLastKnown:
                MoveToNextSearchPoint();
                break;
        }
    }

    private void HandleAntiCampAlert(HiderAntiCampAlertData alert)
    {
        if (roundManager == null ||
            roundManager.CurrentState != PropHuntRoundState.Hunting ||
            CurrentState == SeekerAIState.Eliminated ||
            CurrentState == SeekerAIState.RoundEnded)
        {
            return;
        }

        bool found = false;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2 offset2D = Random.insideUnitCircle.normalized *
                              Random.Range(2f, 4f);
            Vector3 requested = alert.AlertPosition +
                                new Vector3(offset2D.x, 0f, offset2D.y);
            if (navigation != null &&
                navigation.TrySampleReachable(
                    requested,
                    Mathf.Max(2f, alert.AlertRadius),
                    out investigationSnapshot) &&
                Vector3.Distance(
                    investigationSnapshot,
                    alert.AlertPosition) >= 1f)
            {
                found = true;
                break;
            }
        }

        if (!found) return;
        suspicion?.BuildInvestigation(investigationSnapshot);
        suspicionTarget = null;
        suspicionObservedAt = 0f;
        ChangeState(SeekerAIState.Investigate);
    }

    private void HandleRoundStateChanged(PropHuntRoundState state)
    {
        ApplyRoundState(state);
    }

    private void ApplyRoundState(PropHuntRoundState state)
    {
        if (state == PropHuntRoundState.Preparation)
        {
            seekerHealth?.ResetForRound();
            energy?.ResetForRound();
            perception?.ForgetPriorSight();
            suspicion?.Clear();
            weapon?.SetWeaponActive(false);
            hasLastKnownPosition = false;
            lastKnownUpdatedAt = float.NegativeInfinity;
            visibleEvidenceTime = 0f;
            lostSightTime = 0f;
            ChangeState(SeekerAIState.PreparationWait);
        }
        else if (state == PropHuntRoundState.Hunting)
        {
            weapon?.SetWeaponActive(true);
            ChangeState(SeekerAIState.Patrol);
        }
        else if (state == PropHuntRoundState.Ended)
        {
            weapon?.SetWeaponActive(false);
            if (CurrentState != SeekerAIState.Eliminated)
                ChangeState(SeekerAIState.RoundEnded);
        }
        else
        {
            weapon?.SetWeaponActive(false);
            ChangeState(SeekerAIState.PreparationWait);
        }
    }

    private void HandleSeekerHealthChanged(int current, int maximum)
    {
        if (current <= 0) EliminateSeeker();
    }

    private void HandleHiderEliminated(HiderHealth eliminatedHider)
    {
        roundManager?.EndRoundWithWinner(PropHuntRoundWinner.Seekers);
    }

    private void EliminateSeeker()
    {
        if (CurrentState == SeekerAIState.Eliminated ||
            CurrentState == SeekerAIState.RoundEnded) return;
        weapon?.SetWeaponActive(false);
        navigation?.SetStopped(true);
        ChangeState(SeekerAIState.Eliminated);
        roundManager?.EndRoundWithWinner(PropHuntRoundWinner.Hiders);
    }

    private void BeginReloadState(SeekerAIState resume)
    {
        if (CurrentState != SeekerAIState.Reloading)
            resumeAfterReload = resume;
        ChangeState(SeekerAIState.Reloading);
    }

    private void ChangeState(SeekerAIState next)
    {
        if (CurrentState == next) return;
        CurrentState = next;
        stateEnteredAt = Time.time;

        switch (next)
        {
            case SeekerAIState.Patrol:
                suspicion?.Clear();
                suspicionTarget = null;
                navigation?.MoveToRandomPatrolPoint();
                break;

            case SeekerAIState.Observe:
                navigation?.SetStopped(true);
                observeDuration = Random.Range(
                    Mathf.Min(observeDurationRange.x, observeDurationRange.y),
                    Mathf.Max(observeDurationRange.x, observeDurationRange.y));
                observeCenterRotation = transform.rotation;
                break;

            case SeekerAIState.Investigate:
                navigation?.MoveTo(investigationSnapshot, false);
                break;

            case SeekerAIState.SearchLastKnown:
                suspicion?.BuildSearch(searchOrigin);
                suspicionTarget = null;
                searchPointsRemaining = Random.Range(3, 6);
                searchPointReachedAt = 0f;
                MoveToNextSearchPoint();
                break;

            case SeekerAIState.Reloading:
            case SeekerAIState.Eliminated:
            case SeekerAIState.RoundEnded:
            case SeekerAIState.PreparationWait:
                navigation?.SetStopped(true);
                break;
        }
    }

    private void Face(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction),
                360f * Time.deltaTime);
        }
    }

    private void ResolveReferences()
    {
        if (roundManager == null)
            roundManager = FindObjectOfType<PropHuntRoundManager>(true);
        if (hiderHealth == null)
            hiderHealth = FindObjectOfType<HiderHealth>(true);
        if (antiCamp == null && hiderHealth != null)
            antiCamp = hiderHealth.GetComponent<HiderAntiCampSystem>();
        if (seekerHealth == null) seekerHealth = GetComponent<SeekerHealth>();
        if (weapon == null)
            weapon = GetComponentInChildren<SeekerRaycastWeapon>(true);
        if (energy == null) energy = GetComponent<SeekerWeaponEnergy>();
        if (navigation == null) navigation = GetComponent<SeekerAINavigation>();
        if (perception == null) perception = GetComponent<SeekerAIPerception>();
        if (combat == null) combat = GetComponent<SeekerAICombat>();
        if (suspicion == null) suspicion = GetComponent<SeekerAISuspicionSystem>();
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
        if (seekerHealth != null)
        {
            seekerHealth.HealthChanged -= HandleSeekerHealthChanged;
            seekerHealth.HealthChanged += HandleSeekerHealthChanged;
        }
        if (hiderHealth != null)
        {
            hiderHealth.Eliminated -= HandleHiderEliminated;
            hiderHealth.Eliminated += HandleHiderEliminated;
        }
    }

    private void Unsubscribe()
    {
        if (roundManager != null)
            roundManager.RoundStateChanged -= HandleRoundStateChanged;
        if (antiCamp != null)
            antiCamp.AntiCampAlertTriggered -= HandleAntiCampAlert;
        if (seekerHealth != null)
            seekerHealth.HealthChanged -= HandleSeekerHealthChanged;
        if (hiderHealth != null)
            hiderHealth.Eliminated -= HandleHiderEliminated;
    }
}
