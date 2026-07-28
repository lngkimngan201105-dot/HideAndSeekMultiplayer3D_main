using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
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
    public const string SourceGunAssetPath =
        "Assets/Sci Fi Gun Light/Prefabs/SciFiGunLight/SciFiGunLight_Blue.prefab";
    private const float VisualBoundsEpsilon = 0.0001f;
    private const int ManualGunAlignmentVersion = 1;

    public static void EnsurePresentationConfigured()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject seeker = FindSceneObject(scene, "SeekerPlayer");
        Transform worldGun = FindDirectOrDescendant(seeker.transform, "SciFiGunLight_World");
        Transform fpsGun = FindDirectOrDescendant(seeker.transform, "SciFiGunLight_FPS");
        Transform worldMuzzle = FindDirectOrDescendant(seeker.transform, "MuzzlePoint_World");
        Transform cyberVisual = FindDirectOrDescendant(seeker.transform, "CyberSoldierVisual");
        SeekerWeaponPresentation presentation = seeker.GetComponent<SeekerWeaponPresentation>();
        SeekerWeaponEnergy energy = seeker.GetComponent<SeekerWeaponEnergy>();
        SeekerRaycastWeapon weapon = seeker.GetComponentInChildren<SeekerRaycastWeapon>(true);
        SeekerWeaponGripController grip =
            cyberVisual != null ? cyberVisual.GetComponent<SeekerWeaponGripController>() : null;
        bool cyberValid = cyberVisual != null &&
                          cyberVisual.GetComponentInChildren<Animator>(true) != null &&
                          HasRealVisualMesh(cyberVisual.gameObject);
        bool worldValid = worldGun != null &&
                          TryCalculateVisualBounds(worldGun.gameObject, out _, out _);
        bool fpsValid = fpsGun != null &&
                        TryCalculateVisualBounds(fpsGun.gameObject, out _, out _);
        bool referencesValid = presentation != null && energy != null && weapon != null &&
                               presentation.AudioSource != null &&
                               presentation.ShotAudioClip != null &&
                               presentation.ReloadAudioClip != null &&
                               presentation.ImpactPrefab != null &&
                               presentation.MuzzleFlash != null &&
                               worldMuzzle != null &&
                               presentation.MuzzleFlash.transform.IsChildOf(worldMuzzle);
        bool rigValid = grip != null &&
                        grip.ManualAlignmentVersion == ManualGunAlignmentVersion &&
                        grip.WeaponRig != null &&
                        grip.RightArmIk != null &&
                        grip.LeftArmIk != null &&
                        grip.UpperBodyAim != null &&
                        grip.AIAimTarget != null &&
                        cyberVisual.GetComponent<RigBuilder>() != null;
        if (cyberValid && worldValid && fpsValid && referencesValid && rigValid)
        {
            Debug.Log("[SeekerPresentationSetup] Existing presentation is valid; references retained without rebuilding user-adjusted pivots.");
            return;
        }

        Setup();
    }

    [MenuItem("Tools/Prop Hunt/Setup Seeker Presentation + Energy")]
    public static void Setup()
    {
        GameObject cyberSource = FindAsset<GameObject>(
            path => path.EndsWith("CyberSoldier.fbx", StringComparison.OrdinalIgnoreCase),
            "Cyber Soldier model");
        GameObject gunSource = AssetDatabase.LoadAssetAtPath<GameObject>(SourceGunAssetPath);
        if (gunSource == null)
            throw new InvalidOperationException(
                $"Sci-Fi Gun Light Blue prefab is missing at exact path: {SourceGunAssetPath}");
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

        Transform priorWorldPivot = FindDirectOrDescendant(worldRoot.transform, "SeekerWorldGunPivot");
        Transform priorWorldGun = priorWorldPivot != null
            ? FindDirectChild(priorWorldPivot, "SciFiGunLight_World")
            : null;
        bool priorWorldGunValid = priorWorldGun != null &&
                                  TryCalculateVisualBounds(
                                      priorWorldGun.gameObject, out _, out _);
        Vector3 priorPivotPosition = priorWorldGunValid
            ? priorWorldPivot.localPosition
            : Vector3.zero;
        Quaternion priorPivotRotation = priorWorldGunValid
            ? priorWorldPivot.localRotation
            : Quaternion.identity;
        Vector3 priorPivotScale = priorWorldGunValid
            ? priorWorldPivot.localScale
            : Vector3.one;

        GameObject cyberModel = RebuildCyberSoldier(seeker, worldRoot, cyberSource, worldLayer);
        Animator cyberAnimator = cyberModel.GetComponentInChildren<Animator>(true);
        GameObject cyberVisual = FindDirectOrDescendant(cyberModel.transform, "CyberSoldierVisual").gameObject;
        CyberSoldierAnimationEventReceiver animationEventReceiver =
            GetOrAddUniqueComponent<CyberSoldierAnimationEventReceiver>(cyberVisual);
        animationEventReceiver.ConfigureInactive();
        Dictionary<HumanBodyBones, Transform> bones = AuditHumanoidBones(cyberAnimator);
        Transform hips = bones[HumanBodyBones.Hips];
        Transform spine = bones[HumanBodyBones.Spine];
        Transform chest = bones[HumanBodyBones.Chest];
        Transform leftShoulder = bones[HumanBodyBones.LeftShoulder];
        Transform leftUpperArm = bones[HumanBodyBones.LeftUpperArm];
        Transform leftLowerArm = bones[HumanBodyBones.LeftLowerArm];
        Transform leftHand = bones[HumanBodyBones.LeftHand];
        Transform rightShoulder = bones[HumanBodyBones.RightShoulder];
        Transform rightUpperArm = bones[HumanBodyBones.RightUpperArm];
        Transform rightLowerArm = bones[HumanBodyBones.RightLowerArm];
        Transform rightHand = bones[HumanBodyBones.RightHand];
        SeekerWeaponGripController existingGripController =
            cyberAnimator.GetComponent<SeekerWeaponGripController>();
        bool approvedManualAlignment = existingGripController != null &&
                                       existingGripController.ManualAlignmentVersion >=
                                       ManualGunAlignmentVersion;
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
        Transform existingWorldPivot = FindDirectChild(rightHand, "SeekerWorldGunPivot");
        Transform existingWorldGun = existingWorldPivot != null
            ? FindDirectChild(existingWorldPivot, "SciFiGunLight_World")
            : null;
        bool currentWorldGunValid = existingWorldGun != null &&
                                    TryCalculateVisualBounds(
                                        existingWorldGun.gameObject, out _, out _);
        bool preserveWorldPivot = approvedManualAlignment &&
                                  (currentWorldGunValid || priorWorldGunValid);
        Vector3 preservedPivotPosition = preserveWorldPivot
            ? (currentWorldGunValid ? existingWorldPivot.localPosition : priorPivotPosition)
            : Vector3.zero;
        Quaternion preservedPivotRotation = preserveWorldPivot
            ? (currentWorldGunValid ? existingWorldPivot.localRotation : priorPivotRotation)
            : Quaternion.identity;
        Vector3 preservedPivotScale = preserveWorldPivot
            ? (currentWorldGunValid ? existingWorldPivot.localScale : priorPivotScale)
            : Vector3.one;

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
        FitGunToWorldDimension(worldGun, 0.90f, "MuzzlePoint_World");
        ConfigureWorldGripPoints(worldGun, out Transform rightHandGrip, out Transform leftHandGrip);
        if (preserveWorldPivot)
        {
            worldPivot.localPosition = preservedPivotPosition;
            worldPivot.localRotation = preservedPivotRotation;
            worldPivot.localScale = preservedPivotScale;
        }
        else
        {
            worldPivot.SetParent(rightHand, false);
            worldPivot.localScale = Vector3.one;
            worldPivot.SetPositionAndRotation(
                rightHand.position,
                Quaternion.LookRotation(seeker.transform.forward, Vector3.up));
            Vector3 gripOffsetFromPivot = rightHandGrip.position - worldPivot.position;
            worldPivot.position = rightHand.position - gripOffsetFromPivot;
        }
        rightHandGrip.rotation = rightHand.rotation;
        leftHandGrip.rotation = leftHand.rotation;
        AnimatorController holdController = EnsureSeekerHoldController();
        cyberAnimator.runtimeAnimatorController = holdController;
        cyberAnimator.applyRootMotion = false;
        cyberAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        EditorUtility.SetDirty(cyberAnimator);
        PrefabUtility.RecordPrefabInstancePropertyModifications(cyberAnimator);
        ConfigureWeaponRig(
            seeker.transform,
            cyberAnimator,
            chest,
            leftUpperArm,
            leftLowerArm,
            leftHand,
            rightUpperArm,
            rightLowerArm,
            rightHand,
            rightHandGrip,
            leftHandGrip,
            out Rig weaponRig,
            out TwoBoneIKConstraint rightArmIk,
            out TwoBoneIKConstraint leftArmIk,
            out MultiAimConstraint upperBodyAim,
            out Transform aiAimTarget,
            out Transform rightHandIkTarget);
        SeekerWeaponGripController gripController = GetOrAddUniqueComponent<SeekerWeaponGripController>(cyberAnimator.gameObject);
        gripController.Configure(
            cyberAnimator,
            rightHand,
            leftHand,
            chest,
            worldPivot,
            rightHandGrip,
            leftHandGrip,
            rightHandIkTarget,
            weaponRig,
            rightArmIk,
            leftArmIk,
            upperBodyAim,
            aiAimTarget,
            ManualGunAlignmentVersion);

        if (industrialFallback != null) industrialFallback.SetActive(false);
        if (pulseFallback != null) pulseFallback.SetActive(false);

        Transform fpsMuzzlePoint =
            FindDirectOrDescendant(fpsGun.transform, "MuzzlePoint");
        Transform worldMuzzlePoint =
            FindDirectOrDescendant(worldGun.transform, "MuzzlePoint_World");
        if (fpsMuzzlePoint == null || worldMuzzlePoint == null)
            throw new InvalidOperationException(
                "FPS or world muzzle point is missing after gun setup.");
        Material muzzleMaterial = EnsureMuzzleMaterial();
        ConfigureMuzzleFlash(fpsMuzzlePoint, muzzleMaterial);
        ConfigureMuzzleLight(fpsMuzzlePoint);
        ParticleSystem muzzleFlash =
            ConfigureMuzzleFlash(worldMuzzlePoint, muzzleMaterial);
        Light muzzleLight = ConfigureMuzzleLight(worldMuzzlePoint);

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
            $"HumanoidAudit Hips={GetHierarchyPath(hips)}, Spine={GetHierarchyPath(spine)}, Chest={GetHierarchyPath(chest)}\n" +
            $"HumanoidAudit LeftShoulder={GetHierarchyPath(leftShoulder)}, LeftUpperArm={GetHierarchyPath(leftUpperArm)}, LeftLowerArm={GetHierarchyPath(leftLowerArm)}\n" +
            $"HumanoidAudit RightShoulder={GetHierarchyPath(rightShoulder)}, RightUpperArm={GetHierarchyPath(rightUpperArm)}, RightLowerArm={GetHierarchyPath(rightLowerArm)}\n" +
            $"RightHand={GetHierarchyPath(rightHand)}\n" +
            $"LeftHand={GetHierarchyPath(leftHand)}\n" +
            $"HoldController={AssetDatabase.GetAssetPath(holdController)} (IK Pass={holdController.layers[0].iKPass})\n" +
            $"Rig={GetHierarchyPath(weaponRig.transform)}, RightArmIK={GetHierarchyPath(rightArmIk.transform)}, LeftArmIK={GetHierarchyPath(leftArmIk.transform)}, UpperBodyAim={GetHierarchyPath(upperBodyAim.transform)}\n" +
            $"AIAimTarget={GetHierarchyPath(aiAimTarget)}\n" +
            $"Layers: World={worldLayer}, FPS={fpsLayer}\n" +
            $"Cyber local={FormatTransform(cyberModel.transform)}\n" +
            $"WeaponHolder local={FormatTransform(weaponHolder.transform)}\n" +
            $"FPS pivot local={FormatTransform(fpsGun.transform.parent)}\n" +
            $"FPS gun local={FormatTransform(fpsGun.transform)}\n" +
            $"FPS muzzle local={FormatTransform(fpsMuzzlePoint)}\n" +
            $"World muzzle local={FormatTransform(worldMuzzlePoint)}\n" +
            $"World pivot local={FormatTransform(worldPivot)}\n" +
            $"World gun local={FormatTransform(worldGun.transform)}\n" +
            $"RightHandGrip={GetHierarchyPath(rightHandGrip)}, distance={Vector3.Distance(rightHand.position, rightHandGrip.position):F6}m\n" +
            $"LeftHandGrip={GetHierarchyPath(leftHandGrip)}, targetDistance={Vector3.Distance(leftHand.position, leftHandGrip.position):F4}m");
    }

    [MenuItem("Tools/Prop Hunt/Setup Seeker Presentation Twice + Validate")]
    public static void SetupTwiceAndValidate()
    {
        Setup();
        Scene firstScene = SceneManager.GetActiveScene();
        GameObject firstWorldGun = FindSceneObject(firstScene, "SciFiGunLight_World");
        Transform firstMesh = FindDirectChild(firstWorldGun.transform, "GunMesh");
        Transform firstPivot = firstWorldGun.transform.parent;
        Vector3 firstScale = firstMesh != null ? firstMesh.localScale : Vector3.zero;
        Vector3 firstPivotPosition = firstPivot.localPosition;
        Quaternion firstPivotRotation = firstPivot.localRotation;
        Vector3 firstPivotScale = firstPivot.localScale;
        int firstRigBuilders = Object.FindObjectsOfType<RigBuilder>(true).Length;
        int firstRigs = Object.FindObjectsOfType<Rig>(true).Length;
        int firstTwoBoneIks = Object.FindObjectsOfType<TwoBoneIKConstraint>(true).Length;
        int firstMultiAims = Object.FindObjectsOfType<MultiAimConstraint>(true).Length;
        Debug.Log($"[SeekerPresentationSetup] SETUP PASS 1 scale={firstScale:F6}.");

        Setup();
        Scene secondScene = SceneManager.GetActiveScene();
        GameObject secondWorldGun = FindSceneObject(secondScene, "SciFiGunLight_World");
        Transform secondMesh = FindDirectChild(secondWorldGun.transform, "GunMesh");
        Transform secondPivot = secondWorldGun.transform.parent;
        Vector3 secondScale = secondMesh != null ? secondMesh.localScale : Vector3.zero;
        if (Vector3.Distance(firstScale, secondScale) > 0.0001f)
            throw new InvalidOperationException(
                $"World gun scale is not idempotent: pass1={firstScale:F6}, pass2={secondScale:F6}.");
        if (Vector3.Distance(firstPivotPosition, secondPivot.localPosition) > 0.0001f ||
            Quaternion.Angle(firstPivotRotation, secondPivot.localRotation) > 0.01f ||
            Vector3.Distance(firstPivotScale, secondPivot.localScale) > 0.0001f)
            throw new InvalidOperationException(
                "Valid user-adjusted SeekerWorldGunPivot changed during the second setup pass.");
        int secondRigBuilders = Object.FindObjectsOfType<RigBuilder>(true).Length;
        int secondRigs = Object.FindObjectsOfType<Rig>(true).Length;
        int secondTwoBoneIks = Object.FindObjectsOfType<TwoBoneIKConstraint>(true).Length;
        int secondMultiAims = Object.FindObjectsOfType<MultiAimConstraint>(true).Length;
        if (firstRigBuilders != secondRigBuilders ||
            firstRigs != secondRigs ||
            firstTwoBoneIks != secondTwoBoneIks ||
            firstMultiAims != secondMultiAims)
            throw new InvalidOperationException(
                "Animation Rigging component counts changed during the second setup pass.");
        Debug.Log(
            $"[SeekerPresentationSetup] SETUP PASS 2 scale={secondScale:F6}; pivot retained; " +
            $"RigBuilder={secondRigBuilders}, Rig={secondRigs}, " +
            $"TwoBoneIK={secondTwoBoneIks}, MultiAim={secondMultiAims}.");
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
        if (existingVisual != null &&
            (existingVisual.GetComponentInChildren<Animator>(true) == null ||
             !HasRealVisualMesh(existingVisual.gameObject)))
        {
            Debug.LogWarning(
                "[SeekerPresentationSetup] Repairing invalid CyberSoldierVisual " +
                $"at {GetHierarchyPath(existingVisual)}: " +
                $"Animator={existingVisual.GetComponentInChildren<Animator>(true) != null}, " +
                $"SkinnedMeshRenderers={existingVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length}, " +
                $"MeshFilters={existingVisual.GetComponentsInChildren<MeshFilter>(true).Length}.");
            Object.DestroyImmediate(existingVisual.gameObject);
            existingVisual = null;
        }
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
        if (animator == null || !HasRealVisualMesh(visual))
            throw new InvalidOperationException(
                $"Cyber Soldier source '{AssetDatabase.GetAssetPath(source)}' did not produce " +
                "an Animator and a real visual mesh.");
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
        Transform existingPivot = FindDirectChild(parent, pivotName);
        GameObject pivot = EnsureChild(parent, pivotName);
        if (existingPivot == null)
        {
            pivot.transform.SetLocalPositionAndRotation(pivotPosition, pivotRotation);
            pivot.transform.localScale = Vector3.one;
        }

        GameObject gunRoot = EnsureChild(pivot.transform, gunName);
        gunRoot.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        gunRoot.transform.localScale = Vector3.one;
        Transform existingMesh = FindDirectChild(gunRoot.transform, "GunMesh");
        if (existingMesh != null &&
            !TryCalculateVisualBounds(existingMesh.gameObject, out _, out string invalidDiagnostic))
        {
            Debug.LogWarning(
                $"[SeekerPresentationSetup] Repairing invalid {gunName}/GunMesh.\n{invalidDiagnostic}");
            Object.DestroyImmediate(existingMesh.gameObject);
            existingMesh = null;
        }

        GameObject mesh;
        if (existingMesh == null)
        {
            mesh = (GameObject)PrefabUtility.InstantiatePrefab(source, gunRoot.transform);
            mesh.name = "GunMesh";
        }
        else mesh = existingMesh.gameObject;
        mesh.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        mesh.transform.localScale = Vector3.one;
        mesh.SetActive(true);
        StripDownloadedGameplayComponents(mesh);

        if (!TryCalculateVisualBoundsInSpace(mesh, gunRoot.transform, out Bounds localBounds,
                out string rebuildDiagnostic))
            throw new InvalidOperationException(
                $"{gunName} visual hierarchy could not be measured after instantiating '{AssetDatabase.GetAssetPath(source)}'.\n" +
                rebuildDiagnostic);
        Vector3 size = localBounds.size;
        if (size.x >= size.y && size.x >= size.z)
            mesh.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        else if (size.y >= size.x && size.y >= size.z)
            mesh.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        if (!TryCalculateVisualBoundsInSpace(mesh, gunRoot.transform, out localBounds, out rebuildDiagnostic))
            throw new InvalidOperationException($"{gunName} bounds failed after orientation.\n{rebuildDiagnostic}");
        float length = Mathf.Max(localBounds.size.x, localBounds.size.y, localBounds.size.z);
        float sourceLength = length;
        float parentScale = Mathf.Max(
            Mathf.Abs(pivot.transform.lossyScale.x),
            Mathf.Abs(pivot.transform.lossyScale.y),
            Mathf.Abs(pivot.transform.lossyScale.z));
        float targetLocalLength = targetLength / Mathf.Max(parentScale, 0.0001f);
        if (length > 0.001f) mesh.transform.localScale = Vector3.one * (targetLocalLength / length);
        if (!TryCalculateVisualBoundsInSpace(mesh, gunRoot.transform, out localBounds, out rebuildDiagnostic))
            throw new InvalidOperationException($"{gunName} bounds failed after absolute scale assignment.\n{rebuildDiagnostic}");
        mesh.transform.localPosition -= new Vector3(localBounds.center.x, localBounds.center.y, localBounds.min.z);
        if (!TryCalculateVisualBoundsInSpace(mesh, gunRoot.transform, out localBounds, out rebuildDiagnostic))
            throw new InvalidOperationException($"{gunName} bounds failed after centering.\n{rebuildDiagnostic}");

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
        if (!TryCalculateVisualBoundsInSpace(
                worldGun, worldGun.transform, out Bounds bounds, out string diagnostic))
            throw new InvalidOperationException(
                $"{worldGun.name} grip configuration requires a real visual mesh.\n{diagnostic}");
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
        Vector3 baseLocalScale = Vector3.one;
        mesh.localScale = baseLocalScale;
        if (!TryCalculateVisualBounds(gunRoot, out Bounds before, out string beforeDiagnostic))
            throw new InvalidOperationException(
                $"{gunRoot.name} bounds failed before fit.\n{beforeDiagnostic}");
        float beforeMax = Mathf.Max(before.size.x, before.size.y, before.size.z);
        mesh.localScale = Vector3.Scale(
            baseLocalScale,
            Vector3.one * (targetWorldDimension / beforeMax));

        if (!TryCalculateVisualBoundsInSpace(
                mesh.gameObject, gunRoot.transform, out Bounds localBounds, out string localDiagnostic))
            throw new InvalidOperationException(
                $"{gunRoot.name} local bounds failed after fit.\n{localDiagnostic}");
        Transform muzzle = FindDirectOrDescendant(gunRoot.transform, muzzlePointName);
        if (muzzle != null)
            muzzle.localPosition = new Vector3(0f, 0f, localBounds.max.z + 0.01f);
        if (!TryCalculateVisualBounds(gunRoot, out Bounds after, out string afterDiagnostic))
            throw new InvalidOperationException(
                $"{gunRoot.name} bounds failed after fit.\n{afterDiagnostic}");
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
        particleObject.layer = muzzlePoint.gameObject.layer;
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
        lightObject.layer = muzzlePoint.gameObject.layer;
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

    private static Dictionary<HumanBodyBones, Transform> AuditHumanoidBones(Animator animator)
    {
        if (animator == null || !animator.isHuman)
            throw new InvalidOperationException(
                "Cyber Soldier must use a valid Humanoid avatar for Animation Rigging.");

        HumanBodyBones[] required =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Spine,
            HumanBodyBones.Chest,
            HumanBodyBones.LeftShoulder,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightHand
        };
        Dictionary<HumanBodyBones, Transform> result =
            new Dictionary<HumanBodyBones, Transform>();
        foreach (HumanBodyBones bone in required)
        {
            Transform transform = animator.GetBoneTransform(bone);
            if (transform == null)
                throw new InvalidOperationException(
                    $"Cyber Soldier Humanoid avatar has no mapped {bone} bone.");
            result.Add(bone, transform);
            Debug.Log(
                $"[SeekerPresentationSetup] Humanoid bone {bone}={GetHierarchyPath(transform)}");
        }
        return result;
    }

    private static void ConfigureWeaponRig(
        Transform seeker,
        Animator animator,
        Transform chest,
        Transform leftUpperArm,
        Transform leftLowerArm,
        Transform leftHand,
        Transform rightUpperArm,
        Transform rightLowerArm,
        Transform rightHand,
        Transform rightHandGrip,
        Transform leftHandGrip,
        out Rig weaponRig,
        out TwoBoneIKConstraint rightArmIk,
        out TwoBoneIKConstraint leftArmIk,
        out MultiAimConstraint upperBodyAim,
        out Transform aiAimTarget,
        out Transform rightHandIkTarget)
    {
        GameObject aimTargetObject = EnsureChild(seeker, "SeekerAIAimTarget");
        aiAimTarget = aimTargetObject.transform;
        aiAimTarget.SetPositionAndRotation(
            seeker.position + seeker.forward * 12f + Vector3.up * 1.35f,
            seeker.rotation);
        aiAimTarget.localScale = Vector3.one;

        RigBuilder rigBuilder = GetOrAddUniqueComponent<RigBuilder>(animator.gameObject);
        GameObject rigObject = EnsureChild(animator.transform, "WeaponRig");
        rigObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        rigObject.transform.localScale = Vector3.one;
        weaponRig = GetOrAddUniqueComponent<Rig>(rigObject);
        weaponRig.weight = 1f;

        GameObject targetsObject = EnsureChild(rigObject.transform, "WeaponRigTargets");
        targetsObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        targetsObject.transform.localScale = Vector3.one;

        Transform rightTarget = EnsureChild(
            targetsObject.transform, "RightHandIKTarget").transform;
        rightHandIkTarget = rightTarget;
        Vector3 gripSeparation = leftHandGrip.position - rightHandGrip.position;
        float leftArmReach =
            Vector3.Distance(leftUpperArm.position, leftLowerArm.position) +
            Vector3.Distance(leftLowerArm.position, leftHand.position);
        Vector3 desiredLeftGrip =
            leftUpperArm.position +
            seeker.forward * (leftArmReach * 0.58f) +
            seeker.right * (leftArmReach * 0.28f) -
            seeker.up * (leftArmReach * 0.25f);
        rightTarget.SetPositionAndRotation(
            desiredLeftGrip - gripSeparation,
            rightHand.rotation);
        rightTarget.localScale = Vector3.one;

        Transform rightHint = EnsureChild(
            targetsObject.transform, "RightElbowHint").transform;
        rightHint.SetPositionAndRotation(
            rightUpperArm.position + seeker.right * 0.32f +
            Vector3.down * 0.25f + seeker.forward * 0.03f,
            rightUpperArm.rotation);
        rightHint.localScale = Vector3.one;

        Transform leftHint = EnsureChild(
            targetsObject.transform, "LeftElbowHint").transform;
        leftHint.SetPositionAndRotation(
            leftUpperArm.position - seeker.right * 0.32f +
            Vector3.down * 0.25f + seeker.forward * 0.03f,
            leftUpperArm.rotation);
        leftHint.localScale = Vector3.one;

        GameObject aimObject = EnsureChild(rigObject.transform, "UpperBodyAim");
        upperBodyAim = GetOrAddUniqueComponent<MultiAimConstraint>(aimObject);
        MultiAimConstraintData aimData = upperBodyAim.data;
        aimData.constrainedObject = chest;
        WeightedTransformArray aimSources = new WeightedTransformArray(0);
        aimSources.Add(new WeightedTransform(aiAimTarget, 1f));
        aimData.sourceObjects = aimSources;
        aimData.maintainOffset = true;
        aimData.offset = Vector3.zero;
        aimData.limits = new Vector2(-35f, 35f);
        aimData.aimAxis = SelectDominantAxis(
            chest.InverseTransformDirection(seeker.forward));
        aimData.upAxis = SelectDominantAxis(
            chest.InverseTransformDirection(Vector3.up));
        aimData.worldUpType = MultiAimConstraintData.WorldUpType.SceneUp;
        aimData.worldUpAxis = MultiAimConstraintData.Axis.Y;
        aimData.worldUpObject = null;
        aimData.constrainedXAxis = true;
        aimData.constrainedYAxis = true;
        aimData.constrainedZAxis = false;
        upperBodyAim.data = aimData;
        upperBodyAim.weight = 0.15f;

        GameObject rightIkObject = EnsureChild(rigObject.transform, "RightArmIK");
        rightArmIk = GetOrAddUniqueComponent<TwoBoneIKConstraint>(rightIkObject);
        ConfigureTwoBoneIk(
            rightArmIk,
            rightUpperArm,
            rightLowerArm,
            rightHand,
            rightTarget,
            rightHint);

        GameObject leftIkObject = EnsureChild(rigObject.transform, "LeftArmIK");
        leftArmIk = GetOrAddUniqueComponent<TwoBoneIKConstraint>(leftIkObject);
        ConfigureTwoBoneIk(
            leftArmIk,
            leftUpperArm,
            leftLowerArm,
            leftHand,
            leftHandGrip,
            leftHint);

        aimObject.transform.SetSiblingIndex(0);
        rightIkObject.transform.SetSiblingIndex(1);
        leftIkObject.transform.SetSiblingIndex(2);
        targetsObject.transform.SetSiblingIndex(3);
        rigBuilder.layers.Clear();
        rigBuilder.layers.Add(new RigLayer(weaponRig, true));
        EditorUtility.SetDirty(rigBuilder);
        EditorUtility.SetDirty(weaponRig);
        EditorUtility.SetDirty(upperBodyAim);
        EditorUtility.SetDirty(rightArmIk);
        EditorUtility.SetDirty(leftArmIk);
    }

    private static void ConfigureTwoBoneIk(
        TwoBoneIKConstraint constraint,
        Transform root,
        Transform mid,
        Transform tip,
        Transform target,
        Transform hint)
    {
        TwoBoneIKConstraintData data = constraint.data;
        data.root = root;
        data.mid = mid;
        data.tip = tip;
        data.target = target;
        data.hint = hint;
        data.targetPositionWeight = 1f;
        data.targetRotationWeight = 1f;
        data.hintWeight = 1f;
        data.maintainTargetPositionOffset = false;
        data.maintainTargetRotationOffset = false;
        constraint.data = data;
        constraint.weight = 1f;
    }

    private static MultiAimConstraintData.Axis SelectDominantAxis(Vector3 direction)
    {
        direction.Normalize();
        float x = Mathf.Abs(direction.x);
        float y = Mathf.Abs(direction.y);
        float z = Mathf.Abs(direction.z);
        if (x >= y && x >= z)
            return direction.x >= 0f
                ? MultiAimConstraintData.Axis.X
                : MultiAimConstraintData.Axis.X_NEG;
        if (y >= x && y >= z)
            return direction.y >= 0f
                ? MultiAimConstraintData.Axis.Y
                : MultiAimConstraintData.Axis.Y_NEG;
        return direction.z >= 0f
            ? MultiAimConstraintData.Axis.Z
            : MultiAimConstraintData.Axis.Z_NEG;
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
            if (!(item is SeekerWeaponGripController) &&
                !(item is RigBuilder) &&
                !(item is Rig) &&
                !(item is TwoBoneIKConstraint) &&
                !(item is MultiAimConstraint))
                Object.DestroyImmediate(item);
    }

    private static Bounds CalculateWorldBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    public static bool TryCalculateVisualBounds(
        GameObject visualRoot,
        out Bounds bounds,
        out string diagnostic)
    {
        bounds = visualRoot != null
            ? new Bounds(visualRoot.transform.position, Vector3.zero)
            : default;
        if (visualRoot == null)
        {
            diagnostic = "Visual root: <null>";
            return false;
        }

        MeshRenderer[] meshRenderers = visualRoot.GetComponentsInChildren<MeshRenderer>(true);
        SkinnedMeshRenderer[] skinnedRenderers =
            visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        MeshFilter[] meshFilters = visualRoot.GetComponentsInChildren<MeshFilter>(true);
        StringBuilder details = new StringBuilder();
        details.AppendLine($"Root path: {GetHierarchyPath(visualRoot.transform)}");
        details.AppendLine($"ActiveSelf: {visualRoot.activeSelf}");
        details.AppendLine($"ActiveInHierarchy: {visualRoot.activeInHierarchy}");
        details.AppendLine($"LocalScale: {visualRoot.transform.localScale:F6}");
        details.AppendLine($"LossyScale: {visualRoot.transform.lossyScale:F6}");
        details.AppendLine($"Child count: {visualRoot.transform.childCount}");
        details.AppendLine($"MeshRenderers: {meshRenderers.Length}");
        details.AppendLine($"SkinnedMeshRenderers: {skinnedRenderers.Length}");
        details.AppendLine($"MeshFilters: {meshFilters.Length}");
        details.AppendLine($"Source prefab: {SourceGunAssetPath}");

        bool initialized = false;
        foreach (MeshRenderer renderer in meshRenderers)
        {
            if (renderer == null || IsVisualHelper(renderer.transform, visualRoot.transform))
                continue;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            details.AppendLine(
                $"MeshRenderer path={GetHierarchyPath(renderer.transform)}, enabled={renderer.enabled}, " +
                $"mesh={DescribeMesh(mesh)}, bounds={renderer.bounds.size:F6}");
            if (mesh == null || mesh.vertexCount <= 0) continue;
            if (IsValidBounds(renderer.bounds))
            {
                Encapsulate(ref bounds, renderer.bounds, ref initialized);
            }
            else
            {
                Bounds fallback = TransformBounds(filter.transform, mesh.bounds);
                if (IsValidBounds(fallback))
                    Encapsulate(ref bounds, fallback, ref initialized);
            }
        }

        foreach (SkinnedMeshRenderer renderer in skinnedRenderers)
        {
            if (renderer == null || IsVisualHelper(renderer.transform, visualRoot.transform))
                continue;
            Mesh mesh = renderer.sharedMesh;
            details.AppendLine(
                $"SkinnedMeshRenderer path={GetHierarchyPath(renderer.transform)}, enabled={renderer.enabled}, " +
                $"mesh={DescribeMesh(mesh)}, worldBounds={renderer.bounds.size:F6}, " +
                $"localBounds={renderer.localBounds.size:F6}");
            if (mesh == null || mesh.vertexCount <= 0) continue;
            Bounds candidate = IsValidBounds(renderer.bounds)
                ? renderer.bounds
                : TransformBounds(renderer.transform, renderer.localBounds);
            if (IsValidBounds(candidate))
                Encapsulate(ref bounds, candidate, ref initialized);
        }

        // A prefab instance can have a valid MeshFilter while Editor renderer
        // bounds are not initialized yet. Use mesh-local bounds without waiting a frame.
        foreach (MeshFilter filter in meshFilters)
        {
            if (filter == null || IsVisualHelper(filter.transform, visualRoot.transform))
                continue;
            Mesh mesh = filter.sharedMesh;
            MeshRenderer pairedRenderer = filter.GetComponent<MeshRenderer>();
            details.AppendLine(
                $"MeshFilter path={GetHierarchyPath(filter.transform)}, mesh={DescribeMesh(mesh)}, " +
                $"renderer={(pairedRenderer != null ? pairedRenderer.enabled.ToString() : "<missing>")}");
            if (mesh == null || mesh.vertexCount <= 0) continue;
            Bounds fallback = TransformBounds(filter.transform, mesh.bounds);
            if (IsValidBounds(fallback))
                Encapsulate(ref bounds, fallback, ref initialized);
        }

        diagnostic = details.ToString();
        return initialized && IsValidBounds(bounds);
    }

    private static bool TryCalculateVisualBoundsInSpace(
        GameObject visualRoot,
        Transform space,
        out Bounds bounds,
        out string diagnostic)
    {
        bounds = default;
        if (visualRoot == null || space == null)
        {
            diagnostic =
                $"Visual root: {(visualRoot != null ? GetHierarchyPath(visualRoot.transform) : "<null>")}\n" +
                $"Measurement space: {(space != null ? GetHierarchyPath(space) : "<null>")}";
            return false;
        }

        bool initialized = false;
        Bounds result = default;
        MeshFilter[] filters = visualRoot.GetComponentsInChildren<MeshFilter>(true);
        SkinnedMeshRenderer[] skinnedRenderers =
            visualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        StringBuilder details = new StringBuilder();
        details.AppendLine($"Root path: {GetHierarchyPath(visualRoot.transform)}");
        details.AppendLine($"Measurement space: {GetHierarchyPath(space)}");
        details.AppendLine($"MeshFilters: {filters.Length}");
        details.AppendLine($"SkinnedMeshRenderers: {skinnedRenderers.Length}");

        foreach (MeshFilter filter in filters)
        {
            if (filter == null || IsVisualHelper(filter.transform, visualRoot.transform))
                continue;
            Mesh mesh = filter.sharedMesh;
            details.AppendLine(
                $"MeshFilter path={GetHierarchyPath(filter.transform)}, mesh={DescribeMesh(mesh)}");
            if (mesh == null || mesh.vertexCount <= 0) continue;
            EncapsulateTransformedBounds(
                mesh.bounds, filter.transform, space, ref result, ref initialized);
        }

        foreach (SkinnedMeshRenderer renderer in skinnedRenderers)
        {
            if (renderer == null || IsVisualHelper(renderer.transform, visualRoot.transform))
                continue;
            Mesh mesh = renderer.sharedMesh;
            details.AppendLine(
                $"SkinnedMeshRenderer path={GetHierarchyPath(renderer.transform)}, " +
                $"mesh={DescribeMesh(mesh)}, localBounds={renderer.localBounds.size:F6}");
            if (mesh == null || mesh.vertexCount <= 0) continue;
            Bounds sourceBounds = IsValidBounds(renderer.localBounds)
                ? renderer.localBounds
                : mesh.bounds;
            EncapsulateTransformedBounds(
                sourceBounds, renderer.transform, space, ref result, ref initialized);
        }

        bounds = result;
        diagnostic = details.ToString();
        return initialized && IsValidBounds(bounds);
    }

    private static void EncapsulateTransformedBounds(
        Bounds sourceBounds,
        Transform source,
        Transform destination,
        ref Bounds aggregate,
        ref bool initialized)
    {
        Bounds result = aggregate;
        bool hasBounds = initialized;
        ForEachBoundsCorner(sourceBounds, corner =>
        {
            Vector3 point = destination.InverseTransformPoint(source.TransformPoint(corner));
            if (!hasBounds)
            {
                result = new Bounds(point, Vector3.zero);
                hasBounds = true;
            }
            else
            {
                result.Encapsulate(point);
            }
        });
        aggregate = result;
        initialized = hasBounds;
    }

    private static Bounds TransformBounds(Transform source, Bounds localBounds)
    {
        Bounds world = default;
        bool initialized = false;
        ForEachBoundsCorner(localBounds, corner =>
        {
            Vector3 point = source.TransformPoint(corner);
            if (!initialized)
            {
                world = new Bounds(point, Vector3.zero);
                initialized = true;
            }
            else
            {
                world.Encapsulate(point);
            }
        });
        return world;
    }

    private static void ForEachBoundsCorner(Bounds source, Action<Vector3> visitor)
    {
        Vector3 min = source.min;
        Vector3 max = source.max;
        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
            visitor(new Vector3(
                x == 0 ? min.x : max.x,
                y == 0 ? min.y : max.y,
                z == 0 ? min.z : max.z));
    }

    private static void Encapsulate(
        ref Bounds aggregate,
        Bounds candidate,
        ref bool initialized)
    {
        if (!initialized)
        {
            aggregate = candidate;
            initialized = true;
        }
        else
        {
            aggregate.Encapsulate(candidate);
        }
    }

    private static bool IsVisualHelper(Transform item, Transform root)
    {
        while (item != null && item != root)
        {
            string lower = item.name.ToLowerInvariant();
            if (lower.Contains("muzzle") || lower.Contains("grip") ||
                lower.Contains("helper") || lower.Contains("gizmo") ||
                lower.Contains("particle") || lower.Contains("vfx"))
                return true;
            item = item.parent;
        }
        return false;
    }

    private static bool HasRealVisualMesh(GameObject root)
    {
        if (root == null) return false;
        return root.GetComponentsInChildren<MeshFilter>(true)
                   .Any(filter => filter.sharedMesh != null &&
                                  filter.sharedMesh.vertexCount > 0) ||
               root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                   .Any(renderer => renderer.sharedMesh != null &&
                                    renderer.sharedMesh.vertexCount > 0);
    }

    private static bool IsValidBounds(Bounds value)
    {
        return IsFinite(value.center) && IsFinite(value.size) &&
               Mathf.Max(Mathf.Abs(value.size.x), Mathf.Abs(value.size.y),
                   Mathf.Abs(value.size.z)) > VisualBoundsEpsilon;
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string DescribeMesh(Mesh mesh)
    {
        if (mesh == null) return "<missing>";
        return $"{mesh.name} vertices={mesh.vertexCount} asset={AssetDatabase.GetAssetPath(mesh)}";
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
