using Assets.Scripts;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class Lab04SceneSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Tools/Lab 4/Setup NavMesh AI Scene")]
    public static void SetupSampleScene()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject floor = EnsureFloor();
        GameObject player = EnsureCharacter("Player", new Vector3(-6f, 1f, -6f), new Color(0f, 0.45f, 1f), 4f);
        GameObject monster = EnsureCharacter("SchoolManager", new Vector3(6f, 1f, 6f), new Color(1f, 0.45f, 0f), 3.5f);

        EnsureComponent<SchoolManager>(monster);
        ConfigureAi(player, AIMode.FleeFromTarget, monster.transform, 4f);
        ConfigureAi(monster, AIMode.ChaseTarget, player.transform, 3.5f);

        EnsureNavMeshManager();
        EnsureDemoWalls();
        ConfigureFloor(floor);
        ConfigureWalls();
        ConfigureCamera(player.transform, monster.transform);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Lab04SceneSetup: SampleScene configured for Lab 4 NavMesh chase/flee AI.");
    }

    [MenuItem("Tools/Lab 4/Validate NavMesh AI Scene")]
    public static void ValidateSampleScene()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject player = RequireObject("Player");
        GameObject monster = RequireObject("SchoolManager");
        GameObject floor = RequireObject("Floor");
        RequireObject("NavMeshManager");

        RequireComponent<RuntimeNavMeshBuilder>("NavMeshManager");
        RequireComponent<NavMeshSurface>("Floor").BuildNavMesh();

        AIFollow playerAi = RequireComponent<AIFollow>("Player");
        AIFollow monsterAi = RequireComponent<AIFollow>("SchoolManager");
        NavMeshAgent playerAgent = RequireComponent<NavMeshAgent>("Player");
        NavMeshAgent monsterAgent = RequireComponent<NavMeshAgent>("SchoolManager");

        if (playerAi.Mode != AIMode.FleeFromTarget || playerAi.TargetDestination != monster.transform)
        {
            throw new BuildFailedException("Player AIFollow is not configured to flee from SchoolManager.");
        }

        if (monsterAi.Mode != AIMode.ChaseTarget || monsterAi.TargetDestination != player.transform)
        {
            throw new BuildFailedException("SchoolManager AIFollow is not configured to chase Player.");
        }

        ValidateAgent(playerAgent, 4f);
        ValidateAgent(monsterAgent, 3.5f);

        if (!NavMesh.SamplePosition(player.transform.position, out NavMeshHit playerHit, 4f, NavMesh.AllAreas) ||
            !NavMesh.SamplePosition(monster.transform.position, out NavMeshHit monsterHit, 4f, NavMesh.AllAreas))
        {
            throw new BuildFailedException("Player or SchoolManager is not near the built NavMesh.");
        }

        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.CalculatePath(monsterHit.position, playerHit.position, NavMesh.AllAreas, path) ||
            path.status != NavMeshPathStatus.PathComplete)
        {
            throw new BuildFailedException("SchoolManager cannot calculate a complete NavMesh path to Player.");
        }

        BoxCollider floorCollider = RequireComponent<BoxCollider>("Floor");
        if (floorCollider.isTrigger)
        {
            throw new BuildFailedException("Floor collider must not be a trigger.");
        }

        ValidateWalls();
        Debug.Log("Lab04SceneSetup: Validation passed. Lab 4 scene has moving chase/flee AI on a complete NavMesh.");
    }

    private static GameObject EnsureFloor()
    {
        GameObject floor = GameObject.Find("Floor");
        if (floor == null)
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(floor, "Create Floor");
            floor.name = "Floor";
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(24f, 0.2f, 24f);
        }

        return floor;
    }

    private static void ConfigureFloor(GameObject floor)
    {
        BoxCollider collider = EnsureComponent<BoxCollider>(floor);
        collider.isTrigger = false;

        NavMeshSurface surface = EnsureComponent<NavMeshSurface>(floor);
        surface.collectObjects = CollectObjects.All;
        surface.layerMask = ~0;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = false;

        EditorUtility.SetDirty(collider);
        EditorUtility.SetDirty(surface);
    }

    private static GameObject EnsureCharacter(string name, Vector3 position, Color color, float speed)
    {
        GameObject character = GameObject.Find(name);
        if (character == null)
        {
            character = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Undo.RegisterCreatedObjectUndo(character, $"Create {name}");
            character.name = name;
            character.transform.position = position;
        }

        character.transform.localScale = Vector3.one;
        character.transform.position = position;

        Renderer renderer = character.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material material = renderer.sharedMaterial;
            if (material == null || AssetDatabase.GetAssetPath(material).StartsWith("Resources/unity_builtin_extra"))
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                material = new Material(shader);
            }

            material.color = color;
            renderer.sharedMaterial = material;
            EditorUtility.SetDirty(renderer);
        }

        NavMeshAgent agent = EnsureComponent<NavMeshAgent>(character);
        ConfigureAgent(agent, speed);
        return character;
    }

    private static void ConfigureAi(GameObject owner, AIMode mode, Transform target, float speed)
    {
        AIFollow ai = EnsureComponent<AIFollow>(owner);
        ai.Mode = mode;
        ai.TargetDestination = target;
        ai.Speed = speed;
        EditorUtility.SetDirty(ai);
    }

    private static void ConfigureAgent(NavMeshAgent agent, float speed)
    {
        agent.speed = speed;
        agent.radius = 0.5f;
        agent.height = 2f;
        agent.angularSpeed = 180f;
        agent.acceleration = 12f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        agent.autoRepath = true;
        agent.autoBraking = false;
        EditorUtility.SetDirty(agent);
    }

    private static void EnsureNavMeshManager()
    {
        GameObject manager = GameObject.Find("NavMeshManager");
        if (manager == null)
        {
            manager = new GameObject("NavMeshManager");
            Undo.RegisterCreatedObjectUndo(manager, "Create NavMeshManager");
        }

        EnsureComponent<RuntimeNavMeshBuilder>(manager);
    }

    private static void EnsureDemoWalls()
    {
        CreateWallIfMissing("Wall_North", new Vector3(0f, 1f, 11.5f), new Vector3(24f, 2f, 1f));
        CreateWallIfMissing("Wall_South", new Vector3(0f, 1f, -11.5f), new Vector3(24f, 2f, 1f));
        CreateWallIfMissing("Wall_East", new Vector3(11.5f, 1f, 0f), new Vector3(1f, 2f, 24f));
        CreateWallIfMissing("Wall_West", new Vector3(-11.5f, 1f, 0f), new Vector3(1f, 2f, 24f));
        CreateWallIfMissing("Wall_Middle_A", new Vector3(-3f, 1f, 0f), new Vector3(1f, 2f, 10f));
        CreateWallIfMissing("Wall_Middle_B", new Vector3(4f, 1f, 2f), new Vector3(1f, 2f, 10f));
        CreateWallIfMissing("Wall_Block", new Vector3(0f, 1f, -4f), new Vector3(6f, 2f, 1f));
    }

    private static void CreateWallIfMissing(string name, Vector3 position, Vector3 scale)
    {
        if (GameObject.Find(name) != null)
        {
            return;
        }

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(wall, $"Create {name}");
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = scale;
    }

    private static void ConfigureWalls()
    {
        GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (GameObject obj in allObjects)
        {
            if (!obj.name.StartsWith("Wall"))
            {
                continue;
            }

            BoxCollider collider = EnsureComponent<BoxCollider>(obj);
            collider.isTrigger = false;
            collider.center = Vector3.zero;
            collider.size = Vector3.one;
            EditorUtility.SetDirty(collider);

            NavMeshObstacle obstacle = EnsureComponent<NavMeshObstacle>(obj);
            obstacle.carving = true;
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = Vector3.zero;
            obstacle.size = Vector3.one;
            EditorUtility.SetDirty(obstacle);

            NavMeshModifier modifier = obj.GetComponent<NavMeshModifier>();
            if (modifier != null)
            {
                modifier.overrideArea = true;
                modifier.area = NavMesh.GetAreaFromName("Not Walkable");
                EditorUtility.SetDirty(modifier);
            }
        }
    }

    private static void ValidateWalls()
    {
        bool foundWall = false;
        GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (GameObject obj in allObjects)
        {
            if (!obj.name.StartsWith("Wall"))
            {
                continue;
            }

            foundWall = true;
            BoxCollider collider = RequireComponent<BoxCollider>(obj.name);
            NavMeshObstacle obstacle = RequireComponent<NavMeshObstacle>(obj.name);

            if (collider.isTrigger)
            {
                throw new BuildFailedException($"{obj.name} BoxCollider must not be a trigger.");
            }

            if (!obstacle.carving)
            {
                throw new BuildFailedException($"{obj.name} NavMeshObstacle must have carving enabled.");
            }
        }

        if (!foundWall)
        {
            throw new BuildFailedException("No Wall objects found.");
        }
    }

    private static void ValidateAgent(NavMeshAgent agent, float expectedSpeed)
    {
        if (!Mathf.Approximately(agent.speed, expectedSpeed) ||
            !Mathf.Approximately(agent.radius, 0.5f) ||
            !Mathf.Approximately(agent.height, 2f) ||
            !Mathf.Approximately(agent.angularSpeed, 180f) ||
            !Mathf.Approximately(agent.acceleration, 12f) ||
            agent.obstacleAvoidanceType != ObstacleAvoidanceType.HighQualityObstacleAvoidance ||
            !agent.autoRepath ||
            agent.autoBraking)
        {
            throw new BuildFailedException($"{agent.name} NavMeshAgent settings do not match Lab 4 requirements.");
        }
    }

    private static GameObject RequireObject(string name)
    {
        GameObject obj = GameObject.Find(name);
        if (obj == null)
        {
            throw new BuildFailedException($"{name} object is missing.");
        }

        return obj;
    }

    private static T RequireComponent<T>(string objectName) where T : Component
    {
        GameObject obj = RequireObject(objectName);
        return RequireComponent<T>(obj);
    }

    private static T RequireComponent<T>(GameObject obj) where T : Component
    {
        T component = obj.GetComponent<T>();
        if (component == null)
        {
            throw new BuildFailedException($"{obj.name} is missing {typeof(T).Name}.");
        }

        return component;
    }

    private static void ConfigureCamera(Transform player, Transform monster)
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            Undo.RegisterCreatedObjectUndo(cameraObject, "Create Main Camera");
            camera = EnsureComponent<Camera>(cameraObject);
            cameraObject.tag = "MainCamera";
            EnsureComponent<AudioListener>(cameraObject);
        }

        Vector3 midpoint = (player.position + monster.position) * 0.5f;
        camera.transform.position = midpoint + new Vector3(0f, 18f, -14f);
        camera.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
        camera.orthographic = false;
        EditorUtility.SetDirty(camera);
    }

    private static T EnsureComponent<T>(GameObject owner) where T : Component
    {
        T component = owner.GetComponent<T>();
        if (component == null)
        {
            component = Undo.AddComponent<T>(owner);
        }

        return component;
    }
}
