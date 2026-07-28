using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class SeekerAINavigation : MonoBehaviour
{
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Transform[] patrolRegions = new Transform[0];
    [SerializeField, Min(0f)] private float patrolSpeed = 2.3f;
    [SerializeField, Min(0f)] private float chaseSpeed = 4.2f;
    [SerializeField, Min(0.1f)] private float fallbackPatrolRadius = 18f;
    [SerializeField, Min(0.5f)] private float stuckTimeout = 1.75f;

    private readonly List<int> recentPatrolIndices = new List<int>(3);
    private readonly Dictionary<int, float> lastPatrolVisitAt =
        new Dictionary<int, float>();
    private NavMeshPath scratchPath;
    private Vector3 intendedDestination;
    private Vector3 lastProgressPosition;
    private float bestRemainingDistance = float.PositiveInfinity;
    private float lastProgressAt;
    private bool hasIntendedDestination;

    public NavMeshAgent Agent => agent;
    public bool IsReady => agent != null && agent.enabled && agent.isOnNavMesh;
    public bool HasArrived => IsReady && !agent.pathPending &&
                              agent.pathStatus == NavMeshPathStatus.PathComplete &&
                              agent.remainingDistance <= agent.stoppingDistance + 0.15f;
    public bool HasCompletePath => IsReady && agent.hasPath &&
                                   agent.pathStatus == NavMeshPathStatus.PathComplete;
    public int PatrolPointCount => patrolRegions != null ? patrolRegions.Length : 0;
    public int RecentPatrolPointCount => recentPatrolIndices.Count;
    public int CurrentPatrolIndex { get; private set; } = -1;
    public int PatrolSelectionCount { get; private set; }
    public int StuckRecoveryCount { get; private set; }
    public Vector3 IntendedDestination => intendedDestination;
    public float PatrolSpeed => patrolSpeed;
    public float ChaseSpeed => chaseSpeed;
    public float StuckTimeout => stuckTimeout;

    private void Awake()
    {
        EnsureScratchPath();
        ResolveAgent();
        ApplyAgentSettings();
    }

    public void Configure(NavMeshAgent configuredAgent)
    {
        Configure(configuredAgent, patrolRegions);
    }

    public void Configure(
        NavMeshAgent configuredAgent,
        Transform[] configuredPatrolRegions)
    {
        agent = configuredAgent;
        patrolRegions = configuredPatrolRegions ?? new Transform[0];
        EnsureScratchPath();
        ResolveAgent();
        ApplyAgentSettings();
        recentPatrolIndices.Clear();
        lastPatrolVisitAt.Clear();
        CurrentPatrolIndex = -1;
        PatrolSelectionCount = 0;
        ResetProgressTracking();
    }

    public void SetStopped(bool stopped)
    {
        if (!IsReady) return;
        agent.isStopped = stopped;
        if (stopped)
        {
            agent.ResetPath();
            hasIntendedDestination = false;
            ResetProgressTracking();
        }
    }

    public bool MoveTo(Vector3 destination, bool chasing)
    {
        if (!IsReady)
        {
            return false;
        }

        if (!TryResolveCompletePath(destination, 2.5f, out Vector3 sampled))
        {
            return false;
        }

        bool wasTrackingMovement =
            hasIntendedDestination && !agent.isStopped;
        agent.speed = chasing ? chaseSpeed : patrolSpeed;
        agent.stoppingDistance = chasing ? 7.5f : 0.4f;
        agent.isStopped = false;
        intendedDestination = sampled;
        hasIntendedDestination = true;
        bool accepted = agent.SetPath(scratchPath);
        if (!wasTrackingMovement) ResetProgressTracking();
        return accepted;
    }

    public bool MoveToRandomPatrolPoint()
    {
        if (TryChooseWeightedPatrolRegion(out int index, out Vector3 destination) &&
            MoveTo(destination, false))
        {
            RememberPatrolSelection(index);
            return true;
        }

        return MoveToFallbackPatrolPoint();
    }

    public bool MoveToRandomPointNear(
        Vector3 center,
        float minimumRadius,
        float maximumRadius)
    {
        for (int i = 0; i < 14; i++)
        {
            Vector2 direction = Random.insideUnitCircle.normalized;
            float distance = Random.Range(minimumRadius, maximumRadius);
            Vector3 candidate = center +
                                new Vector3(direction.x, 0f, direction.y) * distance;
            if (TryResolveCompletePath(candidate, 2f, out Vector3 sampled) &&
                MoveTo(sampled, false))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryMoveAwayFrom(Vector3 threat, float desiredDistance)
    {
        if (!IsReady) return false;
        Vector3 away = transform.position - threat;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = -transform.forward;
        away.Normalize();

        Vector3 lateral = Vector3.Cross(Vector3.up, away);
        Vector3[] directions =
        {
            away,
            (away + lateral * 0.65f).normalized,
            (away - lateral * 0.65f).normalized
        };
        foreach (Vector3 direction in directions)
        {
            Vector3 candidate = transform.position + direction * desiredDistance;
            if (TryResolveCompletePath(candidate, 2.5f, out Vector3 sampled) &&
                MoveTo(sampled, true))
            {
                return true;
            }
        }

        return false;
    }

    public bool TrySampleReachable(
        Vector3 requested,
        float radius,
        out Vector3 sampled)
    {
        return TryResolveCompletePath(requested, radius, out sampled);
    }

    public bool TickStuckRecovery(out bool abandonedDestination)
    {
        abandonedDestination = false;
        if (!IsReady || !hasIntendedDestination || agent.pathPending ||
            agent.isStopped || HasArrived)
        {
            ResetProgressTracking();
            return false;
        }

        if (!agent.hasPath ||
            agent.pathStatus != NavMeshPathStatus.PathComplete)
        {
            AbandonDestination();
            abandonedDestination = true;
            StuckRecoveryCount++;
            return true;
        }

        float remaining = agent.remainingDistance;
        if (Vector3.Distance(transform.position, lastProgressPosition) >= 0.25f)
        {
            lastProgressPosition = transform.position;
            bestRemainingDistance = remaining;
            lastProgressAt = Time.time;
            return false;
        }

        if (Time.time - lastProgressAt < stuckTimeout)
        {
            return false;
        }

        StuckRecoveryCount++;
        Vector3 midpoint = Vector3.Lerp(transform.position, intendedDestination, 0.55f);
        Vector3 route = intendedDestination - transform.position;
        route.y = 0f;
        Vector3 lateral = route.sqrMagnitude > 0.01f
            ? Vector3.Cross(Vector3.up, route.normalized)
            : transform.right;
        float side = StuckRecoveryCount % 2 == 0 ? 1f : -1f;
        Vector3 detour = midpoint + lateral * side * 2f;
        if (TryResolveCompletePath(detour, 3f, out Vector3 sampled) &&
            Vector3.SqrMagnitude(sampled - transform.position) > 1f)
        {
            agent.isStopped = false;
            intendedDestination = sampled;
            hasIntendedDestination = true;
            agent.SetPath(scratchPath);
            ResetProgressTracking();
            return true;
        }

        AbandonDestination();
        abandonedDestination = true;
        return true;
    }

    public static bool TrySample(Vector3 requested, float radius, out Vector3 sampled)
    {
        if (NavMesh.SamplePosition(
                requested,
                out NavMeshHit hit,
                Mathf.Max(0.25f, radius),
                NavMesh.AllAreas))
        {
            sampled = hit.position;
            return true;
        }

        sampled = requested;
        return false;
    }

    private bool TryChooseWeightedPatrolRegion(
        out int selectedIndex,
        out Vector3 selectedPosition)
    {
        selectedIndex = -1;
        selectedPosition = default;
        if (!IsReady || patrolRegions == null || patrolRegions.Length == 0)
        {
            return false;
        }

        List<int> indices = new List<int>();
        List<Vector3> positions = new List<Vector3>();
        List<float> weights = new List<float>();
        float totalWeight = 0f;

        for (int i = 0; i < patrolRegions.Length; i++)
        {
            Transform region = patrolRegions[i];
            if (region == null || i == CurrentPatrolIndex ||
                recentPatrolIndices.Contains(i))
            {
                continue;
            }

            if (!TryResolveCompletePath(region.position, 5f, out Vector3 sampled))
            {
                continue;
            }

            float age = lastPatrolVisitAt.TryGetValue(i, out float visitedAt)
                ? Mathf.Max(0f, Time.time - visitedAt)
                : 120f;
            float unvisitedWeight = 1f + Mathf.Clamp(age / 25f, 0f, 4f);
            float propWeight = Mathf.Min(2.5f, CountCopyableProps(region.position) * 0.15f);
            float distanceWeight = Mathf.Clamp(
                Vector3.Distance(transform.position, sampled) / 18f,
                0.35f,
                1.5f);
            float weight = (unvisitedWeight + propWeight) * distanceWeight;
            indices.Add(i);
            positions.Add(sampled);
            weights.Add(weight);
            totalWeight += weight;
        }

        if (indices.Count == 0)
        {
            recentPatrolIndices.Clear();
            for (int i = 0; i < patrolRegions.Length; i++)
            {
                Transform region = patrolRegions[i];
                if (region == null || i == CurrentPatrolIndex ||
                    !TryResolveCompletePath(region.position, 5f, out Vector3 sampled))
                {
                    continue;
                }
                indices.Add(i);
                positions.Add(sampled);
                weights.Add(1f);
                totalWeight += 1f;
            }
        }

        if (indices.Count == 0) return false;
        float roll = Random.Range(0f, totalWeight);
        for (int i = 0; i < indices.Count; i++)
        {
            roll -= weights[i];
            if (roll > 0f) continue;
            selectedIndex = indices[i];
            selectedPosition = positions[i];
            return true;
        }

        selectedIndex = indices[indices.Count - 1];
        selectedPosition = positions[positions.Count - 1];
        return true;
    }

    private void RememberPatrolSelection(int index)
    {
        CurrentPatrolIndex = index;
        PatrolSelectionCount++;
        lastPatrolVisitAt[index] = Time.time;
        recentPatrolIndices.Add(index);
        while (recentPatrolIndices.Count > Mathf.Min(3, patrolRegions.Length - 1))
        {
            recentPatrolIndices.RemoveAt(0);
        }
    }

    private bool MoveToFallbackPatrolPoint()
    {
        for (int i = 0; i < 16; i++)
        {
            Vector2 circle = Random.insideUnitCircle * fallbackPatrolRadius;
            Vector3 candidate = transform.position +
                                new Vector3(circle.x, 0f, circle.y);
            if (Vector3.SqrMagnitude(candidate - transform.position) < 16f ||
                !TryResolveCompletePath(
                    candidate,
                    fallbackPatrolRadius,
                    out Vector3 sampled))
            {
                continue;
            }

            CurrentPatrolIndex = -1;
            PatrolSelectionCount++;
            return MoveTo(sampled, false);
        }

        return false;
    }

    private bool TryResolveCompletePath(
        Vector3 requested,
        float sampleRadius,
        out Vector3 sampled)
    {
        sampled = requested;
        EnsureScratchPath();
        if (!IsReady ||
            !TrySample(requested, sampleRadius, out sampled))
        {
            return false;
        }

        scratchPath.ClearCorners();
        return NavMesh.CalculatePath(
                   agent.nextPosition,
                   sampled,
                   agent.areaMask,
                   scratchPath) &&
               scratchPath.status == NavMeshPathStatus.PathComplete &&
               scratchPath.corners != null &&
               scratchPath.corners.Length >= 2;
    }

    private static int CountCopyableProps(Vector3 center)
    {
        int count = 0;
        HashSet<PropTarget> unique = new HashSet<PropTarget>();
        foreach (Collider collider in Physics.OverlapSphere(
                     center,
                     9f,
                     Physics.DefaultRaycastLayers,
                     QueryTriggerInteraction.Collide))
        {
            PropTarget prop = collider != null
                ? collider.GetComponentInParent<PropTarget>()
                : null;
            if (prop != null && unique.Add(prop) &&
                SeekerShotTargetClassifier.IsGenuinelyCopyable(prop))
            {
                count++;
            }
        }
        return count;
    }

    private void AbandonDestination()
    {
        if (IsReady) agent.ResetPath();
        hasIntendedDestination = false;
        ResetProgressTracking();
    }

    private void ResetProgressTracking()
    {
        bestRemainingDistance = float.PositiveInfinity;
        lastProgressPosition = transform.position;
        lastProgressAt = Time.time;
    }

    private void ResolveAgent()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
    }

    private void EnsureScratchPath()
    {
        if (scratchPath == null) scratchPath = new NavMeshPath();
    }

    private void ApplyAgentSettings()
    {
        if (agent == null) return;
        agent.speed = patrolSpeed;
        agent.angularSpeed = 360f;
        agent.acceleration = 12f;
        agent.stoppingDistance = 0.4f;
        agent.autoBraking = true;
        agent.autoRepath = true;
        agent.updateRotation = true;
    }
}
