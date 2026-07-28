using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class SeekerAIController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 4.2f;
    [SerializeField, Min(0.05f)] private float repathInterval = 0.2f;
    [SerializeField, Min(0.1f)] private float catchDistance = 1.25f;
    [SerializeField, Min(0.5f)] private float searchRadius = 35f;

    [Header("Round")]
    [SerializeField] private PropHuntRoundManager roundManager;
    [SerializeField] private bool onlyMoveDuringHunt = true;

    private readonly List<PropTransformSystem> _hiders = new List<PropTransformSystem>();
    private NavMeshAgent _agent;
    private float _nextRepathTime;
    private Vector3 _lastPatrolPoint;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        ConfigureAgent();
    }

    private void Start()
    {
        if (roundManager == null)
        {
            roundManager = FindObjectOfType<PropHuntRoundManager>();
        }

        EnsureOnNavMesh();
        RefreshHiders();
    }

    private void Update()
    {
        ConfigureAgent();
        EnsureOnNavMesh();

        if (!_agent.isOnNavMesh)
        {
            return;
        }

        bool canHunt = !onlyMoveDuringHunt ||
                       roundManager == null ||
                       roundManager.CurrentState == PropHuntRoundState.Hunting;
        _agent.isStopped = !canHunt;
        if (!canHunt)
        {
            return;
        }

        if (Time.time < _nextRepathTime)
        {
            TryCatchCurrentTarget();
            return;
        }

        _nextRepathTime = Time.time + repathInterval;
        RefreshHiders();

        PropTransformSystem target = FindBestTarget();
        if (target != null)
        {
            SetDestination(target.transform.position);
            TryCatch(target);
            return;
        }

        Patrol();
    }

    private void ConfigureAgent()
    {
        if (_agent == null)
        {
            return;
        }

        _agent.speed = moveSpeed;
        _agent.angularSpeed = 360f;
        _agent.acceleration = 18f;
        _agent.stoppingDistance = Mathf.Max(0.2f, catchDistance * 0.45f);
        _agent.radius = 0.45f;
        _agent.height = 2f;
        _agent.autoRepath = true;
        _agent.autoBraking = false;
        _agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
    }

    private void EnsureOnNavMesh()
    {
        if (_agent == null || _agent.isOnNavMesh)
        {
            return;
        }

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 12f, NavMesh.AllAreas))
        {
            _agent.Warp(hit.position);
        }
    }

    private void RefreshHiders()
    {
        _hiders.Clear();
        foreach (PropTransformSystem player in FindObjectsOfType<PropTransformSystem>(true))
        {
            if (player != null && player.playerRole == PlayerRole.Hider && !player.IsEliminated)
            {
                _hiders.Add(player);
            }
        }
    }

    private PropTransformSystem FindBestTarget()
    {
        PropTransformSystem best = null;
        float bestScore = float.PositiveInfinity;

        foreach (PropTransformSystem hider in _hiders)
        {
            if (hider == null || hider.IsEliminated)
            {
                continue;
            }

            float distance = Vector3.Distance(transform.position, hider.transform.position);
            float visibilityBonus = hider.currentState == PlayerDisguiseState.Human ? -8f : 0f;
            float score = distance + visibilityBonus;
            if (score < bestScore)
            {
                bestScore = score;
                best = hider;
            }
        }

        return best;
    }

    private void SetDestination(Vector3 targetPosition)
    {
        if (NavMesh.SamplePosition(targetPosition, out NavMeshHit hit, 4f, NavMesh.AllAreas))
        {
            _agent.SetDestination(hit.position);
        }
    }

    private void TryCatchCurrentTarget()
    {
        foreach (PropTransformSystem hider in _hiders)
        {
            TryCatch(hider);
        }
    }

    private void TryCatch(PropTransformSystem hider)
    {
        if (hider == null || hider.IsEliminated)
        {
            return;
        }

        if (Vector3.Distance(transform.position, hider.transform.position) <= catchDistance)
        {
            hider.SetEliminated(true);
        }
    }

    private void Patrol()
    {
        bool needsPoint = !_agent.hasPath ||
                          _agent.remainingDistance <= Mathf.Max(1f, _agent.stoppingDistance + 0.5f) ||
                          Vector3.Distance(transform.position, _lastPatrolPoint) <= 1f;
        if (!needsPoint)
        {
            return;
        }

        for (int i = 0; i < 16; i++)
        {
            Vector2 random = Random.insideUnitCircle * searchRadius;
            Vector3 candidate = transform.position + new Vector3(random.x, 0f, random.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            {
                _lastPatrolPoint = hit.position;
                _agent.SetDestination(hit.position);
                return;
            }
        }
    }
}
