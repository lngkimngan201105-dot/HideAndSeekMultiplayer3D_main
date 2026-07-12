using UnityEngine;
using UnityEngine.AI;

namespace Assets.Scripts
{
    public enum AIMode
    {
        ChaseTarget,
        FleeFromTarget
    }

    [RequireComponent(typeof(NavMeshAgent))]
    public class AIFollow : MonoBehaviour
    {
        [SerializeField] private AIMode mode = AIMode.ChaseTarget;
        [SerializeField] private Transform targetDestination;
        [SerializeField] private float speed = 3.5f;
        [SerializeField] private float repathRate = 0.25f;
        [SerializeField] private float fleeSearchRadius = 12f;
        [SerializeField] private int fleeSampleCount = 24;
        [SerializeField] private float reachedDistance = 1.2f;
        [SerializeField] private Transform[] escapePoints;

        private NavMeshAgent agent;
        private NavMeshPath reusablePath;
        private Vector3 currentFleeDestination;
        private float nextRepathTime;

        public AIMode Mode
        {
            get => mode;
            set => mode = value;
        }

        public Transform TargetDestination
        {
            get => targetDestination;
            set => targetDestination = value;
        }

        public float Speed
        {
            get => speed;
            set => speed = value;
        }

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            reusablePath = new NavMeshPath();
            ConfigureAgent();
        }

        private void Start()
        {
            ResolveMissingTarget();
            EnsureAgentOnNavMesh();
            ChooseInitialDestination();
        }

        private void Update()
        {
            ConfigureAgent();
            ResolveMissingTarget();
            EnsureAgentOnNavMesh();

            if (targetDestination == null || !agent.isOnNavMesh)
            {
                return;
            }

            if (Time.time < nextRepathTime)
            {
                return;
            }

            nextRepathTime = Time.time + Mathf.Max(0.05f, repathRate);

            if (mode == AIMode.ChaseTarget)
            {
                ChaseTarget();
            }
            else
            {
                FleeFromTarget();
            }
        }

        private void ConfigureAgent()
        {
            if (agent == null)
            {
                return;
            }

            agent.speed = speed;
            agent.radius = 0.5f;
            agent.height = 2f;
            agent.angularSpeed = 180f;
            agent.acceleration = 12f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            agent.autoRepath = true;
            agent.autoBraking = false;
        }

        private void ResolveMissingTarget()
        {
            if (targetDestination != null)
            {
                return;
            }

            if (mode == AIMode.ChaseTarget)
            {
                GameObject player = GameObject.Find("Player");
                if (player != null)
                {
                    targetDestination = player.transform;
                }
            }
            else
            {
                GameObject manager = GameObject.Find("SchoolManager");
                if (manager == null)
                {
                    manager = GameObject.Find("AI_Monster");
                }

                if (manager != null)
                {
                    targetDestination = manager.transform;
                }
            }
        }

        private void EnsureAgentOnNavMesh()
        {
            if (agent == null || agent.isOnNavMesh)
            {
                return;
            }

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }

        private void ChooseInitialDestination()
        {
            if (targetDestination == null || !agent.isOnNavMesh)
            {
                return;
            }

            if (mode == AIMode.ChaseTarget)
            {
                ChaseTarget();
            }
            else
            {
                currentFleeDestination = transform.position;
                FleeFromTarget(true);
            }
        }

        private void ChaseTarget()
        {
            if (TryGetCompletePath(targetDestination.position, out Vector3 sampledTarget))
            {
                agent.SetDestination(sampledTarget);
            }
        }

        private void FleeFromTarget(bool forceNewDestination = false)
        {
            bool needsNewDestination = forceNewDestination ||
                !agent.hasPath ||
                agent.remainingDistance <= reachedDistance ||
                Vector3.Distance(transform.position, currentFleeDestination) <= reachedDistance;

            if (!needsNewDestination)
            {
                return;
            }

            if (TryFindBestEscapeDestination(out Vector3 destination))
            {
                currentFleeDestination = destination;
                agent.SetDestination(destination);
            }
        }

        private bool TryFindBestEscapeDestination(out Vector3 destination)
        {
            destination = transform.position;
            float bestDistance = float.NegativeInfinity;
            bool found = false;

            if (escapePoints != null && escapePoints.Length > 0)
            {
                foreach (Transform escapePoint in escapePoints)
                {
                    if (escapePoint == null)
                    {
                        continue;
                    }

                    if (TryScoreEscapePoint(escapePoint.position, ref bestDistance, ref destination))
                    {
                        found = true;
                    }
                }
            }

            if (found)
            {
                return true;
            }

            int samples = Mathf.Max(4, fleeSampleCount);
            for (int i = 0; i < samples; i++)
            {
                Vector2 randomCircle = Random.insideUnitCircle * fleeSearchRadius;
                Vector3 candidate = transform.position + new Vector3(randomCircle.x, 0f, randomCircle.y);
                if (TryScoreEscapePoint(candidate, ref bestDistance, ref destination))
                {
                    found = true;
                }
            }

            return found;
        }

        private bool TryScoreEscapePoint(Vector3 candidate, ref float bestDistance, ref Vector3 bestDestination)
        {
            if (!TryGetCompletePath(candidate, out Vector3 sampledCandidate))
            {
                return false;
            }

            float distanceFromTarget = Vector3.Distance(sampledCandidate, targetDestination.position);
            if (distanceFromTarget <= bestDistance)
            {
                return false;
            }

            bestDistance = distanceFromTarget;
            bestDestination = sampledCandidate;
            return true;
        }

        private bool TryGetCompletePath(Vector3 worldPosition, out Vector3 sampledPosition)
        {
            sampledPosition = worldPosition;

            if (!agent.isOnNavMesh)
            {
                return false;
            }

            if (!NavMesh.SamplePosition(worldPosition, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                return false;
            }

            sampledPosition = hit.position;
            if (!NavMesh.CalculatePath(transform.position, sampledPosition, NavMesh.AllAreas, reusablePath))
            {
                return false;
            }

            return reusablePath.status == NavMeshPathStatus.PathComplete;
        }
    }
}
