using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class SeekerPresentationSetupTool
{
    public const string ScenePath = "Assets/Scenes/Map_v2.unity";
    public const string WorldLayerName = "SeekerWorldVisual";
    public const string FpsLayerName = "SeekerFPSVisual";
    public const string MuzzleMaterialPath = "Assets/UI/HiderHUD/SeekerMuzzleFlash.mat";
    public const string SourceHoldControllerPath = "Assets/StarterAssets/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
    public const string SeekerHoldControllerPath = "Assets/Animations/PropHunt/SeekerWeaponHold.controller";
    public const string ImpactPackageRoot = "Assets/Inguz Media Studio/Free 2D Impact FX";
    public const string ImpactPrefabPath = ImpactPackageRoot + "/Prefabs/Impact02.prefab";

    [MenuItem("Tools/Prop Hunt/Setup Seeker Presentation + Energy")]
    public static void Setup()
    {
        GameObject cyberSource = FindAsset<GameObject>(
            path => path.EndsWith("CyberSoldier.fbx", StringComparison.OrdinalIgnoreCase),
            "Cyber Soldier model");
        GameObject gunSource = FindAsset<GameObject>(
            path => path.EndsWith("SciFiGunLight_Blue.prefab", StringComparison.OrdinalIgnoreCase),
            "Sci-Fi Gun Light Blue prefab");
        AudioClip shotClip = FindAsset<AudioClip>(
            path => Path.GetFileNameWithoutExtension(path).Equals("light_blast_3", StringComparison.OrdinalIgnoreCase),
            "laser shot audio");
        AudioClip reloadClip = FindAsset<AudioClip>(
            path => Path.GetFileNameWithoutExtension(path).Equals("reloading_012", StringComparison.OrdinalIgnoreCase),
            "reload audio");
        GameObject impactPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ImpactPrefabPath);
        if (impactPrefab == null) throw new InvalidOperationException($"Impact02 prefab not found at {ImpactPrefabPath}.");
        string impactInventory = string.Join(", ", AssetDatabase.FindAssets("t:GameObject", new[] { ImpactPackageRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path)
            .Select(Path.GetFileNameWithoutExtension));

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int worldLayer = EnsureLayer(WorldLayerName, 11);
        int fpsLayer = EnsureLayer(FpsLayerName, 12);

        GameObject seeker = FindSceneObject(scene, "SeekerPlayer");
        GameObject worldRoot = FindDirectOrDescendant(seeker.transform, "SeekerWorldVisualRoot").gameObject;
        GameObject industrialFallback = FindDirectOrDescendant(worldRoot.transform, "IndustrialSeekerModel")?.gameObject;
        GameObject seekerCameraObject = FindDirectOrDescendant(seeker.transform, "SeekerCamera").gameObject;
        Camera seekerCamera = RequireComponent<Camera>(seekerCameraObject, "SeekerCamera");
        GameObject weaponHolder = FindDirectOrDescendant(seekerCameraObject.transform, "WeaponHolder").gameObject;
        GameObject pulseFallback = FindDirectOrDescendant(weaponHolder.transform, "PulseTaggerVisual")?.gameObject;
        GameObject seekerHudRoot = FindSceneObject(scene, "SeekerHUDRoot");
        GameObject seekerHealthBar = FindDirectOrDescendant(seekerHudRoot.transform, "SeekerHealthBar")?.gameObject;

        GameObject cyberModel = RebuildCyberSoldier(seeker, worldRoot, cyberSource, worldLayer);
        Animator cyberAnimator = cyberModel.GetComponentInChildren<Animator>(true);
        GameObject cyberVisual = FindDirectOrDescendant(cyberModel.transform, "CyberSoldierVisual").gameObject;
        CyberSoldierAnimationEventReceiver animationEventReceiver =
            GetOrAddUniqueComponent<CyberSoldierAnimationEventReceiver>(cyberVisual);
        animationEventReceiver.ConfigureInactive();
        Transform rightHand = FindRightHand(cyberAnimator, cyberModel.transform);
        Transform leftHand = FindLeftHand(cyberAnimator, cyberModel.transform);
        GameObject fpsGun = RebuildGun(
            weaponHolder.transform,
            "SeekerFPSGunPivot",
            "SciFiGunLight_FPS",
            gunSource,
            fpsLayer,
            0.62f,
            Vector3.zero,
            Quaternion.identity,
            "MuzzlePoint");
        GameObject worldGun = RebuildGun(
            rightHand,
            "SeekerWorldGunPivot",
            "SciFiGunLight_World",
            gunSource,
            worldLayer,
            0.90f,
            Vector3.zero,
            Quaternion.identity,
            "MuzzlePoint_World");
        Transform worldPivot = worldGun.transform.parent;
        worldPivot.rotation = Quaternion.LookRotation(seeker.transform.forward, Vector3.up);
        FitGunToWorldDimension(worldGun, 0.90f, "MuzzlePoint_World");
        ConfigureWorldGripPoints(worldGun, out Transform rightHandGrip, out Transform leftHandGrip);
        worldPivot.position += rightHand.position - rightHandGrip.position;
        rightHandGrip.rotation = rightHand.rotation;
        leftHandGrip.rotation = leftHand.rotation;
        AnimatorController holdController = EnsureSeekerHoldController();
        cyberAnimator.runtimeAnimatorController = holdController;
        cyberAnimator.applyRootMotion = false;
        SeekerWeaponGripController gripController = GetOrAddUniqueComponent<SeekerWeaponGripController>(cyberAnimator.gameObject);
        gripController.Configure(cyberAnimator, rightHand, leftHand, worldPivot, rightHandGrip, leftHandGrip);

        if (industrialFallback != null) industrialFallback.SetActive(false);
        if (pulseFallback != null) pulseFallback.SetActive(false);

        Transform muzzlePoint = FindDirectOrDescendant(fpsGun.transform, "MuzzlePoint");
        Material muzzleMaterial = EnsureMuzzleMaterial();
        ParticleSystem muzzleFlash = ConfigureMuzzleFlash(muzzlePoint, muzzleMaterial);
        Light muzzleLight = ConfigureMuzzleLight(muzzlePoint);

        GameObject impactPoolObject = EnsureChild(seeker.transform, "SeekerImpactPool");
        impactPoolObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        impactPoolObject.transform.localScale = Vector3.one;

        AudioSource audioSource = GetOrAddUniqueComponent<AudioSource>(seeker);
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 0.65f;

        PropHuntTestRoleSelector roleSelector = Object.FindObjectOfType<PropHuntTestRoleSelector>(true);
        SeekerRaycastWeapon weapon = seeker.GetComponentInChildren<SeekerRaycastWeapon>(true);
        if (roleSelector == null || weapon == null)
            throw new InvalidOperationException("Missing PropHuntTestRoleSelector or SeekerRaycastWeapon in Map_v2.");

        SeekerWeaponEnergy energy = GetOrAddUniqueComponent<SeekerWeaponEnergy>(seeker);
        SeekerWeaponPresentation presentation = GetOrAddUniqueComponent<SeekerWeaponPresentation>(seeker);
        energy.Configure(roleSelector, weapon);
        presentation.Configure(
            audioSource,
            shotClip,
            reloadClip,
            muzzleFlash,
            muzzleLight,
            impactPrefab,
            impactPoolObject.transform,
            seekerCamera,
            energy);
        weapon.ConfigureEnergyAndPresentation(energy, presentation);
        roleSelector.ConfigureWeaponEnergy(energy);

        Renderer[] fpsRenderers = fpsGun.GetComponentsInChildren<Renderer>(true);
        SetWeaponPulseRenderers(weapon, fpsRenderers);
        BuildEnergyHud(seekerHudRoot, seekerHealthBar, energy);
        ConfigureCameraMasks(scene, seekerCamera, worldLayer, fpsLayer);

        SetLayerRecursively(cyberModel, worldLayer);
        SetLayerRecursively(worldGun.transform.parent.gameObject, worldLayer);
        SetLayerRecursively(fpsGun.transform.parent.gameObject, fpsLayer);
        EditorUtility.SetDirty(seeker);
        EditorUtility.SetDirty(roleSelector);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log(
            "[SeekerPresentationSetup] COMPLETE\n" +
            $"Cyber={AssetDatabase.GetAssetPath(cyberSource)}\n" +
            $"Gun={AssetDatabase.GetAssetPath(gunSource)}\n" +
            $"Shot={AssetDatabase.GetAssetPath(shotClip)} ({shotClip.length:F3}s)\n" +
            $"Reload={AssetDatabase.GetAssetPath(reloadClip)} ({reloadClip.length:F3}s)\n" +
            $"Impact={AssetDatabase.GetAssetPath(impactPrefab)}\n" +
            $"Impact prefab inventory=[{impactInventory}]\n" +
            $"AnimationEventReceiver={GetHierarchyPath(animationEventReceiver.transform)}\n" +
            $"RightHand={GetHierarchyPath(rightHand)}\n" +
            $"LeftHand={GetHierarchyPath(leftHand)}\n" +
            $"HoldController={AssetDatabase.GetAssetPath(holdController)} (IK Pass={holdController.layers[0].iKPass})\n" +
            $"Layers: World={worldLayer}, FPS={fpsLayer}\n" +
            $"Cyber local={FormatTransform(cyberModel.transform)}\n" +
            $"WeaponHolder local={FormatTransform(weaponHolder.transform)}\n" +
            $"FPS pivot local={FormatTransform(fpsGun.transform.parent)}\n" +
            $"FPS gun local={FormatTransform(fpsGun.transform)}\n" +
            $"Muzzle local={FormatTransform(muzzlePoint)}\n" +
            $"World pivot local={FormatTransform(worldPivot)}\n" +
            $"World gun local={FormatTransform(worldGun.transform)}\n" +
            $"RightHandGrip={GetHierarchyPath(rightHandGrip)}, distance={Vector3.Distance(rightHand.position, rightHandGrip.position):F6}m\n" +
            $"LeftHandGrip={GetHierarchyPath(leftHandGrip)}, targetDistance={Vector3.Distance(leftHand.position, leftHandGrip.position):F4}m");
    }

    [MenuItem("Tools/Prop Hunt/Setup Seeker Presentation Twice + Validate")]
    public static void SetupTwiceAndValidate()
    {
        Setup();
        Setup();
        SeekerPresentationValidationTool.ValidateStatic();
    }

    private static GameObject RebuildCyberSoldier(
        GameObject seeker,
        GameObject worldRoot,
        GameObject source,
        int layer)
    {
        GameObject wrapper = EnsureChild(worldRoot.transform, "CyberSoldierModel");
        wrapper.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        wrapper.transform.localScale = Vector3.one;
        Transform existingVisual = FindDirectChild(wrapper.transform, "CyberSoldierVisual");
        GameObject visual;
        if (existingVisual == null)
        {
            visual = (GameObject)PrefabUtility.InstantiatePrefab(source, wrapper.transform);
            visual.name = "CyberSoldierVisual";
        }
        else visual = existingVisual.gameObject;
        visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        visual.transform.localScale = Vector3.one;
        StripDownloadedGameplayComponents(visual);

        Animator animator = visual.GetComponentInChildren<Animator>(true);
        if (animator != null) animator.applyRootMotion = false;
        Bounds originalBounds = CalculateWorldBounds(wrapper);
        CharacterController controller = seeker.GetComponent<CharacterController>();
        float targetHeight = controller != null ? controller.height * 0.92f : 1.84f;
        if (originalBounds.size.y > 0.001f)
            wrapper.transform.localScale = Vector3.one * (targetHeight / originalBounds.size.y);
        Bounds fittedBounds = CalculateWorldBounds(wrapper);
        wrapper.transform.position += Vector3.up * (seeker.transform.position.y - fittedBounds.min.y);
        SetLayerRecursively(wrapper, layer);
        return wrapper;
    }

    private static GameObject RebuildGun(
        Transform parent,
        string pivotName,
        string gunName,
        GameObject source,
        int layer,
        float targetLength,
        Vector3 pivotPosition,
        Quaternion pivotRotation,
        string muzzlePointName)
    {
        GameObject pivot = EnsureChild(parent, pivotName);
        pivot.transform.SetLocalPositionAndRotation(pivotPosition, pivotRotation);
        pivot.transform.localScale = Vector3.one;

        GameObject gunRoot = EnsureChild(pivot.transform, gunName);
        gunRoot.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        gunRoot.transform.localScale = Vector3.one;
        Transform existingMesh = FindDirectChild(gunRoot.transform, "GunMesh");
        GameObject mesh;
        if (existingMesh == null)
        {
            mesh = (GameObject)PrefabUtility.InstantiatePrefab(source, gunRoot.transform);
            mesh.name = "GunMesh";
        }
        else mesh = existingMesh.gameObject;
        mesh.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        mesh.transform.localScale = Vector3.one;
        StripDownloadedGameplayComponents(mesh);

        Bounds localBounds = CalculateBoundsInSpace(mesh, gunRoot.transform);
        Vector3 size = localBounds.size;
        if (size.x >= size.y && size.x >= size.z)
            mesh.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        else if (size.y >= size.x && size.y >= size.z)
            mesh.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        localBounds = CalculateBoundsInSpace(mesh, gunRoot.transform);
        float length = Mathf.Max(localBounds.size.x, localBounds.size.y, localBounds.size.z);
        float sourceLength = length;
        float parentScale = Mathf.Max(
            Mathf.Abs(pivot.transform.lossyScale.x),
            Mathf.Abs(pivot.transform.lossyScale.y),
            Mathf.Abs(pivot.transform.lossyScale.z));
        float targetLocalLength = targetLength / Mathf.Max(parentScale, 0.0001f);
        if (length > 0.001f) mesh.transform.localScale = Vector3.one * (targetLocalLength / length);
        localBounds = CalculateBoundsInSpace(mesh, gunRoot.transform);
        mesh.transform.localPosition -= new Vector3(localBounds.center.x, localBounds.center.y, localBounds.min.z);
        localBounds = CalculateBoundsInSpace(mesh, gunRoot.transform);

        if (!string.IsNullOrEmpty(muzzlePointName))
        {
            GameObject muzzle = EnsureChild(gunRoot.transform, muzzlePointName);
            muzzle.transform.localPosition = new Vector3(0f, 0f, localBounds.max.z + 0.01f);
            muzzle.transform.localRotation = Quaternion.identity;
            muzzle.transform.localScale = Vector3.one;
        }
        SetLayerRecursively(pivot, layer);
        Debug.Log($"[SeekerPresentationSetup] {gunName} bounds: sourceMax={sourceLength:F4}m, targetWorldMax={targetLength:F2}m, parentScale={parentScale:F4}, finalLocalSize={localBounds.size:F4}");
        return gunRoot;
    }

    private static void BuildEnergyHud(GameObject seekerHudRoot, GameObject healthBar, SeekerWeaponEnergy energy)
    {
        GameObject bar = EnsureUiChild(seekerHudRoot.transform, "SeekerWeaponEnergyBar");
        RectTransform barRect = bar.GetComponent<RectTransform>();
        RectTransform healthRect = healthBar != null ? healthBar.GetComponent<RectTransform>() : null;
        barRect.anchorMin = healthRect != null ? healthRect.anchorMin : new Vector2(1f, 0f);
        barRect.anchorMax = healthRect != null ? healthRect.anchorMax : new Vector2(1f, 0f);
        barRect.pivot = healthRect != null ? healthRect.pivot : new Vector2(1f, 0f);
        barRect.sizeDelta = healthRect != null ? healthRect.sizeDelta : new Vector2(336f, 24f);
        Vector2 healthPosition = healthRect != null ? healthRect.anchoredPosition : new Vector2(-28f, 108f);
        barRect.anchoredPosition = healthPosition + new Vector2(0f, 36f);

        DestroyChildrenNamed(bar.transform, "EnergyBackground");
        HorizontalLayoutGroup layout = GetOrAddUniqueComponent<HorizontalLayoutGroup>(bar);
        layout.padding = new RectOffset(1, 1, 1, 1);
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        Image[] segmentFills = new Image[5];
        for (int index = 0; index < segmentFills.Length; index++)
        {
            GameObject segment = EnsureUiChild(bar.transform, $"EnergySegment_{index + 1}");
            Image backgroundImage = GetOrAddUniqueComponent<Image>(segment);
            backgroundImage.color = new Color32(4, 14, 19, 245);
            backgroundImage.raycastTarget = false;
            LayoutElement layoutElement = GetOrAddUniqueComponent<LayoutElement>(segment);
            layoutElement.flexibleWidth = 1f;
            layoutElement.minWidth = 24f;
            Outline outline = GetOrAddUniqueComponent<Outline>(segment);
            outline.effectColor = new Color32(0, 85, 96, 220);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = true;

            GameObject fill = EnsureUiChild(segment.transform, "Fill");
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);
            Image fillImage = GetOrAddUniqueComponent<Image>(fill);
            fillImage.color = new Color32(0, 229, 255, 255);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = 0;
            fillImage.fillAmount = 1f;
            fillImage.raycastTarget = false;
            segmentFills[index] = fillImage;
            segment.SetActive(true);
            fill.SetActive(true);
        }

        GameObject textObject = EnsureUiChild(bar.transform, "EnergyText");
        Stretch(textObject.GetComponent<RectTransform>());
        TextMeshProUGUI text = GetOrAddUniqueComponent<TextMeshProUGUI>(textObject);
        TextMeshProUGUI existingText = seekerHudRoot.GetComponentsInChildren<TextMeshProUGUI>(true)
            .FirstOrDefault(candidate => candidate != text);
        if (existingText != null) text.font = existingText.font;
        text.text = string.Empty;
        text.fontSize = 15f;
        text.fontStyle = FontStyles.Bold;
        text.color = new Color32(0, 21, 28, 255);
        text.outlineColor = new Color32(0, 229, 255, 190);
        text.outlineWidth = 0.08f;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        textObject.SetActive(false);

        SeekerWeaponEnergyBarController controller = GetOrAddUniqueComponent<SeekerWeaponEnergyBarController>(bar);
        controller.Configure(energy, segmentFills, text);
        bar.SetActive(true);
    }

    private static void ConfigureWorldGripPoints(
        GameObject worldGun,
        out Transform rightHandGrip,
        out Transform leftHandGrip)
    {
        Bounds bounds = CalculateBoundsInSpace(worldGun, worldGun.transform);
        GameObject right = EnsureChild(worldGun.transform, "RightHandGrip");
        right.transform.localPosition = new Vector3(
            0f,
            Mathf.Lerp(bounds.min.y, bounds.max.y, 0.30f),
            Mathf.Lerp(bounds.min.z, bounds.max.z, 0.27f));
        right.transform.localRotation = Quaternion.identity;
        right.transform.localScale = Vector3.one;

        GameObject left = EnsureChild(worldGun.transform, "LeftHandGrip");
        left.transform.localPosition = new Vector3(
            0f,
            Mathf.Lerp(bounds.min.y, bounds.max.y, 0.52f),
            Mathf.Lerp(bounds.min.z, bounds.max.z, 0.62f));
        left.transform.localRotation = Quaternion.identity;
        left.transform.localScale = Vector3.one;
        rightHandGrip = right.transform;
        leftHandGrip = left.transform;
    }

    private static void FitGunToWorldDimension(GameObject gunRoot, float targetWorldDimension, string muzzlePointName)
    {
        Transform mesh = FindDirectChild(gunRoot.transform, "GunMesh");
        if (mesh == null) throw new InvalidOperationException($"{gunRoot.name} has no GunMesh.");
        Bounds before = CalculateWorldMeshBounds(gunRoot);
        float beforeMax = Mathf.Max(before.size.x, before.size.y, before.size.z);
        if (beforeMax <= 0.001f) throw new InvalidOperationException($"{gunRoot.name} has no measurable mesh bounds.");
        mesh.localScale *= targetWorldDimension / beforeMax;

        Bounds localBounds = CalculateBoundsInSpace(mesh.gameObject, gunRoot.transform);
        Transform muzzle = FindDirectOrDescendant(gunRoot.transform, muzzlePointName);
        if (muzzle != null)
            muzzle.localPosition = new Vector3(0f, 0f, localBounds.max.z + 0.01f);
        Bounds after = CalculateWorldMeshBounds(gunRoot);
        Debug.Log($"[SeekerPresentationSetup] {gunRoot.name} final world mesh bounds: before={before.size:F4} ({beforeMax:F4}m), after={after.size:F4} ({Mathf.Max(after.size.x, after.size.y, after.size.z):F4}m)");
    }

    private static AnimatorController EnsureSeekerHoldController()
    {
        EnsureAssetFolder("Assets/Animations");
        EnsureAssetFolder("Assets/Animations/PropHunt");
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(SeekerHoldControllerPath) == null)
        {
            if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(SourceHoldControllerPath) == null)
                throw new InvalidOperationException($"Starter Assets hold controller not found: {SourceHoldControllerPath}");
            if (!AssetDatabase.CopyAsset(SourceHoldControllerPath, SeekerHoldControllerPath))
                throw new InvalidOperationException($"Could not clone hold controller to {SeekerHoldControllerPath}");
            AssetDatabase.ImportAsset(SeekerHoldControllerPath, ImportAssetOptions.ForceSynchronousImport);
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(SeekerHoldControllerPath);
        if (controller == null || controller.layers.Length == 0)
            throw new InvalidOperationException($"Invalid hold controller: {SeekerHoldControllerPath}");
        AnimatorControllerLayer[] layers = controller.layers;
        layers[0].iKPass = true;
        controller.layers = layers;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void EnsureAssetFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(parent) || !AssetDatabase.IsValidFolder(parent))
            throw new InvalidOperationException($"Cannot create asset folder because parent is missing: {path}");
        AssetDatabase.CreateFolder(parent, name);
    }

    private static ParticleSystem ConfigureMuzzleFlash(Transform muzzlePoint, Material material)
    {
        DestroyChildrenNamed(muzzlePoint, "MuzzleFlashParticle");
        GameObject particleObject = new GameObject("MuzzleFlashParticle", typeof(ParticleSystem));
        particleObject.transform.SetParent(muzzlePoint, false);
        ParticleSystem particle = particleObject.GetComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particle.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.08f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.035f, 0.07f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.35f, 0.95f, 1f, 1f),
            new Color(0.75f, 1f, 1f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        ParticleSystem.EmissionModule emission = particle.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 4, 7) });
        ParticleSystem.ShapeModule shape = particle.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 8f;
        shape.radius = 0.01f;
        ParticleSystemRenderer renderer = particle.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return particle;
    }

    private static Light ConfigureMuzzleLight(Transform muzzlePoint)
    {
        DestroyChildrenNamed(muzzlePoint, "MuzzleLight");
        GameObject lightObject = new GameObject("MuzzleLight", typeof(Light));
        lightObject.transform.SetParent(muzzlePoint, false);
        Light light = lightObject.GetComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.2f, 0.9f, 1f, 1f);
        light.intensity = 2.4f;
        light.range = 2.2f;
        light.shadows = LightShadows.None;
        light.enabled = false;
        return light;
    }

    private static Material EnsureMuzzleMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MuzzleMaterialPath);
        Shader shader = Shader.Find("Particles/Additive");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader == null) throw new InvalidOperationException("Built-in additive particle shader was not found.");
        if (material == null)
        {
            material = new Material(shader) { name = "SeekerMuzzleFlash" };
            AssetDatabase.CreateAsset(material, MuzzleMaterialPath);
        }
        else
        {
            material.shader = shader;
            EditorUtility.SetDirty(material);
        }
        return material;
    }

    private static void ConfigureCameraMasks(Scene scene, Camera seekerCamera, int worldLayer, int fpsLayer)
    {
        foreach (Camera camera in Resources.FindObjectsOfTypeAll<Camera>())
        {
            if (!camera.gameObject.scene.IsValid() || camera.gameObject.scene != scene) continue;
            int mask = camera.cullingMask;
            if (camera == seekerCamera)
            {
                mask |= 1 << fpsLayer;
                mask &= ~(1 << worldLayer);
            }
            else
            {
                mask &= ~(1 << fpsLayer);
                string name = camera.name.ToLowerInvariant();
                if (name.Contains("hider") || name.Contains("ghost") || name.Contains("spectator"))
                    mask |= 1 << worldLayer;
            }
            camera.cullingMask = mask;
            EditorUtility.SetDirty(camera);
        }
    }

    private static Transform FindRightHand(Animator animator, Transform root)
    {
        if (animator != null && animator.isHuman)
        {
            Transform humanoidHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (humanoidHand != null) return humanoidHand;
        }

        string[] candidates = { "RightHand", "Hand.R", "mixamorig:RightHand", "Bip001 R Hand" };
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (candidates.Any(candidate => child.name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            {
                Debug.LogWarning($"[SeekerPresentationSetup] Humanoid RightHand unavailable; using bone name {child.name}.");
                return child;
            }
        }
        throw new InvalidOperationException("Cyber Soldier RightHand bone was not found.");
    }

    private static Transform FindLeftHand(Animator animator, Transform root)
    {
        if (animator != null && animator.isHuman)
        {
            Transform humanoidHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (humanoidHand != null) return humanoidHand;
        }

        string[] candidates = { "LeftHand", "Hand.L", "mixamorig:LeftHand", "Bip001 L Hand" };
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (candidates.Any(candidate => child.name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                return child;
        throw new InvalidOperationException("Cyber Soldier LeftHand bone was not found.");
    }

    private static void StripDownloadedGameplayComponents(GameObject root)
    {
        foreach (Collider item in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(item);
        foreach (CharacterController item in root.GetComponentsInChildren<CharacterController>(true)) Object.DestroyImmediate(item);
        foreach (Rigidbody item in root.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(item);
        foreach (Camera item in root.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(item);
        foreach (AudioListener item in root.GetComponentsInChildren<AudioListener>(true)) Object.DestroyImmediate(item);
        foreach (MonoBehaviour item in root.GetComponentsInChildren<MonoBehaviour>(true))
            if (!(item is SeekerWeaponGripController)) Object.DestroyImmediate(item);
    }

    private static Bounds CalculateWorldBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static Bounds CalculateWorldMeshBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => !(renderer is ParticleSystemRenderer))
            .ToArray();
        if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static Bounds CalculateBoundsInSpace(GameObject root, Transform space)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool initialized = false;
        Bounds result = default;
        foreach (Renderer renderer in renderers)
        {
            Bounds world = renderer.bounds;
            Vector3 min = world.min;
            Vector3 max = world.max;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 corner = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                Vector3 local = space.InverseTransformPoint(corner);
                if (!initialized)
                {
                    result = new Bounds(local, Vector3.zero);
                    initialized = true;
                }
                else result.Encapsulate(local);
            }
        }
        return result;
    }

    private static T FindAsset<T>(Func<string, bool> pathPredicate, string label) where T : Object
    {
        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
        List<string> matches = guids.Select(AssetDatabase.GUIDToAssetPath).Where(pathPredicate).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException($"Expected exactly one {label}; found {matches.Count}: {string.Join(", ", matches)}");
        T asset = AssetDatabase.LoadAssetAtPath<T>(matches[0]);
        if (asset == null) throw new InvalidOperationException($"Could not load {label}: {matches[0]}");
        return asset;
    }

    private static int EnsureLayer(string layerName, int preferredIndex)
    {
        int existing = LayerMask.NameToLayer(layerName);
        if (existing >= 0) return existing;
        SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        int selected = -1;
        if (preferredIndex >= 8 && preferredIndex < 32 && string.IsNullOrEmpty(layers.GetArrayElementAtIndex(preferredIndex).stringValue))
            selected = preferredIndex;
        if (selected < 0)
        {
            for (int i = 8; i < 32; i++)
            {
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) { selected = i; break; }
            }
        }
        if (selected < 0) throw new InvalidOperationException($"No free user layer for {layerName}.");
        layers.GetArrayElementAtIndex(selected).stringValue = layerName;
        tagManager.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        return selected;
    }

    private static GameObject FindSceneObject(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform match = root.name == name ? root.transform : FindDirectOrDescendant(root.transform, name);
            if (match != null) return match.gameObject;
        }
        throw new InvalidOperationException($"Scene object not found: {name}");
    }

    private static Transform FindDirectOrDescendant(Transform root, string name)
    {
        if (root == null) return null;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }

    private static GameObject EnsureChild(Transform parent, string name)
    {
        Transform existing = FindDirectChild(parent, name);
        if (existing != null)
        {
            for (int index = parent.childCount - 1; index >= 0; index--)
            {
                Transform duplicate = parent.GetChild(index);
                if (duplicate != existing && duplicate.name == name) Object.DestroyImmediate(duplicate.gameObject);
            }
            return existing.gameObject;
        }
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static Transform FindDirectChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
            if (parent.GetChild(i).name == name) return parent.GetChild(i);
        return null;
    }

    private static void DestroyChildrenNamed(Transform parent, string name)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            if (parent.GetChild(i).name == name) Object.DestroyImmediate(parent.GetChild(i).gameObject);
    }

    private static T RequireComponent<T>(GameObject gameObject, string label) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        if (component == null) throw new InvalidOperationException($"{label} is missing {typeof(T).Name}.");
        return component;
    }

    private static T GetOrAddUniqueComponent<T>(GameObject gameObject) where T : Component
    {
        T[] components = gameObject.GetComponents<T>();
        T component = components.Length > 0 ? components[0] : gameObject.AddComponent<T>();
        for (int i = components.Length - 1; i >= 1; i--) Object.DestroyImmediate(components[i]);
        return component;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static GameObject EnsureUiChild(Transform parent, string name)
    {
        Transform existing = FindDirectChild(parent, name);
        if (existing == null) return CreateUiObject(name, parent);
        for (int index = parent.childCount - 1; index >= 0; index--)
        {
            Transform duplicate = parent.GetChild(index);
            if (duplicate != existing && duplicate.name == name) Object.DestroyImmediate(duplicate.gameObject);
        }
        if (existing.GetComponent<RectTransform>() == null)
            throw new InvalidOperationException($"Existing UI object {name} has no RectTransform.");
        return existing.gameObject;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = layer;
    }

    private static void SetWeaponPulseRenderers(SeekerRaycastWeapon weapon, Renderer[] renderers)
    {
        SerializedObject serializedWeapon = new SerializedObject(weapon);
        SerializedProperty property = serializedWeapon.FindProperty("pulseRenderers");
        property.arraySize = renderers.Length;
        for (int i = 0; i < renderers.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
        serializedWeapon.ApplyModifiedPropertiesWithoutUndo();
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null) return "<missing>";
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }

    private static string FormatTransform(Transform transform)
    {
        return transform == null
            ? "<missing>"
            : $"pos={transform.localPosition:F4}, euler={transform.localEulerAngles:F3}, scale={transform.localScale:F4}";
    }
}
