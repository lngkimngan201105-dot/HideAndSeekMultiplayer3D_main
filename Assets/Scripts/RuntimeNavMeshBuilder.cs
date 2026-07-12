using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Assets.Scripts
{
    public class RuntimeNavMeshBuilder : MonoBehaviour
    {
        [SerializeField] private NavMeshSurface navMeshSurface;

        private void Start()
        {
            EnsureSurface();
            Build();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.B))
            {
                Build();
            }
        }

        private void EnsureSurface()
        {
            if (navMeshSurface == null)
            {
                navMeshSurface = FindFirstObjectByType<NavMeshSurface>();
            }

            if (navMeshSurface == null)
            {
                GameObject floor = GameObject.Find("Floor");
                if (floor != null)
                {
                    navMeshSurface = floor.GetComponent<NavMeshSurface>();
                    if (navMeshSurface == null)
                    {
                        navMeshSurface = floor.AddComponent<NavMeshSurface>();
                    }
                }
            }

            if (navMeshSurface == null)
            {
                Debug.LogWarning("RuntimeNavMeshBuilder: No NavMeshSurface found and no Floor object exists.");
                return;
            }

            navMeshSurface.collectObjects = CollectObjects.All;
            navMeshSurface.layerMask = ~0;
            navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            navMeshSurface.ignoreNavMeshAgent = true;
            navMeshSurface.ignoreNavMeshObstacle = false;
        }

        private void Build()
        {
            EnsureSurface();
            if (navMeshSurface == null)
            {
                return;
            }

            navMeshSurface.BuildNavMesh();
            Debug.Log($"RuntimeNavMeshBuilder: NavMesh built on {navMeshSurface.gameObject.name}.");
        }
    }
}
