using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CollisionSetupTool
{
    private const string PlayerLayerName = "Player";
    private const string EnvironmentLayerName = "Environment";
    private const float MinimumWorldThickness = 0.25f;
    private const float GroundBlockHeight = 0.15f;
    private const float GroundBlockTopInset = 0.01f;
    private const float MaxGroundBlockTileSize = 8f;
    private const string GeneratedGroundBlocksRootName = "Generated GroundBlocks";
    private const string AsphaltMaterialPath = "Assets/RPG_FPS_game_assets_industrial/Textures/Asphalt/Seamless_asphalt_v1/Seamless_asphalt_v1.mat";
    private const string RoadMaterialPath = "Assets/RPG_FPS_game_assets_industrial/Textures/Asphalt/Road_with_pavements_v1/Road_with_pavements_v1.mat";
    private const string ConcreteMaterialPath = "Assets/RPG_FPS_game_assets_industrial/Textures/Concrete_wall/UNIConcrete_walls/UNIConcrete_wall_v1/UNIConcrete_wall_v1.mat";

    [MenuItem("Tools/HideAndSeek/Fix Scene Colliders")]
    public static void FixSceneColliders()
    {
        if (PrefabStageUtility.GetCurrentPrefabStage() != null)
        {
            Debug.LogWarning("CollisionSetupTool: Open a scene before running this tool. Prefab Mode is skipped to avoid changing source prefabs.");
            return;
        }

        EnsureLayer(PlayerLayerName);
        EnsureLayer(EnvironmentLayerName);

        int playerLayer = LayerMask.NameToLayer(PlayerLayerName);
        int environmentLayer = LayerMask.NameToLayer(EnvironmentLayerName);

        if (playerLayer < 0 || environmentLayer < 0)
        {
            Debug.LogError("CollisionSetupTool: Player or Environment layer is missing.");
            return;
        }

        Physics.IgnoreLayerCollision(playerLayer, environmentLayer, false);
        Physics.IgnoreLayerCollision(playerLayer, 0, false);
        Physics.IgnoreLayerCollision(environmentLayer, 0, false);

        List<string> skippedObjects = new List<string>();
        List<string> groundBlockSources = new List<string>();
        int addedColliders = 0;
        int reinforcedThinColliders = 0;
        int generatedGroundBlocks = 0;
        int environmentLayerAssignments = 0;

        GameObject staticRoot = GameObject.Find("Static");
        if (staticRoot != null)
        {
            environmentLayerAssignments += SetLayerRecursively(staticRoot, environmentLayer, true);
        }

        GameObject player = FindPlayerCapsule();
        if (player != null)
        {
            environmentLayerAssignments += SetLayerRecursively(player, playerLayer, true);
            ConfigureCharacterController(player);
        }
        else
        {
            skippedObjects.Add("PlayerCapsule not found");
        }

        MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (MeshRenderer meshRenderer in renderers)
        {
            GameObject current = meshRenderer.gameObject;
            if (!IsSceneObject(current))
            {
                continue;
            }

            if (ShouldSkip(current, skippedObjects))
            {
                continue;
            }

            environmentLayerAssignments += SetLayerRecursively(current, environmentLayer, false);

            if (IsGroundOrRoadCandidate(meshRenderer))
            {
                int createdGroundBlocks = CreateGroundBlocks(meshRenderer, environmentLayer);
                if (createdGroundBlocks > 0)
                {
                    generatedGroundBlocks += createdGroundBlocks;
                    groundBlockSources.Add($"{GetPath(current)} ({createdGroundBlocks} block(s))");
                }
            }

            Collider[] colliders = current.GetComponents<Collider>();
            Collider[] solidColliders = colliders.Where(collider => collider != null && !collider.isTrigger).ToArray();

            if (solidColliders.Length == 0)
            {
                Collider added = AddBestCollider(current, meshRenderer);
                if (added != null)
                {
                    addedColliders++;
                    EnsureColliderThickness(current, meshRenderer, added);
                }
                continue;
            }

            if (NeedsThickerCollider(meshRenderer, solidColliders) && !solidColliders.Any(collider => collider is BoxCollider))
            {
                BoxCollider supportCollider = Undo.AddComponent<BoxCollider>(current);
                FitBoxColliderToRenderer(current, meshRenderer, supportCollider);
                reinforcedThinColliders++;
            }
            else
            {
                foreach (Collider collider in solidColliders)
                {
                    EnsureColliderThickness(current, meshRenderer, collider);
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log($"CollisionSetupTool: Added {addedColliders} collider(s), reinforced {reinforcedThinColliders} thin collider(s), generated {generatedGroundBlocks} GroundBlock(s), assigned {environmentLayerAssignments} layer object(s).");
        Debug.Log(groundBlockSources.Count > 0
            ? $"CollisionSetupTool: Ground/road one-sided candidates fixed:\n- {string.Join("\n- ", groundBlockSources)}"
            : "CollisionSetupTool: No one-sided ground/road candidates needed GroundBlocks.");
        Debug.Log(skippedObjects.Count > 0
            ? $"CollisionSetupTool: Skipped objects:\n- {string.Join("\n- ", skippedObjects.Distinct())}"
            : "CollisionSetupTool: No objects skipped.");
    }

    private static GameObject FindPlayerCapsule()
    {
        CharacterController[] controllers = Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (CharacterController controller in controllers)
        {
            if (controller.name == "PlayerCapsule" || controller.GetComponent<StarterAssets.FirstPersonController>() != null)
            {
                return controller.gameObject;
            }
        }

        GameObject namedPlayer = GameObject.Find("PlayerCapsule");
        return namedPlayer != null && namedPlayer.GetComponent<CharacterController>() != null ? namedPlayer : null;
    }

    private static void ConfigureCharacterController(GameObject player)
    {
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller == null)
        {
            return;
        }

        Undo.RecordObject(controller, "Configure CharacterController Collision");
        controller.radius = Mathf.Clamp(controller.radius, 0.4f, 0.5f);
        controller.height = Mathf.Clamp(controller.height, 1.8f, 2.0f);
        controller.center = new Vector3(controller.center.x, Mathf.Clamp(controller.center.y, 0.9f, 1.0f), controller.center.z);
        controller.skinWidth = Mathf.Clamp(controller.skinWidth, 0.03f, 0.08f);
        controller.stepOffset = Mathf.Clamp(controller.stepOffset, 0.2f, 0.4f);
        controller.slopeLimit = 45f;
        EditorUtility.SetDirty(controller);
    }

    private static Collider AddBestCollider(GameObject current, MeshRenderer meshRenderer)
    {
        MeshFilter meshFilter = current.GetComponent<MeshFilter>();
        Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
        Bounds localBounds = GetLocalBounds(meshRenderer, mesh);

        if (ShouldUseBoxCollider(mesh, localBounds))
        {
            BoxCollider boxCollider = Undo.AddComponent<BoxCollider>(current);
            FitBoxColliderToRenderer(current, meshRenderer, boxCollider);
            return boxCollider;
        }

        MeshCollider meshCollider = Undo.AddComponent<MeshCollider>(current);
        meshCollider.sharedMesh = mesh;
        meshCollider.convex = false;
        return meshCollider;
    }

    private static bool ShouldUseBoxCollider(Mesh mesh, Bounds localBounds)
    {
        Vector3 size = localBounds.size;
        float smallest = Mathf.Min(size.x, size.y, size.z);
        float largest = Mathf.Max(size.x, size.y, size.z);
        bool thinOrFlat = smallest <= 0.3f || largest / Mathf.Max(0.001f, smallest) >= 8f;
        bool simpleMesh = mesh == null || mesh.vertexCount <= 96;

        return thinOrFlat || simpleMesh;
    }

    private static void FitBoxColliderToRenderer(GameObject current, MeshRenderer meshRenderer, BoxCollider boxCollider)
    {
        MeshFilter meshFilter = current.GetComponent<MeshFilter>();
        Bounds localBounds = GetLocalBounds(meshRenderer, meshFilter != null ? meshFilter.sharedMesh : null);
        Vector3 size = localBounds.size;
        Vector3 scale = current.transform.lossyScale;

        size.x = Mathf.Max(size.x, MinimumLocalThickness(scale.x));
        size.y = Mathf.Max(size.y, MinimumLocalThickness(scale.y));
        size.z = Mathf.Max(size.z, MinimumLocalThickness(scale.z));

        boxCollider.center = localBounds.center;
        boxCollider.size = size;
        boxCollider.isTrigger = false;
        EditorUtility.SetDirty(boxCollider);
    }

    private static Bounds GetLocalBounds(MeshRenderer meshRenderer, Mesh mesh)
    {
        if (mesh != null)
        {
            return mesh.bounds;
        }

        Bounds worldBounds = meshRenderer.bounds;
        Transform transform = meshRenderer.transform;
        Vector3 localCenter = transform.InverseTransformPoint(worldBounds.center);
        Vector3 scale = transform.lossyScale;
        Vector3 localSize = new Vector3(
            worldBounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            worldBounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
            worldBounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));

        return new Bounds(localCenter, localSize);
    }

    private static bool NeedsThickerCollider(MeshRenderer meshRenderer, Collider[] solidColliders)
    {
        Bounds renderBounds = meshRenderer.bounds;
        float rendererSmallest = Mathf.Min(renderBounds.size.x, renderBounds.size.y, renderBounds.size.z);
        if (rendererSmallest > MinimumWorldThickness)
        {
            return false;
        }

        foreach (Collider collider in solidColliders)
        {
            Bounds colliderBounds = collider.bounds;
            float colliderSmallest = Mathf.Min(colliderBounds.size.x, colliderBounds.size.y, colliderBounds.size.z);
            if (colliderSmallest >= MinimumWorldThickness)
            {
                return false;
            }
        }

        return true;
    }

    private static void EnsureColliderThickness(GameObject current, MeshRenderer meshRenderer, Collider collider)
    {
        if (collider is BoxCollider boxCollider)
        {
            Undo.RecordObject(boxCollider, "Fix Thin Collider");
            FitBoxColliderToRenderer(current, meshRenderer, boxCollider);
        }
        else if (collider is MeshCollider meshCollider)
        {
            meshCollider.convex = false;
            EditorUtility.SetDirty(meshCollider);
        }
    }

    private static bool IsGroundOrRoadCandidate(MeshRenderer meshRenderer)
    {
        GameObject current = meshRenderer.gameObject;
        string lowerName = GetPath(current).ToLowerInvariant();
        bool nameLooksLikeGround = lowerName.Contains("road") ||
            lowerName.Contains("floor") ||
            lowerName.Contains("ground") ||
            lowerName.Contains("asphalt") ||
            lowerName.Contains("pavement");

        if (!nameLooksLikeGround)
        {
            return false;
        }

        Bounds bounds = meshRenderer.bounds;
        bool broadHorizontalSurface = bounds.size.x >= 1f && bounds.size.z >= 1f && bounds.size.y <= 0.35f;
        bool hasZeroOrThinCollider = current.GetComponents<Collider>()
            .Where(collider => collider != null && !collider.isTrigger)
            .Any(collider => Mathf.Min(collider.bounds.size.x, collider.bounds.size.y, collider.bounds.size.z) < 0.05f);

        MeshFilter meshFilter = current.GetComponent<MeshFilter>();
        bool meshLooksOneSided = meshFilter == null ||
            meshFilter.sharedMesh == null ||
            meshFilter.sharedMesh.bounds.size.y <= 0.05f;

        return broadHorizontalSurface && (meshLooksOneSided || hasZeroOrThinCollider);
    }

    private static int CreateGroundBlocks(MeshRenderer sourceRenderer, int environmentLayer)
    {
        Bounds bounds = sourceRenderer.bounds;
        if (bounds.size.x <= 0.01f || bounds.size.z <= 0.01f)
        {
            return 0;
        }

        Transform root = GetOrCreateGroundBlocksRoot().transform;
        int xTiles = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / MaxGroundBlockTileSize));
        int zTiles = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / MaxGroundBlockTileSize));
        float tileSizeX = bounds.size.x / xTiles;
        float tileSizeZ = bounds.size.z / zTiles;
        float topY = bounds.max.y - GroundBlockTopInset;
        Material material = ChooseGroundBlockMaterial(sourceRenderer);
        int created = 0;

        for (int x = 0; x < xTiles; x++)
        {
            for (int z = 0; z < zTiles; z++)
            {
                Vector3 center = new Vector3(
                    bounds.min.x + tileSizeX * (x + 0.5f),
                    topY - GroundBlockHeight * 0.5f,
                    bounds.min.z + tileSizeZ * (z + 0.5f));

                string blockName = $"GroundBlock_{sourceRenderer.gameObject.name}_{x}_{z}";
                if (FindExistingGroundBlock(root, blockName, center))
                {
                    continue;
                }

                GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Undo.RegisterCreatedObjectUndo(block, "Create GroundBlock");
                block.name = blockName;
                block.transform.SetParent(root, true);
                block.transform.position = center;
                block.transform.rotation = Quaternion.identity;
                block.transform.localScale = new Vector3(tileSizeX, GroundBlockHeight, tileSizeZ);
                block.layer = environmentLayer;

                Renderer blockRenderer = block.GetComponent<Renderer>();
                if (blockRenderer != null && material != null)
                {
                    blockRenderer.sharedMaterial = material;
                    EditorUtility.SetDirty(blockRenderer);
                }

                BoxCollider boxCollider = block.GetComponent<BoxCollider>();
                if (boxCollider != null)
                {
                    boxCollider.isTrigger = false;
                    EditorUtility.SetDirty(boxCollider);
                }

                GameObjectUtility.SetStaticEditorFlags(block, StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccluderStatic |
                    StaticEditorFlags.OccludeeStatic |
                    StaticEditorFlags.ContributeGI |
                    StaticEditorFlags.ReflectionProbeStatic);

                created++;
            }
        }

        return created;
    }

    private static GameObject GetOrCreateGroundBlocksRoot()
    {
        GameObject root = GameObject.Find(GeneratedGroundBlocksRootName);
        if (root != null)
        {
            return root;
        }

        root = new GameObject(GeneratedGroundBlocksRootName);
        Undo.RegisterCreatedObjectUndo(root, "Create GroundBlocks Root");

        GameObject staticRoot = GameObject.Find("Static");
        if (staticRoot != null)
        {
            root.transform.SetParent(staticRoot.transform, false);
        }

        int environmentLayer = LayerMask.NameToLayer(EnvironmentLayerName);
        if (environmentLayer >= 0)
        {
            root.layer = environmentLayer;
        }

        GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.ContributeGI |
            StaticEditorFlags.ReflectionProbeStatic);

        return root;
    }

    private static bool FindExistingGroundBlock(Transform root, string blockName, Vector3 center)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == blockName && Vector3.Distance(child.position, center) < 0.01f)
            {
                return true;
            }
        }

        return false;
    }

    private static Material ChooseGroundBlockMaterial(MeshRenderer sourceRenderer)
    {
        string lowerName = GetPath(sourceRenderer.gameObject).ToLowerInvariant();
        if (lowerName.Contains("road") || lowerName.Contains("asphalt") || lowerName.Contains("pavement"))
        {
            return LoadMaterial(RoadMaterialPath) ?? LoadMaterial(AsphaltMaterialPath) ?? sourceRenderer.sharedMaterial;
        }

        if (lowerName.Contains("concrete"))
        {
            return LoadMaterial(ConcreteMaterialPath) ?? sourceRenderer.sharedMaterial;
        }

        return sourceRenderer.sharedMaterial ?? LoadMaterial(AsphaltMaterialPath) ?? LoadMaterial(ConcreteMaterialPath);
    }

    private static Material LoadMaterial(string path)
    {
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    private static float MinimumLocalThickness(float worldScale)
    {
        return MinimumWorldThickness / Mathf.Max(0.0001f, Mathf.Abs(worldScale));
    }

    private static bool ShouldSkip(GameObject current, List<string> skippedObjects)
    {
        if (current.GetComponentInParent<CharacterController>() != null || current.name.Contains("Player"))
        {
            skippedObjects.Add(GetPath(current));
            return true;
        }

        if (current.GetComponentInParent<Camera>() != null || current.GetComponentInParent<Light>() != null)
        {
            skippedObjects.Add(GetPath(current));
            return true;
        }

        if (current.GetComponentInParent<Canvas>() != null || current.GetComponent<RectTransform>() != null || current.layer == LayerMask.NameToLayer("UI"))
        {
            skippedObjects.Add(GetPath(current));
            return true;
        }

        Collider[] colliders = current.GetComponents<Collider>();
        if (current.name.ToLowerInvariant().Contains("trigger") || colliders.Any(collider => collider != null && collider.isTrigger))
        {
            skippedObjects.Add(GetPath(current));
            return true;
        }

        return false;
    }

    private static bool IsSceneObject(GameObject current)
    {
        return current.scene.IsValid() && current.scene.isLoaded && !EditorUtility.IsPersistent(current);
    }

    private static int SetLayerRecursively(GameObject current, int layer, bool includeChildren)
    {
        int changed = 0;
        Transform[] transforms = includeChildren ? current.GetComponentsInChildren<Transform>(true) : new[] { current.transform };
        foreach (Transform transform in transforms)
        {
            if (transform.gameObject.layer == layer)
            {
                continue;
            }

            Undo.RecordObject(transform.gameObject, "Set Collision Layer");
            transform.gameObject.layer = layer;
            EditorUtility.SetDirty(transform.gameObject);
            changed++;
        }

        return changed;
    }

    private static void EnsureLayer(string layerName)
    {
        if (LayerMask.NameToLayer(layerName) >= 0)
        {
            return;
        }

        SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(layer.stringValue))
            {
                continue;
            }

            layer.stringValue = layerName;
            tagManager.ApplyModifiedProperties();
            return;
        }

        Debug.LogError($"CollisionSetupTool: No empty layer slot available for {layerName}.");
    }

    private static string GetPath(GameObject current)
    {
        Stack<string> names = new Stack<string>();
        Transform transform = current.transform;
        while (transform != null)
        {
            names.Push(transform.name);
            transform = transform.parent;
        }

        return string.Join("/", names);
    }
}
