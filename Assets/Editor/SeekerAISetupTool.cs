using System;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class SeekerAISetupTool
{
    public const string ScenePath = "Assets/Scenes/Map_v2.unity";
    public const string NavMeshAssetPath = "Assets/Navigation/Map_v2_NavMesh.asset";

    [MenuItem("Tools/Prop Hunt/Setup Single Player AI Seeker")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before running Seeker AI setup.");

        HiderCompleteHUDSetupTool.SetupHiderCompleteHud();
        SeekerPresentationSetupTool.EnsurePresentationConfigured();
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject seeker = RequireUniqueNamed(scene, "SeekerPlayer");
        PropTransformSystem hider = Object.FindObjectsOfType<PropTransformSystem>(true)
            .FirstOrDefault(item => item.playerRole == PlayerRole.Hider);
        if (hider == null) throw new InvalidOperationException("Hider PropTransformSystem was not found.");

        PropHuntRoundManager round = Object.FindObjectOfType<PropHuntRoundManager>(true);
        PropHuntTestRoleSelector selector = Object.FindObjectOfType<PropHuntTestRoleSelector>(true);
        HiderHealth hiderHealth = hider.GetComponent<HiderHealth>();
        HiderAntiCampSystem antiCamp = hider.GetComponent<HiderAntiCampSystem>();
        HiderRevealController reveal = hider.GetComponent<HiderRevealController>();
        HiderPerceptionSignature signature = EnsureUnique<HiderPerceptionSignature>(hider.gameObject);
        signature.Configure(hider, reveal);

        CharacterController characterController = seeker.GetComponent<CharacterController>();
        SeekerFirstPersonController humanController = seeker.GetComponent<SeekerFirstPersonController>();
        SeekerHealth seekerHealth = seeker.GetComponent<SeekerHealth>();
        SeekerWeaponEnergy energy = seeker.GetComponent<SeekerWeaponEnergy>();
        SeekerWeaponPresentation presentation = seeker.GetComponent<SeekerWeaponPresentation>();
        SeekerRaycastWeapon weapon = seeker.GetComponentInChildren<SeekerRaycastWeapon>(true);
        Camera seekerCamera = seeker.GetComponentInChildren<Camera>(true);
        if (round == null || selector == null || hiderHealth == null || antiCamp == null ||
            seekerHealth == null || energy == null || presentation == null || weapon == null)
            throw new InvalidOperationException("Existing Prop Hunt gameplay references are incomplete.");

        GameObject worldVisual = RequireDescendant(seeker.transform, "SeekerWorldVisualRoot").gameObject;
        GameObject worldGun = RequireDescendant(seeker.transform, "SciFiGunLight_World").gameObject;
        GameObject fpsGun = RequireDescendant(seeker.transform, "SciFiGunLight_FPS").gameObject;
        Transform worldMuzzle = RequireDescendant(seeker.transform, "MuzzlePoint_World");
        Transform fpsMuzzle = RequireDescendant(seeker.transform, "MuzzlePoint");
        GameObject seekerHud = RequireUniqueNamed(scene, "SeekerHUDRoot");
        Animator animator = RequireDescendant(seeker.transform, "CyberSoldierModel")
            .GetComponentInChildren<Animator>(true);

        NavMeshAgent agent = EnsureUnique<NavMeshAgent>(seeker);
        agent.agentTypeID = 0;
        agent.radius = 0.35f;
        agent.height = 1.8f;
        agent.baseOffset = 0f;
        agent.speed = 2.3f;
        agent.angularSpeed = 360f;
        agent.acceleration = 12f;
        agent.stoppingDistance = 0.4f;
        agent.autoBraking = true;
        agent.autoRepath = true;

        GameObject eyeObject = EnsureDirectChild(seeker.transform, "SeekerAIEye");
        eyeObject.transform.localPosition = new Vector3(0f, 1.6f, 0.12f);
        eyeObject.transform.localRotation = Quaternion.identity;
        eyeObject.transform.localScale = Vector3.one;

        SeekerAINavigation navigation = EnsureUnique<SeekerAINavigation>(seeker);
        SeekerAIPerception perception = EnsureUnique<SeekerAIPerception>(seeker);
        SeekerAICombat combat = EnsureUnique<SeekerAICombat>(seeker);
        SeekerAISuspicionSystem suspicion = EnsureUnique<SeekerAISuspicionSystem>(seeker);
        SeekerAIAnimatorDriver animatorDriver = EnsureUnique<SeekerAIAnimatorDriver>(seeker);
        SeekerAIController controller = EnsureUnique<SeekerAIController>(seeker);
        PropHuntSinglePlayerBootstrap bootstrap = EnsureUnique<PropHuntSinglePlayerBootstrap>(seeker);
        Transform[] patrolRegions = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(item => item.name.StartsWith("ZoneAnchor_", StringComparison.Ordinal))
            .OrderBy(item => item.name, StringComparer.Ordinal)
            .ToArray();
        if (patrolRegions.Length < 5)
            throw new InvalidOperationException(
                $"Expected at least five project patrol regions, found {patrolRegions.Length}.");

        navigation.Configure(agent, patrolRegions);
        perception.Configure(eyeObject.transform, signature);
        combat.Configure(weapon, energy, worldMuzzle, perception);
        animatorDriver.Configure(animator, agent);
        controller.Configure(
            round, hiderHealth, antiCamp, seekerHealth, weapon, energy,
            navigation, perception, combat, suspicion);

        ParticleSystem worldMuzzleFlash = CopyMuzzleParticle(fpsMuzzle, worldMuzzle);
        Light worldMuzzleLight = CopyMuzzleLight(fpsMuzzle, worldMuzzle);
        presentation.ConfigureWorldMuzzle(worldMuzzleFlash, worldMuzzleLight);
        AudioSource seekerAudio = presentation.AudioSource;
        if (seekerAudio != null)
        {
            seekerAudio.spatialBlend = 1f;
            seekerAudio.minDistance = 3f;
            seekerAudio.maxDistance = 45f;
            seekerAudio.rolloffMode = AudioRolloffMode.Logarithmic;
        }

        selector.ConfigureSinglePlayerHiderMode(true);
        bootstrap.Configure(
            selector, hider, humanController, characterController, weapon, energy,
            seekerCamera, fpsGun.transform.parent.gameObject, worldVisual, seekerHud);

        if (humanController != null) humanController.enabled = false;
        if (characterController != null) characterController.enabled = false;
        weapon.SetPlayerInputEnabled(false);
        energy.SetPlayerReloadInputEnabled(false);
        fpsGun.transform.parent.gameObject.SetActive(false);
        worldVisual.SetActive(true);
        worldGun.SetActive(true);
        seekerHud.SetActive(false);
        if (selector.RoleSelectionPanel != null) selector.RoleSelectionPanel.SetActive(false);
        if (seekerCamera != null)
        {
            seekerCamera.gameObject.tag = "Untagged";
            AudioListener seekerListener = seekerCamera.GetComponent<AudioListener>();
            if (seekerListener != null) seekerListener.enabled = false;
            seekerCamera.gameObject.SetActive(false);
        }

        ConfigureHiderTechnicalCapsule(hider);
        ConfigureSingleHiderCamera(hider, seekerCamera);
        NavMeshSurface surface = BuildProjectOwnedNavMesh(scene, hider.gameObject, seeker);
        PlaceSeekerAtSpawn(selector, seeker, agent);
        RemoveRuntimeNavMeshBuilders();

        foreach (Component component in seeker.GetComponents<Component>())
            if (component != null) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(signature);
        EditorUtility.SetDirty(selector);
        EditorUtility.SetDirty(surface);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log(
            "[SeekerAISetup] PASS — existing SeekerPlayer converted to AI; " +
            $"NavMesh={NavMeshAssetPath}; Agent(speed=2.3/4.2, angular=360, acceleration=12); " +
            $"PatrolRegions={patrolRegions.Length}; world muzzle + 3D audio bound; " +
            "human Seeker input/camera/HUD disabled.");
    }

    private static NavMeshSurface BuildProjectOwnedNavMesh(
        Scene scene,
        GameObject hider,
        GameObject seeker)
    {
        GameObject root = FindNamed(scene, "SeekerAINavMeshSurface") ??
                          new GameObject("SeekerAINavMeshSurface");
        NavMeshSurface surface = EnsureUnique<NavMeshSurface>(root);
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = BuildNavMeshLayerMask();
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = false;

        ConfigureIgnoredModifier(hider);
        ConfigureIgnoredModifier(seeker);
        surface.BuildNavMesh();
        NavMeshData built = surface.navMeshData;
        if (built == null) throw new InvalidOperationException("NavMesh build returned no data.");

        Directory.CreateDirectory("Assets/Navigation");
        NavMeshData existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshAssetPath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(built, NavMeshAssetPath);
        }
        else if (existing != built)
        {
            surface.RemoveData();
            EditorUtility.CopySerialized(built, existing);
            Object.DestroyImmediate(built);
            surface.navMeshData = existing;
            surface.AddData();
            EditorUtility.SetDirty(existing);
        }

        return surface;
    }

    private static int BuildNavMeshLayerMask()
    {
        int mask = Physics.DefaultRaycastLayers;
        string[] excluded = { "UI", "SeekerWorldVisual", "SeekerFPSVisual", "SeekerReveal" };
        foreach (string layerName in excluded)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0) mask &= ~(1 << layer);
        }
        return mask;
    }

    private static void ConfigureIgnoredModifier(GameObject root)
    {
        NavMeshModifier modifier = EnsureUnique<NavMeshModifier>(root);
        modifier.ignoreFromBuild = true;
        modifier.applyToChildren = true;
        EditorUtility.SetDirty(modifier);
    }

    private static void PlaceSeekerAtSpawn(
        PropHuntTestRoleSelector selector,
        GameObject seeker,
        NavMeshAgent agent)
    {
        Transform spawn = selector.SeekerSpawnPoint;
        Vector3 requested = spawn != null ? spawn.position : seeker.transform.position;
        if (!NavMesh.SamplePosition(requested, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            throw new InvalidOperationException("SeekerSpawnPoint is not near the baked NavMesh.");

        seeker.transform.SetPositionAndRotation(
            hit.position,
            spawn != null ? spawn.rotation : seeker.transform.rotation);
        if (agent.enabled && agent.isOnNavMesh) agent.Warp(hit.position);
    }

    private static void ConfigureSingleHiderCamera(PropTransformSystem hider, Camera seekerCamera)
    {
        if (hider.cameraModeManager != null)
        {
            hider.cameraModeManager.InitializeHiderTps(hider.transform);
            hider.cameraModeManager.ConfigureSinglePlayerHiderCamera(true);
            hider.cameraModeManager.SetCameraSystemEnabled(true);
            hider.cameraModeManager.ApplyResolvedHiderCameraMode();
            EditorUtility.SetDirty(hider.cameraModeManager);
        }

        Camera desired = hider.cameraModeManager != null
            ? hider.cameraModeManager.fpsCamera
            : hider.mainCamera;
        foreach (Camera camera in Object.FindObjectsOfType<Camera>(true))
        {
            bool active = camera == desired;
            if (camera == seekerCamera) active = false;
            camera.targetDisplay = 0;
            camera.targetTexture = null;
            camera.enabled = active;
            camera.gameObject.SetActive(active);
            AudioListener listener = camera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = active;
            if (active) camera.gameObject.tag = "MainCamera";
            else if (camera.CompareTag("MainCamera")) camera.gameObject.tag = "Untagged";
            EditorUtility.SetDirty(camera);
            if (listener != null) EditorUtility.SetDirty(listener);
        }

        if (hider.cameraModeManager != null &&
            !hider.cameraModeManager.EnsureGameplayCameraRendering("SeekerAISetup"))
            throw new InvalidOperationException(
                "Seeker AI setup could not establish the Hider TPS camera.\n" +
                hider.cameraModeManager.BuildCameraDiagnostic());
    }

    private static void ConfigureHiderTechnicalCapsule(PropTransformSystem hider)
    {
        MeshRenderer capsuleRenderer = hider.humanVisualRoot != null
            ? hider.humanVisualRoot.GetComponentsInChildren<MeshRenderer>(true)
                .FirstOrDefault(item => item.name == "Capsule")
            : null;
        CapsuleCollider capsuleCollider = capsuleRenderer != null
            ? capsuleRenderer.GetComponent<CapsuleCollider>()
            : null;
        if (capsuleRenderer == null || capsuleCollider == null)
        {
            throw new InvalidOperationException(
                "Hider technical Capsule renderer/collider setup is incomplete.");
        }

        capsuleRenderer.enabled = false;
        capsuleCollider.enabled = true;
        EditorUtility.SetDirty(capsuleRenderer);
        EditorUtility.SetDirty(capsuleCollider);
    }

    private static ParticleSystem CopyMuzzleParticle(Transform source, Transform target)
    {
        ParticleSystem sourceParticle = source.GetComponentInChildren<ParticleSystem>(true);
        ParticleSystem targetParticle = target.GetComponentInChildren<ParticleSystem>(true);
        if (sourceParticle == null) return targetParticle;
        if (targetParticle == null)
        {
            GameObject effectObject = new GameObject(sourceParticle.gameObject.name);
            effectObject.transform.SetParent(target, false);
            targetParticle = effectObject.AddComponent<ParticleSystem>();
        }

        targetParticle.transform.localPosition = sourceParticle.transform.localPosition;
        targetParticle.transform.localRotation = sourceParticle.transform.localRotation;
        targetParticle.transform.localScale = sourceParticle.transform.localScale;
        EditorUtility.CopySerialized(sourceParticle, targetParticle);

        ParticleSystemRenderer sourceRenderer = source.GetComponent<ParticleSystemRenderer>();
        if (sourceRenderer == null)
            sourceRenderer = sourceParticle.GetComponent<ParticleSystemRenderer>();
        ParticleSystemRenderer targetRenderer = targetParticle.GetComponent<ParticleSystemRenderer>();
        if (sourceRenderer != null && targetRenderer != null)
            EditorUtility.CopySerialized(sourceRenderer, targetRenderer);
        targetParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return targetParticle;
    }

    private static Light CopyMuzzleLight(Transform source, Transform target)
    {
        Light sourceLight = source.GetComponentInChildren<Light>(true);
        Light targetLight = target.GetComponentInChildren<Light>(true);
        if (sourceLight != null)
        {
            if (targetLight == null)
            {
                GameObject effectObject = new GameObject(sourceLight.gameObject.name);
                effectObject.transform.SetParent(target, false);
                targetLight = effectObject.AddComponent<Light>();
            }
            targetLight.transform.localPosition = sourceLight.transform.localPosition;
            targetLight.transform.localRotation = sourceLight.transform.localRotation;
            targetLight.transform.localScale = sourceLight.transform.localScale;
            EditorUtility.CopySerialized(sourceLight, targetLight);
        }
        if (targetLight != null) targetLight.enabled = false;
        return targetLight;
    }

    private static void RemoveRuntimeNavMeshBuilders()
    {
        foreach (Assets.Scripts.RuntimeNavMeshBuilder builder in
                 Object.FindObjectsOfType<Assets.Scripts.RuntimeNavMeshBuilder>(true))
            Object.DestroyImmediate(builder);
    }

    private static T EnsureUnique<T>(GameObject owner) where T : Component
    {
        T[] components = owner.GetComponents<T>();
        T result = components.FirstOrDefault();
        if (result == null) result = owner.AddComponent<T>();
        for (int i = 1; i < components.Length; i++) Object.DestroyImmediate(components[i]);
        return result;
    }

    private static GameObject EnsureDirectChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.gameObject;
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static GameObject RequireUniqueNamed(Scene scene, string name)
    {
        GameObject[] matches = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(item => item.name == name)
            .Select(item => item.gameObject)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"Expected exactly one '{name}', found {matches.Length}.");
        return matches[0];
    }

    private static GameObject FindNamed(Scene scene, string name)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(item => item.name == name)?.gameObject;
    }

    private static Transform RequireDescendant(Transform root, string name)
    {
        Transform found = root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name == name);
        if (found == null) throw new InvalidOperationException($"Missing '{name}' under '{root.name}'.");
        return found;
    }
}
