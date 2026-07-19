using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class PropHuntRoleTestValidationTool
{
    private const string MapV2Path = "Assets/Scenes/Map_v2.unity";
    private const string SmokeRunningKey = "PropHunt.RoleTestSmokeRunning";
    private const string SmokeResultKey = "PropHunt.RoleTestSmokeResult";
    private static readonly List<string> SmokeFailures = new List<string>();

    static PropHuntRoleTestValidationTool()
    {
        if (SessionState.GetBool(SmokeRunningKey, false))
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        }
    }

    [MenuItem("Tools/Prop Hunt/Setup HUD Reveal Twice And Validate")]
    public static void SetupHudRevealTwiceAndValidate()
    {
        HiderCompleteHUDSetupTool.SetupHiderCompleteHud();
        HiderCompleteHUDSetupTool.SetupHiderCompleteHud();
        ValidateScene();
    }

    [MenuItem("Tools/Prop Hunt/Validate Role Selector And Seeker")]
    public static void ValidateScene()
    {
        EditorSceneManager.OpenScene(MapV2Path, OpenSceneMode.Single);
        List<string> failures = new List<string>();
        PropHuntTestRoleSelector[] selectors =
            UnityEngine.Object.FindObjectsOfType<PropHuntTestRoleSelector>(true);
        SeekerFirstPersonController[] seekerControllers =
            UnityEngine.Object.FindObjectsOfType<SeekerFirstPersonController>(true);
        SeekerRaycastWeapon[] weapons =
            UnityEngine.Object.FindObjectsOfType<SeekerRaycastWeapon>(true);
        PropTransformSystem hider = UnityEngine.Object.FindObjectsOfType<PropTransformSystem>(true)
            .FirstOrDefault(player => player.playerRole == PlayerRole.Hider);
        GameObject seekerPlayer = FindNamedObject("SeekerPlayer");
        GameObject seekerWorldVisualRoot = FindNamedObject("SeekerWorldVisualRoot");
        GameObject industrialSeekerModel = FindNamedObject("IndustrialSeekerModel");
        GameObject cyberSoldierModel = FindNamedObject("CyberSoldierModel");
        GameObject roleCanvasObject = FindNamedObject("PropHuntRoleSelectionCanvas");
        GameObject rolePanel = FindNamedObject("RoleSelectionPanel");
        GameObject instructionPanel = FindNamedObject("SeekerInstructionPanel");
        GameObject gunPlaceholder = FindNamedObject("GunPlaceholder");
        GameObject pulseTaggerVisual = FindNamedObject("PulseTaggerVisual");
        GameObject hiderSpawn = FindNamedObject("HiderTestSpawnPoint");
        GameObject seekerSpawn = FindNamedObject("SeekerSpawnPoint");
        GameObject hudCanvas = FindNamedObject("PropHuntHUDCanvas");
        GameObject hiderHealthBar = FindNamedObject("HiderHealthBar");
        GameObject seekerHudRoot = FindNamedObject("SeekerHUDRoot");
        GameObject seekerHealthBar = FindNamedObject("SeekerHealthBar");
        Image hiderHealthFill = FindNamedObject("HealthFill")?.GetComponent<Image>();
        TextMeshProUGUI hiderHealthText = FindNamedObject("HealthText")?.GetComponent<TextMeshProUGUI>();
        Image seekerHealthFill = FindNamedObject("SeekerHealthFill")?.GetComponent<Image>();
        TextMeshProUGUI seekerHealthText = FindNamedObject("SeekerHealthText")?.GetComponent<TextMeshProUGUI>();
        PropHuntHUDController hiderHud = hudCanvas != null
            ? hudCanvas.GetComponent<PropHuntHUDController>()
            : null;
        SeekerHealth[] seekerHealthComponents =
            UnityEngine.Object.FindObjectsOfType<SeekerHealth>(true);
        SeekerHealthBarController seekerHealthHud = seekerHealthBar != null
            ? seekerHealthBar.GetComponent<SeekerHealthBarController>()
            : null;
        Button[] roleButtons = rolePanel != null
            ? rolePanel.GetComponentsInChildren<Button>(true)
            : Array.Empty<Button>();
        Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>(true);
        AudioListener[] listeners = UnityEngine.Object.FindObjectsOfType<AudioListener>(true);
        int activeCameras = cameras.Count(camera => camera.enabled && camera.gameObject.activeInHierarchy);
        int enabledListeners = listeners.Count(listener => listener.enabled && listener.gameObject.activeInHierarchy);

        Require(selectors.Length == 1, $"Expected 1 role selector, found {selectors.Length}.", failures);
        Require(seekerControllers.Length == 1,
            $"Expected 1 SeekerFirstPersonController, found {seekerControllers.Length}.", failures);
        Require(weapons.Length == 1, $"Expected 1 SeekerRaycastWeapon, found {weapons.Length}.", failures);
        Require(CountNamedObjects("SeekerPlayer") == 1, "SeekerPlayer is missing or duplicated.", failures);
        Require(CountNamedObjects("SeekerWorldVisualRoot") == 1,
            "SeekerWorldVisualRoot is missing or duplicated.", failures);
        Require(CountNamedObjects("IndustrialSeekerModel") == 1,
            "IndustrialSeekerModel is missing or duplicated.", failures);
        Require(CountNamedObjects("CyberSoldierModel") == 1,
            "CyberSoldierModel is missing or duplicated.", failures);
        Require(CountNamedObjects("SeekerCameraRoot") == 1,
            "SeekerCameraRoot is missing or duplicated.", failures);
        Require(CountNamedObjects("SeekerCamera") == 1, "SeekerCamera is missing or duplicated.", failures);
        Require(CountNamedObjects("PropHuntRoleSelectionCanvas") == 1,
            "Role selection canvas is missing or duplicated.", failures);
        Require(CountNamedObjects("RoleSelectionPanel") == 1,
            "RoleSelectionPanel is missing or duplicated.", failures);
        Require(CountNamedObjects("SeekerCrosshair") == 1,
            "Seeker crosshair is missing or duplicated.", failures);
        Require(CountNamedObjects("SeekerInstructionPanel") == 1,
            "SeekerInstructionPanel is missing or duplicated.", failures);
        Require(CountNamedObjects("WeaponHolder") == 1,
            "WeaponHolder is missing or duplicated.", failures);
        Require(CountNamedObjects("PulseTaggerVisual") == 1,
            "PulseTaggerVisual is missing or duplicated.", failures);
        Require(CountNamedObjects("GunPlaceholder") == 1,
            "GunPlaceholder is missing or duplicated.", failures);
        Require(CountNamedObjects("PropHuntRoleTestSpawns") == 1,
            "PropHuntRoleTestSpawns is missing or duplicated.", failures);
        Require(CountNamedObjects("HiderTestSpawnPoint") == 1,
            "HiderTestSpawnPoint is missing or duplicated.", failures);
        Require(CountNamedObjects("SeekerSpawnPoint") == 1,
            "SeekerSpawnPoint is missing or duplicated.", failures);
        Require(CountNamedObjects("HiderHealthBar") == 1,
            "HiderHealthBar is missing or duplicated.", failures);
        Require(CountNamedObjects("SeekerHealthBar") == 1,
            "SeekerHealthBar is missing or duplicated.", failures);
        Require(CountNamedObjects("SeekerHealthFill") == 1,
            "SeekerHealthFill is missing or duplicated.", failures);
        Require(CountNamedObjects("SeekerHealthText") == 1,
            "SeekerHealthText is missing or duplicated.", failures);
        Require(hiderSpawn != null && seekerSpawn != null &&
                Vector3.Distance(hiderSpawn.transform.position, seekerSpawn.transform.position) >= 2f,
            "HiderTestSpawnPoint and SeekerSpawnPoint overlap.", failures);
        Require(CountNamedObjects("PropHuntRoleTestManager") == 1,
            "Role test manager is missing or duplicated.", failures);
        Require(roleButtons.Length == 2,
            $"RoleSelectionPanel must contain exactly 2 buttons, found {roleButtons.Length}.", failures);
        Require(FindButtonLabel(roleButtons, "HiderRoleButton") == "VAI TRÒ HIDER",
            "Hider role button label is incorrect.", failures);
        Require(FindButtonLabel(roleButtons, "SeekerRoleButton") == "VAI TRÒ SEEKER",
            "Seeker role button label is incorrect.", failures);
        TextMeshProUGUI title = FindNamedObject("RoleSelectionTitle")?.GetComponent<TextMeshProUGUI>();
        Require(title != null && title.text == "CHỌN VAI TRÒ KIỂM THỬ",
            "Role selection title is missing or incorrect.", failures);

        Require(seekerPlayer != null && hider != null && seekerPlayer != hider.gameObject,
            "SeekerPlayer is not separate from the Hider PlayerCapsule.", failures);
        Require(seekerPlayer != null && seekerPlayer.GetComponent<CharacterController>() != null,
            "Seeker CharacterController is missing.", failures);
        Require(seekerPlayer != null && seekerPlayer.GetComponent<PropTransformSystem>() == null,
            "SeekerPlayer must not replace/register as the Hider PropTransformSystem.", failures);
        Require(seekerWorldVisualRoot != null && seekerWorldVisualRoot.transform.parent == seekerPlayer?.transform,
            "SeekerWorldVisualRoot must be directly under SeekerPlayer.", failures);
        Require(industrialSeekerModel != null && industrialSeekerModel.transform.parent == seekerWorldVisualRoot?.transform,
            "IndustrialSeekerModel must be directly under SeekerWorldVisualRoot.", failures);
        Require(industrialSeekerModel != null && !industrialSeekerModel.activeSelf,
            "IndustrialSeekerModel must remain as an inactive fallback.", failures);
        Require(cyberSoldierModel != null && cyberSoldierModel.transform.parent == seekerWorldVisualRoot?.transform &&
                cyberSoldierModel.activeSelf && cyberSoldierModel.GetComponentInParent<Camera>() == null,
            "CyberSoldierModel must be the active world visual under SeekerWorldVisualRoot.", failures);
        Require(cyberSoldierModel != null &&
                cyberSoldierModel.GetComponentsInChildren<Renderer>(true).Any(renderer => renderer.enabled),
            "CyberSoldierModel must have an enabled humanoid renderer.", failures);
        Require(cyberSoldierModel != null &&
                cyberSoldierModel.GetComponentsInChildren<Collider>(true).Length == 0 &&
                cyberSoldierModel.GetComponentsInChildren<Rigidbody>(true).Length == 0 &&
                cyberSoldierModel.GetComponentsInChildren<Camera>(true).Length == 0 &&
                cyberSoldierModel.GetComponentsInChildren<AudioListener>(true).Length == 0,
            "CyberSoldierModel must not carry gameplay physics, a camera or AudioListener.", failures);
        Animator seekerAnimator = cyberSoldierModel != null
            ? cyberSoldierModel.GetComponentInChildren<Animator>(true)
            : null;
        Require(seekerAnimator != null && seekerAnimator.isHuman && !seekerAnimator.applyRootMotion,
            "CyberSoldierModel must use its Humanoid Animator with Apply Root Motion disabled.", failures);
        Require(instructionPanel != null && instructionPanel.transform.parent != null &&
                instructionPanel.transform.parent.name == "SeekerHUDRoot",
            "SeekerInstructionPanel is not directly under SeekerHUDRoot.", failures);
        Require(hudCanvas != null && hiderHealthBar != null &&
                hiderHealthBar.transform.parent == hudCanvas.transform,
            "HiderHealthBar must be directly under PropHuntHUDCanvas.", failures);
        Require(hudCanvas != null && seekerHudRoot != null &&
                seekerHudRoot.transform.parent == hudCanvas.transform,
            "SeekerHUDRoot must be directly under PropHuntHUDCanvas.", failures);
        Require(seekerHealthBar != null && seekerHealthBar.transform.parent == seekerHudRoot?.transform,
            "SeekerHealthBar must be directly under SeekerHUDRoot.", failures);
        Require(hiderHealthFill != null && seekerHealthFill != null && hiderHealthFill != seekerHealthFill,
            "Hider and Seeker health bars share the same Fill Image.", failures);
        Require(hiderHealthText != null && seekerHealthText != null && hiderHealthText != seekerHealthText,
            "Hider and Seeker health bars share the same Text component.", failures);

        HiderHealth configuredHiderHealth = hider != null ? hider.GetComponent<HiderHealth>() : null;
        if (hiderHud != null)
        {
            SerializedObject serializedHud = new SerializedObject(hiderHud);
            Require(serializedHud.FindProperty("hiderHealth")?.objectReferenceValue == configuredHiderHealth,
                "HiderHealthBar is not bound to HiderHealth.", failures);
            Require(serializedHud.FindProperty("hiderHealthBar")?.objectReferenceValue == hiderHealthBar,
                "Hider HUD controller does not reference HiderHealthBar.", failures);
            Require(serializedHud.FindProperty("hiderHealthFill")?.objectReferenceValue == hiderHealthFill,
                "Hider HUD controller does not reference HealthFill.", failures);
            Require(serializedHud.FindProperty("hiderHealthText")?.objectReferenceValue == hiderHealthText,
                "Hider HUD controller does not reference HealthText.", failures);
        }
        else
        {
            failures.Add("PropHuntHUDController is missing from PropHuntHUDCanvas.");
        }

        Require(seekerHealthComponents.Length == 1,
            $"Expected exactly one SeekerHealth, found {seekerHealthComponents.Length}.", failures);
        SeekerHealth configuredSeekerHealth = seekerHealthComponents.FirstOrDefault();
        Require(configuredSeekerHealth != null && configuredSeekerHealth.gameObject == seekerPlayer,
            "SeekerHealth must be attached to SeekerPlayer.", failures);
        Require(configuredSeekerHealth != null && configuredSeekerHealth.MaxHealth == 100 &&
                configuredSeekerHealth.CurrentHealth == 100 && configuredSeekerHealth.IsAlive,
            "SeekerHealth is not reset to 100/100.", failures);
        Require(seekerHealthHud != null && seekerHealthHud.HealthSource == configuredSeekerHealth,
            "SeekerHealthBar is not bound to SeekerHealth.", failures);
        Require(seekerHealthHud != null && seekerHealthHud.HealthFill == seekerHealthFill &&
                seekerHealthHud.HealthText == seekerHealthText,
            "SeekerHealthBar controller references are incomplete.", failures);
        TextMeshProUGUI instructionText = FindNamedObject("SeekerInstructionText")
            ?.GetComponent<TextMeshProUGUI>();
        Require(instructionText != null && instructionText.text == "F1: VAI TR\u00d2 HIDER",
            "Seeker instruction text is missing or incorrect.", failures);
        Require(instructionPanel != null &&
                instructionPanel.GetComponent<RectTransform>().rect.height <= 60f,
            "SeekerInstructionPanel was not reduced to one-line height.", failures);
        Require(gunPlaceholder != null && !gunPlaceholder.activeSelf,
            "Legacy GunPlaceholder must remain disabled.", failures);
        Require(pulseTaggerVisual != null && pulseTaggerVisual.transform.parent != null &&
                pulseTaggerVisual.transform.parent.name == "WeaponHolder" &&
                pulseTaggerVisual.transform.parent.parent != null &&
                pulseTaggerVisual.transform.parent.parent.name == "SeekerCamera",
            "PulseTagger hierarchy must be SeekerCamera/WeaponHolder/PulseTaggerVisual.", failures);
        Require(pulseTaggerVisual != null && !pulseTaggerVisual.activeSelf,
            "PulseTaggerVisual must remain as an inactive fallback.", failures);
        Require(pulseTaggerVisual != null &&
                pulseTaggerVisual.GetComponentsInChildren<MeshFilter>(true).Length >= 4 &&
                pulseTaggerVisual.GetComponentsInChildren<Renderer>(true).Length >= 4,
            "PulseTaggerVisual must contain the low-poly visual parts.", failures);
        Require(pulseTaggerVisual != null &&
                pulseTaggerVisual.GetComponentsInChildren<Collider>(true).Length == 0 &&
                pulseTaggerVisual.GetComponentsInChildren<Rigidbody>(true).Length == 0,
            "PulseTaggerVisual must not have colliders or rigidbodies.", failures);
        Require(pulseTaggerVisual != null &&
                pulseTaggerVisual.GetComponentsInChildren<MonoBehaviour>(true).Length == 0,
            "PulseTaggerVisual must not contain gameplay scripts.", failures);
        CanvasGroup rolePanelGroup = rolePanel != null ? rolePanel.GetComponent<CanvasGroup>() : null;
        Require(roleCanvasObject != null && roleCanvasObject.activeSelf &&
                roleCanvasObject.GetComponent<Canvas>()?.renderMode == RenderMode.ScreenSpaceOverlay &&
                roleCanvasObject.GetComponent<Canvas>()?.sortingOrder >= 250,
            "Role canvas is not an active high-priority Screen Space Overlay canvas.", failures);
        Require(roleCanvasObject != null && roleCanvasObject.GetComponent<GraphicRaycaster>()?.enabled == true,
            "Role canvas GraphicRaycaster is missing or disabled.", failures);
        Require(rolePanelGroup != null && rolePanelGroup.alpha == 1f &&
                rolePanelGroup.interactable && rolePanelGroup.blocksRaycasts,
            "RoleSelectionPanel CanvasGroup cannot receive pointer input.", failures);
        Require(roleButtons.All(button => button.interactable && button.targetGraphic != null &&
                                          button.targetGraphic.raycastTarget),
            "One or more role buttons are not interactable/raycastable.", failures);

        if (weapons.Length == 1)
        {
            Require(Mathf.Approximately(weapons[0].Range, 50f), "Weapon range is not 50m.", failures);
            Require(weapons[0].Damage == 20, "Weapon damage is not 20.", failures);
            Require(Mathf.Approximately(weapons[0].Cooldown, 0.35f),
                "Weapon cooldown is not 0.35s.", failures);
            Require(weapons[0].AllowDebugWeaponDuringPreparation,
                "Weapon debug firing during Preparation is not enabled.", failures);
            int weaponExcludedLayer = LayerMask.NameToLayer("SeekerWorldVisual");
            if (weaponExcludedLayer >= 0)
            {
                Require((weapons[0].HitMask.value & (1 << weaponExcludedLayer)) == 0,
                    "Weapon hitMask must exclude SeekerWorldVisual.", failures);
            }
            Require((weapons[0].HitMask.value & 1) != 0,
                "Weapon hitMask must include Default world colliders.", failures);
            int playerLayer = LayerMask.NameToLayer("Player");
            Require(playerLayer < 0 || (weapons[0].HitMask.value & (1 << playerLayer)) != 0,
                "Weapon hitMask must include the Hider Player layer.", failures);
            SerializedObject serializedWeapon = new SerializedObject(weapons[0]);
            Require(serializedWeapon.FindProperty("shotCamera")?.objectReferenceValue ==
                    selectors.FirstOrDefault()?.SeekerCamera,
                "Weapon gameplayCamera is not assigned to SeekerCamera.", failures);
            Require(serializedWeapon.FindProperty("roleSelector")?.objectReferenceValue == selectors.FirstOrDefault(),
                "Weapon role-selector reference is missing.", failures);
            Require(serializedWeapon.FindProperty("roundManager")?.objectReferenceValue != null,
                "Weapon round-manager reference is missing.", failures);
            Require(serializedWeapon.FindProperty("crosshair")?.objectReferenceValue != null,
                "Weapon crosshair feedback reference is missing.", failures);
            Require(serializedWeapon.FindProperty("pulseRenderers")?.arraySize >= 1,
                "Weapon presentation feedback renderers are missing.", failures);
        }

        Require(cameras.Length == 4, $"Expected 4 role cameras total, found {cameras.Length}.", failures);
        Require(activeCameras == 1, $"Expected 1 active camera, found {activeCameras}.", failures);
        Require(enabledListeners == 1,
            $"Expected 1 enabled/active AudioListener, found {enabledListeners}.", failures);
        Require(UnityEngine.Object.FindObjectsOfType<EventSystem>(true).Length == 1,
            "EventSystem is missing or duplicated.", failures);
        Require(UnityEngine.Object.FindObjectsOfType<InputSystemUIInputModule>(true).Length == 1,
            "InputSystemUIInputModule is missing or duplicated.", failures);
        Require(UnityEngine.Object.FindObjectsOfType<StandaloneInputModule>(true).Length == 0,
            "StandaloneInputModule conflicts with the Input System UI module.", failures);
        EventSystem activeEventSystem = UnityEngine.Object.FindObjectsOfType<EventSystem>(true).FirstOrDefault();
        Require(activeEventSystem != null && activeEventSystem.gameObject.activeInHierarchy,
            "The single EventSystem is not active.", failures);

        int seekerVisualLayer = LayerMask.NameToLayer("SeekerWorldVisual");
        Require(seekerVisualLayer >= 0 && seekerWorldVisualRoot != null &&
                seekerWorldVisualRoot.GetComponentsInChildren<Transform>(true)
                    .All(child => child.gameObject.layer == seekerVisualLayer),
            "SeekerWorldVisual layer is missing or not assigned.", failures);
        if (seekerVisualLayer >= 0 && hider != null && hider.cameraModeManager != null)
        {
            int visualBit = 1 << seekerVisualLayer;
            Require(hider.cameraModeManager.tpsCamera != null &&
                    (hider.cameraModeManager.tpsCamera.cullingMask & visualBit) != 0,
                "Hider TPS/Ghost camera does not render SeekerWorldVisual.", failures);
            Require(hider.cameraModeManager.spectatorCamera == null ||
                    (hider.cameraModeManager.spectatorCamera.cullingMask & visualBit) != 0,
                "Hider spectator camera does not render SeekerWorldVisual.", failures);
            Camera configuredSeekerCamera = selectors.FirstOrDefault()?.SeekerCamera;
            Require(configuredSeekerCamera != null &&
                    (configuredSeekerCamera.cullingMask & visualBit) == 0,
                "Seeker camera should exclude its own world visual.", failures);
        }

        if (selectors.Length == 1 && hider != null)
        {
            SerializedObject serializedSelector = new SerializedObject(selectors[0]);
            Require(serializedSelector.FindProperty("hiderTransformSystem")?.objectReferenceValue == hider,
                "Role selector Hider reference is missing.", failures);
            Require(serializedSelector.FindProperty("seekerController")?.objectReferenceValue ==
                    seekerControllers.FirstOrDefault(),
                "Role selector Seeker controller reference is missing.", failures);
            Require(serializedSelector.FindProperty("seekerWeapon")?.objectReferenceValue == weapons.FirstOrDefault(),
                "Role selector weapon reference is missing.", failures);
            Require(serializedSelector.FindProperty("hiderTestSpawnPoint")?.objectReferenceValue ==
                    (hiderSpawn != null ? hiderSpawn.transform : null),
                "Role selector HiderTestSpawnPoint reference is missing.", failures);
            Require(serializedSelector.FindProperty("seekerSpawnPoint")?.objectReferenceValue ==
                    (seekerSpawn != null ? seekerSpawn.transform : null),
                "Role selector SeekerSpawnPoint reference is missing.", failures);
            Require(serializedSelector.FindProperty("hiderTestPropDefinition")?.objectReferenceValue != null,
                "Role selector initial test prop definition is missing.", failures);
            Require(serializedSelector.FindProperty("hiderHealthBar")?.objectReferenceValue == hiderHealthBar &&
                    serializedSelector.FindProperty("seekerHealthBar")?.objectReferenceValue == seekerHealthBar,
                "Role selector does not own two independent health-bar visibility references.", failures);
            Require(serializedSelector.FindProperty("seekerHealth")?.objectReferenceValue == configuredSeekerHealth,
                "Role selector SeekerHealth reference is missing.", failures);

            bool panelWasActive = rolePanel != null && rolePanel.activeSelf;
            if (rolePanel != null) rolePanel.SetActive(false);
            selectors[0].ApplyHealthBarVisibility(PropHuntTestRole.Hider);
            Require(hiderHealthBar != null && hiderHealthBar.activeSelf &&
                    seekerHealthBar != null && !seekerHealthBar.activeSelf,
                "F1/Hider visibility must show only HiderHealthBar.", failures);
            if (seekerHudRoot != null) seekerHudRoot.SetActive(true);
            selectors[0].ApplyHealthBarVisibility(PropHuntTestRole.Seeker);
            Require(hiderHealthBar != null && !hiderHealthBar.activeSelf &&
                    seekerHealthBar != null && seekerHealthBar.activeSelf,
                "F2/Seeker visibility must show only SeekerHealthBar.", failures);
            if (seekerHudRoot != null) seekerHudRoot.SetActive(false);
            selectors[0].ApplyHealthBarVisibility(PropHuntTestRole.None);
            Require(hiderHealthBar != null && !hiderHealthBar.activeSelf &&
                    seekerHealthBar != null && !seekerHealthBar.activeSelf,
                "Role selection panel must hide both health bars.", failures);
            if (rolePanel != null) rolePanel.SetActive(panelWasActive);
        }

        HiderRevealController revealController = hider != null
            ? hider.GetComponent<HiderRevealController>()
            : null;
        Require(revealController != null &&
                Mathf.Approximately(revealController.HighlightAlpha, HiderRevealController.RevealHighlightAlpha),
            "Reveal highlight alpha is not exactly 0.05 (5%).", failures);
        Require(revealController != null && Mathf.Approximately(revealController.RevealDuration, 5f),
            "Clone reveal duration is no longer 5 seconds.", failures);
        int seekerRevealLayer = LayerMask.NameToLayer("SeekerReveal");
        Require(seekerRevealLayer >= 0, "SeekerReveal layer is missing.", failures);
        if (seekerRevealLayer >= 0 && selectors.Length == 1)
        {
            int revealBit = 1 << seekerRevealLayer;
            Require(selectors[0].SeekerCamera != null &&
                    (selectors[0].SeekerCamera.cullingMask & revealBit) != 0,
                "SeekerCamera does not render SeekerReveal.", failures);
            if (hider != null && hider.cameraModeManager != null)
            {
                Camera[] hiderRevealCameras =
                {
                    hider.mainCamera,
                    hider.cameraModeManager.fpsCamera,
                    hider.cameraModeManager.tpsCamera,
                    hider.cameraModeManager.spectatorCamera
                };
                Require(hiderRevealCameras.Where(camera => camera != null)
                        .All(camera => (camera.cullingMask & revealBit) == 0),
                    "A Hider/Ghost/Spectator camera still renders SeekerReveal.", failures);
            }
        }

        if (hider != null && hider.cameraModeManager != null)
        {
            SerializedObject serializedCamera = new SerializedObject(hider.cameraModeManager);
            Require(hider.cameraModeManager.tpsCamera != null,
                "Hider adaptive TPS camera reference is missing.", failures);
            Require(serializedCamera.FindProperty("zoomScrollSensitivity")?.floatValue > 0f,
                "Hider TPS zoom is not configured.", failures);
            Require(serializedCamera.FindProperty("cameraCollisionRadius")?.floatValue > 0f &&
                    hider.cameraModeManager.cameraCollisionMask.value != 0,
                "Hider TPS camera collision is not configured.", failures);
            Require(serializedCamera.FindProperty("ghostMaxDistance")?.floatValue > 0f &&
                    serializedCamera.FindProperty("ghostCollisionRadius")?.floatValue > 0f,
                "Hider Ghost Camera is not configured.", failures);
        }

        Debug.Log(
            $"PropHuntRoleValidation: Hierarchy: selectors={selectors.Length}, SeekerPlayer={CountNamedObjects("SeekerPlayer")}, " +
            $"roleButtons={roleButtons.Length}, cameras={cameras.Length} (active={activeCameras}), " +
            $"listeners={listeners.Length} (enabledActive={enabledListeners}), healthBars=" +
            $"{CountNamedObjects("HiderHealthBar")}/{CountNamedObjects("SeekerHealthBar")}, EventSystems=" +
            $"{UnityEngine.Object.FindObjectsOfType<EventSystem>(true).Length}.");
        Finish("Scene validation", failures);
    }

    [MenuItem("Tools/Prop Hunt/Run Role Selector And Seeker Smoke Test")]
    public static void RunPlayModeSmokeTest()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("PropHuntRoleSmoke: Unity is already entering Play Mode.");
            if (Application.isBatchMode) EditorApplication.Exit(2);
            return;
        }

        EditorSceneManager.OpenScene(MapV2Path, OpenSceneMode.Single);
        SessionState.SetBool(SmokeRunningKey, true);
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
            EditorApplication.delayCall += ExecuteStatePreservationSmoke;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            string result = SessionState.GetString(SmokeResultKey, string.Empty);
            SessionState.EraseBool(SmokeRunningKey);
            SessionState.EraseString(SmokeResultKey);
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
            if (string.IsNullOrEmpty(result))
            {
                Debug.Log("PropHuntRoleSmoke: PASS — initial selection and state-preserving possession.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("PropHuntRoleSmoke: FAIL\n" + result.Replace("\n---\n", "\n"));
                if (Application.isBatchMode) EditorApplication.Exit(3);
            }
        }
    }

    private static void ExecutePlayModeSmoke()
    {
        SmokeFailures.Clear();
        try
        {
            PropHuntTestRoleSelector selector = UnityEngine.Object.FindObjectOfType<PropHuntTestRoleSelector>();
            PropTransformSystem hider = UnityEngine.Object.FindObjectsOfType<PropTransformSystem>()
                .FirstOrDefault(player => player.playerRole == PlayerRole.Hider);
            HiderHealth health = hider != null ? hider.GetComponent<HiderHealth>() : null;
            HiderEliminationController elimination =
                hider != null ? hider.GetComponent<HiderEliminationController>() : null;
            HiderSpectatorController spectator =
                hider != null ? hider.GetComponent<HiderSpectatorController>() : null;
            HiderCloneAbility cloneAbility = hider != null ? hider.GetComponent<HiderCloneAbility>() : null;
            HiderRevealController reveal = hider != null ? hider.GetComponent<HiderRevealController>() : null;
            HiderRosterManager roster = UnityEngine.Object.FindObjectOfType<HiderRosterManager>();
            SeekerFirstPersonController seeker =
                UnityEngine.Object.FindObjectOfType<SeekerFirstPersonController>(true);
            SeekerRaycastWeapon weapon = UnityEngine.Object.FindObjectOfType<SeekerRaycastWeapon>(true);
            GameObject panel = FindNamedObject("RoleSelectionPanel");

            Require(selector != null && hider != null && health != null && elimination != null,
                "Required role/Hider components are missing in Play Mode.", SmokeFailures);
            if (selector == null || hider == null || health == null || elimination == null)
            {
                PersistAndExitPlayMode();
                return;
            }

            Require(selector.CurrentRole == PropHuntTestRole.None,
                "Initial role is not None while selector panel is shown.", SmokeFailures);
            Require(panel != null && panel.activeSelf,
                "CHỌN VAI TRÒ KIỂM THỬ panel is not visible on Play.", SmokeFailures);
            Require(panel != null && panel.GetComponentsInChildren<Button>(true).Length == 2,
                "Role panel does not have exactly two buttons in Play Mode.", SmokeFailures);

            selector.HiderRoleButton.onClick.Invoke();
            Require(selector.CurrentRole == PropHuntTestRole.Hider,
                "Hider role button did not select Hider.", SmokeFailures);
            Require(!hider.IsGameplayInputLocked,
                "Hider input was not enabled after Hider role button.", SmokeFailures);
            Require(selector.HiderTestSpawnPoint != null &&
                    Vector3.Distance(hider.transform.position, selector.HiderTestSpawnPoint.position) < 0.05f,
                "Hider was not teleported using HiderTestSpawnPoint.", SmokeFailures);
            Require(hider.IsDisguised && hider.cameraModeManager != null &&
                    hider.cameraModeManager.CurrentMode == PlayerCameraMode.PropTPS &&
                    hider.cameraModeManager.tpsCamera.gameObject.activeInHierarchy &&
                    !hider.cameraModeManager.fpsCamera.gameObject.activeInHierarchy,
                "F1/Hider selection did not enter adaptive Prop TPS.", SmokeFailures);
            PropTarget prop = UnityEngine.Object.FindObjectsOfType<PropTarget>(true)
                .FirstOrDefault(IsValidPropDefinition);
            bool disguised = hider.IsDisguised || (prop != null && hider.TryBecomePropForTesting(prop));
            Require(disguised, "Could not prepare a disguised Hider for Clone test.", SmokeFailures);
            bool cloneCreated = disguised && cloneAbility != null && cloneAbility.TryCreateClone();
            Require(cloneCreated && cloneAbility.ActiveClones.Count == 1,
                "Clone could not be created before switching to Seeker.", SmokeFailures);
            HiderCloneInstance preparedClone = cloneCreated ? cloneAbility.ActiveClones[0] : null;

            selector.SeekerRoleButton.onClick.Invoke();
            Require(selector.CurrentRole == PropHuntTestRole.Seeker, "Seeker button did not select Seeker.", SmokeFailures);
            Require(seeker != null && seeker.IsControlActive, "Seeker FPS movement was not enabled.", SmokeFailures);
            Require(selector.SeekerSpawnPoint != null && seeker != null &&
                    Vector3.Distance(seeker.transform.position, selector.SeekerSpawnPoint.position) < 0.05f,
                "Seeker was not teleported using SeekerSpawnPoint.", SmokeFailures);
            Require(weapon != null && weapon.IsWeaponActive, "Seeker weapon was not enabled.", SmokeFailures);
            Require(hider.IsGameplayInputLocked, "Hider input was not locked in Seeker role.", SmokeFailures);
            Require(elimination.IsSpectatorSuppressed,
                "Hider spectator takeover was not suppressed in Seeker role.", SmokeFailures);
            Require(health.IsAlive && health.CurrentHealth == 100,
                "Hider was not kept alive at 100 HP as Seeker target.", SmokeFailures);
            Require(hider.GetComponentsInChildren<Collider>(true).Any(collider => collider.enabled),
                "Hider hit colliders were disabled before shooting.", SmokeFailures);
            Require(CountActiveCameras() == 1 && selector.SeekerCamera.gameObject.activeInHierarchy,
                "Seeker does not exclusively own the active camera.", SmokeFailures);
            Require(CountEnabledListeners() == 1,
                "Seeker role does not have exactly one enabled AudioListener.", SmokeFailures);

            int aliveBefore = roster != null ? roster.AliveHiderCount : -1;
            int healthBeforeClone = health.CurrentHealth;
            Require(!cloneCreated || cloneAbility.ActiveClones.Count == 1,
                "F2/Seeker switch incorrectly destroyed the existing Clone.", SmokeFailures);
            if (preparedClone != null && cloneAbility.ActiveClones.Count == 1)
            {
                HiderCloneInstance clone = preparedClone;
                clone.transform.position = hider.transform.position + Vector3.up * 12f;
                Physics.SyncTransforms();
                Collider cloneCollider = clone.GetComponentInChildren<Collider>(true);
                Require(cloneCollider != null && FireDirectlyAt(weapon, cloneCollider),
                    "Raycast did not hit the Clone.", SmokeFailures);
                Require(weapon.LastShotResult == SeekerShotResult.Clone,
                    "Clone hit was not classified as Clone.", SmokeFailures);
                Require(cloneAbility.ActiveClones.Count == 0,
                    "Clone did not disappear/remove from owner list.", SmokeFailures);
                Require(reveal != null && reveal.IsRevealed && reveal.RevealTimeRemaining > 4.5f,
                    "Real Hider was not revealed for 5 seconds.", SmokeFailures);
                Require(health.CurrentHealth == healthBeforeClone,
                    "Clone hit incorrectly damaged Hider health.", SmokeFailures);
                Require(roster == null || roster.AliveHiderCount == aliveBefore,
                    "Clone hit incorrectly changed AliveHiderCount.", SmokeFailures);
            }

            int eliminatedEvents = 0;
            health.Eliminated += _ => eliminatedEvents++;
            List<int> healthSequence = new List<int>();
            for (int shot = 0; shot < 4; shot++)
            {
                Collider hiderCollider = hider.GetComponentsInChildren<Collider>(true)
                    .FirstOrDefault(collider => collider.enabled && !collider.isTrigger);
                Require(hiderCollider != null && FireDirectlyAt(weapon, hiderCollider),
                    $"Raycast did not hit Hider on shot {shot + 1}.", SmokeFailures);
                healthSequence.Add(health.CurrentHealth);
            }

            Require(healthSequence.SequenceEqual(new[] { 75, 50, 25, 0 }),
                "Hider health sequence was not 100→75→50→25→0: " + string.Join(",", healthSequence),
                SmokeFailures);
            Require(eliminatedEvents == 1,
                $"Hider elimination fired {eliminatedEvents} times instead of once.", SmokeFailures);
            Require(roster == null || roster.AliveHiderCount == Mathf.Max(0, aliveBefore - 1),
                "AliveHiderCount did not decrease exactly one.", SmokeFailures);
            Require(selector.CurrentRole == PropHuntTestRole.Seeker &&
                    selector.SeekerCamera.gameObject.activeInHierarchy,
                "Hider death stole the Seeker camera.", SmokeFailures);
            Require(spectator == null || !spectator.IsSpectating,
                "Hider entered spectator while controlled as Seeker.", SmokeFailures);
            Require(hider.currentState != PlayerDisguiseState.Spectator,
                "Hider control state changed to Spectator in Seeker role.", SmokeFailures);

            selector.HiderRoleButton.onClick.Invoke();
            Require(selector.CurrentRole == PropHuntTestRole.Hider,
                "Hider button/F1 path did not select Hider.", SmokeFailures);
            Require(health.CurrentHealth == 100 && health.IsAlive,
                "F1/Hider switch did not revive Hider to 100 HP.", SmokeFailures);
            Require(!hider.IsGameplayInputLocked,
                "Hider input remained locked after returning to Hider.", SmokeFailures);
            Require(hider.CurrentVisualRoot != null && hider.CurrentVisualRoot.gameObject.activeInHierarchy,
                "Hider visual was not restored.", SmokeFailures);
            Require(hider.GetComponentsInChildren<Collider>(true).Any(collider => collider.enabled),
                "Hider colliders were not restored.", SmokeFailures);
            Require(roster == null || roster.AliveHiderCount == aliveBefore,
                "AliveHiderCount was not restored after F1/Hider reset.", SmokeFailures);
            Require(seeker == null || !seeker.IsControlActive,
                "Seeker movement remained active after F1.", SmokeFailures);
            Require(weapon == null || !weapon.IsWeaponActive,
                "Seeker weapon remained active after F1.", SmokeFailures);
            Require(hider.IsDisguised && hider.cameraModeManager != null &&
                    hider.cameraModeManager.IsCameraSystemEnabled &&
                    hider.cameraModeManager.CurrentMode == PlayerCameraMode.PropTPS,
                "F1 did not restore Hider adaptive Prop TPS mode.", SmokeFailures);
            Require(CountActiveCameras() == 1 &&
                    hider.cameraModeManager.tpsCamera.gameObject.activeInHierarchy &&
                    !hider.cameraModeManager.fpsCamera.gameObject.activeInHierarchy,
                "Camera did not return exclusively to Hider TPS.", SmokeFailures);
            Require(CountEnabledListeners() == 1,
                "Hider role does not have exactly one enabled AudioListener.", SmokeFailures);

            Debug.Log(
                "PropHuntRoleSmoke: panel=PASS, buttons=2, SeekerCamera exclusive, " +
                "CloneHit=Reveal5s/HP unchanged/roster unchanged, HiderHP=100→75→50→25→0, " +
                $"eliminationEvents={eliminatedEvents}, F1ResetHP={health.CurrentHealth}, " +
                $"HiderCamera={hider.cameraModeManager.CurrentMode}, activeCameras={CountActiveCameras()}, " +
                $"enabledListeners={CountEnabledListeners()}.");
        }
        catch (Exception exception)
        {
            SmokeFailures.Add(exception.ToString());
        }

        PersistAndExitPlayMode();
    }

    private static void ExecuteStatePreservationSmoke()
    {
        SmokeFailures.Clear();
        bool awaitingMouseInputFrame = false;
        try
        {
            PropHuntTestRoleSelector selector = UnityEngine.Object.FindObjectOfType<PropHuntTestRoleSelector>();
            PropTransformSystem hider = UnityEngine.Object.FindObjectsOfType<PropTransformSystem>()
                .FirstOrDefault(player => player.playerRole == PlayerRole.Hider);
            HiderHealth health = hider != null ? hider.GetComponent<HiderHealth>() : null;
            HiderEliminationController elimination =
                hider != null ? hider.GetComponent<HiderEliminationController>() : null;
            HiderSpectatorController spectator =
                hider != null ? hider.GetComponent<HiderSpectatorController>() : null;
            HiderAbilityController abilities =
                hider != null ? hider.GetComponent<HiderAbilityController>() : null;
            HiderCloneAbility cloneAbility = hider != null ? hider.GetComponent<HiderCloneAbility>() : null;
            HiderRevealController reveal = hider != null ? hider.GetComponent<HiderRevealController>() : null;
            HiderRosterManager roster = UnityEngine.Object.FindObjectOfType<HiderRosterManager>();
            SeekerFirstPersonController seeker =
                UnityEngine.Object.FindObjectOfType<SeekerFirstPersonController>(true);
            SeekerRaycastWeapon weapon = UnityEngine.Object.FindObjectOfType<SeekerRaycastWeapon>(true);
            SeekerHealth seekerHealth = UnityEngine.Object.FindObjectOfType<SeekerHealth>(true);
            SeekerHealthBarController seekerHealthHud =
                UnityEngine.Object.FindObjectOfType<SeekerHealthBarController>(true);
            GameObject panel = FindNamedObject("RoleSelectionPanel");
            GameObject hiderHealthBar = FindNamedObject("HiderHealthBar");
            GameObject seekerHealthBar = FindNamedObject("SeekerHealthBar");
            TextMeshProUGUI hiderHealthText = FindNamedObject("HealthText")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI seekerHealthText = FindNamedObject("SeekerHealthText")?.GetComponent<TextMeshProUGUI>();
            GameObject seekerWorldVisual = FindNamedObject("SeekerWorldVisualRoot");
            GameObject industrialSeekerModel = FindNamedObject("IndustrialSeekerModel");
            GameObject cyberSoldierModel = FindNamedObject("CyberSoldierModel");
            SeekerWeaponEnergy weaponEnergy = UnityEngine.Object.FindObjectOfType<SeekerWeaponEnergy>(true);

            Require(selector != null && hider != null && health != null && elimination != null &&
                    seeker != null && weapon != null && weaponEnergy != null && seekerHealth != null && seekerHealthHud != null &&
                    hiderHealthBar != null && seekerHealthBar != null,
                "Required role test components are missing in Play Mode.", SmokeFailures);
            if (selector == null || hider == null || health == null || elimination == null ||
                seeker == null || weapon == null || weaponEnergy == null || seekerHealth == null || seekerHealthHud == null ||
                hiderHealthBar == null || seekerHealthBar == null)
            {
                PersistAndExitPlayMode();
                return;
            }

            CanvasGroup panelGroup = panel != null ? panel.GetComponent<CanvasGroup>() : null;
            Require(selector.CurrentRole == PropHuntTestRole.None &&
                    selector.IsRoleSelectionPanelOpen && selector.InitialSpawnCompleted,
                "Initial role panel state is incorrect.", SmokeFailures);
            Require(Cursor.visible && Cursor.lockState == CursorLockMode.None,
                "Cursor is not visible/unlocked while role panel is open.", SmokeFailures);
            Require(panelGroup != null && panelGroup.interactable && panelGroup.blocksRaycasts &&
                    Mathf.Approximately(panelGroup.alpha, 1f),
                "Role panel CanvasGroup cannot receive pointer input.", SmokeFailures);
            Require(selector.HiderRoleButton.interactable && selector.SeekerRoleButton.interactable &&
                    selector.HiderRoleButton.targetGraphic.raycastTarget &&
                    selector.SeekerRoleButton.targetGraphic.raycastTarget,
                "Role buttons are not mouse-clickable.", SmokeFailures);
            Require(!seeker.IsControlActive && !weapon.IsWeaponActive && hider.IsGameplayInputLocked,
                "Movement, camera look or weapon remains active behind role panel.", SmokeFailures);
            Require(!hiderHealthBar.activeInHierarchy && !seekerHealthBar.activeInHierarchy,
                "Role panel shows one or both health bars.", SmokeFailures);
            Require(hider.cameraModeManager != null && hider.cameraModeManager.IsCameraSystemEnabled &&
                    hider.cameraModeManager.CurrentMode == PlayerCameraMode.PropTPS &&
                    hider.cameraModeManager.tpsCamera.gameObject.activeInHierarchy &&
                    !selector.SeekerCamera.gameObject.activeInHierarchy &&
                    CountActiveCameras() == 1 && CountEnabledListeners() == 1,
                "Panel preview does not have exactly one Hider PropTPS camera/listener.", SmokeFailures);
            Require(selector.HiderTestSpawnPoint != null && selector.SeekerSpawnPoint != null &&
                    Vector3.Distance(hider.transform.position, selector.HiderTestSpawnPoint.position) < 0.05f &&
                    Vector3.Distance(seeker.transform.position, selector.SeekerSpawnPoint.position) < 0.05f,
                "Initial preview did not spawn both players exactly once.", SmokeFailures);

            selector.HiderRoleButton.onClick.Invoke();
            Require(selector.InitialSpawnCompleted && selector.CurrentRole == PropHuntTestRole.Hider &&
                    !selector.IsRoleSelectionPanelOpen,
                "Initial Hider button did not complete selection exactly once.", SmokeFailures);
            if (!Application.isBatchMode)
            {
                Require(!Cursor.visible && Cursor.lockState == CursorLockMode.Locked,
                    "Cursor was not locked after initial selection.", SmokeFailures);
            }
            else
            {
                Debug.Log(
                    $"PropHuntRoleSmoke: headless cursor lock check skipped " +
                    $"(visible={Cursor.visible}, lockState={Cursor.lockState}); Game View validation required.");
            }
            Require(selector.HiderTestSpawnPoint != null && selector.SeekerSpawnPoint != null &&
                    Vector3.Distance(hider.transform.position, selector.HiderTestSpawnPoint.position) < 0.05f &&
                    Vector3.Distance(seeker.transform.position, selector.SeekerSpawnPoint.position) < 0.05f,
                "Initial selection did not place both players at their independent spawn points.", SmokeFailures);
            Require(hider.IsDisguised && hider.cameraModeManager != null &&
                    hider.cameraModeManager.CurrentMode == PlayerCameraMode.PropTPS,
                "Initial Hider selection did not activate PropTPS.", SmokeFailures);
            Require(hiderHealthBar.activeInHierarchy && !seekerHealthBar.activeInHierarchy &&
                    hiderHealthText != null && hiderHealthText.text == "100 / 100",
                "Hider selection does not show the independent 100/100 Hider bar.", SmokeFailures);
            Require(seekerWorldVisual != null && seekerWorldVisual.activeInHierarchy &&
                    cyberSoldierModel != null &&
                    cyberSoldierModel.GetComponentsInChildren<Renderer>(true)
                        .Any(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy),
                "Cyber Soldier Seeker model is hidden while Hider is controlled.", SmokeFailures);
            Require(industrialSeekerModel != null && !industrialSeekerModel.activeSelf,
                "Industrial Seeker fallback became active.", SmokeFailures);

            Vector3 hiderPosition = hider.transform.position;
            Quaternion hiderRotation = hider.transform.rotation;
            Vector3 hiderScale = hider.transform.localScale;
            Vector3 seekerPosition = seeker.transform.position;
            Quaternion seekerRotation = seeker.transform.rotation;
            Transform propVisual = hider.CurrentPropVisualTransform;
            Vector3 propScale = propVisual != null ? propVisual.localScale : Vector3.zero;
            Quaternion propRotation = propVisual != null ? propVisual.rotation : Quaternion.identity;
            bool wallAttached = hider.IsWallAttached;
            int humanVisible = hider.humanVisualRoot != null
                ? hider.humanVisualRoot.GetComponentsInChildren<Renderer>(true)
                    .Count(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)
                : 0;
            int propVisible = propVisual != null
                ? propVisual.GetComponentsInChildren<Renderer>(true)
                    .Count(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)
                : 0;
            Require(humanVisible == 0 && propVisible > 0,
                "Disguised Hider shows the purple capsule or hides its copied prop.", SmokeFailures);

            bool cloneCreated = cloneAbility != null && cloneAbility.TryCreateClone();
            HiderCloneInstance preparedClone = cloneCreated ? cloneAbility.ActiveClones.FirstOrDefault() : null;
            Require(cloneCreated && preparedClone != null,
                "Could not create a Clone for state-preservation test.", SmokeFailures);
            int cloneCharges = abilities != null ? abilities.RemainingCloneCharges : -1;
            int randomCharges = abilities != null ? abilities.RemainingRandomPropCharges : -1;
            reveal?.RevealForSeconds(5f);
            Require(reveal != null && reveal.IsRevealed &&
                    Mathf.Approximately(reveal.HighlightAlpha, 0.05f) &&
                    Mathf.Approximately(reveal.RevealDuration, 5f),
                "Runtime reveal is not configured for alpha 0.05 and duration 5s.", SmokeFailures);
            GameObject runtimeOverlay = FindNamedObject("HiderRevealOverlayRoot");
            Renderer runtimeOverlayRenderer = runtimeOverlay != null
                ? runtimeOverlay.GetComponentInChildren<Renderer>(true)
                : null;
            Material runtimeOverlayMaterial = runtimeOverlayRenderer != null
                ? runtimeOverlayRenderer.sharedMaterial
                : null;
            Require(runtimeOverlay != null && runtimeOverlay.activeInHierarchy &&
                    runtimeOverlayMaterial != null && runtimeOverlayMaterial.HasProperty("_Color") &&
                    Mathf.Approximately(runtimeOverlayMaterial.GetColor("_Color").a, 0.05f),
                "Runtime reveal overlay material alpha is not exactly 0.05.", SmokeFailures);
            reveal?.StopReveal();
            Require(runtimeOverlay == null || !runtimeOverlay.activeInHierarchy,
                "Reveal overlay remains visible after StopReveal.", SmokeFailures);
            reveal?.RevealForSeconds(5f);
            health.TakeDamage(60, HiderDamageSource.Debug);
            int preservedHealth = health.CurrentHealth;
            int aliveBefore = roster != null ? roster.AliveHiderCount : -1;
            Require(preservedHealth == 40 && hiderHealthText != null && hiderHealthText.text == "40 / 100",
                "Hider health bar did not preserve/display 40/100 before F2.", SmokeFailures);

            selector.ShowRoleSelection();
            Require(selector.InitialSpawnCompleted && selector.IsRoleSelectionPanelOpen &&
                    Cursor.visible && Cursor.lockState == CursorLockMode.None,
                "Reopened role panel lost initial state or did not unlock cursor.", SmokeFailures);
            int inputShotsBeforeSelection = weapon.InputFireCount;
            selector.SeekerRoleButton.onClick.Invoke();
            Require(weapon.InputFireCount == inputShotsBeforeSelection,
                "The Seeker role-selection click leaked into an unintended shot.", SmokeFailures);
            float configuredFireBlock = (float)(typeof(SeekerRaycastWeapon)
                .GetField("fireBlockedUntil", System.Reflection.BindingFlags.Instance |
                                              System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(weapon) ?? 0f);
            Require(configuredFireBlock > Time.realtimeSinceStartup,
                "Seeker role selection did not arm the 0.2s fire block.", SmokeFailures);
            Require(selector.CurrentRole == PropHuntTestRole.Seeker && seeker.IsControlActive &&
                    weapon.IsWeaponActive && elimination.IsSpectatorSuppressed,
                "F2 did not transfer control exclusively to Seeker.", SmokeFailures);
            Require(!hiderHealthBar.activeInHierarchy && seekerHealthBar.activeInHierarchy &&
                    seekerHealth.CurrentHealth == 100 && seekerHealthText != null &&
                    seekerHealthText.text == "100 / 100",
                "F2 did not show the independent Seeker 100/100 bar.", SmokeFailures);
            Require(Vector3.Distance(hider.transform.position, hiderPosition) < 0.001f &&
                    Quaternion.Angle(hider.transform.rotation, hiderRotation) < 0.01f &&
                    Vector3.Distance(hider.transform.localScale, hiderScale) < 0.001f,
                "F2 changed Hider transform.", SmokeFailures);
            Require(Vector3.Distance(seeker.transform.position, seekerPosition) < 0.001f &&
                    Quaternion.Angle(seeker.transform.rotation, seekerRotation) < 0.01f,
                "F2 teleported or rotated Seeker.", SmokeFailures);
            Require(health.CurrentHealth == preservedHealth && hider.IsDisguised &&
                    hider.CurrentPropVisualTransform == propVisual && propVisual != null &&
                    propVisual.gameObject.activeInHierarchy &&
                    Vector3.Distance(propVisual.localScale, propScale) < 0.001f &&
                    Quaternion.Angle(propVisual.rotation, propRotation) < 0.01f &&
                    hider.IsWallAttached == wallAttached,
                "F2 reset HP, disguise, copied prop or wall pose.", SmokeFailures);
            Require(cloneAbility != null && cloneAbility.ActiveClones.Count == 1 &&
                    (abilities == null || abilities.RemainingCloneCharges == cloneCharges) &&
                    (abilities == null || abilities.RemainingRandomPropCharges == randomCharges),
                "F2 destroyed Clone or reset ability charges.", SmokeFailures);
            Require(reveal != null && reveal.IsRevealed && reveal.RevealTimeRemaining > 0f,
                "F2 stopped the active reveal.", SmokeFailures);

            for (int index = 0; index < 10; index++)
            {
                if ((index & 1) == 0) selector.PossessHiderForDebug();
                else selector.PossessSeekerForDebug();
                Require(Vector3.Distance(hider.transform.position, hiderPosition) < 0.001f &&
                        Vector3.Distance(seeker.transform.position, seekerPosition) < 0.001f,
                    $"Possession switch {index + 1} teleported a player.", SmokeFailures);
                Require(health.CurrentHealth == preservedHealth && hider.IsDisguised &&
                        hider.CurrentPropVisualTransform == propVisual,
                    $"Possession switch {index + 1} reset HP or disguise.", SmokeFailures);
                bool expectsHider = selector.CurrentRole == PropHuntTestRole.Hider;
                Require(hiderHealthBar.activeInHierarchy == expectsHider &&
                        seekerHealthBar.activeInHierarchy == !expectsHider &&
                        hiderHealthText != null && hiderHealthText.text == "40 / 100" &&
                        seekerHealth.CurrentHealth == 100 && seekerHealthText != null &&
                        seekerHealthText.text == "100 / 100",
                    $"Possession switch {index + 1} mixed health-bar visibility or data.", SmokeFailures);
                Require(cloneAbility != null && cloneAbility.ActiveClones.Count == 1 &&
                        (abilities == null || abilities.RemainingCloneCharges == cloneCharges) &&
                        (abilities == null || abilities.RemainingRandomPropCharges == randomCharges),
                    $"Possession switch {index + 1} reset Clone or charges.", SmokeFailures);
                Require(CountActiveCameras() == 1 && CountEnabledListeners() == 1,
                    $"Possession switch {index + 1} has invalid camera/listener count.", SmokeFailures);
            }

            Require(selector.CurrentRole == PropHuntTestRole.Seeker,
                "Ten switches did not finish on Seeker.", SmokeFailures);
            int seekerCallbacksBeforeDamage = seekerHealthHud.HealthEventCallbackCount;
            seekerHealth.TakeDamage(1);
            Require(seekerHealthHud.HealthEventCallbackCount == seekerCallbacksBeforeDamage + 1 &&
                    seekerHealth.CurrentHealth == 99 && seekerHealthText != null &&
                    seekerHealthText.text == "99 / 100",
                "Seeker health event callback is missing or duplicated after ten switches.", SmokeFailures);
            seekerHealth.Heal(1);
            Require(seekerHealth.CurrentHealth == 100 && seekerHealthText != null &&
                    seekerHealthText.text == "100 / 100",
                "Seeker health did not restore independently after the callback probe.", SmokeFailures);
            if (preparedClone != null && cloneAbility.ActiveClones.Count == 1)
            {
                preparedClone.transform.position = hider.transform.position + Vector3.up * 12f;
                Physics.SyncTransforms();
                Collider cloneCollider = preparedClone.GetComponentInChildren<Collider>(true);
                Require(cloneCollider != null && FireDirectlyAt(weapon, cloneCollider) &&
                        weapon.LastShotResult == SeekerShotResult.Clone,
                    "Seeker raycast did not hit the preserved Clone.", SmokeFailures);
                Require(cloneAbility.ActiveClones.Count == 0 && health.CurrentHealth == preservedHealth &&
                        reveal != null && reveal.IsRevealed && reveal.RevealTimeRemaining > 4.5f,
                    "Clone hit did not destroy Clone/reveal Hider or incorrectly changed HP.", SmokeFailures);
            }

            weaponEnergy.ResetForRound();

            health.SetHealth(100);
            Require(health.CurrentHealth == 100 && health.IsAlive,
                "Could not restore 100 HP before the five-shot weapon sequence.", SmokeFailures);
            int healthBeforeWall = health.CurrentHealth;
            Require(FireAtWorldBlockerBeforeHider(weapon, hider),
                "Could not execute the wall-block raycast test.", SmokeFailures);
            Require(weapon.LastShotResult == SeekerShotResult.World &&
                    health.CurrentHealth == healthBeforeWall,
                "World collider did not stop the shot before the Hider.", SmokeFailures);
            weaponEnergy.ResetForRound();

            int eliminatedEvents = 0;
            health.Eliminated += _ => eliminatedEvents++;
            List<int> healthSequence = new List<int>();
            Ray sixthShotRay = default;
            for (int shot = 0; shot < 5; shot++)
            {
                Collider hiderCollider = hider.GetComponentsInChildren<Collider>(true)
                    .FirstOrDefault(collider => collider.enabled && !collider.isTrigger);
                if (hiderCollider != null) sixthShotRay = BuildDirectRay(hiderCollider);
                Require(hiderCollider != null && FireDirectlyAt(weapon, hiderCollider),
                    $"Raycast did not hit Hider on shot {shot + 1}.", SmokeFailures);
                healthSequence.Add(health.CurrentHealth);
            }
            Require(healthSequence.SequenceEqual(new[] { 80, 60, 40, 20, 0 }) && eliminatedEvents == 1,
                "Hider HP did not follow 100→80→60→40→20→0 exactly once: " +
                string.Join(",", healthSequence), SmokeFailures);
            Require(roster == null || roster.AliveHiderCount == Mathf.Max(0, aliveBefore - 1),
                "Elimination did not decrement roster exactly once.", SmokeFailures);
            int aliveAfterElimination = roster != null ? roster.AliveHiderCount : -1;
            weapon.TryFireRay(sixthShotRay, true);
            Require(eliminatedEvents == 1 &&
                    (roster == null || roster.AliveHiderCount == aliveAfterElimination),
                "Sixth shot triggered duplicate elimination or roster decrement.", SmokeFailures);
            Require(selector.SeekerCamera.gameObject.activeInHierarchy &&
                    (spectator == null || !spectator.IsSpectating),
                "Hider elimination stole the Seeker camera.", SmokeFailures);

            selector.PossessHiderForDebug();
            Require(health.CurrentHealth == 0 && health.IsEliminated && hider.IsGameplayInputLocked,
                "F1 incorrectly revived/reset eliminated Hider.", SmokeFailures);
            Require(roster == null || roster.AliveHiderCount == Mathf.Max(0, aliveBefore - 1),
                "F1 incorrectly reset roster.", SmokeFailures);
            Require(CountActiveCameras() == 1 && CountEnabledListeners() == 1,
                "F1 did not restore exactly one Hider/spectator camera and listener.", SmokeFailures);

            selector.PossessSeekerForDebug();
            Require(selector.SeekerCamera.gameObject.activeInHierarchy &&
                    CountActiveCameras() == 1 && CountEnabledListeners() == 1,
                "Spectator stole the camera after returning to F2.", SmokeFailures);

            Debug.Log(
                "PropHuntRoleSmoke: panelMouse=PASS, initialSpawnOnce=PASS, possessionSwitches=10, " +
                "roleClickShot=BLOCKED, teleport=NONE, " +
                "state=HP/disguise/prop/clone/reveal/charges preserved, CloneHP=UNCHANGED, " +
                "wallBlock=PASS, HiderHP=100→80→60→40→20→0, " +
                $"eliminationEvents={eliminatedEvents}, finalHP={health.CurrentHealth}, " +
                $"activeCameras={CountActiveCameras()}, enabledListeners={CountEnabledListeners()}.");
            PropHuntRoundManager inputSmokeRound = UnityEngine.Object.FindObjectOfType<PropHuntRoundManager>();
            typeof(PropHuntRoundManager)
                .GetProperty("CurrentState", System.Reflection.BindingFlags.Instance |
                                             System.Reflection.BindingFlags.Public)
                ?.GetSetMethod(true)
                ?.Invoke(inputSmokeRound, new object[] { PropHuntRoundState.Preparation });
            weaponEnergy.ResetForRound();
            BeginMultiFrameMouseInputSmoke(weapon, selector.SeekerCamera);
            awaitingMouseInputFrame = true;
        }
        catch (Exception exception)
        {
            SmokeFailures.Add(exception.ToString());
        }

        if (!awaitingMouseInputFrame) PersistAndExitPlayMode();
    }

    private static void BeginMultiFrameMouseInputSmoke(SeekerRaycastWeapon weapon, Camera seekerCamera)
    {
        Mouse previousMouse = Mouse.current;
        Mouse smokeMouse = InputSystem.AddDevice<Mouse>("PropHuntFrameSmokeMouse");
        InputSystem.QueueStateEvent(smokeMouse, new MouseState());
        InputSystem.Update();
        smokeMouse.MakeCurrent();

        int shotsBefore = weapon.InputFireCount;
        int phase = 0;
        int phaseFrame = Time.frameCount;
        double timeoutAt = EditorApplication.timeSinceStartup + 5d;
        Quaternion savedRotation = seekerCamera.transform.rotation;
        EditorApplication.CallbackFunction callback = null;
        callback = () =>
        {
            if (!Application.isPlaying || EditorApplication.timeSinceStartup >= timeoutAt)
            {
                EditorApplication.update -= callback;
                SmokeFailures.Add("Timed out while waiting for the multi-frame Mouse.current fire test.");
                CleanupMouseSmoke(smokeMouse, previousMouse, seekerCamera, savedRotation);
                PersistAndExitPlayMode();
                return;
            }

            if (phase == 0 && Time.frameCount > phaseFrame)
            {
                SetPrivateWeaponGate(weapon, "fireBlockedUntil", 0f);
                SetPrivateWeaponGate(weapon, "nextShotAt", 0f);
                typeof(SeekerRaycastWeapon)
                    .GetField("requireFireRelease", System.Reflection.BindingFlags.Instance |
                                                 System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(weapon, false);
                seekerCamera.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
                InputSystem.QueueStateEvent(smokeMouse, new MouseState { buttons = 1 });
                InputSystem.Update();
                smokeMouse.MakeCurrent();
                InputAction smokeFireAction = typeof(SeekerRaycastWeapon)
                    .GetField("fireAction", System.Reflection.BindingFlags.Instance |
                                            System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(weapon) as InputAction;
                Debug.Log(
                    $"PropHuntRoleSmoke Input probe: actionEnabled={smokeFireAction?.enabled}, " +
                    $"actionPressed={smokeFireAction?.WasPressedThisFrame()}, " +
                    $"smokeMousePressed={smokeMouse.leftButton.wasPressedThisFrame}, " +
                    $"currentMouse={(Mouse.current != null ? Mouse.current.deviceId : -1)}, " +
                    $"smokeMouse={smokeMouse.deviceId}, role={UnityEngine.Object.FindObjectOfType<PropHuntTestRoleSelector>()?.CurrentRole}, " +
                    $"cameraActive={seekerCamera.gameObject.activeInHierarchy}, weaponActive={weapon.IsWeaponActive}.");
                weapon.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
                phase = 1;
                phaseFrame = Time.frameCount;
                return;
            }

            if (phase == 1 && Time.frameCount > phaseFrame)
            {
                EditorApplication.update -= callback;
                Require(weapon.InputFireCount == shotsBefore + 1,
                    "The <Mouse>/leftButton InputAction did not call Fire in the Player Loop.",
                    SmokeFailures);
                InputSystem.QueueStateEvent(smokeMouse, new MouseState());
                InputSystem.Update();
                CleanupMouseSmoke(smokeMouse, previousMouse, seekerCamera, savedRotation);
                Debug.Log(
                    $"PropHuntRoleSmoke: inputFire=" +
                    $"{(weapon.InputFireCount == shotsBefore + 1 ? "InputAction(<Mouse>/leftButton) PASS" : "FAIL")}, " +
                    $"inputShots={weapon.InputFireCount - shotsBefore}, lastShot={weapon.LastShotResult}.");
                PersistAndExitPlayMode();
            }
        };
        EditorApplication.update += callback;
    }

    private static void SetPrivateWeaponGate(SeekerRaycastWeapon weapon, string fieldName, float value)
    {
        typeof(SeekerRaycastWeapon)
            .GetField(fieldName, System.Reflection.BindingFlags.Instance |
                                 System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(weapon, value);
    }

    private static void CleanupMouseSmoke(
        Mouse smokeMouse,
        Mouse previousMouse,
        Camera seekerCamera,
        Quaternion savedRotation)
    {
        if (seekerCamera != null) seekerCamera.transform.rotation = savedRotation;
        if (smokeMouse != null && smokeMouse.added) InputSystem.RemoveDevice(smokeMouse);
        if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
    }

    private static bool FireAtWorldBlockerBeforeHider(
        SeekerRaycastWeapon weapon,
        PropTransformSystem hider)
    {
        if (weapon == null || hider == null) return false;
        Collider target = hider.GetComponentsInChildren<Collider>(true)
            .FirstOrDefault(collider => collider.enabled && !collider.isTrigger);
        if (target == null) return false;

        Bounds bounds = target.bounds;
        float targetExtent = bounds.extents.x;
        GameObject blocker = new GameObject("PropHuntSmokeWorldBlocker");
        blocker.layer = 0;
        BoxCollider blockerCollider = blocker.AddComponent<BoxCollider>();
        blockerCollider.size = new Vector3(0.2f, Mathf.Max(2.5f, bounds.size.y + 0.5f),
            Mathf.Max(2.5f, bounds.size.z + 0.5f));
        blocker.transform.position = bounds.center - Vector3.right * (targetExtent + 0.8f);
        Physics.SyncTransforms();
        Ray ray = new Ray(blocker.transform.position - Vector3.right * 0.6f, Vector3.right);
        try
        {
            return weapon.TryFireRay(ray, true);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(blocker);
            Physics.SyncTransforms();
        }
    }

    private static Ray BuildDirectRay(Collider target)
    {
        if (target == null) return default;
        Bounds bounds = target.bounds;
        return new Ray(bounds.center - Vector3.right * (bounds.extents.x + 0.2f), Vector3.right);
    }

    private static bool FireDirectlyAt(SeekerRaycastWeapon weapon, Collider target)
    {
        if (weapon == null || target == null || !target.enabled) return false;
        Bounds bounds = target.bounds;
        Vector3[] directions = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        foreach (Vector3 direction in directions)
        {
            float extent = Mathf.Abs(direction.x) * bounds.extents.x +
                           Mathf.Abs(direction.y) * bounds.extents.y +
                           Mathf.Abs(direction.z) * bounds.extents.z;
            Vector3 origin = bounds.center - direction * (extent + 0.2f);
            Ray ray = new Ray(origin, direction);
            if (Physics.Raycast(ray, out RaycastHit previewHit, 5f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Collide) && IsSameTarget(previewHit.collider, target))
            {
                return weapon.TryFireRay(ray, true);
            }
        }

        return false;
    }

    private static bool IsSameTarget(Collider hit, Collider expected)
    {
        if (hit == null || expected == null) return false;
        HiderCloneInstance expectedClone = expected.GetComponentInParent<HiderCloneInstance>();
        if (expectedClone != null)
            return hit.GetComponentInParent<HiderCloneInstance>() == expectedClone;
        HiderHealth expectedHealth = expected.GetComponentInParent<HiderHealth>();
        if (expectedHealth != null)
            return hit.GetComponentInParent<HiderHealth>() == expectedHealth;
        return hit == expected;
    }

    private static bool IsValidPropDefinition(PropTarget prop)
    {
        if (prop == null || !prop.GameplayEnabled || prop.visualParts == null ||
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

    private static int CountActiveCameras()
    {
        return UnityEngine.Object.FindObjectsOfType<Camera>(true)
            .Count(camera => camera.enabled && camera.gameObject.activeInHierarchy);
    }

    private static int CountEnabledListeners()
    {
        return UnityEngine.Object.FindObjectsOfType<AudioListener>(true)
            .Count(listener => listener.enabled && listener.gameObject.activeInHierarchy);
    }

    private static string FindButtonLabel(IEnumerable<Button> buttons, string objectName)
    {
        Button button = buttons.FirstOrDefault(candidate => candidate.name == objectName);
        return button != null ? button.GetComponentInChildren<TextMeshProUGUI>(true)?.text : null;
    }

    private static int CountNamedObjects(string name)
    {
        return SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Count(transform => transform.name == name);
    }

    private static GameObject FindNamedObject(string name)
    {
        return SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(transform => transform.name == name)
            .Select(transform => transform.gameObject)
            .FirstOrDefault();
    }

    private static void Require(bool condition, string message, ICollection<string> failures)
    {
        if (!condition) failures.Add(message);
    }

    private static void PersistAndExitPlayMode()
    {
        SessionState.SetString(SmokeResultKey, string.Join("\n---\n", SmokeFailures));
        EditorApplication.ExitPlaymode();
    }

    private static void Finish(string label, IReadOnlyCollection<string> failures)
    {
        if (failures.Count == 0)
        {
            Debug.Log($"PropHuntRoleValidation: PASS — {label}.");
            return;
        }

        Debug.LogError($"PropHuntRoleValidation: FAIL — {label}\n" + string.Join("\n", failures));
        if (Application.isBatchMode) EditorApplication.Exit(2);
    }
}
