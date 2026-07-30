using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StarterAssets;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public static class HiderCompleteHUDSetupTool
{
    private const string MapV2Path = "Assets/Scenes/Map_v2.unity";
    private const string HudAssetFolder = "Assets/UI/HiderHUD";
    private const string CloneSpritePath = HudAssetFolder + "/SpeedBoost.png";
    private const string AntiCampSpritePath = HudAssetFolder + "/AntiCamp.png";
    private const string RandomPropSpritePath = HudAssetFolder + "/RandomProp.png";
    private const string BevelSpritePath = HudAssetFolder + "/BeveledPanel.png";
    private const string GeneratedFontPath = HudAssetFolder + "/HiderVietnameseDynamic.asset";
    private const string ZoneDomeMeshPath = HudAssetFolder + "/PropHuntZoneDomeMesh.asset";
    private const string ZoneDomeMaterialPath = HudAssetFolder + "/PropHuntZoneDome.mat";
    private const string SeekerWorldMaterialPath = HudAssetFolder + "/SeekerWorldVisual.mat";
    private const string SeekerOperatorArmsMaterialPath = HudAssetFolder + "/SeekerOperatorArms.mat";
    private const string SeekerOperatorLegsMaterialPath = HudAssetFolder + "/SeekerOperatorLegs.mat";
    private const string PulseTaggerBodyMaterialPath = HudAssetFolder + "/PulseTaggerBody.mat";
    private const string PulseTaggerAccentMaterialPath = HudAssetFolder + "/PulseTaggerAccent.mat";
    private const string SeekerModelPrefabPath =
        "Assets/StarterAssets/ThirdPersonController/Prefabs/PlayerArmature.prefab";
    private const string ZoneDomeShaderPath = "Assets/Shaders/PropHuntZoneDome.shader";

    private static readonly Color Border = Hex("151515");
    private static readonly Color TextColor = Color.white;
    private static readonly Color SeekerColor = Hex("D8A62D");
    private static readonly Color SeekerHighlight = Hex("F2C64E");
    private static readonly Color SeekerShadow = Hex("9B6819");
    private static readonly Color TimerColor = Hex("D8D8D8");
    private static readonly Color TimerHighlight = Hex("F2F2F2");
    private static readonly Color TimerShadow = Hex("787878");
    private static readonly Color HiderColor = Hex("25A9B3");
    private static readonly Color HiderHighlight = Hex("56CDD2");
    private static readonly Color HiderShadow = Hex("13717B");
    private static readonly Color CloneCoverColor = Hex("51449B");
    private static readonly Color RandomCoverColor = Hex("279A98");

    private static TMP_FontAsset _fontAsset;
    private static Sprite _bevelSprite;
    private static bool _fontMaterialWarningLogged;

    [MenuItem("Tools/Prop Hunt/Setup Hider Complete HUD")]
    public static void SetupHiderCompleteHud()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("HiderCompleteHUDSetupTool: exit Play Mode before running HUD setup.");
            return;
        }

        EnsureTmpEssentialResources();

        if (!OpenMapV2())
        {
            return;
        }

        EnsureAssetFolder();
        ConfigureAbilitySpriteImport(CloneSpritePath);
        ConfigureAbilitySpriteImport(AntiCampSpritePath);
        ConfigureAbilitySpriteImport(RandomPropSpritePath);
        _bevelSprite = EnsureBevelSprite();
        _fontAsset = FindVietnameseFontAsset();
        if (_fontAsset == null)
        {
            _fontAsset = CreateDynamicVietnameseFontAsset();
        }

        if (_fontAsset == null)
        {
            _fontAsset = GetDefaultFontAssetSafely();
        }

        PropTransformSystem hider = FindHiderInActiveScene();
        if (hider == null)
        {
            Debug.LogError("HiderCompleteHUDSetupTool: no Hider PlayerCapsule with PropTransformSystem was found.");
            return;
        }

        ConfigureHiderJumpAndStartupAudio(hider);

        PropHuntRoundManager roundManager = GetOrCreateRoundManager();
        roundManager.ConfigureDurations(30f, 180f);
        HiderRosterManager rosterManager = GetOrAddComponent<HiderRosterManager>(roundManager.gameObject);
        PropTransformSystem[] players = UnityEngine.Object.FindObjectsOfType<PropTransformSystem>(true);
        ConfigureHiderGameplay(
            hider,
            roundManager,
            out HiderAbilityController abilityController,
            out HiderAntiCampSystem antiCampSystem,
            out HiderHealth hiderHealth
        );
        SetupZoneSystem(
            roundManager,
            players,
            hider,
            out PropHuntShrinkingZone shrinkingZone,
            out HiderZoneStatusController localZoneStatus,
            out HiderPlayableAreaBounds playableArea,
            out PropHuntZoneAnchor[] zoneAnchors
        );
        SetupEliminationLifecycle(
            players,
            hider,
            roundManager,
            rosterManager,
            out HiderEliminationController localEliminationController,
            out HiderSpectatorController localSpectatorController,
            out HiderEliminationController[] hiderControllers
        );
        rosterManager.Configure(roundManager, hiderControllers);
        SetupRoleTestingSystem(
            hider,
            roundManager,
            hiderHealth,
            localEliminationController,
            abilityController,
            out PropHuntTestRoleSelector roleSelector,
            out SeekerFirstPersonController seekerController,
            out SeekerRaycastWeapon seekerWeapon,
            out SeekerHealth seekerHealth,
            out Camera seekerCamera
        );
        CreateOrUpdateHud(
            hider,
            roundManager,
            abilityController,
            antiCampSystem,
            hiderHealth,
            rosterManager,
            localEliminationController,
            localSpectatorController,
            roleSelector,
            seekerHealth,
            shrinkingZone,
            localZoneStatus
        );

        roundManager.ConfigureLocalParticipants(players);

        GameObject configuredCanvas = SceneManager.GetActiveScene().GetRootGameObjects()
            .FirstOrDefault(root => root.name == "PropHuntHUDCanvas");
        if (configuredCanvas != null)
        {
            RectTransform configuredRect = configuredCanvas.GetComponent<RectTransform>();
            configuredRect.anchorMin = Vector2.zero;
            configuredRect.anchorMax = Vector2.one;
            configuredRect.offsetMin = Vector2.zero;
            configuredRect.offsetMax = Vector2.zero;
            configuredRect.localScale = Vector3.one;
            EditorUtility.SetDirty(configuredRect);
        }

        EditorUtility.SetDirty(roundManager);
        EditorUtility.SetDirty(rosterManager);
        EditorUtility.SetDirty(hider);
        EditorUtility.SetDirty(abilityController);
        EditorUtility.SetDirty(antiCampSystem);
        EditorUtility.SetDirty(hiderHealth);
        EditorUtility.SetDirty(shrinkingZone);
        EditorUtility.SetDirty(playableArea);
        EditorUtility.SetDirty(localZoneStatus);
        EditorUtility.SetDirty(localEliminationController);
        EditorUtility.SetDirty(localSpectatorController);
        EditorUtility.SetDirty(roleSelector);
        EditorUtility.SetDirty(seekerController);
        EditorUtility.SetDirty(seekerWeapon);
        EditorUtility.SetDirty(seekerHealth);
        EditorUtility.SetDirty(seekerCamera);
        foreach (HiderEliminationController controller in hiderControllers)
        {
            EditorUtility.SetDirty(controller);
        }
        foreach (PropHuntZoneAnchor anchor in zoneAnchors)
        {
            EditorUtility.SetDirty(anchor);
        }
        EditorUtility.SetDirty(hider.GetComponent<HiderCloneAbility>());
        EditorUtility.SetDirty(hider.GetComponent<HiderRevealController>());
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), MapV2Path);
        AssetDatabase.SaveAssets();

        Debug.Log("HiderCompleteHUDSetupTool: Canvas active.");
        Debug.Log("HiderCompleteHUDSetupTool: TopRoundBar created.");
        Debug.Log("HiderCompleteHUDSetupTool: HiderContextPanel created.");
        Debug.Log("HiderCompleteHUDSetupTool: Ability sprites assigned.");
        Debug.Log("HiderCompleteHUDSetupTool: Clone card configured from the existing X card.");
        Debug.Log("HiderCompleteHUDSetupTool: Anti-camp card created.");
        Debug.Log("HiderCompleteHUDSetupTool: Random prop card created.");
        Debug.Log("HiderCompleteHUDSetupTool: independent Hider and Seeker health bars created and bound.");
        Debug.Log("HiderCompleteHUDSetupTool: reveal highlight alpha configured to 0.05 (5%).");
        Debug.Log($"HiderCompleteHUDSetupTool: elimination roster configured ({hiderControllers.Length} true Hider(s)).");
        Debug.Log("HiderCompleteHUDSetupTool: spectator follow camera and status panel configured without creating a camera.");
        Debug.Log("HiderCompleteHUDSetupTool: test role selector and separate SeekerPlayer configured.");
        Debug.Log($"HiderCompleteHUDSetupTool: shrinking zone configured with {zoneAnchors.Length} anchors.");
        Debug.Log("HiderCompleteHUDSetupTool: zone warning and damage flash HUD configured.");
        Debug.Log(_fontAsset != null
            ? "HiderCompleteHUDSetupTool: TMP font assigned."
            : "HiderCompleteHUDSetupTool: TMP font unavailable; outline skipped safely.");
        Debug.Log("HiderCompleteHUDSetupTool: Scene saved.");
        Debug.Log("HiderCompleteHUDSetupTool:\nHUD setup complete.");
    }

    [MenuItem("Tools/Prop Hunt/Fix Hider Jump And Startup Audio")]
    public static void SetupHiderJumpAndStartupAudioOnly()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("HiderCompleteHUDSetupTool: exit Play Mode before running jump/audio setup.");
            return;
        }

        if (!OpenMapV2())
        {
            return;
        }

        PropTransformSystem hider = FindHiderInActiveScene();
        if (hider == null)
        {
            throw new InvalidOperationException(
                "HiderCompleteHUDSetupTool: Hider PlayerCapsule was not found.");
        }

        ConfigureHiderJumpAndStartupAudio(hider);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), MapV2Path);
        AssetDatabase.SaveAssets();
        Debug.Log(
            "HiderJumpStartupAudioSetup: PASS — one Hider movement controller, " +
            "visual physics removed, startup music disabled, Map_v2 saved.");
    }

    private static void ConfigureHiderJumpAndStartupAudio(PropTransformSystem hider)
    {
        GameObject player = hider.gameObject;
        CharacterController characterController =
            GetOrAddUniqueComponent<CharacterController>(player);
        characterController.enabled = true;

        FirstPersonController[] firstPersonControllers =
            player.GetComponents<FirstPersonController>();
        FirstPersonController movement = firstPersonControllers.FirstOrDefault();
        if (movement == null)
        {
            movement = Undo.AddComponent<FirstPersonController>(player);
        }

        movement.enabled = true;
        if (movement.Gravity >= 0f)
        {
            movement.Gravity = -15f;
        }

        for (int i = 1; i < firstPersonControllers.Length; i++)
        {
            firstPersonControllers[i].enabled = false;
            EditorUtility.SetDirty(firstPersonControllers[i]);
        }

        foreach (ThirdPersonController duplicate in player.GetComponents<ThirdPersonController>())
        {
            duplicate.enabled = false;
            EditorUtility.SetDirty(duplicate);
        }

        foreach (SeekerFirstPersonController duplicate in
                 player.GetComponents<SeekerFirstPersonController>())
        {
            duplicate.enabled = false;
            EditorUtility.SetDirty(duplicate);
        }

        if (hider.propVisualRoot != null)
        {
            foreach (Collider collider in
                     hider.propVisualRoot.GetComponentsInChildren<Collider>(true))
            {
                Undo.DestroyObjectImmediate(collider);
            }

            foreach (Rigidbody body in
                     hider.propVisualRoot.GetComponentsInChildren<Rigidbody>(true))
            {
                Undo.DestroyObjectImmediate(body);
            }

            foreach (Rigidbody2D body in
                     hider.propVisualRoot.GetComponentsInChildren<Rigidbody2D>(true))
            {
                Undo.DestroyObjectImmediate(body);
            }
        }

        foreach (AudioSource source in
                 UnityEngine.Object.FindObjectsOfType<AudioSource>(true)
                     .Where(source => source != null &&
                                      source.gameObject.name == "Map2MusicPlayer"))
        {
            source.Stop();
            source.playOnAwake = false;
            source.loop = false;
            source.clip = null;
            EditorUtility.SetDirty(source);
        }

        EditorUtility.SetDirty(characterController);
        EditorUtility.SetDirty(movement);
        EditorUtility.SetDirty(hider);
    }

    private static bool OpenMapV2()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.path == MapV2Path)
        {
            return true;
        }

        if (activeScene.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return false;
        }

        return EditorSceneManager.OpenScene(MapV2Path, OpenSceneMode.Single).IsValid();
    }

    private static PropTransformSystem FindHiderInActiveScene()
    {
        return UnityEngine.Object.FindObjectsOfType<PropTransformSystem>(true)
            .FirstOrDefault(system => system.playerRole == PlayerRole.Hider);
    }

    private static PropHuntRoundManager GetOrCreateRoundManager()
    {
        PropHuntRoundManager manager = UnityEngine.Object.FindObjectOfType<PropHuntRoundManager>(true);
        if (manager != null)
        {
            return manager;
        }

        GameObject managerObject = new GameObject("PropHuntRoundManager");
        Undo.RegisterCreatedObjectUndo(managerObject, "Create PropHuntRoundManager");
        return Undo.AddComponent<PropHuntRoundManager>(managerObject);
    }

    private static Transform GetOrCreateCloneContainer()
    {
        GameObject[] matches = SceneManager.GetActiveScene().GetRootGameObjects()
            .Where(root => root.name == "HiderCloneContainer")
            .ToArray();
        GameObject container = matches.FirstOrDefault();
        for (int i = 1; i < matches.Length; i++)
        {
            Undo.DestroyObjectImmediate(matches[i]);
        }

        if (container == null)
        {
            container = new GameObject("HiderCloneContainer");
            Undo.RegisterCreatedObjectUndo(container, "Create HiderCloneContainer");
        }

        container.transform.SetParent(null);
        container.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        container.transform.localScale = Vector3.one;
        return container.transform;
    }

    private static void ConfigureRevealLayerAndHiderCameras(PropTransformSystem hider)
    {
        int revealLayer = EnsureLayer("SeekerReveal");
        if (revealLayer < 0 || hider == null)
        {
            return;
        }

        int revealBit = 1 << revealLayer;
        HashSet<Camera> cameras = new HashSet<Camera>();
        if (hider.mainCamera != null) cameras.Add(hider.mainCamera);
        if (hider.cameraModeManager != null)
        {
            if (hider.cameraModeManager.fpsCamera != null) cameras.Add(hider.cameraModeManager.fpsCamera);
            if (hider.cameraModeManager.tpsCamera != null) cameras.Add(hider.cameraModeManager.tpsCamera);
            if (hider.cameraModeManager.spectatorCamera != null) cameras.Add(hider.cameraModeManager.spectatorCamera);
        }

        foreach (Camera camera in hider.GetComponentsInChildren<Camera>(true))
        {
            cameras.Add(camera);
        }

        foreach (Camera camera in cameras)
        {
            camera.cullingMask &= ~revealBit;
            EditorUtility.SetDirty(camera);
        }
    }

    private static int EnsureLayer(string layerName)
    {
        int existingLayer = LayerMask.NameToLayer(layerName);
        if (existingLayer >= 0)
        {
            return existingLayer;
        }

        UnityEngine.Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tagManagerAssets.Length == 0)
        {
            Debug.LogWarning("HiderCompleteHUDSetupTool: TagManager asset is unavailable; SeekerReveal layer was not created.");
            return -1;
        }

        SerializedObject tagManager = new SerializedObject(tagManagerAssets[0]);
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
            AssetDatabase.SaveAssets();
            return i;
        }

        Debug.LogWarning("HiderCompleteHUDSetupTool: no empty layer slot is available for SeekerReveal; existing layers were not overwritten.");
        return -1;
    }

    private static void ConfigureHiderGameplay(
        PropTransformSystem hider,
        PropHuntRoundManager roundManager,
        out HiderAbilityController abilityController,
        out HiderAntiCampSystem antiCampSystem,
        out HiderHealth hiderHealth)
    {
        GameObject player = hider.gameObject;
        GameObject antiCampAudioObject = GetOrCreatePlainChild(
            player.transform,
            HiderAntiCampSystem.DedicatedAudioObjectName);
        AudioSource audioSource = GetOrAddUniqueComponent<AudioSource>(antiCampAudioObject);

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 1f;
        audioSource.minDistance = 4f;
        audioSource.maxDistance = 35f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;

        abilityController = GetOrAddComponent<HiderAbilityController>(player);
        antiCampSystem = GetOrAddComponent<HiderAntiCampSystem>(player);
        HiderAntiCampAudioPresentation antiCampAudioPresentation =
            GetOrAddUniqueComponent<HiderAntiCampAudioPresentation>(antiCampAudioObject);
        hiderHealth = GetOrAddComponent<HiderHealth>(player);
        HiderRevealController revealController = GetOrAddComponent<HiderRevealController>(player);
        HiderCloneAbility cloneAbility = GetOrAddComponent<HiderCloneAbility>(player);
        Transform cloneContainer = GetOrCreateCloneContainer();
        PropTarget[] propDefinitions = UnityEngine.Object.FindObjectsOfType<PropTarget>(true)
            .Where(IsValidOriginalPropDefinition)
            .ToArray();

        hider.roundManager = roundManager;
        revealController.Configure(hider);
        cloneAbility.Configure(hider, roundManager, revealController, cloneContainer);
        abilityController.Configure(
            hider,
            roundManager,
            cloneAbility,
            propDefinitions
        );
        antiCampSystem.Configure(hider, roundManager);
        antiCampAudioPresentation.Configure(antiCampSystem, audioSource);
        hiderHealth.Configure(hider, roundManager);
        ConfigureRevealLayerAndHiderCameras(hider);

        if (hider.cameraModeManager != null)
        {
            hider.cameraModeManager.nearCameraDistance = 4f;
            hider.cameraModeManager.nearCameraHeight = 2.5f;
            hider.cameraModeManager.farCameraDistance = 7f;
            hider.cameraModeManager.farCameraHeight = 3.5f;
            hider.cameraModeManager.SetPropCameraFar(false);
            EditorUtility.SetDirty(hider.cameraModeManager);
        }

        foreach (PropInteractionUI interactionUI in player.GetComponents<PropInteractionUI>())
        {
            if (interactionUI.promptText != null)
            {
                interactionUI.promptText.gameObject.SetActive(false);
            }

            if (interactionUI.legacyPromptText != null)
            {
                interactionUI.legacyPromptText.gameObject.SetActive(false);
            }

            interactionUI.enabled = false;
            EditorUtility.SetDirty(interactionUI);
        }

        EditorUtility.SetDirty(audioSource);
        EditorUtility.SetDirty(antiCampAudioPresentation);
        EditorUtility.SetDirty(revealController);
        EditorUtility.SetDirty(cloneAbility);
    }

    private static void SetupEliminationLifecycle(
        IEnumerable<PropTransformSystem> players,
        PropTransformSystem localHider,
        PropHuntRoundManager roundManager,
        HiderRosterManager rosterManager,
        out HiderEliminationController localEliminationController,
        out HiderSpectatorController localSpectatorController,
        out HiderEliminationController[] hiderControllers)
    {
        List<HiderEliminationController> configuredControllers =
            new List<HiderEliminationController>();
        localEliminationController = null;
        localSpectatorController = null;

        foreach (PropTransformSystem hider in players.Where(player =>
                     player != null && player.playerRole == PlayerRole.Hider))
        {
            GameObject playerObject = hider.gameObject;
            HiderHealth health = GetOrAddComponent<HiderHealth>(playerObject);
            HiderEliminationController elimination =
                GetOrAddComponent<HiderEliminationController>(playerObject);
            HiderZoneStatusController zoneStatus =
                playerObject.GetComponent<HiderZoneStatusController>();
            HiderCloneAbility cloneAbility = playerObject.GetComponent<HiderCloneAbility>();
            HiderRevealController revealController = playerObject.GetComponent<HiderRevealController>();
            HiderAntiCampSystem antiCamp = playerObject.GetComponent<HiderAntiCampSystem>();
            FirstPersonController movement = playerObject.GetComponent<FirstPersonController>();
            HiderSpectatorController spectator = null;

            health.Configure(hider, roundManager);
            if (hider == localHider)
            {
                spectator = GetOrAddComponent<HiderSpectatorController>(playerObject);
                Camera spectatorCamera = hider.cameraModeManager != null
                    ? hider.cameraModeManager.spectatorCamera
                    : null;
                SpectatorCameraController legacyController = spectatorCamera != null
                    ? spectatorCamera.GetComponent<SpectatorCameraController>()
                    : null;
                spectator.Configure(
                    elimination,
                    rosterManager,
                    hider.cameraModeManager,
                    spectatorCamera,
                    legacyController);
                localSpectatorController = spectator;
            }

            Collider[] hitColliders = playerObject.GetComponentsInChildren<Collider>(true);
            Renderer[] renderers = playerObject.GetComponentsInChildren<Renderer>(true);
            elimination.Configure(
                health,
                hider,
                movement,
                cloneAbility,
                revealController,
                antiCamp,
                zoneStatus,
                rosterManager,
                spectator,
                hitColliders,
                renderers);

            configuredControllers.Add(elimination);
            EditorUtility.SetDirty(health);
            EditorUtility.SetDirty(elimination);
            if (spectator != null) EditorUtility.SetDirty(spectator);
            if (hider == localHider) localEliminationController = elimination;
        }

        hiderControllers = configuredControllers.ToArray();
    }

    private static void SetupRoleTestingSystem(
        PropTransformSystem hider,
        PropHuntRoundManager roundManager,
        HiderHealth hiderHealth,
        HiderEliminationController hiderElimination,
        HiderAbilityController hiderAbilities,
        out PropHuntTestRoleSelector roleSelector,
        out SeekerFirstPersonController seekerController,
        out SeekerRaycastWeapon seekerWeapon,
        out SeekerHealth seekerHealth,
        out Camera seekerCamera)
    {
        GameObject seekerObject = FindOrCreateUniqueSceneRoot("SeekerPlayer");
        if (seekerObject.transform.position == Vector3.zero)
        {
            seekerObject.transform.position = hider.transform.position + Vector3.right * 4f;
        }
        seekerObject.transform.rotation = Quaternion.identity;
        seekerObject.transform.localScale = Vector3.one;

        int seekerWorldVisualLayer = EnsureLayer("SeekerWorldVisual");
        SetupIndustrialSeekerVisual(
            seekerObject.transform,
            seekerWorldVisualLayer,
            out GameObject seekerWorldVisualRoot,
            out GameObject industrialSeekerModel,
            out Renderer[] seekerModelRenderers,
            out Animator seekerModelAnimator);
        if (seekerWorldVisualRoot.transform.Find("CyberSoldierModel") != null)
        {
            industrialSeekerModel.SetActive(false);
        }

        CharacterController seekerCharacter =
            GetOrAddUniqueComponent<CharacterController>(seekerObject);
        seekerCharacter.height = 1.8f;
        seekerCharacter.radius = 0.35f;
        seekerCharacter.center = new Vector3(0f, 0.9f, 0f);
        seekerCharacter.stepOffset = 0.3f;
        seekerCharacter.slopeLimit = 45f;

        GameObject cameraRootObject = GetOrCreatePlainChild(seekerObject.transform, "SeekerCameraRoot");
        cameraRootObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        cameraRootObject.transform.localRotation = Quaternion.identity;
        cameraRootObject.transform.localScale = Vector3.one;

        GameObject cameraObject = GetOrCreatePlainChild(cameraRootObject.transform, "SeekerCamera");
        cameraObject.transform.localPosition = Vector3.zero;
        cameraObject.transform.localRotation = Quaternion.identity;
        cameraObject.transform.localScale = Vector3.one;
        seekerCamera = GetOrAddUniqueComponent<Camera>(cameraObject);
        seekerCamera.fieldOfView = 60f;
        seekerCamera.nearClipPlane = 0.05f;
        seekerCamera.farClipPlane = 1000f;
        if (hider.mainCamera != null)
        {
            seekerCamera.clearFlags = hider.mainCamera.clearFlags;
            seekerCamera.backgroundColor = hider.mainCamera.backgroundColor;
            seekerCamera.cullingMask = hider.mainCamera.cullingMask;
        }
        int revealLayer = LayerMask.NameToLayer("SeekerReveal");
        if (revealLayer >= 0)
        {
            seekerCamera.cullingMask |= 1 << revealLayer;
        }
        if (seekerWorldVisualLayer >= 0)
        {
            int seekerVisualBit = 1 << seekerWorldVisualLayer;
            seekerCamera.cullingMask &= ~seekerVisualBit;
            HashSet<Camera> hiderCameras = new HashSet<Camera>();
            if (hider.mainCamera != null) hiderCameras.Add(hider.mainCamera);
            if (hider.cameraModeManager != null)
            {
                if (hider.cameraModeManager.fpsCamera != null) hiderCameras.Add(hider.cameraModeManager.fpsCamera);
                if (hider.cameraModeManager.tpsCamera != null) hiderCameras.Add(hider.cameraModeManager.tpsCamera);
                if (hider.cameraModeManager.spectatorCamera != null)
                    hiderCameras.Add(hider.cameraModeManager.spectatorCamera);
            }
            foreach (Camera hiderCamera in hiderCameras)
            {
                hiderCamera.cullingMask |= seekerVisualBit;
                EditorUtility.SetDirty(hiderCamera);
            }
        }
        int seekerFpsVisualLayer = LayerMask.NameToLayer("SeekerFPSVisual");
        if (seekerFpsVisualLayer >= 0)
        {
            int fpsVisualBit = 1 << seekerFpsVisualLayer;
            seekerCamera.cullingMask |= fpsVisualBit;
            foreach (Camera hiderCamera in UnityEngine.Object.FindObjectsOfType<Camera>(true))
            {
                if (hiderCamera != seekerCamera) hiderCamera.cullingMask &= ~fpsVisualBit;
            }
        }

        AudioListener seekerListener = GetOrAddUniqueComponent<AudioListener>(cameraObject);
        seekerController = GetOrAddUniqueComponent<SeekerFirstPersonController>(seekerObject);
        seekerController.Configure(seekerCharacter, cameraRootObject.transform);
        seekerHealth = GetOrAddUniqueComponent<SeekerHealth>(seekerObject);
        seekerHealth.ConfigureMaxHealth(100);
        seekerHealth.ResetForRound();
        seekerWeapon = GetOrAddUniqueComponent<SeekerRaycastWeapon>(cameraObject);
        seekerController.SetControlActive(false);
        seekerWeapon.SetWeaponActive(false);
        cameraObject.tag = "Untagged";
        seekerListener.enabled = false;
        cameraObject.SetActive(false);

        GameObject spawnRoot = FindOrCreateUniqueSceneRoot("PropHuntRoleTestSpawns");
        Transform existingHiderSpawn = spawnRoot.transform.Find("HiderTestSpawnPoint");
        GameObject hiderSpawnObject = GetOrCreatePlainChild(spawnRoot.transform, "HiderTestSpawnPoint");
        if (existingHiderSpawn == null)
        {
            hiderSpawnObject.transform.SetPositionAndRotation(hider.transform.position, hider.transform.rotation);
        }
        hiderSpawnObject.transform.localScale = Vector3.one;

        Transform existingSeekerSpawn = spawnRoot.transform.Find("SeekerSpawnPoint");
        GameObject seekerSpawnObject = GetOrCreatePlainChild(spawnRoot.transform, "SeekerSpawnPoint");
        if (existingSeekerSpawn == null)
        {
            seekerSpawnObject.transform.SetPositionAndRotation(seekerObject.transform.position, seekerObject.transform.rotation);
        }
        if (Vector3.Distance(hiderSpawnObject.transform.position, seekerSpawnObject.transform.position) < 2f)
        {
            seekerSpawnObject.transform.position = hiderSpawnObject.transform.position + Vector3.right * 6f;
        }
        seekerSpawnObject.transform.localScale = Vector3.one;

        GameObject weaponHolder = GetOrCreatePlainChild(cameraObject.transform, "WeaponHolder");
        weaponHolder.transform.localPosition = new Vector3(0.3f, -0.27f, 0.58f);
        weaponHolder.transform.localRotation = Quaternion.Euler(3f, -3f, 0f);
        weaponHolder.transform.localScale = Vector3.one;
        GameObject gunPlaceholder = GetOrCreatePlainChild(weaponHolder.transform, "GunPlaceholder");
        StripNonTransformComponents(gunPlaceholder, false);
        gunPlaceholder.SetActive(false);

        GameObject pulseTaggerVisual = GetOrCreatePlainChild(weaponHolder.transform, "PulseTaggerVisual");
        pulseTaggerVisual.transform.localPosition = Vector3.zero;
        pulseTaggerVisual.transform.localRotation = Quaternion.identity;
        pulseTaggerVisual.transform.localScale = Vector3.one;
        ClearChildren(pulseTaggerVisual.transform);
        StripNonTransformComponents(pulseTaggerVisual, false);
        Renderer[] pulseTaggerRenderers = BuildPulseTaggerVisual(pulseTaggerVisual.transform);
        Transform integratedFpsGun = weaponHolder.transform.Find("SeekerFPSGunPivot/SciFiGunLight_FPS");
        if (integratedFpsGun != null)
        {
            pulseTaggerVisual.SetActive(false);
            pulseTaggerRenderers = integratedFpsGun.GetComponentsInChildren<Renderer>(true);
        }

        GameObject roleCanvasObject = FindOrCreateUniqueRoot("PropHuntRoleSelectionCanvas");
        roleCanvasObject.SetActive(true);
        Canvas roleCanvas = GetOrAddUniqueComponent<Canvas>(roleCanvasObject);
        roleCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        roleCanvas.sortingOrder = 250;
        CanvasScaler roleScaler = GetOrAddUniqueComponent<CanvasScaler>(roleCanvasObject);
        roleScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        roleScaler.referenceResolution = new Vector2(1920f, 1080f);
        roleScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        roleScaler.matchWidthOrHeight = 0.5f;
        GraphicRaycaster roleRaycaster = GetOrAddUniqueComponent<GraphicRaycaster>(roleCanvasObject);
        roleRaycaster.enabled = true;
        RectTransform roleCanvasRect = GetOrAddUniqueComponent<RectTransform>(roleCanvasObject);
        roleCanvasRect.anchorMin = Vector2.zero;
        roleCanvasRect.anchorMax = Vector2.one;
        roleCanvasRect.offsetMin = Vector2.zero;
        roleCanvasRect.offsetMax = Vector2.zero;
        roleCanvasRect.localScale = Vector3.one;

        GameObject selectionPanel = CreateChild(roleCanvasObject.transform, "RoleSelectionPanel");
        SetRect(
            selectionPanel,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(620f, 300f));
        ConfigurePanelImage(selectionPanel, new Color(0.015f, 0.025f, 0.035f, 0.94f), new Vector2(2f, -2f));
        CanvasGroup selectionGroup = GetOrAddUniqueComponent<CanvasGroup>(selectionPanel);
        selectionGroup.alpha = 1f;
        selectionGroup.interactable = true;
        selectionGroup.blocksRaycasts = true;

        TextMeshProUGUI title = CreateText(
            selectionPanel.transform,
            "RoleSelectionTitle",
            "CHỌN VAI TRÒ KIỂM THỬ",
            30f,
            TextAlignmentOptions.Center);
        SetRect(
            title.gameObject,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -35f),
            new Vector2(560f, 60f));
        title.fontStyle = FontStyles.Bold;
        ApplyOutlineSafely(title, Color.black, 0.16f);

        Button hiderButton = CreateRoleButton(
            selectionPanel.transform,
            "HiderRoleButton",
            "VAI TRÒ HIDER",
            new Vector2(0f, 18f),
            new Color32(28, 157, 170, 255));
        Button seekerButton = CreateRoleButton(
            selectionPanel.transform,
            "SeekerRoleButton",
            "VAI TRÒ SEEKER",
            new Vector2(0f, -82f),
            new Color32(218, 158, 42, 255));

        GameObject seekerHudRoot = CreateUniqueNamedChild(roleCanvasObject.transform, "SeekerHUDRoot");
        SetStretchRect(
            seekerHudRoot,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero);
        CanvasGroup seekerHudGroup = GetOrAddUniqueComponent<CanvasGroup>(seekerHudRoot);
        seekerHudGroup.blocksRaycasts = false;
        seekerHudGroup.interactable = false;
        TextMeshProUGUI crosshair = CreateText(
            seekerHudRoot.transform,
            "SeekerCrosshair",
            "+",
            34f,
            TextAlignmentOptions.Center);
        SetRect(
            crosshair.gameObject,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(48f, 48f));
        crosshair.fontStyle = FontStyles.Bold;
        ApplyOutlineSafely(crosshair, Color.black, 0.2f);

        GameObject instructionPanel = CreateChild(seekerHudRoot.transform, "SeekerInstructionPanel");
        SetRect(
            instructionPanel,
            Vector2.zero,
            Vector2.zero,
            Vector2.zero,
            new Vector2(24f, 24f),
            new Vector2(280f, 54f));
        ConfigurePanelImage(instructionPanel, new Color(0f, 0f, 0f, 0.62f), new Vector2(1f, -1f));
        TextMeshProUGUI instructionText = CreateText(
            instructionPanel.transform,
            "SeekerInstructionText",
            "F1: VAI TR\u00d2 HIDER",
            20f,
            TextAlignmentOptions.MidlineLeft);
        SetStretchRect(
            instructionText.gameObject,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            new Vector2(16f, 8f),
            new Vector2(-16f, -8f));
        instructionText.fontStyle = FontStyles.Bold;
        instructionText.enableWordWrapping = false;
        seekerHudRoot.SetActive(false);
        selectionPanel.SetActive(true);

        EnsureRoleSelectionEventSystem();
        GameObject managerObject = FindOrCreateUniqueSceneRoot("PropHuntRoleTestManager");
        roleSelector = GetOrAddUniqueComponent<PropHuntTestRoleSelector>(managerObject);
        PropTarget hiderTestProp = UnityEngine.Object.FindObjectsOfType<PropTarget>(true)
            .FirstOrDefault(IsValidOriginalPropDefinition);
        roleSelector.Configure(
            selectionPanel,
            hiderButton,
            seekerButton,
            seekerHudRoot,
            hider,
            hiderHealth,
            hiderElimination,
            hiderAbilities,
            hiderTestProp,
            hiderSpawnObject.transform,
            seekerController,
            seekerWeapon,
            seekerHealth,
            seekerCamera,
            seekerSpawnObject.transform);

        LayerMask weaponHitMask = Physics.DefaultRaycastLayers;
        if (seekerWorldVisualLayer >= 0)
        {
            weaponHitMask.value &= ~(1 << seekerWorldVisualLayer);
        }
        seekerWeapon.Configure(
            seekerCamera,
            roleSelector,
            roundManager,
            crosshair,
            pulseTaggerRenderers,
            50f,
            20,
            0.35f,
            weaponHitMask);

        if (hider.cameraModeManager != null)
        {
            hider.cameraModeManager.InitializeHiderTps(hider.transform);
            hider.cameraModeManager.ConfigureSinglePlayerHiderCamera(true);
            hider.cameraModeManager.SetCameraSystemEnabled(true);
            hider.cameraModeManager.ApplyResolvedHiderCameraMode();
            EditorUtility.SetDirty(hider.cameraModeManager);
        }

        EditorUtility.SetDirty(seekerObject);
        EditorUtility.SetDirty(seekerCharacter);
        EditorUtility.SetDirty(seekerController);
        EditorUtility.SetDirty(seekerHealth);
        EditorUtility.SetDirty(seekerWeapon);
        EditorUtility.SetDirty(seekerCamera);
        EditorUtility.SetDirty(seekerListener);
        EditorUtility.SetDirty(seekerWorldVisualRoot);
        EditorUtility.SetDirty(industrialSeekerModel);
        foreach (Renderer renderer in seekerModelRenderers) EditorUtility.SetDirty(renderer);
        if (seekerModelAnimator != null) EditorUtility.SetDirty(seekerModelAnimator);
        EditorUtility.SetDirty(spawnRoot);
        EditorUtility.SetDirty(hiderSpawnObject);
        EditorUtility.SetDirty(seekerSpawnObject);
        EditorUtility.SetDirty(weaponHolder);
        EditorUtility.SetDirty(gunPlaceholder);
        EditorUtility.SetDirty(pulseTaggerVisual);
        foreach (Renderer renderer in pulseTaggerRenderers) EditorUtility.SetDirty(renderer);
        EditorUtility.SetDirty(roleCanvasObject);
        EditorUtility.SetDirty(roleSelector);
    }

    private static Button CreateRoleButton(
        Transform parent,
        string objectName,
        string label,
        Vector2 anchoredPosition,
        Color color)
    {
        GameObject buttonObject = CreateChild(parent, objectName);
        SetRect(
            buttonObject,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            anchoredPosition,
            new Vector2(460f, 76f));
        Image background = ConfigurePanelImage(buttonObject, color, new Vector2(2f, -2f));
        background.raycastTarget = true;
        Button button = GetOrAddUniqueComponent<Button>(buttonObject);
        button.targetGraphic = background;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.selectedColor = Color.white;
        button.colors = colors;

        TextMeshProUGUI text = CreateText(
            buttonObject.transform,
            "Label",
            label,
            24f,
            TextAlignmentOptions.Center);
        SetStretchRect(
            text.gameObject,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            new Vector2(8f, 0f),
            new Vector2(-8f, 0f));
        text.fontStyle = FontStyles.Bold;
        ApplyOutlineSafely(text, Color.black, 0.14f);
        return button;
    }

    private static void EnsureRoleSelectionEventSystem()
    {
        EventSystem[] systems = UnityEngine.Object.FindObjectsOfType<EventSystem>(true);
        EventSystem eventSystem;
        if (systems.Length > 0)
        {
            eventSystem = systems[0];
            for (int i = 1; i < systems.Length; i++)
            {
                if (systems[i] != null) Undo.DestroyObjectImmediate(systems[i].gameObject);
            }
        }
        else
        {
            GameObject eventObject = new GameObject("PropHuntEventSystem");
            Undo.RegisterCreatedObjectUndo(eventObject, "Create Prop Hunt EventSystem");
            eventSystem = Undo.AddComponent<EventSystem>(eventObject);
        }

        InputSystemUIInputModule inputModule =
            GetOrAddUniqueComponent<InputSystemUIInputModule>(eventSystem.gameObject);
        foreach (StandaloneInputModule legacyModule in eventSystem.GetComponents<StandaloneInputModule>())
        {
            Undo.DestroyObjectImmediate(legacyModule);
        }
        if (inputModule.actionsAsset == null)
        {
            inputModule.AssignDefaultActions();
        }
        eventSystem.gameObject.SetActive(true);
        EditorUtility.SetDirty(eventSystem);
        EditorUtility.SetDirty(inputModule);
    }

    private static void SetupIndustrialSeekerVisual(
        Transform seekerPlayer,
        int visualLayer,
        out GameObject visualRoot,
        out GameObject model,
        out Renderer[] modelRenderers,
        out Animator modelAnimator)
    {
        List<GameObject> roots = new List<GameObject>();
        for (int index = 0; index < seekerPlayer.childCount; index++)
        {
            Transform child = seekerPlayer.GetChild(index);
            if (child.name == "SeekerWorldVisualRoot" || child.name == "SeekerWorldVisual")
                roots.Add(child.gameObject);
        }

        visualRoot = roots.FirstOrDefault(candidate => candidate.name == "SeekerWorldVisualRoot") ??
                     roots.FirstOrDefault();
        foreach (GameObject candidate in roots)
        {
            if (candidate != visualRoot) Undo.DestroyObjectImmediate(candidate);
        }
        if (visualRoot == null)
        {
            visualRoot = new GameObject("SeekerWorldVisualRoot");
            Undo.RegisterCreatedObjectUndo(visualRoot, "Create SeekerWorldVisualRoot");
            Undo.SetTransformParent(visualRoot.transform, seekerPlayer, "Parent SeekerWorldVisualRoot");
        }

        visualRoot.name = "SeekerWorldVisualRoot";
        visualRoot.transform.SetParent(seekerPlayer, false);
        visualRoot.transform.localPosition = Vector3.zero;
        visualRoot.transform.localRotation = Quaternion.identity;
        visualRoot.transform.localScale = Vector3.one;
        // This wrapper is shared with the integrated Cyber Soldier/world-gun
        // presentation. Cleaning it recursively used to strip MeshFilter,
        // MeshRenderer, muzzle VFX and lights from otherwise valid children.
        StripNonTransformComponentsOnObject(visualRoot);

        Transform existingModel = visualRoot.transform.Find("IndustrialSeekerModel");
        if (existingModel != null &&
            existingModel.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
        {
            Undo.DestroyObjectImmediate(existingModel.gameObject);
            existingModel = null;
        }

        for (int index = visualRoot.transform.childCount - 1; index >= 0; index--)
        {
            Transform child = visualRoot.transform.GetChild(index);
            if (child.name == "IndustrialSeekerModel" && child != existingModel)
                Undo.DestroyObjectImmediate(child.gameObject);
        }

        if (existingModel == null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SeekerModelPrefabPath);
            if (prefab == null)
                throw new InvalidOperationException($"Industrial Seeker model is missing: {SeekerModelPrefabPath}");
            model = PrefabUtility.InstantiatePrefab(prefab, visualRoot.transform) as GameObject;
            if (model == null)
                throw new InvalidOperationException("Could not instantiate the Industrial Seeker model prefab.");
            Undo.RegisterCreatedObjectUndo(model, "Create Industrial Seeker Model");
        }
        else
        {
            model = existingModel.gameObject;
        }

        if (PrefabUtility.IsPartOfPrefabInstance(model))
        {
            PrefabUtility.UnpackPrefabInstance(
                PrefabUtility.GetOutermostPrefabInstanceRoot(model),
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
        }
        model.name = "IndustrialSeekerModel";
        model.transform.SetParent(visualRoot.transform, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;
        model.SetActive(true);
        StripNonTransformComponents(model, true);

        Material body = EnsureStandardMaterial(
            SeekerWorldMaterialPath, "SeekerOperatorBody", new Color(0.075f, 0.12f, 0.15f, 1f), Color.black);
        Material arms = EnsureStandardMaterial(
            SeekerOperatorArmsMaterialPath, "SeekerOperatorArms", new Color(0.18f, 0.23f, 0.26f, 1f), Color.black);
        Material legs = EnsureStandardMaterial(
            SeekerOperatorLegsMaterialPath, "SeekerOperatorLegs", new Color(0.045f, 0.065f, 0.08f, 1f), Color.black);
        modelRenderers = model.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in modelRenderers)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
            {
                materials[index] = index == 0 ? body : index == 1 ? arms : legs;
            }
            renderer.sharedMaterials = materials;
            renderer.enabled = true;
        }

        modelAnimator = model.GetComponentInChildren<Animator>(true);
        if (modelAnimator != null)
        {
            modelAnimator.applyRootMotion = false;
            modelAnimator.enabled = true;
        }

        if (visualLayer >= 0)
        {
            foreach (Transform child in visualRoot.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = visualLayer;
        }
    }

    private static Renderer[] BuildPulseTaggerVisual(Transform root)
    {
        Material body = EnsureStandardMaterial(
            PulseTaggerBodyMaterialPath, "PulseTaggerBody", new Color(0.075f, 0.11f, 0.135f, 1f), Color.black);
        Material accent = EnsureStandardMaterial(
            PulseTaggerAccentMaterialPath,
            "PulseTaggerAccent",
            new Color(0.04f, 0.72f, 0.82f, 1f),
            new Color(0.02f, 0.65f, 0.8f, 1f) * 1.8f);

        List<Renderer> renderers = new List<Renderer>
        {
            CreatePulseVisualPart(root, "TaggerBody", "Cube.fbx", Vector3.zero,
                Quaternion.identity, new Vector3(0.18f, 0.12f, 0.38f), body),
            CreatePulseVisualPart(root, "TopEnergyRail", "Cube.fbx", new Vector3(0f, 0.075f, 0.015f),
                Quaternion.identity, new Vector3(0.11f, 0.025f, 0.25f), accent),
            CreatePulseVisualPart(root, "IndustrialGrip", "Cube.fbx", new Vector3(0f, -0.135f, -0.09f),
                Quaternion.Euler(12f, 0f, 0f), new Vector3(0.075f, 0.18f, 0.095f), body),
            CreatePulseVisualPart(root, "EmitterHousing", "Cylinder.fbx", new Vector3(0f, 0f, 0.225f),
                Quaternion.Euler(90f, 0f, 0f), new Vector3(0.072f, 0.065f, 0.072f), body),
            CreatePulseVisualPart(root, "EmitterCore", "Sphere.fbx", new Vector3(0f, 0f, 0.315f),
                Quaternion.identity, new Vector3(0.075f, 0.075f, 0.055f), accent),
            CreatePulseVisualPart(root, "SideScanner", "Cube.fbx", new Vector3(-0.105f, 0.012f, 0.035f),
                Quaternion.identity, new Vector3(0.035f, 0.075f, 0.13f), accent)
        };
        return renderers.ToArray();
    }

    private static Renderer CreatePulseVisualPart(
        Transform parent,
        string name,
        string meshName,
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        Material material)
    {
        GameObject part = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(part, $"Create {name}");
        Undo.SetTransformParent(part.transform, parent, $"Parent {name}");
        part.transform.localPosition = localPosition;
        part.transform.localRotation = localRotation;
        part.transform.localScale = localScale;
        MeshFilter filter = Undo.AddComponent<MeshFilter>(part);
        MeshRenderer renderer = Undo.AddComponent<MeshRenderer>(part);
        filter.sharedMesh = Resources.GetBuiltinResource<Mesh>(meshName);
        renderer.sharedMaterial = material;
        return renderer;
    }

    private static void StripNonTransformComponents(GameObject root, bool preserveVisualComponents)
    {
        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null) Undo.DestroyObjectImmediate(behaviour);
        }

        foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
        {
            foreach (Component component in node.GetComponents<Component>())
            {
                if (component == null || component is Transform) continue;
                if (component is MonoBehaviour) continue;
                if (preserveVisualComponents &&
                    (component is Renderer || component is MeshFilter || component is Animator))
                    continue;
                Undo.DestroyObjectImmediate(component);
            }
        }
    }

    private static void StripNonTransformComponentsOnObject(GameObject root)
    {
        foreach (Component component in root.GetComponents<Component>())
        {
            if (component == null || component is Transform) continue;
            Undo.DestroyObjectImmediate(component);
        }
    }

    private static Material EnsureStandardMaterial(
        string path,
        string assetName,
        Color color,
        Color emission)
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null || !shader.isSupported)
            throw new InvalidOperationException("Built-in Standard shader is missing or unsupported.");

        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = assetName };
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.name = assetName;
            if (material.shader != shader) material.shader = shader;
        }

        material.SetColor("_Color", color);
        material.SetFloat("_Metallic", 0.45f);
        material.SetFloat("_Glossiness", 0.58f);
        material.SetColor("_EmissionColor", emission);
        if (emission.maxColorComponent > 0f) material.EnableKeyword("_EMISSION");
        else material.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(material);
        return material;
    }

    private static bool IsValidOriginalPropDefinition(PropTarget prop)
    {
        if (prop == null ||
            !prop.GameplayEnabled ||
            prop.visualParts == null ||
            prop.visualParts.Length == 0)
        {
            return false;
        }

        foreach (PropVisualPartData part in prop.visualParts)
        {
            if (part == null || part.mesh == null ||
                part.mesh.name.IndexOf("Combined Mesh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                part.materials == null || !part.materials.Any(material => material != null))
            {
                return false;
            }

            Vector3 size = Vector3.Scale(part.mesh.bounds.size, part.localScale);
            if (Mathf.Abs(size.x) > 20f || Mathf.Abs(size.y) > 20f || Mathf.Abs(size.z) > 20f)
            {
                return false;
            }
        }

        return true;
    }

    private static void SetupZoneSystem(
        PropHuntRoundManager roundManager,
        IEnumerable<PropTransformSystem> players,
        PropTransformSystem localHider,
        out PropHuntShrinkingZone shrinkingZone,
        out HiderZoneStatusController localZoneStatus,
        out HiderPlayableAreaBounds playableArea,
        out PropHuntZoneAnchor[] zoneAnchors)
    {
        GameObject zoneRoot = FindOrCreateUniqueSceneRoot("PropHuntZoneSystem");
        zoneRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        zoneRoot.transform.localScale = Vector3.one;

        Bounds mapBounds = CalculateStaticMapBounds(players, zoneRoot.transform);
        BoxCollider boundsCollider = GetOrAddComponent<BoxCollider>(zoneRoot);
        boundsCollider.isTrigger = true;
        boundsCollider.center = mapBounds.center;
        boundsCollider.size = mapBounds.size;

        playableArea = GetOrAddComponent<HiderPlayableAreaBounds>(zoneRoot);
        playableArea.Configure(boundsCollider);

        GameObject boundaryObject = FindOrCreateZoneVisualChild(
            zoneRoot.transform,
            "ZoneGroundRing",
            "ZoneBoundaryVisual");
        boundaryObject.layer = 0;
        LineRenderer boundary = GetOrAddComponent<LineRenderer>(boundaryObject);
        boundary.useWorldSpace = true;
        boundary.loop = true;
        boundary.positionCount = 96;
        boundary.startWidth = 0.10f;
        boundary.endWidth = 0.10f;
        boundary.startColor = new Color(0.08f, 0.88f, 1f, 0.9f);
        boundary.endColor = new Color(0.08f, 0.88f, 1f, 0.9f);
        boundary.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        boundary.receiveShadows = false;
        boundaryObject.SetActive(false);

        GameObject domeObject = FindOrCreateZoneVisualChild(
            zoneRoot.transform,
            "ZoneDomeVisual",
            "ZoneEnergyDome");
        domeObject.layer = 0;
        MeshFilter domeMeshFilter = GetOrAddComponent<MeshFilter>(domeObject);
        MeshRenderer domeMeshRenderer = GetOrAddComponent<MeshRenderer>(domeObject);
        PropHuntZoneDomeVisual domeVisual = GetOrAddComponent<PropHuntZoneDomeVisual>(domeObject);
        Shader domeShader = AssetDatabase.LoadAssetAtPath<Shader>(ZoneDomeShaderPath);
        Mesh domeMesh = EnsureZoneDomeMesh();
        Material domeMaterial = EnsureZoneDomeMaterial(domeShader);
        domeMeshFilter.sharedMesh = domeMesh;
        domeMeshRenderer.sharedMaterial = domeMaterial;
        domeVisual.Configure(domeMeshFilter, domeMeshRenderer, domeShader);
        domeMeshRenderer.enabled = true;
        domeMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        domeMeshRenderer.receiveShadows = false;
        domeMeshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        domeMeshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        GameObjectUtility.SetStaticEditorFlags(domeObject, 0);
        domeObject.SetActive(false);

        GameObject anchorsRoot = CreateSceneChild(zoneRoot.transform, "ZoneAnchors");
        Vector2[] desiredAnchorPositions =
        {
            new Vector2(-10f, 5f),
            new Vector2(-5f, -36f),
            new Vector2(55f, 10f),
            new Vector2(68f, -15f),
            new Vector2(39f, -63f),
            new Vector2(88f, -75f)
        };

        int playerLayer = LayerMask.NameToLayer("Player");
        int groundMaskValue = Physics.DefaultRaycastLayers;
        if (playerLayer >= 0)
        {
            groundMaskValue &= ~(1 << playerLayer);
        }

        LayerMask groundMask = groundMaskValue;
        List<PropHuntZoneAnchor> configuredAnchors = new List<PropHuntZoneAnchor>();
        for (int index = 0; index < desiredAnchorPositions.Length; index++)
        {
            string anchorName = $"ZoneAnchor_{index + 1:00}";
            GameObject anchorObject = CreateSceneChild(anchorsRoot.transform, anchorName);
            Vector2 desired = desiredAnchorPositions[index];
            Vector3 target = new Vector3(
                desired.x,
                mapBounds.max.y + 20f,
                desired.y);

            if (TryProjectAnchorToGround(target, mapBounds, groundMask, out Vector3 groundedPosition))
            {
                anchorObject.transform.position = groundedPosition;
            }
            else
            {
                anchorObject.transform.position = new Vector3(target.x, mapBounds.min.y + 0.1f, target.z);
                Debug.LogWarning($"HiderCompleteHUDSetupTool: {anchorName} could not find ground and uses fallback Y.");
            }

            anchorObject.transform.rotation = Quaternion.identity;
            anchorObject.transform.localScale = Vector3.one;
            PropHuntZoneAnchor anchor = GetOrAddComponent<PropHuntZoneAnchor>(anchorObject);
            anchor.Configure(playableArea, groundMask, 15f);
            if (!anchor.ValidateAnchor(out string validationMessage))
            {
                Debug.LogWarning($"HiderCompleteHUDSetupTool: {anchorName} invalid: {validationMessage}.");
            }
            else
            {
                string areaEvaluation = EvaluateAnchorArea(anchorObject.transform.position, 15f, players, zoneRoot.transform);
                Debug.Log($"HiderCompleteHUDSetupTool: {anchorName} position={anchorObject.transform.position:F2}, {validationMessage}, {areaEvaluation}.");
            }

            configuredAnchors.Add(anchor);
            EditorUtility.SetDirty(anchorObject);
            EditorUtility.SetDirty(anchor);
        }

        shrinkingZone = GetOrAddComponent<PropHuntShrinkingZone>(zoneRoot);
        shrinkingZone.Configure(roundManager, playableArea, boundary, domeVisual, configuredAnchors, mapBounds);

        localZoneStatus = null;
        foreach (PropTransformSystem player in players.Where(player => player != null && player.playerRole == PlayerRole.Hider))
        {
            HiderHealth health = GetOrAddComponent<HiderHealth>(player.gameObject);
            health.Configure(player, roundManager);
            HiderAntiCampSystem antiCamp = player.GetComponent<HiderAntiCampSystem>();
            HiderZoneStatusController status = GetOrAddComponent<HiderZoneStatusController>(player.gameObject);
            status.Configure(
                shrinkingZone,
                roundManager,
                health,
                player.GetComponent<CharacterController>(),
                player.transform,
                player,
                antiCamp
            );

            SerializedObject playerSerialized = new SerializedObject(player);
            SerializedProperty playableBoundsProperty = playerSerialized.FindProperty("playableAreaBounds");
            if (playableBoundsProperty != null)
            {
                playableBoundsProperty.objectReferenceValue = playableArea;
                playerSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            if (player == localHider)
            {
                localZoneStatus = status;
            }

            EditorUtility.SetDirty(health);
            EditorUtility.SetDirty(status);
            EditorUtility.SetDirty(player);
        }

        if (localZoneStatus == null)
        {
            localZoneStatus = localHider.GetComponent<HiderZoneStatusController>();
        }

        zoneAnchors = configuredAnchors.ToArray();
        EditorUtility.SetDirty(zoneRoot);
        EditorUtility.SetDirty(boundsCollider);
        EditorUtility.SetDirty(boundary);
        EditorUtility.SetDirty(domeMeshFilter);
        EditorUtility.SetDirty(domeMeshRenderer);
        EditorUtility.SetDirty(domeVisual);
        EditorUtility.SetDirty(shrinkingZone);
    }

    private static Bounds CalculateStaticMapBounds(
        IEnumerable<PropTransformSystem> players,
        Transform zoneRoot)
    {
        HashSet<Transform> excludedPlayers = new HashSet<Transform>(
            players.Where(player => player != null).Select(player => player.transform));
        bool hasBounds = false;
        Bounds combined = default;

        foreach (Collider collider in UnityEngine.Object.FindObjectsOfType<Collider>(true))
        {
            if (collider == null || !collider.enabled || collider.isTrigger || collider.attachedRigidbody != null ||
                collider.bounds.size.sqrMagnitude <= 0.0001f ||
                (zoneRoot != null && (collider.transform == zoneRoot || collider.transform.IsChildOf(zoneRoot))) ||
                excludedPlayers.Any(player => collider.transform == player || collider.transform.IsChildOf(player)))
            {
                continue;
            }

            if (!hasBounds)
            {
                combined = collider.bounds;
                hasBounds = true;
            }
            else
            {
                combined.Encapsulate(collider.bounds);
            }
        }

        if (hasBounds)
        {
            return combined;
        }

        Debug.LogWarning("HiderCompleteHUDSetupTool: static map colliders were not found; fallback PlayableArea bounds are used.");
        return new Bounds(new Vector3(34.7f, 8.5f, -23.8f), new Vector3(151.1f, 24f, 162.5f));
    }

    private static bool TryProjectAnchorToGround(
        Vector3 target,
        Bounds mapBounds,
        LayerMask groundMask,
        out Vector3 groundedPosition)
    {
        Vector2[] searchOffsets =
        {
            Vector2.zero,
            new Vector2(-5f, 0f), new Vector2(5f, 0f),
            new Vector2(0f, -5f), new Vector2(0f, 5f),
            new Vector2(-5f, -5f), new Vector2(-5f, 5f),
            new Vector2(5f, -5f), new Vector2(5f, 5f),
            new Vector2(-10f, 0f), new Vector2(10f, 0f),
            new Vector2(0f, -10f), new Vector2(0f, 10f)
        };

        foreach (Vector2 offset in searchOffsets)
        {
            Vector3 probe = target + new Vector3(offset.x, 0f, offset.y);
            if (probe.x < mapBounds.min.x + 15f || probe.x > mapBounds.max.x - 15f ||
                probe.z < mapBounds.min.z + 15f || probe.z > mapBounds.max.z - 15f)
            {
                continue;
            }

            RaycastHit[] hits = Physics.RaycastAll(
                probe,
                Vector3.down,
                mapBounds.size.y + 60f,
                groundMask,
                QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits
                         .Where(hit => IsPreferredAnchorGround(hit.collider))
                         .OrderBy(hit => hit.distance))
            {
                if (hit.collider == null || hit.collider.GetComponentInParent<PropTransformSystem>() != null ||
                    Vector3.Dot(hit.normal, Vector3.up) < 0.65f)
                {
                    continue;
                }

                Vector3 point = hit.point + Vector3.up * 0.08f;
                Collider[] capsuleOverlaps = Physics.OverlapCapsule(
                    point + Vector3.up * 0.35f,
                    point + Vector3.up * 1.75f,
                    0.45f,
                    groundMask,
                    QueryTriggerInteraction.Ignore);
                bool blocked = capsuleOverlaps.Any(overlap => overlap != null && overlap != hit.collider);
                if (blocked)
                {
                    continue;
                }

                groundedPosition = point;
                return true;
            }
        }

        groundedPosition = default;
        return false;
    }

    private static bool IsPreferredAnchorGround(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        string colliderName = collider.name;
        return colliderName.IndexOf("Road_set", StringComparison.OrdinalIgnoreCase) >= 0 ||
               colliderName.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0 ||
               colliderName.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0 ||
               colliderName.IndexOf("terrain", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void CreateOrUpdateHud(
        PropTransformSystem hider,
        PropHuntRoundManager roundManager,
        HiderAbilityController abilityController,
        HiderAntiCampSystem antiCampSystem,
        HiderHealth hiderHealth,
        HiderRosterManager rosterManager,
        HiderEliminationController eliminationController,
        HiderSpectatorController spectatorController,
        PropHuntTestRoleSelector roleSelector,
        SeekerHealth seekerHealth,
        PropHuntShrinkingZone shrinkingZone,
        HiderZoneStatusController localZoneStatus)
    {
        GameObject canvasObject = FindOrCreateUniqueRoot("PropHuntHUDCanvas");
        canvasObject.SetActive(true);

        Canvas canvas = GetOrAddComponent<Canvas>(canvasObject);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.targetDisplay = 0;
        canvas.sortingOrder = 100;
        canvas.enabled = true;

        CanvasScaler scaler = GetOrAddComponent<CanvasScaler>(canvasObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100f;

        GraphicRaycaster raycaster = GetOrAddComponent<GraphicRaycaster>(canvasObject);
        raycaster.enabled = false;

        RectTransform canvasRect = GetOrAddComponent<RectTransform>(canvasObject);
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;
        canvasRect.localScale = Vector3.one;

        CreateTopRoundBar(
            canvasObject.transform,
            out TextMeshProUGUI seekerCountText,
            out TextMeshProUGUI timerText,
            out TextMeshProUGUI hiderCountText
        );
        CreateContextPanel(canvasObject.transform, out GameObject contextPanel, out TextMeshProUGUI contextText);
        CreateAbilityPanel(
            canvasObject.transform,
            out GameObject abilityPanel,
            out CanvasGroup cloneGroup,
            out CanvasGroup antiCampGroup,
            out CanvasGroup randomGroup,
            out TextMeshProUGUI cloneChargeText,
            out TextMeshProUGUI randomChargeText,
            out TextMeshProUGUI antiCampCountdownText,
            out Image randomCooldown,
            out Image cloneIcon,
            out Image antiCampIcon,
            out Image randomIcon
        );
        CreateHealthBar(
            canvasObject.transform,
            "HiderHealthBar",
            "HealthFill",
            "HealthText",
            out GameObject healthBar,
            out Image healthFill,
            out TextMeshProUGUI healthText
        );

        GameObject seekerHudRoot = roleSelector != null ? roleSelector.SeekerHudRoot : null;
        if (seekerHudRoot == null)
        {
            seekerHudRoot = CreateUniqueNamedChild(canvasObject.transform, "SeekerHUDRoot");
        }
        else
        {
            Undo.SetTransformParent(seekerHudRoot.transform, canvasObject.transform, "Move SeekerHUDRoot to HUD canvas");
        }
        SetStretchRect(
            seekerHudRoot,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero);
        CreateHealthBar(
            seekerHudRoot.transform,
            "SeekerHealthBar",
            "SeekerHealthFill",
            "SeekerHealthText",
            out GameObject seekerHealthBar,
            out Image seekerHealthFill,
            out TextMeshProUGUI seekerHealthText
        );
        SeekerHealthBarController seekerHealthHud =
            GetOrAddUniqueComponent<SeekerHealthBarController>(seekerHealthBar);
        seekerHealthHud.Configure(seekerHealth, seekerHealthFill, seekerHealthText);
        roleSelector?.ConfigureHealthBars(healthBar, seekerHealthBar);
        CreateSpectatorStatusPanel(
            canvasObject.transform,
            out GameObject spectatorStatusPanel,
            out TextMeshProUGUI spectatorStatusText
        );
        CreateZoneHud(
            canvasObject.transform,
            out GameObject zoneWarningPanel,
            out Image zoneWarningBackground,
            out TextMeshProUGUI zoneWarningText,
            out Image zoneDamageFlash
        );

        PropHuntHUDController hud = GetOrAddComponent<PropHuntHUDController>(canvasObject);
        hud.Configure(
            roundManager,
            hider,
            abilityController,
            antiCampSystem,
            hiderHealth,
            rosterManager,
            eliminationController,
            spectatorController,
            roleSelector,
            seekerCountText,
            timerText,
            hiderCountText,
            contextPanel,
            contextText,
            cloneChargeText,
            randomChargeText,
            antiCampCountdownText,
            randomCooldown,
            cloneIcon,
            antiCampIcon,
            randomIcon,
            abilityPanel,
            cloneGroup,
            antiCampGroup,
            randomGroup,
            healthBar,
            healthFill,
            healthText,
            spectatorStatusPanel,
            spectatorStatusText
        );

        PropHuntZoneHUDController zoneHud = GetOrAddComponent<PropHuntZoneHUDController>(canvasObject);
        zoneHud.Configure(
            shrinkingZone,
            localZoneStatus,
            hider,
            zoneWarningPanel,
            zoneWarningBackground,
            zoneWarningText,
            zoneDamageFlash
        );

        EditorUtility.SetDirty(canvasObject);
        EditorUtility.SetDirty(hud);
        EditorUtility.SetDirty(seekerHudRoot);
        EditorUtility.SetDirty(seekerHealthBar);
        EditorUtility.SetDirty(seekerHealthHud);
        EditorUtility.SetDirty(zoneHud);
    }

    private static void CreateTopRoundBar(
        Transform canvas,
        out TextMeshProUGUI seekerCountText,
        out TextMeshProUGUI timerText,
        out TextMeshProUGUI hiderCountText)
    {
        GameObject bar = CreateChild(canvas, "TopRoundBar");
        SetRect(bar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(660f, 64f));
        HorizontalLayoutGroup layout = GetOrAddComponent<HorizontalLayoutGroup>(bar);
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 0f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        seekerCountText = CreateRoundPanel(bar.transform, "SeekerCounterPanel", "THỢ SĂN 02", SeekerColor, SeekerHighlight, SeekerShadow, 255f);
        timerText = CreateRoundPanel(bar.transform, "RoundTimerPanel", "00:40", TimerColor, TimerHighlight, TimerShadow, 150f);
        hiderCountText = CreateRoundPanel(bar.transform, "HiderCounterPanel", "ĐỒ VẬT 05", HiderColor, HiderHighlight, HiderShadow, 255f);
    }

    private static TextMeshProUGUI CreateRoundPanel(
        Transform parent,
        string name,
        string initialText,
        Color background,
        Color highlight,
        Color shadow,
        float width)
    {
        GameObject panel = CreateChild(parent, name);
        Image image = ConfigurePanelImage(panel, background, new Vector2(3f, -3f));
        LayoutElement layout = GetOrAddComponent<LayoutElement>(panel);
        layout.preferredWidth = width;
        layout.minWidth = width;
        layout.preferredHeight = 64f;
        layout.minHeight = 64f;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;

        GameObject top = CreateChild(panel.transform, "TopHighlight");
        SetStretchRect(top, new Vector2(0f, 0.58f), Vector2.one, new Vector2(0.5f, 1f), new Vector2(5f, 0f), new Vector2(-5f, -5f));
        Image topImage = GetOrAddComponent<Image>(top);
        topImage.color = new Color(highlight.r, highlight.g, highlight.b, 0.72f);
        topImage.raycastTarget = false;

        GameObject bottom = CreateChild(panel.transform, "BottomShade");
        SetStretchRect(bottom, Vector2.zero, new Vector2(1f, 0.35f), new Vector2(0.5f, 0f), new Vector2(5f, 5f), new Vector2(-5f, 0f));
        Image bottomImage = GetOrAddComponent<Image>(bottom);
        bottomImage.color = new Color(shadow.r, shadow.g, shadow.b, 0.78f);
        bottomImage.raycastTarget = false;

        TextMeshProUGUI text = CreateText(panel.transform, "ValueText", initialText, 27f, TextAlignmentOptions.Center);
        SetStretchRect(text.gameObject, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(7f, 2f), new Vector2(-7f, -2f));
        text.fontStyle = FontStyles.Bold;
        ApplyOutlineSafely(text, Hex("101010"), 0.18f);
        return text;
    }

    private static void CreateContextPanel(
        Transform canvas,
        out GameObject panel,
        out TextMeshProUGUI contextText)
    {
        panel = CreateChild(canvas, "HiderContextPanel");
        SetRect(panel, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(24f, 24f), new Vector2(290f, 92f));
        Image background = ConfigurePanelImage(panel, new Color(0f, 0f, 0f, 0.62f), new Vector2(1f, -1f));
        background.raycastTarget = false;
        CanvasGroup group = GetOrAddComponent<CanvasGroup>(panel);
        group.blocksRaycasts = false;
        group.interactable = false;

        contextText = CreateText(panel.transform, "ContextText", string.Empty, 20f, TextAlignmentOptions.MidlineLeft);
        SetStretchRect(contextText.gameObject, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(18f, 12f), new Vector2(-18f, -12f));
        contextText.richText = true;
        contextText.enableWordWrapping = false;
        contextText.lineSpacing = 2f;
        panel.SetActive(false);
    }

    private static void CreateAbilityPanel(
        Transform canvas,
        out GameObject abilityPanel,
        out CanvasGroup cloneGroup,
        out CanvasGroup antiCampGroup,
        out CanvasGroup randomGroup,
        out TextMeshProUGUI cloneCharge,
        out TextMeshProUGUI randomCharge,
        out TextMeshProUGUI antiCampCountdown,
        out Image randomCooldown,
        out Image cloneIcon,
        out Image antiCampIcon,
        out Image randomIcon)
    {
        abilityPanel = CreateChild(canvas, "HiderAbilityPanel");
        SetRect(abilityPanel, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 24f), new Vector2(332f, 104f));
        HorizontalLayoutGroup layout = GetOrAddComponent<HorizontalLayoutGroup>(abilityPanel);
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.LowerRight;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ReuseLegacySpeedCardAsCloneCard(abilityPanel.transform);
        cloneIcon = CreateAbilityCard(
            abilityPanel.transform,
            "CloneCard",
            CloneSpritePath,
            true,
            false,
            CloneCoverColor,
            out cloneGroup,
            out cloneCharge,
            out _,
            out _
        );
        antiCampIcon = CreateAbilityCard(
            abilityPanel.transform,
            "AntiCampCard",
            AntiCampSpritePath,
            false,
            false,
            Color.clear,
            out antiCampGroup,
            out _,
            out _,
            out antiCampCountdown
        );
        randomIcon = CreateAbilityCard(
            abilityPanel.transform,
            "RandomPropCard",
            RandomPropSpritePath,
            true,
            true,
            RandomCoverColor,
            out randomGroup,
            out randomCharge,
            out randomCooldown,
            out _
        );
    }

    private static void CreateHealthBar(
        Transform canvas,
        string healthBarName,
        string healthFillName,
        string healthTextName,
        out GameObject healthBar,
        out Image healthFill,
        out TextMeshProUGUI healthText)
    {
        healthBar = CreateUniqueNamedChild(canvas, healthBarName);
        SetRect(
            healthBar,
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(-28f, 140f),
            new Vector2(336f, 20f)
        );

        Image background = ConfigurePanelImage(healthBar, new Color(0f, 0f, 0f, 0.45f), new Vector2(1f, -1f));
        background.raycastTarget = false;

        UnityEngine.UI.Outline border = GetOrAddComponent<UnityEngine.UI.Outline>(healthBar);
        border.effectColor = new Color(1f, 1f, 1f, 0.32f);
        border.effectDistance = new Vector2(1f, -1f);
        border.useGraphicAlpha = false;

        CanvasGroup group = GetOrAddComponent<CanvasGroup>(healthBar);
        group.blocksRaycasts = false;
        group.interactable = false;

        GameObject fillObject = CreateChild(healthBar.transform, healthFillName);
        SetStretchRect(
            fillObject,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            new Vector2(2f, 2f),
            new Vector2(-2f, -2f)
        );
        healthFill = GetOrAddComponent<Image>(fillObject);
        healthFill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        healthFill.type = Image.Type.Filled;
        healthFill.fillMethod = Image.FillMethod.Horizontal;
        healthFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        healthFill.fillAmount = 1f;
        healthFill.color = new Color32(52, 199, 89, 255);
        healthFill.raycastTarget = false;

        healthText = CreateText(healthBar.transform, healthTextName, "100 / 100", 14f, TextAlignmentOptions.Center);
        SetStretchRect(
            healthText.gameObject,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            new Vector2(4f, 0f),
            new Vector2(-4f, 0f)
        );
        healthText.fontStyle = FontStyles.Bold;
        ApplyOutlineSafely(healthText, Color.black, 0.16f);
        healthText.transform.SetAsLastSibling();
    }

    private static void CreateSpectatorStatusPanel(
        Transform canvas,
        out GameObject statusPanel,
        out TextMeshProUGUI statusText)
    {
        statusPanel = CreateChild(canvas, "SpectatorStatusPanel");
        SetRect(
            statusPanel,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -148f),
            new Vector2(560f, 46f)
        );
        Image background = ConfigurePanelImage(
            statusPanel,
            new Color(0.02f, 0.08f, 0.1f, 0.82f),
            new Vector2(1f, -1f));
        background.raycastTarget = false;
        CanvasGroup group = GetOrAddComponent<CanvasGroup>(statusPanel);
        group.blocksRaycasts = false;
        group.interactable = false;

        statusText = CreateText(
            statusPanel.transform,
            "SpectatorStatusText",
            "ĐÃ BỊ LOẠI — ĐANG THEO DÕI",
            20f,
            TextAlignmentOptions.Center);
        SetStretchRect(
            statusText.gameObject,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            new Vector2(12f, 0f),
            new Vector2(-12f, 0f));
        statusText.fontStyle = FontStyles.Bold;
        ApplyOutlineSafely(statusText, Color.black, 0.14f);
        statusPanel.SetActive(false);
    }

    private static void CreateZoneHud(
        Transform canvas,
        out GameObject warningPanel,
        out Image warningBackground,
        out TextMeshProUGUI warningText,
        out Image damageFlash)
    {
        GameObject flashObject = CreateChild(canvas, "ZoneDamageFlash");
        SetStretchRect(
            flashObject,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero
        );
        damageFlash = GetOrAddComponent<Image>(flashObject);
        damageFlash.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        damageFlash.type = Image.Type.Sliced;
        damageFlash.color = new Color(1f, 0.03f, 0.02f, 0f);
        damageFlash.raycastTarget = false;
        flashObject.transform.SetAsFirstSibling();

        warningPanel = CreateChild(canvas, "ZoneWarningPanel");
        SetRect(
            warningPanel,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -88f),
            new Vector2(620f, 48f)
        );
        warningBackground = ConfigurePanelImage(
            warningPanel,
            new Color(0f, 0f, 0f, 0.68f),
            new Vector2(1f, -1f));
        warningBackground.raycastTarget = false;
        CanvasGroup warningGroup = GetOrAddComponent<CanvasGroup>(warningPanel);
        warningGroup.blocksRaycasts = false;
        warningGroup.interactable = false;

        warningText = CreateText(
            warningPanel.transform,
            "ZoneWarningText",
            string.Empty,
            22f,
            TextAlignmentOptions.Center);
        SetStretchRect(
            warningText.gameObject,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            new Vector2(16f, 4f),
            new Vector2(-16f, -4f)
        );
        warningText.fontStyle = FontStyles.Bold;
        ApplyOutlineSafely(warningText, Color.black, 0.18f);
        warningPanel.transform.SetAsLastSibling();
        warningPanel.SetActive(false);
    }

    private static void ReuseLegacySpeedCardAsCloneCard(Transform abilityPanel)
    {
        Transform cloneCard = abilityPanel.Find("CloneCard");
        Transform legacyCard = abilityPanel.Find("SpeedBoostCard");
        if (cloneCard == null && legacyCard != null)
        {
            legacyCard.name = "CloneCard";
            return;
        }

        if (cloneCard != null && legacyCard != null && cloneCard != legacyCard)
        {
            Undo.DestroyObjectImmediate(legacyCard.gameObject);
        }
    }

    private static Image CreateAbilityCard(
        Transform parent,
        string name,
        string spritePath,
        bool showCharge,
        bool showCooldown,
        Color coverColor,
        out CanvasGroup group,
        out TextMeshProUGUI chargeText,
        out Image cooldownOverlay,
        out TextMeshProUGUI countdownText)
    {
        GameObject card = CreateChild(parent, name);
        SetRect(card, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(104f, 104f));
        LayoutElement layout = GetOrAddComponent<LayoutElement>(card);
        layout.preferredWidth = 104f;
        layout.minWidth = 104f;
        layout.preferredHeight = 104f;
        layout.minHeight = 104f;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;

        group = GetOrAddComponent<CanvasGroup>(card);
        group.blocksRaycasts = false;
        group.interactable = false;

        Image icon = GetOrAddComponent<Image>(card);
        icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        icon.type = Image.Type.Simple;
        icon.preserveAspect = true;
        icon.color = Color.white;
        icon.raycastTarget = false;

        chargeText = null;
        if (showCharge)
        {
            GameObject cover = CreateChild(card.transform, "StaticChargeCover");
            SetRect(cover, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(70f, 4f), new Vector2(31f, 25f));
            Image coverImage = GetOrAddComponent<Image>(cover);
            coverImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            coverImage.type = Image.Type.Sliced;
            coverImage.color = coverColor;
            coverImage.raycastTarget = false;

            chargeText = CreateText(card.transform, "ChargeText", "x5", 18f, TextAlignmentOptions.Center);
            SetRect(chargeText.gameObject, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(70f, 3f), new Vector2(31f, 25f));
            chargeText.fontStyle = FontStyles.Bold;
            ApplyOutlineSafely(chargeText, Color.black, 0.18f);
        }

        cooldownOverlay = null;
        if (showCooldown)
        {
            GameObject cooldown = CreateChild(card.transform, "CooldownOverlay");
            SetStretchRect(cooldown, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            cooldownOverlay = GetOrAddComponent<Image>(cooldown);
            cooldownOverlay.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            cooldownOverlay.type = Image.Type.Filled;
            cooldownOverlay.fillMethod = Image.FillMethod.Radial360;
            cooldownOverlay.fillOrigin = (int)Image.Origin360.Top;
            cooldownOverlay.fillClockwise = false;
            cooldownOverlay.fillAmount = 0f;
            cooldownOverlay.color = new Color(0f, 0f, 0f, 0.55f);
            cooldownOverlay.raycastTarget = false;
        }
        else
        {
            Transform legacyCooldown = card.transform.Find("CooldownOverlay");
            if (legacyCooldown != null)
            {
                Undo.DestroyObjectImmediate(legacyCooldown.gameObject);
            }
        }

        countdownText = null;
        if (!showCharge)
        {
            countdownText = CreateText(card.transform, "AntiCampCountdownText", string.Empty, 48f, TextAlignmentOptions.Center);
            SetStretchRect(countdownText.gameObject, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            countdownText.fontStyle = FontStyles.Bold;
            ApplyOutlineSafely(countdownText, Color.black, 0.24f);
            countdownText.gameObject.SetActive(false);
        }

        if (chargeText != null) chargeText.transform.SetAsLastSibling();
        if (countdownText != null) countdownText.transform.SetAsLastSibling();
        return icon;
    }

    private static TextMeshProUGUI CreateText(
        Transform parent,
        string name,
        string value,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = CreateChild(parent, name);
        TextMeshProUGUI text = GetOrAddComponent<TextMeshProUGUI>(textObject);
        if (_fontAsset != null)
        {
            text.font = _fontAsset;
        }

        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = TextColor;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        return text;
    }

    private static void ApplyOutlineSafely(TextMeshProUGUI text, Color color, float width)
    {
        if (text.font != null && text.fontSharedMaterial != null)
        {
            try
            {
                text.outlineColor = color;
                text.outlineWidth = width;
                return;
            }
            catch (NullReferenceException)
            {
                // Inactive, newly created TMP objects can defer their material instance until OnEnable.
            }
        }

        if (!_fontMaterialWarningLogged)
        {
            Debug.LogWarning("HiderCompleteHUDSetupTool: TMP font material is unavailable; text outline was skipped and HUD setup will continue.");
            _fontMaterialWarningLogged = true;
        }
    }

    private static Image ConfigurePanelImage(GameObject panel, Color color, Vector2 outlineDistance)
    {
        Image image = GetOrAddComponent<Image>(panel);
        image.sprite = _bevelSprite != null
            ? _bevelSprite
            : AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;

        UnityEngine.UI.Outline outline = GetOrAddComponent<UnityEngine.UI.Outline>(panel);
        outline.effectColor = Border;
        outline.effectDistance = outlineDistance;
        outline.useGraphicAlpha = true;
        return image;
    }

    private static GameObject FindOrCreateUniqueRoot(string name)
    {
        GameObject[] matches = SceneManager.GetActiveScene().GetRootGameObjects()
            .Where(root => root.name == name)
            .ToArray();
        GameObject selected = matches.FirstOrDefault();
        for (int i = 1; i < matches.Length; i++)
        {
            Undo.DestroyObjectImmediate(matches[i]);
        }

        if (selected != null)
        {
            selected.transform.SetParent(null);
            return selected;
        }

        GameObject created = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(created, $"Create {name}");
        return created;
    }

    private static GameObject FindOrCreateUniqueSceneRoot(string name)
    {
        GameObject[] matches = SceneManager.GetActiveScene().GetRootGameObjects()
            .Where(root => root.name == name)
            .ToArray();
        GameObject selected = matches.FirstOrDefault();
        for (int index = 1; index < matches.Length; index++)
        {
            Undo.DestroyObjectImmediate(matches[index]);
        }

        if (selected == null)
        {
            selected = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(selected, $"Create {name}");
        }

        selected.transform.SetParent(null);
        return selected;
    }

    private static GameObject CreateSceneChild(Transform parent, string name)
    {
        GameObject existing = null;
        for (int index = parent.childCount - 1; index >= 0; index--)
        {
            Transform child = parent.GetChild(index);
            if (child.name != name)
            {
                continue;
            }

            if (existing == null)
            {
                existing = child.gameObject;
            }
            else
            {
                Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        if (existing == null)
        {
            existing = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(existing, $"Create {name}");
            Undo.SetTransformParent(existing.transform, parent, $"Parent {name}");
        }
        else
        {
            existing.transform.SetParent(parent, false);
        }

        existing.transform.localPosition = Vector3.zero;
        existing.transform.localRotation = Quaternion.identity;
        existing.transform.localScale = Vector3.one;
        return existing;
    }

    private static GameObject FindOrCreateZoneVisualChild(Transform parent, string desiredName, params string[] legacyNames)
    {
        HashSet<string> acceptedNames = new HashSet<string>(legacyNames) { desiredName };
        List<GameObject> matches = new List<GameObject>();
        for (int index = 0; index < parent.childCount; index++)
        {
            Transform child = parent.GetChild(index);
            if (acceptedNames.Contains(child.name))
            {
                matches.Add(child.gameObject);
            }
        }

        GameObject selected = matches.FirstOrDefault(match => match.name == desiredName) ?? matches.FirstOrDefault();
        foreach (GameObject match in matches)
        {
            if (match != selected)
            {
                Undo.DestroyObjectImmediate(match);
            }
        }

        if (selected == null)
        {
            selected = new GameObject(desiredName);
            Undo.RegisterCreatedObjectUndo(selected, $"Create {desiredName}");
            Undo.SetTransformParent(selected.transform, parent, $"Parent {desiredName}");
        }
        else
        {
            selected.name = desiredName;
            selected.transform.SetParent(parent, false);
        }

        selected.transform.localPosition = Vector3.zero;
        selected.transform.localRotation = Quaternion.identity;
        selected.transform.localScale = Vector3.one;
        return selected;
    }

    private static Mesh EnsureZoneDomeMesh()
    {
        Mesh generated = PropHuntZoneDomeVisual.CreateHemisphereMesh(64, 16, "PropHuntZoneDomeMesh_64x16");
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(ZoneDomeMeshPath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(generated, ZoneDomeMeshPath);
            return generated;
        }

        EditorUtility.CopySerialized(generated, existing);
        existing.name = "PropHuntZoneDomeMesh_64x16";
        UnityEngine.Object.DestroyImmediate(generated);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    private static Material EnsureZoneDomeMaterial(Shader shader)
    {
        if (shader == null || !shader.isSupported)
        {
            Debug.LogError("HiderCompleteHUDSetupTool: Built-in PropHuntZoneDome shader is missing or unsupported.");
            return null;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(ZoneDomeMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "PropHuntZoneDome" };
            AssetDatabase.CreateAsset(material, ZoneDomeMaterialPath);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        material.SetColor("_EnergyColor", new Color(0.04f, 0.76f, 1f, 0.85f));
        material.SetFloat("_BodyAlpha", 0.075f);
        material.SetFloat("_FresnelStrength", 1.1f);
        material.SetFloat("_StreakStrength", 1f);
        material.SetFloat("_PulseSpeed", 0.72f);
        material.SetFloat("_ScrollSpeed", 0.48f);
        material.SetFloat("_ShrinkIntensity", 0f);
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 40;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static string EvaluateAnchorArea(
        Vector3 center,
        float radius,
        IEnumerable<PropTransformSystem> players,
        Transform zoneRoot)
    {
        int propCount = UnityEngine.Object.FindObjectsOfType<PropTarget>(true)
            .Count(prop => IsValidOriginalPropDefinition(prop) &&
                           Vector2.Distance(
                               new Vector2(center.x, center.z),
                               new Vector2(prop.transform.position.x, prop.transform.position.z)) <= radius);

        HashSet<Transform> playerTransforms = new HashSet<Transform>(
            players.Where(player => player != null).Select(player => player.transform));
        int coverCount = Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Ignore)
            .Where(collider => collider != null && collider.enabled && !collider.isTrigger &&
                               !IsPreferredAnchorGround(collider) &&
                               (zoneRoot == null || !collider.transform.IsChildOf(zoneRoot)) &&
                               !playerTransforms.Any(player => collider.transform == player || collider.transform.IsChildOf(player)) &&
                               collider.bounds.size.y >= 1f &&
                               Mathf.Max(collider.bounds.size.x, collider.bounds.size.z) >= 2f &&
                               collider.bounds.size.x <= 40f && collider.bounds.size.z <= 40f)
            .Select(collider => collider.transform)
            .Distinct()
            .Count();

        Vector2[] directions = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };
        int approachCount = directions.Count(direction => HasAccessibleAnchorApproach(center, direction, radius));
        bool passes = propCount >= 8 && coverCount >= 2 && approachCount >= 2;
        return $"Props={propCount}/8, Covers={coverCount}/2, Approaches={approachCount}/2, Area={(passes ? "PASS" : "REVIEW")}";
    }

    private static bool HasAccessibleAnchorApproach(Vector3 center, Vector2 direction, float radius)
    {
        Vector3 sample = center + new Vector3(direction.x, 0f, direction.y) * (radius * 0.72f);
        Vector3 origin = sample + Vector3.up * 12f;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                     .Where(hit => IsPreferredAnchorGround(hit.collider))
                     .OrderBy(hit => hit.distance))
        {
            if (Vector3.Dot(hit.normal, Vector3.up) < 0.65f) continue;
            Vector3 point = hit.point + Vector3.up * 0.08f;
            bool blocked = Physics.OverlapCapsule(
                    point + Vector3.up * 0.35f,
                    point + Vector3.up * 1.75f,
                    0.45f,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore)
                .Any(overlap => overlap != null && overlap != hit.collider);
            if (!blocked) return true;
        }

        return false;
    }

    private static void ClearChildren(Transform parent)
    {
        for (int index = parent.childCount - 1; index >= 0; index--)
        {
            Undo.DestroyObjectImmediate(parent.GetChild(index).gameObject);
        }
    }

    private static GameObject CreateChild(Transform parent, string name)
    {
        GameObject existing = null;
        for (int index = parent.childCount - 1; index >= 0; index--)
        {
            Transform existingChild = parent.GetChild(index);
            if (existingChild.name != name)
            {
                continue;
            }

            if (existing == null)
            {
                existing = existingChild.gameObject;
            }
            else
            {
                Undo.DestroyObjectImmediate(existingChild.gameObject);
            }
        }

        if (existing != null)
        {
            existing.transform.SetParent(parent, false);
            existing.transform.localScale = Vector3.one;
            return existing;
        }

        GameObject createdChild = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(createdChild, $"Create {name}");
        Undo.SetTransformParent(createdChild.transform, parent, $"Parent {name}");
        createdChild.transform.localScale = Vector3.one;
        return createdChild;
    }

    private static GameObject CreateUniqueNamedChild(Transform parent, string name)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        GameObject[] matches = UnityEngine.Object.FindObjectsOfType<Transform>(true)
            .Where(transform => transform != null && transform.name == name &&
                                transform.gameObject.scene == activeScene)
            .Select(transform => transform.gameObject)
            .ToArray();
        GameObject selected = matches.FirstOrDefault(match => match.transform.parent == parent) ??
                              matches.FirstOrDefault();

        foreach (GameObject match in matches)
        {
            if (match != null && match != selected)
            {
                Undo.DestroyObjectImmediate(match);
            }
        }

        if (selected == null)
        {
            selected = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(selected, $"Create {name}");
        }

        Undo.SetTransformParent(selected.transform, parent, $"Parent {name}");
        selected.transform.localScale = Vector3.one;
        return selected;
    }

    private static GameObject GetOrCreatePlainChild(Transform parent, string name)
    {
        GameObject selected = null;
        for (int index = parent.childCount - 1; index >= 0; index--)
        {
            Transform child = parent.GetChild(index);
            if (child.name != name) continue;
            if (selected == null) selected = child.gameObject;
            else Undo.DestroyObjectImmediate(child.gameObject);
        }

        if (selected == null)
        {
            selected = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(selected, $"Create {name}");
            Undo.SetTransformParent(selected.transform, parent, $"Parent {name}");
        }
        else
        {
            selected.transform.SetParent(parent, false);
        }

        return selected;
    }

    private static void SetRect(
        GameObject target,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 sizeDelta)
    {
        RectTransform rect = GetOrAddComponent<RectTransform>(target);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        rect.localScale = Vector3.one;
    }

    private static void SetStretchRect(
        GameObject target,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        RectTransform rect = GetOrAddComponent<RectTransform>(target);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        rect.localScale = Vector3.one;
    }

    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }

    private static T GetOrAddUniqueComponent<T>(GameObject target) where T : Component
    {
        T[] components = target.GetComponents<T>();
        T selected = components.FirstOrDefault(component => component != null);
        for (int i = 1; i < components.Length; i++)
        {
            if (components[i] != null) Undo.DestroyObjectImmediate(components[i]);
        }

        return selected != null ? selected : Undo.AddComponent<T>(target);
    }

    private static void EnsureAssetFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/UI"))
        {
            AssetDatabase.CreateFolder("Assets", "UI");
        }

        if (!AssetDatabase.IsValidFolder(HudAssetFolder))
        {
            AssetDatabase.CreateFolder("Assets/UI", "HiderHUD");
        }
    }

    private static void ConfigureAbilitySpriteImport(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"HiderCompleteHUDSetupTool: required ability image is missing at '{assetPath}'.");
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
    }

    private static Sprite EnsureBevelSprite()
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(BevelSpritePath);
        if (existing != null)
        {
            return existing;
        }

        const int size = 32;
        const int cut = 6;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool outside = x + y < cut ||
                               (size - 1 - x) + y < cut ||
                               x + (size - 1 - y) < cut ||
                               (size - 1 - x) + (size - 1 - y) < cut;
                pixels[y * size + x] = outside
                    ? new Color32(255, 255, 255, 0)
                    : new Color32(255, 255, 255, 255);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        File.WriteAllBytes(BevelSpritePath, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(BevelSpritePath, ImportAssetOptions.ForceSynchronousImport);

        TextureImporter importer = AssetImporter.GetAtPath(BevelSpritePath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = new Vector4(8f, 8f, 8f, 8f);
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(BevelSpritePath);
    }

    private static TMP_FontAsset FindVietnameseFontAsset()
    {
        string[] preferredNames =
        {
            "Roboto Condensed Bold", "Oswald Bold", "Anton", "Bebas Neue", "Noto Sans Bold"
        };
        string vietnameseCharacters = "ĐđĂăÂâÊêÔôƠơƯư";
        List<TMP_FontAsset> fonts = AssetDatabase.FindAssets("t:TMP_FontAsset")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>)
            .Where(font => font != null)
            .ToList();

        TMP_FontAsset best = null;
        int bestScore = int.MinValue;
        foreach (TMP_FontAsset font in fonts)
        {
            if (!font.HasCharacters(vietnameseCharacters))
            {
                continue;
            }

            int score = 1000;
            int preferredIndex = Array.FindIndex(preferredNames,
                preferred => font.name.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0);
            if (preferredIndex >= 0) score += 100 - preferredIndex;
            if (score <= bestScore) continue;
            best = font;
            bestScore = score;
        }

        return best;
    }

    private static TMP_FontAsset GetDefaultFontAssetSafely()
    {
        try
        {
            return TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"HiderCompleteHUDSetupTool: TMP default font is unavailable ({exception.Message}); setup will continue without outline.");
            return null;
        }
    }

    private static void EnsureTmpEssentialResources()
    {
        const string settingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        if (AssetDatabase.LoadAssetAtPath<TMP_Settings>(settingsPath) != null)
        {
            return;
        }

        string packagePath = Directory
            .GetFiles("Library/PackageCache", "TMP Essential Resources.unitypackage", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(packagePath))
        {
            Debug.LogWarning("HiderCompleteHUDSetupTool: TMP Essential Resources package was not found; safe font fallback will be used.");
            return;
        }

        AssetDatabase.ImportPackage(Path.GetFullPath(packagePath), false);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }

    private static TMP_FontAsset CreateDynamicVietnameseFontAsset()
    {
        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GeneratedFontPath);
        if (existing != null)
        {
            return existing;
        }

        const string sourceFontPath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";
        TrueTypeFontImporter sourceImporter = AssetImporter.GetAtPath(sourceFontPath) as TrueTypeFontImporter;
        if (sourceImporter != null && !sourceImporter.includeFontData)
        {
            sourceImporter.includeFontData = true;
            sourceImporter.SaveAndReimport();
        }

        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(sourceFontPath);
        if (sourceFont == null)
        {
            Debug.LogWarning("HiderCompleteHUDSetupTool: LiberationSans source font is unavailable.");
            return null;
        }

        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont);
        if (fontAsset == null)
        {
            return null;
        }

        fontAsset.name = "HiderVietnameseDynamic";
        fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        AssetDatabase.CreateAsset(fontAsset, GeneratedFontPath);

        if (fontAsset.material != null && !EditorUtility.IsPersistent(fontAsset.material))
        {
            fontAsset.material.name = "HiderVietnameseDynamic Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        foreach (Texture2D atlasTexture in fontAsset.atlasTextures)
        {
            if (atlasTexture != null && !EditorUtility.IsPersistent(atlasTexture))
            {
                atlasTexture.name = "HiderVietnameseDynamic Atlas";
                AssetDatabase.AddObjectToAsset(atlasTexture, fontAsset);
            }
        }

        fontAsset.TryAddCharacters("ĐđĂăÂâÊêÔôƠơƯưÁÀẢÃẠÉÈẺẼẸÍÌỈĨỊÓÒỎÕỌÚÙỦŨỤÝỲỶỸỴ");
        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        return fontAsset;
    }

    private static Color Hex(string hex)
    {
        return ColorUtility.TryParseHtmlString($"#{hex}", out Color color) ? color : Color.white;
    }
}
