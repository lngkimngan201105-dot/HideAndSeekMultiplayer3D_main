using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class SeekerPresentationValidationTool
{
    private const string SmokeRunningKey = "PropHunt.SeekerPresentationSmokeRunning";
    private const string SmokeCommandLineKey = "PropHunt.SeekerPresentationSmokeCommandLine";
    private const string SmokeResultKey = "PropHunt.SeekerPresentationSmokeResult";
    private static readonly List<string> SmokeFailures = new List<string>();
    private static GameObject smokeBlocker;
    private static Material smokeBlockerMaterial;
    private static PropHuntTestRoleSelector smokeSelector;
    private static SeekerRaycastWeapon smokeWeapon;
    private static SeekerWeaponEnergy smokeEnergy;
    private static SeekerWeaponPresentation smokePresentation;
    private static SeekerWeaponEnergyBarController smokeEnergyHud;
    private static Vector3 expectedImpactPosition;
    private const int ImpactProofWidth = 800;
    private const int ImpactProofHeight = 450;
    private const string ImpactProofPath = "SeekerImpactGameViewProof.png";
    private static Color32[] impactProofBeforePixels;
    private static Ray impactProofRay;
    private static float impactProofCaptureAt;
    private static float impactProofParticleTime;
    private static Vector3 impactProofCameraPosition;
    private static Quaternion impactProofCameraRotation;
    private static int stressEnergyEvents;
    private static int stressReloadStateEvents;
    private static bool footstepReceiverWarningSeen;
    private static float impactDrainDeadline;
    private static float impactManualAdvanceAt;
    private static bool impactManualAdvanceApplied;

    static SeekerPresentationValidationTool()
    {
        if (SessionState.GetBool(SmokeRunningKey, false))
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        }
    }

    [MenuItem("Tools/Prop Hunt/Validate Seeker Presentation + Energy")]
    public static void ValidateStatic()
    {
        Scene scene = EditorSceneManager.OpenScene(SeekerPresentationSetupTool.ScenePath, OpenSceneMode.Single);
        List<string> failures = new List<string>();
        int worldLayer = LayerMask.NameToLayer(SeekerPresentationSetupTool.WorldLayerName);
        int fpsLayer = LayerMask.NameToLayer(SeekerPresentationSetupTool.FpsLayerName);

        GameObject seeker = FindNamed(scene, "SeekerPlayer");
        GameObject cyber = FindNamed(scene, "CyberSoldierModel");
        GameObject cyberVisual = FindNamed(scene, "CyberSoldierVisual");
        GameObject industrial = FindNamed(scene, "IndustrialSeekerModel");
        GameObject fpsPivot = FindNamed(scene, "SeekerFPSGunPivot");
        GameObject fpsGun = FindNamed(scene, "SciFiGunLight_FPS");
        GameObject worldPivot = FindNamed(scene, "SeekerWorldGunPivot");
        GameObject worldGun = FindNamed(scene, "SciFiGunLight_World");
        GameObject muzzlePoint = FindNamed(scene, "MuzzlePoint");
        GameObject worldMuzzlePoint = FindNamed(scene, "MuzzlePoint_World");
        GameObject rightHandGrip = FindNamed(scene, "RightHandGrip");
        GameObject leftHandGrip = FindNamed(scene, "LeftHandGrip");
        GameObject weaponRigObject = FindNamed(scene, "WeaponRig");
        GameObject rightArmIkObject = FindNamed(scene, "RightArmIK");
        GameObject leftArmIkObject = FindNamed(scene, "LeftArmIK");
        GameObject upperBodyAimObject = FindNamed(scene, "UpperBodyAim");
        GameObject leftElbowHint = FindNamed(scene, "LeftElbowHint");
        GameObject rightElbowHint = FindNamed(scene, "RightElbowHint");
        GameObject rightHandIkTarget = FindNamed(scene, "RightHandIKTarget");
        GameObject aiAimTarget = FindNamed(scene, "SeekerAIAimTarget");
        GameObject pulseFallback = FindNamed(scene, "PulseTaggerVisual");
        GameObject energyBar = FindNamed(scene, "SeekerWeaponEnergyBar");
        GameObject energyText = FindNamed(scene, "EnergyText");

        Require(worldLayer == 11, $"SeekerWorldVisual must remain layer 11, got {worldLayer}.", failures);
        Require(fpsLayer >= 8, "SeekerFPSVisual layer is missing.", failures);
        Require(CountNamed(scene, "CyberSoldierModel") == 1, "CyberSoldierModel is missing or duplicated.", failures);
        Require(CountNamed(scene, "SeekerFPSGunPivot") == 1, "SeekerFPSGunPivot is missing or duplicated.", failures);
        Require(CountNamed(scene, "SciFiGunLight_FPS") == 1, "SciFiGunLight_FPS is missing or duplicated.", failures);
        Require(CountNamed(scene, "SeekerWorldGunPivot") == 1, "SeekerWorldGunPivot is missing or duplicated.", failures);
        Require(CountNamed(scene, "SciFiGunLight_World") == 1, "SciFiGunLight_World is missing or duplicated.", failures);
        Require(CountNamed(scene, "SeekerWeaponEnergyBar") == 1, "SeekerWeaponEnergyBar is missing or duplicated.", failures);
        Require(CountNamed(scene, "MuzzlePoint") == 1, "FPS MuzzlePoint is missing or duplicated.", failures);
        Require(CountNamed(scene, "MuzzlePoint_World") == 1, "World MuzzlePoint_World is missing or duplicated.", failures);
        Require(CountNamed(scene, "RightHandGrip") == 1 && CountNamed(scene, "LeftHandGrip") == 1,
            "World gun grip markers are missing or duplicated.", failures);
        Require(CountNamed(scene, "WeaponRig") == 1 &&
                CountNamed(scene, "RightArmIK") == 1 &&
                CountNamed(scene, "LeftArmIK") == 1 &&
                CountNamed(scene, "UpperBodyAim") == 1 &&
                CountNamed(scene, "LeftElbowHint") == 1 &&
                CountNamed(scene, "RightElbowHint") == 1 &&
                CountNamed(scene, "RightHandIKTarget") == 1 &&
                CountNamed(scene, "SeekerAIAimTarget") == 1,
            "Weapon rig, constraint, target or hint objects are missing/duplicated.", failures);
        Require(cyber != null && cyber.activeSelf, "CyberSoldierModel must be active.", failures);
        CyberSoldierAnimationEventReceiver[] footstepReceivers =
            cyber != null ? cyber.GetComponentsInChildren<CyberSoldierAnimationEventReceiver>(true) : Array.Empty<CyberSoldierAnimationEventReceiver>();
        CyberSoldierAnimationEventReceiver[] sceneFootstepReceivers = Object.FindObjectsOfType<CyberSoldierAnimationEventReceiver>(true);
        MethodInfo footstepMethod = typeof(CyberSoldierAnimationEventReceiver).GetMethod(
            "OnFootstep", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(AnimationEvent) }, null);
        Require(cyberVisual != null && footstepReceivers.Length == 1 && sceneFootstepReceivers.Length == 1 &&
                footstepReceivers[0].gameObject == cyberVisual && footstepMethod != null,
            "CyberSoldierVisual must own exactly one public OnFootstep(AnimationEvent) receiver.", failures);
        if (footstepReceivers.Length == 1)
        {
            SerializedObject receiverState = new SerializedObject(footstepReceivers[0]);
            Require(!receiverState.FindProperty("enableOptionalFootstepAudio").boolValue &&
                    receiverState.FindProperty("optionalFootstepAudioSource").objectReferenceValue == null &&
                    receiverState.FindProperty("optionalFootstepClip").objectReferenceValue == null,
                "Optional footstep audio must remain disabled and unassigned.", failures);
        }
        Require(industrial != null && !industrial.activeSelf, "IndustrialSeekerModel fallback must remain inactive.", failures);
        Require(pulseFallback != null && !pulseFallback.activeSelf, "PulseTaggerVisual fallback must remain inactive.", failures);
        Require(cyber != null && AllLayersEqual(cyber, worldLayer), "Cyber Soldier hierarchy is not entirely SeekerWorldVisual.", failures);
        Require(fpsPivot != null && AllLayersEqual(fpsPivot, fpsLayer), "FPS gun hierarchy is not entirely SeekerFPSVisual.", failures);
        Require(worldPivot != null && AllLayersEqual(worldPivot, worldLayer), "World gun hierarchy is not entirely SeekerWorldVisual.", failures);
        Require(cyber != null && cyber.GetComponentsInChildren<Collider>(true).Length == 0 &&
                cyber.GetComponentsInChildren<Rigidbody>(true).Length == 0 &&
                cyber.GetComponentsInChildren<Camera>(true).Length == 0 &&
                cyber.GetComponentsInChildren<AudioListener>(true).Length == 0,
            "Cyber Soldier visual contains forbidden gameplay/physics/camera components.", failures);
        Require(fpsGun != null && worldGun != null &&
                fpsGun.GetComponentsInChildren<Collider>(true).Length == 0 &&
                fpsGun.GetComponentsInChildren<Rigidbody>(true).Length == 0 &&
                worldGun.GetComponentsInChildren<Collider>(true).Length == 0 &&
                worldGun.GetComponentsInChildren<Rigidbody>(true).Length == 0,
            "A gun visual contains a collider or Rigidbody.", failures);

        Animator animator = cyber != null ? cyber.GetComponentInChildren<Animator>(true) : null;
        Transform rightHand = animator != null && animator.isHuman
            ? animator.GetBoneTransform(HumanBodyBones.RightHand)
            : null;
        Transform leftHand = animator != null && animator.isHuman
            ? animator.GetBoneTransform(HumanBodyBones.LeftHand)
            : null;
        Transform chest = animator != null && animator.isHuman
            ? animator.GetBoneTransform(HumanBodyBones.Chest)
            : null;
        Transform leftUpperArm = animator != null && animator.isHuman
            ? animator.GetBoneTransform(HumanBodyBones.LeftUpperArm)
            : null;
        Transform leftLowerArm = animator != null && animator.isHuman
            ? animator.GetBoneTransform(HumanBodyBones.LeftLowerArm)
            : null;
        Transform rightUpperArm = animator != null && animator.isHuman
            ? animator.GetBoneTransform(HumanBodyBones.RightUpperArm)
            : null;
        Transform rightLowerArm = animator != null && animator.isHuman
            ? animator.GetBoneTransform(HumanBodyBones.RightLowerArm)
            : null;
        Require(animator != null && animator.isHuman && !animator.applyRootMotion &&
                animator.cullingMode == AnimatorCullingMode.AlwaysAnimate,
            "Cyber Soldier must use Humanoid, no Root Motion and Always Animate.", failures);
        Require(rightHand != null && worldPivot != null && worldPivot.transform.parent == rightHand,
            "World gun is not parented to Humanoid RightHand.", failures);
        Require(leftHand != null, "Humanoid LeftHand was not resolved.", failures);
        Require(rightHand != null && rightHandGrip != null && Vector3.Distance(rightHand.position, rightHandGrip.transform.position) <= 0.01f,
            "RightHandGrip is not aligned to Humanoid RightHand within 1 cm.", failures);
        Require(rightHandGrip != null && rightHandGrip.transform.parent == worldGun?.transform &&
                leftHandGrip != null && leftHandGrip.transform.parent == worldGun?.transform &&
                worldMuzzlePoint != null && worldMuzzlePoint.transform.parent == worldGun?.transform,
            "World grip/muzzle markers must be directly under SciFiGunLight_World.", failures);
        SeekerWeaponGripController gripController = animator != null
            ? animator.GetComponent<SeekerWeaponGripController>()
            : null;
        AnimatorController animatorController = animator != null
            ? animator.runtimeAnimatorController as AnimatorController
            : null;
        RigBuilder rigBuilder = animator != null ? animator.GetComponent<RigBuilder>() : null;
        Rig weaponRig = weaponRigObject != null ? weaponRigObject.GetComponent<Rig>() : null;
        TwoBoneIKConstraint rightArmIk =
            rightArmIkObject != null ? rightArmIkObject.GetComponent<TwoBoneIKConstraint>() : null;
        TwoBoneIKConstraint leftArmIk =
            leftArmIkObject != null ? leftArmIkObject.GetComponent<TwoBoneIKConstraint>() : null;
        MultiAimConstraint upperBodyAim =
            upperBodyAimObject != null ? upperBodyAimObject.GetComponent<MultiAimConstraint>() : null;
        Require(gripController != null && gripController.Animator == animator &&
                gripController.MovementSource == seeker?.GetComponent<CharacterController>() &&
                gripController.RightHand == rightHand && gripController.LeftHand == leftHand &&
                gripController.Chest == chest &&
                gripController.WorldGunPivot == worldPivot?.transform &&
                gripController.RightHandGrip == rightHandGrip?.transform &&
                gripController.LeftHandGrip == leftHandGrip?.transform &&
                gripController.RightHandIkTarget == rightHandIkTarget?.transform &&
                gripController.ManualAlignmentVersion == 1 &&
                gripController.WeaponRig == weaponRig &&
                gripController.RightArmIk == rightArmIk &&
                gripController.LeftArmIk == leftArmIk &&
                gripController.UpperBodyAim == upperBodyAim &&
                gripController.AIAimTarget == aiAimTarget?.transform &&
                gripController.LeftHandIkEnabled,
            "SeekerWeaponGripController manual-alignment/Animation-Rigging references are incomplete.", failures);
        Require(rigBuilder != null && rigBuilder.layers.Count == 1 &&
                rigBuilder.layers[0].active && rigBuilder.layers[0].rig == weaponRig,
            "Cyber Animator must own one active RigBuilder layer referencing WeaponRig.", failures);
        if (rightArmIk != null)
        {
            TwoBoneIKConstraintData data = rightArmIk.data;
            Require(data.root == rightUpperArm && data.mid == rightLowerArm &&
                    data.tip == rightHand && data.target == rightHandIkTarget?.transform &&
                    data.hint == rightElbowHint?.transform &&
                    Approximately(data.targetPositionWeight, 1f) &&
                    Approximately(data.targetRotationWeight, 1f) &&
                    Approximately(data.hintWeight, 1f) &&
                    !data.maintainTargetPositionOffset &&
                    !data.maintainTargetRotationOffset,
                "RightArmIK bone chain, target, hint or absolute weights are invalid.", failures);
        }
        if (leftArmIk != null)
        {
            TwoBoneIKConstraintData data = leftArmIk.data;
            Require(data.root == leftUpperArm && data.mid == leftLowerArm &&
                    data.tip == leftHand && data.target == leftHandGrip?.transform &&
                    data.hint == leftElbowHint?.transform &&
                    Approximately(data.targetPositionWeight, 1f) &&
                    Approximately(data.targetRotationWeight, 1f) &&
                    Approximately(data.hintWeight, 1f) &&
                    !data.maintainTargetPositionOffset &&
                    !data.maintainTargetRotationOffset,
                "LeftArmIK bone chain, LeftHandGrip target, hint or absolute weights are invalid.", failures);
        }
        if (upperBodyAim != null)
        {
            MultiAimConstraintData data = upperBodyAim.data;
            Require(data.constrainedObject == chest &&
                    data.sourceObjects.Count == 1 &&
                    data.sourceObjects[0].transform == aiAimTarget?.transform &&
                    Approximately(data.sourceObjects[0].weight, 1f) &&
                    data.maintainOffset &&
                    data.worldUpType == MultiAimConstraintData.WorldUpType.SceneUp &&
                    data.constrainedXAxis && data.constrainedYAxis &&
                    !data.constrainedZAxis,
                "UpperBodyAim chest/source/axis configuration is invalid.", failures);
        }
        Require(animatorController != null &&
                AssetDatabase.GetAssetPath(animatorController) == SeekerPresentationSetupTool.SeekerHoldControllerPath &&
                animatorController.layers.Length > 0 && animatorController.layers[0].iKPass,
            "Cyber Animator must use the integration-owned hold controller with IK Pass enabled.", failures);
        if (animatorController != null)
        {
            foreach (string clipName in new[] { "Walk_N", "Run_N" })
            {
                AnimationClip clip = animatorController.animationClips.FirstOrDefault(candidate => candidate.name == clipName);
                Require(clip != null && AnimationUtility.GetAnimationEvents(clip)
                        .Any(animationEvent => animationEvent.functionName == "OnFootstep"),
                    $"{clipName} no longer exposes its publisher OnFootstep AnimationEvent.", failures);
            }
        }

        Bounds worldGunBounds = default;
        string worldGunDiagnostic = "World gun root is missing.";
        bool hasRobustWorldBounds = worldGun != null &&
                                    SeekerPresentationSetupTool.TryCalculateVisualBounds(
                                        worldGun, out worldGunBounds,
                                        out worldGunDiagnostic);
        Require(hasRobustWorldBounds,
            "World gun has no robust measurable visual bounds.\n" + worldGunDiagnostic, failures);
        Mesh[] worldMeshes = worldGun != null
            ? worldGun.GetComponentsInChildren<MeshFilter>(true)
                .Select(filter => filter.sharedMesh)
                .Concat(worldGun.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Select(renderer => renderer.sharedMesh))
                .Where(mesh => mesh != null)
                .ToArray()
            : Array.Empty<Mesh>();
        Require(worldMeshes.Any(mesh => mesh.vertexCount > 0),
            "World gun has no real mesh with vertexCount > 0.", failures);
        float worldGunMaxDimension = Mathf.Max(worldGunBounds.size.x, worldGunBounds.size.y, worldGunBounds.size.z);
        Require(worldGunMaxDimension >= 0.80f && worldGunMaxDimension <= 1.00f,
            $"World gun max dimension must be 0.80-1.00 m, got {worldGunMaxDimension:F3} m.", failures);
        float muzzleForwardProjection = worldMuzzlePoint != null && worldGun != null
            ? Vector3.Dot(
                worldMuzzlePoint.transform.position - worldGunBounds.center,
                worldGun.transform.forward)
            : float.NegativeInfinity;
        Require(muzzleForwardProjection >= worldGunMaxDimension * 0.35f,
            $"MuzzlePoint_World is not at the barrel tip (forward projection={muzzleForwardProjection:F3} m).",
            failures);
        Require(seeker != null && worldGunBounds.min.y >= seeker.transform.position.y - 0.05f,
            $"World gun clips below Seeker ground: minY={worldGunBounds.min.y:F3}.", failures);
        Require(fpsGun != null && worldGun != null && fpsGun.transform != worldGun.transform &&
                fpsGun.transform.parent != worldGun.transform.parent,
            "FPS and world guns must remain distinct transforms.", failures);
        Bounds fpsGunBounds = CalculateWorldBounds(fpsGun);
        float fpsGunMaxDimension = Mathf.Max(fpsGunBounds.size.x, fpsGunBounds.size.y, fpsGunBounds.size.z);
        Require(fpsGunMaxDimension >= 0.55f && fpsGunMaxDimension <= 0.75f,
            $"FPS gun max dimension must remain readable without dominating the camera, got {fpsGunMaxDimension:F3} m.", failures);
        Require(muzzlePoint != null && muzzlePoint.transform.parent == fpsGun?.transform,
            "FPS MuzzlePoint must be directly under SciFiGunLight_FPS.", failures);
        ParticleSystem muzzle = muzzlePoint != null ? muzzlePoint.GetComponentInChildren<ParticleSystem>(true) : null;
        Light muzzleLight = muzzlePoint != null ? muzzlePoint.GetComponentInChildren<Light>(true) : null;
        Require(muzzle != null && !muzzle.main.playOnAwake && !muzzle.main.loop,
            "Muzzle flash must exist and be non-looping with Play On Awake disabled.", failures);
        Require(muzzleLight != null && muzzleLight.type == LightType.Point && !muzzleLight.enabled,
            "Muzzle Point Light is missing or enabled at edit time.", failures);

        SeekerWeaponEnergy[] energies = Object.FindObjectsOfType<SeekerWeaponEnergy>(true);
        SeekerWeaponPresentation[] presentations = Object.FindObjectsOfType<SeekerWeaponPresentation>(true);
        SeekerRaycastWeapon weapon = Object.FindObjectOfType<SeekerRaycastWeapon>(true);
        PropHuntTestRoleSelector selector = Object.FindObjectOfType<PropHuntTestRoleSelector>(true);
        Require(energies.Length == 1, $"Expected one SeekerWeaponEnergy, found {energies.Length}.", failures);
        Require(presentations.Length == 1, $"Expected one SeekerWeaponPresentation, found {presentations.Length}.", failures);
        SeekerWeaponEnergy energy = energies.FirstOrDefault();
        SeekerWeaponPresentation presentation = presentations.FirstOrDefault();
        Require(energy != null && energy.transform == seeker?.transform && energy.MaxCharges == 5 &&
                energy.CurrentCharges == 5 && Approximately(energy.ReloadDuration, 1.8f) &&
                energy.State == SeekerWeaponEnergyState.Ready && !energy.HasActiveReload,
            "Energy must be on SeekerPlayer and configured as 5/5, 1.8 seconds.", failures);
        string[] cappedInventoryFields = { "totalAmmo", "reserveAmmo", "maximumReloads", "maxReloads", "maximumShots", "finiteBattery" };
        HashSet<string> energyFieldNames = typeof(SeekerWeaponEnergy)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(field => field.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(cappedInventoryFields.All(name => !energyFieldNames.Contains(name)),
            "Seeker energy contains a finite total-ammo/reload-cap field.", failures);
        Require(weapon != null && weapon.WeaponEnergy == energy && weapon.WeaponPresentation == presentation,
            "SeekerRaycastWeapon energy/presentation references are missing.", failures);
        Require(weapon != null && weapon.Damage == 20 && Approximately(weapon.Range, 50f) && Approximately(weapon.Cooldown, 0.35f),
            "Weapon gameplay constants changed from 20 damage / 50 m / 0.35 s.", failures);
        Require(selector != null && selector.SeekerWeaponEnergy == energy,
            "Role selector does not own the independent Seeker energy state.", failures);
        Require(presentation != null && presentation.AudioSource != null &&
                !presentation.AudioSource.playOnAwake && !presentation.AudioSource.loop &&
                Approximately(presentation.AudioSource.spatialBlend, 0f),
            "Presentation AudioSource configuration is invalid.", failures);

        string shotPath = presentation != null ? AssetDatabase.GetAssetPath(presentation.ShotAudioClip) : string.Empty;
        string reloadPath = presentation != null ? AssetDatabase.GetAssetPath(presentation.ReloadAudioClip) : string.Empty;
        string impactPath = presentation != null ? AssetDatabase.GetAssetPath(presentation.ImpactPrefab) : string.Empty;
        Require(shotPath.EndsWith("light_blast_3.wav", StringComparison.OrdinalIgnoreCase) &&
                presentation.ShotAudioClip != null && Approximately(presentation.ShotAudioClip.length, 0.390f, 0.01f),
            $"Unexpected shot clip: {shotPath}", failures);
        Require(reloadPath.EndsWith("reloading_012.wav", StringComparison.OrdinalIgnoreCase) &&
                presentation.ReloadAudioClip != null && Approximately(presentation.ReloadAudioClip.length, 1.603f, 0.01f),
            $"Unexpected reload clip: {reloadPath}", failures);
        Require(impactPath == SeekerPresentationSetupTool.ImpactPrefabPath && presentation.ImpactPoolSize == 8,
            $"Unexpected impact prefab/pool configuration: {impactPath}", failures);
        Require(presentation?.ImpactPrefab != null &&
                presentation.ImpactPrefab.GetComponentsInChildren<Collider>(true).Length == 0 &&
                presentation.ImpactPrefab.GetComponentsInChildren<Rigidbody>(true).Length == 0,
            "Impact02 source must remain visual-only.", failures);
        ParticleSystem[] impactParticles = presentation?.ImpactPrefab != null
            ? presentation.ImpactPrefab.GetComponentsInChildren<ParticleSystem>(true)
            : Array.Empty<ParticleSystem>();
        ParticleSystemRenderer impactRenderer = impactParticles.Length == 1
            ? impactParticles[0].GetComponent<ParticleSystemRenderer>()
            : null;
        Require(impactParticles.Length == 1 && impactRenderer != null &&
                presentation.ImpactPrefab.GetComponentsInChildren<SpriteRenderer>(true).Length == 0 &&
                presentation.ImpactPrefab.GetComponentsInChildren<Animator>(true).Length == 0 &&
                impactRenderer.renderMode == ParticleSystemRenderMode.Billboard &&
                impactRenderer.sharedMaterial != null && impactRenderer.sharedMaterial.shader != null &&
                impactRenderer.sharedMaterial.shader.isSupported,
            "Impact02 must contain exactly one supported billboard ParticleSystem.", failures);

        SeekerWeaponEnergyBarController energyHud = energyBar != null
            ? energyBar.GetComponent<SeekerWeaponEnergyBarController>()
            : null;
        SeekerHealthBarController healthHud = FindNamed(scene, "SeekerHealthBar")?.GetComponent<SeekerHealthBarController>();
        Image[] segmentFills = energyHud != null ? energyHud.SegmentFills : Array.Empty<Image>();
        Require(energyHud != null && energyHud.Energy == energy && segmentFills.Length == 5 &&
                segmentFills.All(fill => fill != null) && segmentFills.Distinct().Count() == 5,
            "Energy HUD must reference five distinct segment fill Images.", failures);
        for (int index = 0; index < 5; index++)
        {
            GameObject segment = FindDirectChild(energyBar != null ? energyBar.transform : null, $"EnergySegment_{index + 1}");
            Image fill = FindDirectChild(segment != null ? segment.transform : null, "Fill")?.GetComponent<Image>();
            Require(segment != null && CountNamed(scene, $"EnergySegment_{index + 1}") == 1 &&
                    fill == segmentFills.ElementAtOrDefault(index) && fill.type == Image.Type.Filled &&
                    fill.fillMethod == Image.FillMethod.Horizontal &&
                    ApproximatelyColor(fill.color, new Color32(0, 229, 255, 255)),
                $"Energy segment {index + 1} is missing, duplicated, miswired or not cyan horizontal Filled UI.", failures);
        }
        Require(healthHud != null && energyHud != null &&
                segmentFills.All(fill => fill != healthHud.HealthFill) &&
                healthHud.HealthSource != null && !ReferenceEquals(healthHud.HealthSource, energyHud.Energy),
            "Health and energy HUD sources/fills are not independent.", failures);
        TextMeshProUGUI energyStatus = energyText != null ? energyText.GetComponent<TextMeshProUGUI>() : null;
        Require(energyStatus != null && energyHud != null && energyHud.InactiveFallbackText == energyStatus &&
                string.IsNullOrEmpty(energyStatus.text) && !energyStatus.gameObject.activeSelf &&
                energyBar.GetComponentsInChildren<TextMeshProUGUI>(false).Length == 0,
            "Energy HUD must contain no active text, numbers, percentage, /5 count or reload hint.", failures);
        string hudSource = File.ReadAllText("Assets/Scripts/PropHunt/SeekerWeaponEnergyBarController.cs");
        Require(!Regex.IsMatch(hudSource, @"%|/5|press\s*R|năng\s*lượng", RegexOptions.IgnoreCase),
            "Energy HUD source still contains percentage, label, /5 count, or reload hint output.", failures);
        Require(!Regex.IsMatch(hudSource, @"NĂNG|LƯỢNG|/5|nhấn\s*R|press\s*R", RegexOptions.IgnoreCase),
            "Energy HUD source still contains a label, /5 count, or reload hint.", failures);
        string energySource = File.ReadAllText("Assets/Scripts/PropHunt/SeekerWeaponEnergy.cs");
        Require(!Regex.IsMatch(energySource, @"AudioSource\s*\.\s*isPlaying|shotAudioClip\s*\.\s*length|reloadAudioClip\s*\.\s*length|StartCoroutine|StopCoroutine"),
            "Reload state/timer is still coupled to audio playback or a coroutine handle.", failures);

        Camera seekerCamera = selector != null ? selector.SeekerCamera : null;
        Require(seekerCamera != null && MaskContains(seekerCamera.cullingMask, fpsLayer) &&
                !MaskContains(seekerCamera.cullingMask, worldLayer),
            "Seeker camera mask must include FPS gun and exclude world body/gun.", failures);
        Vector3 fpsCenterInCamera = seekerCamera != null
            ? seekerCamera.transform.InverseTransformPoint(fpsGunBounds.center)
            : Vector3.zero;
        Require(seekerCamera != null && fpsCenterInCamera.z > seekerCamera.nearClipPlane + 0.1f && fpsCenterInCamera.z < 2f,
            $"FPS gun center is clipped or implausibly far from camera (camera-local z={fpsCenterInCamera.z:F3}).", failures);
        Require(presentation != null && presentation.ImpactCamera == seekerCamera && presentation.ImpactLayer == 0 &&
                MaskContains(seekerCamera != null ? seekerCamera.cullingMask : 0, presentation != null ? presentation.ImpactLayer : -1) &&
                Approximately(presentation.BaseImpactScale, 0.192f) &&
                Approximately(presentation.ImpactScale, 0.384f),
            "Impact02 camera, visible Default layer, or scale configuration is invalid.", failures);
        foreach (Camera camera in Object.FindObjectsOfType<Camera>(true))
        {
            if (camera == seekerCamera) continue;
            Require(!MaskContains(camera.cullingMask, fpsLayer), $"{camera.name} can render SeekerFPSVisual.", failures);
            string lower = camera.name.ToLowerInvariant();
            if (lower.Contains("hider") || lower.Contains("ghost") || lower.Contains("spectator"))
                Require(MaskContains(camera.cullingMask, worldLayer) && MaskContains(camera.cullingMask, 0),
                    $"{camera.name} cannot render SeekerWorldVisual or Default-layer Impact02.", failures);
        }

        ValidateShaders(cyber, failures);
        ValidateShaders(fpsGun, failures);
        ValidateShaders(worldGun, failures);
        if (presentation?.ImpactPrefab != null) ValidateShaders(presentation.ImpactPrefab, failures);
        // Camera.Render on Unity's NullGfxDevice can crash natively for the
        // package's 8192px particle texture. The same proof remains enabled in
        // an interactive Editor where a real graphics device exists.
        if (presentation?.ImpactPrefab != null && !Application.isBatchMode)
            ValidateIsolatedImpactRender(presentation.ImpactPrefab, failures);
        Require(seeker != null && seeker.GetComponentsInChildren<AudioListener>(true).Length == 1,
            "Seeker hierarchy should retain exactly its existing camera AudioListener.", failures);

        if (failures.Count > 0)
            throw new InvalidOperationException("Seeker presentation static validation failed:\n- " + string.Join("\n- ", failures));

        Debug.Log(
            "[SeekerPresentationValidation] STATIC PASS — OnFootstep receiver, five cyan HUD segments, " +
            "0.8-1.0 m world gun/two-hand IK, 20/50/0.35 gameplay, uncapped reload and pooled Impact02 verified.");
    }

    [MenuItem("Tools/Prop Hunt/Run Seeker Presentation Play Mode Smoke")]
    public static void RunPlayModeSmoke()
    {
        StartPlayModeSmoke(false);
    }

    public static void RunCommandLineVerification()
    {
        try
        {
            SeekerPresentationSetupTool.SetupTwiceAndValidate();
            StartPlayModeSmoke(true);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
    }

    private static void StartPlayModeSmoke(bool commandLine)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Unity is already entering or running Play Mode.");
        SessionState.SetBool(SmokeRunningKey, true);
        SessionState.SetBool(SmokeCommandLineKey, commandLine);
        SessionState.EraseString(SmokeResultKey);
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    private static void HandlePlayModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(SmokeRunningKey, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            EditorApplication.delayCall += ExecuteSmokeStart;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            string result = SessionState.GetString(SmokeResultKey, string.Empty);
            bool commandLine = SessionState.GetBool(SmokeCommandLineKey, false);
            SessionState.EraseBool(SmokeRunningKey);
            SessionState.EraseBool(SmokeCommandLineKey);
            SessionState.EraseString(SmokeResultKey);
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
            if (string.IsNullOrEmpty(result))
                Debug.Log("[SeekerPresentationValidation] PLAY MODE PASS — 50 cycles / 250 shots / 50 reloads; " +
                          "energyEvents=300, reloadStateEvents=100; " +
                          "early reloads, reload spam, single-player Hider ownership, round reset, disable recovery, IK and live Impact02 verified.");
            else
                Debug.LogError("[SeekerPresentationValidation] PLAY MODE FAIL\n" + result);
            if (commandLine && Application.isBatchMode) EditorApplication.Exit(string.IsNullOrEmpty(result) ? 0 : 1);
        }
    }

    private static void ExecuteSmokeStart()
    {
        SmokeFailures.Clear();
        try
        {
            smokeSelector = Object.FindObjectOfType<PropHuntTestRoleSelector>(true);
            smokeWeapon = Object.FindObjectOfType<SeekerRaycastWeapon>(true);
            smokeEnergy = Object.FindObjectOfType<SeekerWeaponEnergy>(true);
            smokePresentation = Object.FindObjectOfType<SeekerWeaponPresentation>(true);
            smokeEnergyHud = Object.FindObjectOfType<SeekerWeaponEnergyBarController>(true);
            PropHuntRoundManager round =
                Object.FindObjectOfType<PropHuntRoundManager>(true);
            Require(smokeSelector != null && smokeWeapon != null && smokeEnergy != null && smokePresentation != null && smokeEnergyHud != null,
                "Required Seeker presentation components are missing in Play Mode.", SmokeFailures);
            Require(round != null, "PropHuntRoundManager is missing in Play Mode.", SmokeFailures);
            if (SmokeFailures.Count > 0) { FinishSmoke(); return; }

            round.BeginHunting();
            Require(smokeSelector.SinglePlayerHiderMode &&
                    smokeSelector.CurrentControlledRole == PropHuntTestRole.Hider &&
                    (smokeSelector.RoleSelectionPanel == null ||
                     !smokeSelector.RoleSelectionPanel.activeSelf) &&
                    smokeWeapon.IsWeaponActive,
                "Single-player Hider ownership or AI weapon activation is invalid.",
                SmokeFailures);
            smokeEnergy.ResetForRound();
            Require(smokeEnergyHud.SegmentFills.Length == 5 && smokeEnergyHud.SegmentFills.All(fill => fill != null) &&
                    smokeEnergyHud.SegmentFills.Distinct().Count() == 5 &&
                    smokeEnergyHud.GetComponentsInChildren<TextMeshProUGUI>(false).Length == 0,
                "Runtime energy HUD is not five independent text-free segments.", SmokeFailures);
            RequireEnergySegments(5f, "full energy");

            CyberSoldierAnimationEventReceiver receiver = Object.FindObjectOfType<CyberSoldierAnimationEventReceiver>(true);
            Animator cyberAnimator = receiver != null ? receiver.GetComponent<Animator>() : null;
            Require(receiver != null && cyberAnimator != null,
                "Runtime CyberSoldierVisual receiver or Animator is missing.", SmokeFailures);
            if (receiver != null && cyberAnimator != null)
            {
                footstepReceiverWarningSeen = false;
                Application.logMessageReceived -= CaptureFootstepWarning;
                Application.logMessageReceived += CaptureFootstepWarning;
                int receivedBefore = receiver.ReceivedFootstepCount;
                cyberAnimator.Play("Base Layer.Idle Walk Run Blend", 0, 0f);
                cyberAnimator.SetFloat("Speed", 2f);
                cyberAnimator.Update(1.25f);
                cyberAnimator.SetFloat("Speed", 6f);
                cyberAnimator.Update(1.25f);
                receiver.OnFootstep(new AnimationEvent());
                Require(receiver.ReceivedFootstepCount == receivedBefore + 1 && !footstepReceiverWarningSeen,
                    $"Walk_N/Run_N footstep events were not received cleanly: received={receiver.ReceivedFootstepCount - receivedBefore}, warning={footstepReceiverWarningSeen}.",
                    SmokeFailures);
                Application.logMessageReceived -= CaptureFootstepWarning;
            }

            for (int shot = 0; shot < 5; shot++) smokeEnergy.TryConsumeShot();
            RequireEnergySegments(0f, "empty energy");
            Require(smokeEnergy.TryStartReloadFromAI(), "Could not start smooth five-segment reload validation.", SmokeFailures);
            for (int step = 1; step <= 10; step++)
            {
                smokeEnergy.AdvanceReloadForValidation(smokeEnergy.ReloadDuration / 10f);
                RequireEnergySegments(step * 0.5f, $"reload step {step}/10");
            }
            smokeEnergy.ResetForRound();
            smokeEnergy.TryConsumeShot();
            smokeEnergy.TryConsumeShot();
            Require(smokeEnergy.TryStartReloadFromAI(), "Could not start partial-charge reload validation.", SmokeFailures);
            smokeEnergy.AdvanceReloadForValidation(smokeEnergy.ReloadDuration * 0.45f);
            RequireEnergySegments(3.9f, "reload from 3/5 at 45%");
            smokeEnergy.AdvanceReloadForValidation(smokeEnergy.ReloadDuration);
            RequireEnergySegments(5f, "completed partial-charge reload");
            smokeEnergy.ResetForRound();

            Camera camera = smokeSelector.SeekerCamera;
            Vector3 unobstructedProofForward = (camera.transform.forward + camera.transform.right * 0.35f).normalized;
            camera.transform.rotation = Quaternion.LookRotation(unobstructedProofForward, Vector3.up);
            smokeBlocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            smokeBlocker.name = "SeekerEnergyStressWorldBlocker";
            smokeBlocker.transform.position = camera.transform.position + camera.transform.forward * 1.5f;
            smokeBlocker.transform.localScale = new Vector3(1.2f, 1.2f, 0.15f);
            Shader proofShader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            smokeBlockerMaterial = new Material(proofShader) { name = "ImpactProofDarkTarget_Runtime" };
            smokeBlockerMaterial.color = new Color(0.005f, 0.012f, 0.018f, 1f);
            smokeBlocker.GetComponent<Renderer>().sharedMaterial = smokeBlockerMaterial;
            Physics.SyncTransforms();
            Ray ray = new Ray(camera.transform.position, camera.transform.forward);
            Collider blockerCollider = smokeBlocker.GetComponent<Collider>();
            Require(blockerCollider.Raycast(ray, out RaycastHit expectedHit, 50f),
                "Stress blocker was not hit by the Seeker camera ray.", SmokeFailures);
            expectedImpactPosition = expectedHit.point + expectedHit.normal * 0.02f;
            impactProofCameraPosition = camera.transform.position;
            impactProofCameraRotation = camera.transform.rotation;
            impactProofBeforePixels = Application.isBatchMode
                ? null
                : CaptureCameraPixels(camera, null);

            smokeEnergy.ResetForRound();
            smokePresentation.SetImpactDebugLoggingForValidation(true);
            Require(smokeWeapon.TryFireRayFromAI(ray, true), "Impact render-proof shot was rejected.", SmokeFailures);
            impactProofRay = ray;
            if (Application.isBatchMode)
            {
                // NullGfxDevice cannot safely render the package's 8192px sheet.
                // Runtime spawn, layer, hit point, pooling and gameplay checks
                // continue below without invoking Camera.Render.
                impactProofCaptureAt = Time.realtimeSinceStartup + 2f;
                EditorApplication.update -= WaitForRigBeforeStress;
                EditorApplication.update += WaitForRigBeforeStress;
                return;
            }
            // Batch-mode wall time and particle simulation time do not advance 1:1.
            // Use this as a safety deadline; WaitForImpactRenderProof samples the
            // actual ParticleSystem time at the sheet's highest-alpha phase.
            impactProofCaptureAt = Time.realtimeSinceStartup + 2f;
            EditorApplication.update -= WaitForImpactRenderProof;
            EditorApplication.update += WaitForImpactRenderProof;
        }
        catch (Exception exception)
        {
            SmokeFailures.Add(exception.ToString());
            FinishSmoke();
        }
    }

    private static void WaitForRigBeforeStress()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorApplication.update -= WaitForRigBeforeStress;
            return;
        }

        SeekerWeaponGripController grip =
            Object.FindObjectOfType<SeekerWeaponGripController>(true);
        if ((grip == null || grip.RigEvaluationCount <= 0) &&
            Time.realtimeSinceStartup < impactProofCaptureAt)
            return;

        EditorApplication.update -= WaitForRigBeforeStress;
        RunStressAfterImpactProof();
    }

    private static void WaitForImpactRenderProof()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorApplication.update -= WaitForImpactRenderProof;
            return;
        }
        ParticleSystem activeImpact = smokePresentation
            .GetComponentsInChildren<ParticleSystem>(true)
            .FirstOrDefault(particle =>
                particle.gameObject.activeInHierarchy &&
                particle.name.StartsWith("Impact02_Pooled_", StringComparison.Ordinal));
        if (activeImpact != null)
        {
            impactProofParticleTime = activeImpact.time;
            // Frame 15 / 64 of the one-second sheet is the brightest energy-ring phase.
            if (impactProofParticleTime < 15f / 64f && Time.realtimeSinceStartup < impactProofCaptureAt) return;
        }
        else if (Time.realtimeSinceStartup < impactProofCaptureAt)
        {
            return;
        }

        try
        {
            EditorApplication.update -= WaitForImpactRenderProof;
            ValidateImpactRenderProof(smokeSelector.SeekerCamera);
            smokePresentation.SetImpactDebugLoggingForValidation(false);
            RunStressAfterImpactProof();
        }
        catch (Exception exception)
        {
            SmokeFailures.Add(exception.ToString());
            FinishSmoke();
        }
    }

    private static void RunStressAfterImpactProof()
    {
        smokeEnergy.ResetForRound();
        int shotStart = smokePresentation.ShotFeedbackCount;
        int impactStart = smokePresentation.ImpactFeedbackCount;
        int reloadStart = smokePresentation.ReloadFeedbackCount;
        int completedStart = smokeEnergy.CompletedReloadCount;
        stressEnergyEvents = 0;
        stressReloadStateEvents = 0;
        smokeEnergy.EnergyChanged += (_, __) => stressEnergyEvents++;
        smokeEnergy.ReloadStateChanged += _ => stressReloadStateEvents++;

        for (int cycle = 0; cycle < 50; cycle++)
        {
            for (int shot = 0; shot < 5; shot++)
            {
                Require(smokeWeapon.TryFireRayFromAI(impactProofRay, true),
                    $"Cycle {cycle + 1}, shot {shot + 1} was rejected.", SmokeFailures);
                Require(smokeEnergy.CurrentCharges == 4 - shot,
                    $"Cycle {cycle + 1}, shot {shot + 1}: expected {4 - shot}/5, got {smokeEnergy.CurrentCharges}/5.", SmokeFailures);
                RequireEnergySegments(4f - shot, $"cycle {cycle + 1}, shot {shot + 1}");
            }

            int feedbackAtEmpty = smokePresentation.ImpactFeedbackCount;
            Require(!smokeWeapon.TryFireRayFromAI(impactProofRay, true),
                $"Cycle {cycle + 1} accepted a sixth shot at 0/5.", SmokeFailures);
            Require(smokePresentation.ImpactFeedbackCount == feedbackAtEmpty,
                $"Cycle {cycle + 1} emitted an impact for an empty shot.", SmokeFailures);
            Require(smokeEnergy.TryStartReloadFromAI(), $"Cycle {cycle + 1} rejected reload.", SmokeFailures);
            for (int spam = 0; spam < 5; spam++)
                Require(!smokeEnergy.TryStartReloadFromAI(), $"Cycle {cycle + 1} reload spam restarted reload.", SmokeFailures);
            smokeEnergy.AdvanceReloadForValidation(smokeEnergy.ReloadDuration);
            Require(smokeEnergy.State == SeekerWeaponEnergyState.Ready &&
                    smokeEnergy.CurrentCharges == 5 && !smokeEnergy.HasActiveReload,
                $"Cycle {cycle + 1} did not recover to Ready 5/5.", SmokeFailures);
            RequireEnergySegments(5f, $"cycle {cycle + 1} completed reload");
        }

        Require(smokePresentation.ShotFeedbackCount == shotStart + 250,
            "Stress test did not produce exactly 250 shot feedback callbacks.", SmokeFailures);
        Require(smokePresentation.ImpactFeedbackCount == impactStart + 250,
            "Stress test did not produce exactly 250 world impacts.", SmokeFailures);
        Require(smokePresentation.ReloadFeedbackCount == reloadStart + 50 &&
                smokeEnergy.CompletedReloadCount == completedStart + 50,
            "Stress test did not complete exactly 50 independent reloads.", SmokeFailures);
        Require(stressEnergyEvents == 300 && stressReloadStateEvents == 100,
            $"Unexpected stress events: energy={stressEnergyEvents} (expected 300), reloadState={stressReloadStateEvents} (expected 100).",
            SmokeFailures);
        ParticleSystem muzzle = smokePresentation.MuzzleFlash;
        ParticleSystemRenderer muzzleRenderer =
            muzzle != null ? muzzle.GetComponent<ParticleSystemRenderer>() : null;
        Require(muzzle != null && muzzle.gameObject.activeInHierarchy &&
                muzzle.emission.enabled &&
                (muzzleRenderer == null || muzzleRenderer.enabled),
            "Muzzle particle is not active and renderable after AI shot feedback.",
            SmokeFailures);
        EditorApplication.update -= WaitForImpactValidation;
        EditorApplication.update += WaitForImpactValidation;
    }

    private static void WaitForImpactValidation()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorApplication.update -= WaitForImpactValidation;
            return;
        }
        try
        {
            EditorApplication.update -= WaitForImpactValidation;
            Require(smokePresentation.LastImpactParticleCount > 0 && smokePresentation.ActiveImpactCount > 0,
                $"Impact02 false positive: particleCount={smokePresentation.LastImpactParticleCount}, activePool={smokePresentation.ActiveImpactCount}.",
                SmokeFailures);
            ParticleSystem[] pooledImpacts = smokePresentation
                .GetComponentsInChildren<ParticleSystem>(true)
                .Where(particle => particle.name.StartsWith("Impact02_Pooled_", StringComparison.Ordinal))
                .ToArray();
            Require(pooledImpacts.Length == 8 &&
                    pooledImpacts.All(particle =>
                        Vector3.Distance(particle.transform.localScale, Vector3.one * 0.384f) <= 0.0001f) &&
                    Vector3.Distance(smokePresentation.LastImpactScale, Vector3.one * 0.384f) <= 0.0001f,
                $"Impact pool scale is not uniformly reset to 0.384 after stress reuse; " +
                $"instances={pooledImpacts.Length}, last={smokePresentation.LastImpactScale:F3}.",
                SmokeFailures);
            Require(smokePresentation.LastImpactLayer == 0 &&
                    smokePresentation.LastImpactCameraRenderedLayer &&
                    smokePresentation.LastImpactColliderPath.Contains("SeekerEnergyStressWorldBlocker") &&
                    Vector3.Distance(smokePresentation.LastImpactPosition, expectedImpactPosition) <= 0.005f,
                $"Impact02 layer/offset/collider/camera invalid: layer={smokePresentation.LastImpactLayer}, camera={smokePresentation.LastImpactCameraRenderedLayer}, collider={smokePresentation.LastImpactColliderPath}, distance={Vector3.Distance(smokePresentation.LastImpactPosition, expectedImpactPosition):F4}m.",
                SmokeFailures);

            smokeBlocker.SetActive(false);
            ValidateWorldHiderAndCloneImpactPaths();

            int invalidImpactBaseline = smokePresentation.ImpactFeedbackCount;
            smokeEnergy.ResetForRound();
            Require(smokeWeapon.TryFireRayFromAI(new Ray(Vector3.up * 10000f, Vector3.up), true),
                "A valid raycast miss was incorrectly rejected.", SmokeFailures);
            Require(smokePresentation.ImpactFeedbackCount == invalidImpactBaseline,
                "Raycast miss incorrectly spawned Impact02.", SmokeFailures);
            smokeEnergy.ResetForRound();
            Require(smokeWeapon.TryFireRayFromAI(new Ray(smokeSelector.SeekerCamera.transform.position, smokeSelector.SeekerCamera.transform.forward), true),
                "Could not prepare cooldown rejection test.", SmokeFailures);
            int cooldownImpactBaseline = smokePresentation.ImpactFeedbackCount;
            Require(!smokeWeapon.TryFireRayFromAI(new Ray(smokeSelector.SeekerCamera.transform.position, smokeSelector.SeekerCamera.transform.forward), false) &&
                    smokePresentation.ImpactFeedbackCount == cooldownImpactBaseline,
                "Cooldown-blocked shot spawned Impact02.", SmokeFailures);
            smokeEnergy.ResetForRound();
            smokeEnergy.TryConsumeShot();
            Require(smokeEnergy.TryStartReloadFromAI(), "Could not prepare reloading rejection test.", SmokeFailures);
            int reloadingImpactBaseline = smokePresentation.ImpactFeedbackCount;
            Require(!smokeWeapon.TryFireRayFromAI(new Ray(smokeSelector.SeekerCamera.transform.position, smokeSelector.SeekerCamera.transform.forward), true) &&
                    smokePresentation.ImpactFeedbackCount == reloadingImpactBaseline,
                "Shot during Reloading spawned Impact02.", SmokeFailures);
            smokeEnergy.ResetForRound();

            for (int targetCharges = 1; targetCharges <= 4; targetCharges++)
            {
                smokeEnergy.ResetForRound();
                for (int i = 0; i < 5 - targetCharges; i++)
                    Require(smokeEnergy.TryConsumeShot(), "Early reload preparation failed.", SmokeFailures);
                Require(smokeEnergy.CurrentCharges == targetCharges && smokeEnergy.TryStartReloadFromAI(),
                    $"Early reload at {targetCharges}/5 was rejected.", SmokeFailures);
                smokeEnergy.AdvanceReloadForValidation(smokeEnergy.ReloadDuration);
                Require(smokeEnergy.CurrentCharges == 5 && smokeEnergy.State == SeekerWeaponEnergyState.Ready,
                    $"Early reload at {targetCharges}/5 did not complete.", SmokeFailures);
            }
            Require(!smokeEnergy.TryStartReloadFromAI(), "Full 5/5 energy incorrectly accepted reload.", SmokeFailures);

            smokeSelector.ShowRoleSelection();
            smokeSelector.PossessSeekerForDebug();
            Require(smokeSelector.SinglePlayerHiderMode &&
                    smokeSelector.CurrentControlledRole == PropHuntTestRole.Hider &&
                    !smokeSelector.IsRoleSelectionPanelOpen &&
                    (smokeSelector.RoleSelectionPanel == null ||
                     !smokeSelector.RoleSelectionPanel.activeSelf) &&
                    !smokeEnergyHud.gameObject.activeInHierarchy,
                "Single-player mode allowed Role Panel or Seeker possession.",
                SmokeFailures);

            smokeEnergy.ResetForRound();
            smokeEnergy.TryConsumeShot();
            Require(!smokeEnergy.TryStartReload(),
                "Player reload API was accepted while the player owns Hider.",
                SmokeFailures);
            Require(smokeEnergy.TryStartReloadFromAI() && smokeEnergy.IsReloading,
                "AI reload API failed in single-player Hider mode.",
                SmokeFailures);
            smokeEnergy.AdvanceReloadForValidation(smokeEnergy.ReloadDuration);
            RequireEnergySegments(5f, "AI reload while player remains Hider");

            smokeEnergy.TryConsumeShot();
            Require(smokeEnergy.TryStartReloadFromAI(), "Could not start round-reset reload scenario.", SmokeFailures);
            smokeEnergy.ResetForRound();
            Require(smokeEnergy.CurrentCharges == 5 && smokeEnergy.State == SeekerWeaponEnergyState.Ready && !smokeEnergy.HasActiveReload,
                "Round reset did not cancel reload to Ready 5/5.", SmokeFailures);

            smokeEnergy.TryConsumeShot();
            Require(smokeEnergy.TryStartReloadFromAI(), "Could not start disable/re-enable reload scenario.", SmokeFailures);
            smokeEnergy.enabled = false;
            Require(smokeEnergy.State == SeekerWeaponEnergyState.Ready && !smokeEnergy.HasActiveReload,
                "OnDisable left energy stuck in Reloading.", SmokeFailures);
            smokeEnergy.enabled = true;
            smokeEnergy.TryConsumeShot();
            Require(smokeEnergy.TryStartReloadFromAI(),
                "AI reload did not recover after energy component re-enable.",
                SmokeFailures);
            smokeEnergy.AdvanceReloadForValidation(smokeEnergy.ReloadDuration);

            SeekerWeaponGripController grip = Object.FindObjectOfType<SeekerWeaponGripController>(true);
            RigBuilder runtimeRigBuilder = grip != null ? grip.GetComponent<RigBuilder>() : null;
            Require(grip != null && grip.RigEvaluationCount > 0 &&
                    runtimeRigBuilder != null && runtimeRigBuilder.graph.IsValid() &&
                    grip.WorldGunPivot != null && grip.WorldGunPivot.parent == grip.RightHand &&
                    Vector3.Distance(grip.RightHand.position, grip.RightHandGrip.position) <= 0.01f &&
                    Vector3.Distance(grip.LeftHand.position, grip.LeftHandGrip.position) <= 0.08f,
                "Runtime Animation Rigging binding was not active.", SmokeFailures);
            impactDrainDeadline = Time.realtimeSinceStartup + 4f;
            impactManualAdvanceAt = Time.realtimeSinceStartup + 2.7f;
            impactManualAdvanceApplied = false;
            EditorApplication.update -= WaitForImpactPoolDrain;
            EditorApplication.update += WaitForImpactPoolDrain;
        }
        catch (Exception exception)
        {
            SmokeFailures.Add(exception.ToString());
            FinishSmoke();
        }
    }

    private static void FinishSmoke()
    {
        EditorApplication.update -= WaitForImpactValidation;
        EditorApplication.update -= WaitForImpactRenderProof;
        EditorApplication.update -= WaitForRigBeforeStress;
        EditorApplication.update -= WaitForImpactPoolDrain;
        Application.logMessageReceived -= CaptureFootstepWarning;
        if (smokeBlocker != null) Object.Destroy(smokeBlocker);
        if (smokeBlockerMaterial != null) Object.Destroy(smokeBlockerMaterial);
        SessionState.SetString(SmokeResultKey, string.Join("\n", SmokeFailures));
        EditorApplication.ExitPlaymode();
    }

    private static void WaitForImpactPoolDrain()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorApplication.update -= WaitForImpactPoolDrain;
            return;
        }

        if (smokePresentation != null && smokePresentation.ActiveImpactCount == 0)
        {
            EditorApplication.update -= WaitForImpactPoolDrain;
            FinishSmoke();
            return;
        }

        if (!impactManualAdvanceApplied && Time.realtimeSinceStartup >= impactManualAdvanceAt)
        {
            impactManualAdvanceApplied = true;
            smokePresentation?.CompleteImpactParticlesForValidation();
        }

        if (Time.realtimeSinceStartup < impactDrainDeadline) return;
        SmokeFailures.Add($"Impact pool did not drain after natural lifetime; active={smokePresentation?.ActiveImpactCount ?? -1}.");
        EditorApplication.update -= WaitForImpactPoolDrain;
        FinishSmoke();
    }

    private static void CaptureFootstepWarning(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Warning && condition.IndexOf("AnimationEvent", StringComparison.OrdinalIgnoreCase) >= 0 &&
            condition.IndexOf("OnFootstep", StringComparison.OrdinalIgnoreCase) >= 0 &&
            condition.IndexOf("no receiver", StringComparison.OrdinalIgnoreCase) >= 0)
            footstepReceiverWarningSeen = true;
    }

    private static void RequireEnergySegments(float expectedTotal, string context)
    {
        if (smokeEnergyHud == null || smokeEnergyHud.SegmentFills == null || smokeEnergyHud.SegmentFills.Length != 5)
        {
            SmokeFailures.Add($"{context}: energy HUD does not have five segment fills.");
            return;
        }

        // The Seeker HUD stays hidden in single-player Hider mode. Refresh its
        // serialized segment model explicitly without activating the UI.
        smokeEnergyHud.Refresh();
        for (int index = 0; index < smokeEnergyHud.SegmentFills.Length; index++)
        {
            float expected = Mathf.Clamp01(expectedTotal - index);
            Image fill = smokeEnergyHud.SegmentFills[index];
            Require(fill != null && Approximately(fill.fillAmount, expected, 0.025f),
                $"{context}: segment {index + 1} expected {expected:F2}, got {(fill != null ? fill.fillAmount : -1f):F2}.",
                SmokeFailures);
        }
    }

    private static void ValidateImpactRenderProof(Camera camera)
    {
        if (smokePresentation.MuzzleFlash != null)
            smokePresentation.MuzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (smokePresentation.MuzzleLight != null)
            smokePresentation.MuzzleLight.enabled = false;
        Vector3 liveCameraPosition = camera.transform.position;
        Quaternion liveCameraRotation = camera.transform.rotation;
        camera.transform.SetPositionAndRotation(impactProofCameraPosition, impactProofCameraRotation);
        Texture2D afterImage = new Texture2D(ImpactProofWidth, ImpactProofHeight, TextureFormat.RGBA32, false);
        Color32[] afterPixels = CaptureCameraPixels(camera, afterImage);
        try
        {
            Vector3 viewport = camera.WorldToViewportPoint(expectedImpactPosition);
            Require(viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f,
                $"Impact proof point is outside SeekerCamera viewport: {viewport}.", SmokeFailures);

            int centerX = Mathf.Clamp(Mathf.RoundToInt(viewport.x * (ImpactProofWidth - 1)), 0, ImpactProofWidth - 1);
            int centerY = Mathf.Clamp(Mathf.RoundToInt(viewport.y * (ImpactProofHeight - 1)), 0, ImpactProofHeight - 1);
            int changedPixels = 0;
            int maxRgbDelta = 0;
            const int radius = 64;
            for (int y = Mathf.Max(0, centerY - radius); y <= Mathf.Min(ImpactProofHeight - 1, centerY + radius); y++)
            for (int x = Mathf.Max(0, centerX - radius); x <= Mathf.Min(ImpactProofWidth - 1, centerX + radius); x++)
            {
                int index = y * ImpactProofWidth + x;
                Color32 before = impactProofBeforePixels[index];
                Color32 after = afterPixels[index];
                int delta = Mathf.Abs(after.r - before.r) + Mathf.Abs(after.g - before.g) + Mathf.Abs(after.b - before.b);
                if (delta >= 24) changedPixels++;
                if (delta > maxRgbDelta) maxRgbDelta = delta;
            }

            string absoluteProofPath = Path.GetFullPath(ImpactProofPath);
            File.WriteAllBytes(absoluteProofPath, afterImage.EncodeToPNG());
            bool renderVisible = changedPixels >= 40 && maxRgbDelta >= 48;
            Require(renderVisible,
                $"SeekerCamera render proof did not visibly change around hit.point: changedPixels={changedPixels}, maxRgbDelta={maxRgbDelta}.",
                SmokeFailures);
            string proofStatus = renderVisible ? "PASS" : "FAIL";
            Debug.Log(
                $"[SeekerImpactRenderProof] {proofStatus} path={absoluteProofPath}, viewport={viewport:F3}, " +
                $"particleTime={impactProofParticleTime:F3}, changedPixels={changedPixels}, " +
                $"maxRgbDelta={maxRgbDelta}, size={ImpactProofWidth}x{ImpactProofHeight}.");
        }
        finally
        {
            camera.transform.SetPositionAndRotation(liveCameraPosition, liveCameraRotation);
            Object.Destroy(afterImage);
        }
    }

    private static void ValidateIsolatedImpactRender(GameObject impactPrefab, List<string> failures)
    {
        const int size = 512;
        GameObject cameraObject = new GameObject("Impact02_IsolatedProofCamera") { hideFlags = HideFlags.HideAndDontSave };
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 10f;
        camera.fieldOfView = 50f;
        camera.cullingMask = 1 << 31;
        cameraObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        GameObject instance = Object.Instantiate(impactPrefab);
        instance.name = "Impact02_IsolatedProof";
        instance.hideFlags = HideFlags.HideAndDontSave;
        instance.transform.SetPositionAndRotation(new Vector3(0f, 0f, 3f), Quaternion.identity);
        instance.transform.localScale = Vector3.one * 0.384f;
        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
        instance.SetActive(true);
        foreach (ParticleSystem particle in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particle.Play(true);
            particle.Simulate(0.35f, true, false, false);
            particle.Play(true);
        }

        RenderTexture renderTexture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
        Texture2D image = new Texture2D(size, size, TextureFormat.RGBA32, false);
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            camera.targetTexture = renderTexture;
            RenderTexture.active = renderTexture;
            camera.Render();
            image.ReadPixels(new Rect(0f, 0f, size, size), 0, 0, false);
            image.Apply(false, false);
            Color32[] pixels = image.GetPixels32();
            int visiblePixels = pixels.Count(pixel => pixel.r + pixel.g + pixel.b >= 30);
            int maxBrightness = pixels.Max(pixel => pixel.r + pixel.g + pixel.b);
            string path = Path.GetFullPath("SeekerImpactIsolatedProof.png");
            File.WriteAllBytes(path, image.EncodeToPNG());
            Require(visiblePixels >= 40 && maxBrightness >= 90,
                $"Isolated Impact02 did not render visibly: pixels={visiblePixels}, maxBrightness={maxBrightness}.", failures);
            Debug.Log($"[SeekerImpactIsolatedProof] pixels={visiblePixels}, maxBrightness={maxBrightness}, path={path}.");
        }
        finally
        {
            RenderTexture.active = previousActive;
            renderTexture.Release();
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(renderTexture);
            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(cameraObject);
        }
    }

    private static void ValidateWorldHiderAndCloneImpactPaths()
    {
        Camera camera = smokeSelector.SeekerCamera;
        Vector3 livePosition = camera.transform.position;
        Quaternion liveRotation = camera.transform.rotation;
        camera.transform.SetPositionAndRotation(impactProofCameraPosition, impactProofCameraRotation);
        Vector3 cameraPosition = impactProofCameraPosition;
        Vector3 forward = impactProofCameraRotation * Vector3.forward;
        int failureCountBefore = SmokeFailures.Count;

        GameObject floor = CreateImpactTarget("ImpactProof_Floor", PrimitiveType.Cube,
            cameraPosition + forward * 0.8f + Vector3.down * 0.6f, new Vector3(0.8f, 0.15f, 0.8f));
        ValidateTemporaryImpactTarget(floor, SeekerShotResult.World, "floor");

        GameObject crate = CreateImpactTarget("ImpactProof_Crate", PrimitiveType.Cube,
            cameraPosition + forward * 0.9f, new Vector3(0.45f, 0.45f, 0.45f));
        ValidateTemporaryImpactTarget(crate, SeekerShotResult.World, "crate");

        GameObject barrel = CreateImpactTarget("ImpactProof_Barrel", PrimitiveType.Cylinder,
            cameraPosition + forward * 0.9f, new Vector3(0.35f, 0.55f, 0.35f));
        ValidateTemporaryImpactTarget(barrel, SeekerShotResult.World, "barrel");

        GameObject hider = CreateImpactTarget("ImpactProof_Hider", PrimitiveType.Capsule,
            cameraPosition + forward * 0.9f, new Vector3(0.4f, 0.6f, 0.4f));
        HiderHealth hiderHealth = hider.AddComponent<HiderHealth>();
        int hiderHealthBefore = hiderHealth.CurrentHealth;
        ValidateTemporaryImpactTarget(hider, SeekerShotResult.Hider, "Hider");
        Require(hiderHealth.CurrentHealth == hiderHealthBefore - 20 && hiderHealth.LastDamageSource == HiderDamageSource.SeekerWeapon,
            $"Hider proof expected exactly 20 damage, got {hiderHealthBefore - hiderHealth.CurrentHealth}.", SmokeFailures);

        GameObject cloneOwnerObject = new GameObject("ImpactProof_CloneOwner");
        HiderRevealController reveal = cloneOwnerObject.AddComponent<HiderRevealController>();
        HiderCloneAbility cloneOwner = cloneOwnerObject.AddComponent<HiderCloneAbility>();
        cloneOwner.Configure(null, null, reveal, null);
        GameObject clone = CreateImpactTarget("ImpactProof_Clone", PrimitiveType.Cube,
            cameraPosition + forward * 0.9f, new Vector3(0.45f, 0.45f, 0.45f));
        Collider cloneCollider = clone.GetComponent<Collider>();
        HiderCloneInstance cloneInstance = clone.AddComponent<HiderCloneInstance>();
        cloneInstance.Initialize(cloneOwner, clone, cloneCollider, false, Vector3.zero);
        ValidateTemporaryImpactTarget(clone, SeekerShotResult.Clone, "Clone", false);
        Require(cloneInstance.HasBeenHit && !cloneCollider.enabled && reveal.IsRevealed &&
                Approximately(reveal.RevealTimeRemaining, 5f, 0.05f) && hiderHealth.CurrentHealth == hiderHealthBefore - 20,
            $"Clone proof invalid: hit={cloneInstance.HasBeenHit}, collider={cloneCollider.enabled}, reveal={reveal.IsRevealed}, " +
            $"remaining={reveal.RevealTimeRemaining:F3}, hiderHealth={hiderHealth.CurrentHealth}.", SmokeFailures);
        string categoryStatus = SmokeFailures.Count == failureCountBefore ? "PASS" : "FAIL";
        Debug.Log($"[SeekerImpactCategoryProof] {categoryStatus} — wall, floor, crate, barrel, Hider 20 damage, Clone hit/reveal 5s and invalid-shot guards verified.");

        hider.SetActive(false);
        Object.Destroy(hider);
        Object.Destroy(cloneOwnerObject);
        camera.transform.SetPositionAndRotation(livePosition, liveRotation);
    }

    private static GameObject CreateImpactTarget(string name, PrimitiveType primitiveType, Vector3 position, Vector3 scale)
    {
        GameObject target = GameObject.CreatePrimitive(primitiveType);
        target.name = name;
        target.layer = 0;
        target.transform.position = position;
        target.transform.localScale = scale;
        Physics.SyncTransforms();
        return target;
    }

    private static void ValidateTemporaryImpactTarget(
        GameObject target,
        SeekerShotResult expectedResult,
        string label,
        bool destroyAfter = true)
    {
        Camera camera = smokeSelector.SeekerCamera;
        Collider collider = target.GetComponent<Collider>();
        Ray ray = new Ray(camera.transform.position, (collider.bounds.center - camera.transform.position).normalized);
        Require(collider.Raycast(ray, out RaycastHit expectedHit, 50f),
            $"Could not raycast the temporary {label} proof target.", SmokeFailures);
        int impactBefore = smokePresentation.ImpactFeedbackCount;
        smokeEnergy.ResetForRound();
        Require(smokeWeapon.TryFireRayFromAI(ray, true) && smokeWeapon.LastShotResult == expectedResult,
            $"{label} proof did not resolve as {expectedResult}; result={smokeWeapon.LastShotResult}.", SmokeFailures);
        Vector3 expectedPosition = expectedHit.point + expectedHit.normal * 0.02f;
        Require(smokePresentation.ImpactFeedbackCount == impactBefore + 1 &&
                smokePresentation.LastImpactColliderPath.Contains(target.name) &&
                Vector3.Distance(smokePresentation.LastImpactPosition, expectedPosition) <= 0.005f,
            $"{label} impact did not spawn at its collider hit.point; collider={smokePresentation.LastImpactColliderPath}, " +
            $"distance={Vector3.Distance(smokePresentation.LastImpactPosition, expectedPosition):F4}m.", SmokeFailures);
        target.SetActive(false);
        if (destroyAfter) Object.Destroy(target);
    }

    private static Color32[] CaptureCameraPixels(Camera camera, Texture2D destination)
    {
        RenderTexture renderTexture = new RenderTexture(ImpactProofWidth, ImpactProofHeight, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        Texture2D image = destination ?? new Texture2D(ImpactProofWidth, ImpactProofHeight, TextureFormat.RGBA32, false);
        try
        {
            camera.targetTexture = renderTexture;
            RenderTexture.active = renderTexture;
            camera.Render();
            image.ReadPixels(new Rect(0f, 0f, ImpactProofWidth, ImpactProofHeight), 0, 0, false);
            image.Apply(false, false);
            return image.GetPixels32();
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            renderTexture.Release();
            Object.Destroy(renderTexture);
            if (destination == null) Object.Destroy(image);
        }
    }

    private static void ValidateShaders(GameObject root, List<string> failures)
    {
        if (root == null) return;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        foreach (Material material in renderer.sharedMaterials)
        {
            Require(material != null && material.shader != null &&
                    material.shader.name != "Hidden/InternalErrorShader" && material.shader.isSupported,
                $"Missing, pink/error or unsupported shader on {renderer.name}.", failures);
        }
    }

    private static GameObject FindNamed(Scene scene, string name)
    {
        return EnumerateScene(scene).FirstOrDefault(item => item.name == name)?.gameObject;
    }

    private static GameObject FindDirectChild(Transform parent, string name)
    {
        if (parent == null) return null;
        for (int index = 0; index < parent.childCount; index++)
        {
            Transform child = parent.GetChild(index);
            if (child.name == name) return child.gameObject;
        }
        return null;
    }

    private static int CountNamed(Scene scene, string name)
    {
        return EnumerateScene(scene).Count(item => item.name == name);
    }

    private static IEnumerable<Transform> EnumerateScene(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            yield return transform;
    }

    private static bool AllLayersEqual(GameObject root, int layer)
    {
        return root != null && root.GetComponentsInChildren<Transform>(true).All(item => item.gameObject.layer == layer);
    }

    private static bool MaskContains(int mask, int layer)
    {
        return layer >= 0 && (mask & (1 << layer)) != 0;
    }

    private static bool Approximately(float left, float right, float tolerance = 0.001f)
    {
        return Mathf.Abs(left - right) <= tolerance;
    }

    private static bool ApproximatelyColor(Color left, Color right, float tolerance = 0.002f)
    {
        return Approximately(left.r, right.r, tolerance) &&
               Approximately(left.g, right.g, tolerance) &&
               Approximately(left.b, right.b, tolerance) &&
               Approximately(left.a, right.a, tolerance);
    }

    private static Bounds CalculateWorldBounds(GameObject root)
    {
        Renderer[] renderers = root != null
            ? root.GetComponentsInChildren<Renderer>(true).Where(renderer => !(renderer is ParticleSystemRenderer)).ToArray()
            : Array.Empty<Renderer>();
        if (renderers.Length == 0) return new Bounds(root != null ? root.transform.position : Vector3.zero, Vector3.zero);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void Require(bool condition, string message, List<string> failures)
    {
        if (!condition) failures.Add(message);
    }
}
