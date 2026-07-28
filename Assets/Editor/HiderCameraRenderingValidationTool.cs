using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StarterAssets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class HiderCameraRenderingValidationTool
{
    private const string SmokeKey = "PropHunt.HiderCameraRendering.Smoke";
    private const string SmokeCompletedKey =
        "PropHunt.HiderCameraRendering.SmokeCompleted";
    private static readonly List<string> Failures = new List<string>();
    private static double phaseStartedAt;
    private static double nextPreparationCheckAt;
    private static int phase;

    static HiderCameraRenderingValidationTool()
    {
        if (SessionState.GetBool(SmokeKey, false))
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        }
    }

    [MenuItem("Tools/Prop Hunt/Validate Single-Player Hider Cameras")]
    public static void SetupAndValidate()
    {
        SeekerAISetupTool.Setup();
        ValidateStatic();
        StartPlaySmoke();
    }

    public static void ValidateStatic()
    {
        EditorSceneManager.OpenScene(
            SeekerAISetupTool.ScenePath, OpenSceneMode.Single);
        List<string> failures = new List<string>();
        PropTransformSystem hider = FindHider();
        PlayerCameraModeManager manager =
            hider != null ? hider.cameraModeManager : null;
        PropHuntTestRoleSelector selector =
            Object.FindObjectOfType<PropHuntTestRoleSelector>(true);
        PropHuntSinglePlayerBootstrap bootstrap =
            Object.FindObjectOfType<PropHuntSinglePlayerBootstrap>(true);

        Require(hider != null, "Hider is missing.", failures);
        Require(manager != null, "PlayerCameraModeManager is missing.", failures);
        Require(bootstrap != null,
            "PropHuntSinglePlayerBootstrap is missing.", failures);
        Require(selector != null && selector.SinglePlayerHiderMode,
            "Role selector is not locked to single-player Hider.", failures);
        Require(manager != null && manager.SinglePlayerHiderCameraMode,
            "Camera manager is not in single-player Hider mode.", failures);
        Require(hider != null && !hider.IsDisguised &&
                hider.currentState == PlayerDisguiseState.Human,
            "Edit-time/round-start Hider state must be Human.", failures);
        Require(manager != null &&
                manager.ResolveModeFromHiderState() == PlayerCameraMode.HumanFPS &&
                manager.CurrentMode == PlayerCameraMode.HumanFPS,
            $"Round-start camera must be HumanFPS, got {manager?.CurrentMode}.",
            failures);

        if (manager != null)
        {
            Require(manager.fpsCamera != null,
                "Hider FPS camera reference is missing.", failures);
            Require(manager.tpsCamera != null,
                "Hider TPS camera reference is missing.", failures);
            Require(manager.spectatorCamera != null,
                "Hider Spectator camera reference is missing.", failures);
            ValidateOutput(manager.fpsCamera, "Hider FPS", failures);
            ValidateOutput(manager.tpsCamera, "Hider TPS", failures);
            ValidateOutput(manager.spectatorCamera, "Hider Spectator", failures);
            Require(IsRendering(manager.fpsCamera),
                "Hider FPS camera is not the edit-time rendering camera.", failures);
            Require(!IsRendering(manager.tpsCamera),
                "Hider TPS camera must be inactive while Human.", failures);
            Require(!IsRendering(manager.spectatorCamera),
                "Spectator camera must be inactive while Hider is alive.", failures);
            Require(manager.HiderCameraTarget != null &&
                    manager.HiderCameraTarget.IsChildOf(hider.transform),
                "Hider first-person target is missing or has the wrong owner.",
                failures);
            Require(manager.HiderCameraTarget != null &&
                    manager.HiderCameraTarget.GetComponent<Renderer>() == null,
                "Hider first-person target must be an empty Transform.", failures);
            Require(manager.fpsCamera != null &&
                    manager.fpsCamera.transform.parent == manager.HiderCameraTarget &&
                    manager.fpsCamera.transform.localPosition.sqrMagnitude < 0.000001f,
                "Hider FPS camera is not fixed to the eye target.", failures);
            Require(!HasUnstableVisualAncestor(
                    manager.fpsCamera.transform, hider.transform),
                "FPS camera is parented under a replaceable visual.", failures);
            Require(!HasUnstableVisualAncestor(
                    manager.tpsCamera.transform, hider.transform),
                "TPS camera is parented under a replaceable visual.", failures);
        }

        ValidateInputBindings(hider, failures, false);
        ValidateTechnicalCapsule(hider, failures, false);

        Camera seekerCamera = selector != null ? selector.SeekerCamera : null;
        Require(seekerCamera != null, "SeekerCamera reference is missing.", failures);
        Require(seekerCamera != null && !IsRendering(seekerCamera),
            "SeekerCamera must remain inactive.", failures);
        Require(CountRenderingSceneCameras() == 1,
            $"Expected exactly one Display 1 camera, got " +
            $"{CountRenderingSceneCameras()}.", failures);
        Require(CountEnabledSceneListeners() == 1,
            $"Expected exactly one AudioListener, got " +
            $"{CountEnabledSceneListeners()}.", failures);
        Require(CountMainCameraTags() == 1,
            $"Expected exactly one MainCamera tag, got {CountMainCameraTags()}.",
            failures);

        string inventory = manager != null
            ? manager.BuildCameraDiagnostic()
            : BuildFallbackCameraDiagnostic();
        Debug.Log("[HiderCameraValidation] STATIC CAMERA INVENTORY\n" + inventory);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "[HiderCameraValidation] STATIC FAIL\n" +
                string.Join("\n", failures) + "\n" + inventory);
        }

        Debug.Log(
            "[HiderCameraValidation] STATIC PASS — HumanFPS default, empty eye " +
            "target, hidden technical Capsule, active collider, inactive " +
            "Seeker/TPS/Spectator and one AudioListener verified.");
    }

    private static void StartPlaySmoke()
    {
        Failures.Clear();
        phase = 0;
        SessionState.SetBool(SmokeKey, true);
        SessionState.SetBool(SmokeCompletedKey, false);
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    private static void HandlePlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            phaseStartedAt = EditorApplication.timeSinceStartup;
            nextPreparationCheckAt = phaseStartedAt;
            phase = 0;
            EditorApplication.update -= TickSmoke;
            EditorApplication.update += TickSmoke;
        }
        else if (change == PlayModeStateChange.EnteredEditMode &&
                 SessionState.GetBool(SmokeKey, false))
        {
            if (!SessionState.GetBool(SmokeCompletedKey, false))
            {
                AddFailure(false,
                    "Play Mode ended before all camera phases completed.");
            }
            SessionState.SetBool(SmokeKey, false);
            SessionState.SetBool(SmokeCompletedKey, false);
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
            if (Failures.Count == 0)
            {
                Debug.Log(
                    "[HiderCameraValidation] PLAY MODE PASS — Human FPS start, " +
                    "failed copy, Clone invariance, Human/Prop Ghost return, E/O " +
                    "TPS, clear disguise, FPS/TPS elimination, AI shot and full " +
                    "preparation soak verified.");
            }
            else
            {
                Debug.LogError(
                    "[HiderCameraValidation] PLAY MODE FAIL\n" +
                    string.Join("\n", Failures));
            }
        }
    }

    private static void TickSmoke()
    {
        if (!EditorApplication.isPlaying) return;

        if (phase == 0)
        {
            if (EditorApplication.timeSinceStartup - phaseStartedAt < 1d) return;
            RunHumanStartupChecks();
            phase = 1;
            phaseStartedAt = EditorApplication.timeSinceStartup;
            nextPreparationCheckAt = phaseStartedAt;
            return;
        }

        if (phase == 1)
        {
            PropHuntRoundManager round =
                Object.FindObjectOfType<PropHuntRoundManager>(true);
            double now = EditorApplication.timeSinceStartup;
            if (now >= nextPreparationCheckAt)
            {
                ValidateRuntimeCamera("preparation Human", PlayerCameraMode.HumanFPS);
                nextPreparationCheckAt = now + 0.5d;
            }

            if (round != null && round.CurrentState == PropHuntRoundState.Hunting)
            {
                double elapsed = now - phaseStartedAt;
                AddFailure(elapsed >= 28d,
                    $"Preparation ended too early for the 30s soak: {elapsed:F2}s.");
                Debug.Log(
                    $"[HiderCameraValidation] PREPARATION PASS — HumanFPS rendered " +
                    $"{elapsed:F2}s through the Hunting transition.");
                RunDisguiseAbilityGhostAndEliminationChecks();
                PrepareAcceleratedAntiCampCheck();
                phase = 2;
                phaseStartedAt = now;
                return;
            }

            if (now - phaseStartedAt > 38d)
            {
                AddFailure(false,
                    "Preparation did not transition to Hunting within 38s.");
                FinishSmoke();
            }
            return;
        }

        if (phase == 2 &&
            EditorApplication.timeSinceStartup - phaseStartedAt >= 0.5d)
        {
            HiderAntiCampSystem antiCamp =
                Object.FindObjectOfType<HiderAntiCampSystem>(true);
            AddFailure(antiCamp != null &&
                       (antiCamp.CurrentState == HiderAntiCampState.Warning ||
                        antiCamp.CurrentState == HiderAntiCampState.Revealed),
                $"Anti-Camp did not reach warning: {antiCamp?.CurrentState}.");
            ValidateRuntimeCamera(
                "Anti-Camp disguised", PlayerCameraMode.PropTPS);

            HiderHealth health = Object.FindObjectOfType<HiderHealth>(true);
            if (health != null) health.SetHealth(0);
            else AddFailure(false, "HiderHealth missing before elimination.");
            phase = 3;
            phaseStartedAt = EditorApplication.timeSinceStartup;
            return;
        }

        if (phase == 3 &&
            EditorApplication.timeSinceStartup - phaseStartedAt >= 0.5d)
        {
            ValidateRuntimeCamera(
                "actual TPS elimination", PlayerCameraMode.Spectator);
            PropHuntRoundManager round =
                Object.FindObjectOfType<PropHuntRoundManager>(true);
            AddFailure(round != null && round.CurrentState == PropHuntRoundState.Ended,
                $"Round did not end after elimination: {round?.CurrentState}.");
            FinishSmoke();
        }
    }

    private static void RunHumanStartupChecks()
    {
        PropTransformSystem hider = FindHider();
        PlayerCameraModeManager manager =
            hider != null ? hider.cameraModeManager : null;
        PropHuntTestRoleSelector selector =
            Object.FindObjectOfType<PropHuntTestRoleSelector>(true);
        PropHuntRoundManager round =
            Object.FindObjectOfType<PropHuntRoundManager>(true);

        AddFailure(hider != null && manager != null,
            "Hider camera references missing at startup.");
        if (hider == null || manager == null) return;

        ValidateRuntimeCamera("round start", PlayerCameraMode.HumanFPS);
        AddFailure(!hider.IsDisguised &&
                   hider.currentState == PlayerDisguiseState.Human,
            "Hider was not Human at round start.");
        AddFailure(selector != null &&
                   selector.CurrentControlledRole == PropHuntTestRole.Hider,
            "Player ownership was not Hider.");
        AddFailure(selector == null || selector.RoleSelectionPanel == null ||
                   !selector.RoleSelectionPanel.activeInHierarchy,
            "Role Selection Panel was visible.");
        AddFailure(round != null &&
                   round.CurrentState == PropHuntRoundState.Preparation,
            $"Expected Preparation, got {round?.CurrentState}.");

        List<string> bindingFailures = new List<string>();
        ValidateInputBindings(hider, bindingFailures, true);
        ValidateTechnicalCapsule(hider, bindingFailures, true);
        foreach (string failure in bindingFailures) AddFailure(false, failure);

        FirstPersonController movement =
            hider.GetComponent<FirstPersonController>();
        AddFailure(movement != null && !movement.IsCameraLookLocked,
            "FPS mouse-look controller is locked at round start.");
        AddFailure(manager.fpsCamera != null &&
                   manager.fpsCamera.transform.parent == manager.HiderCameraTarget &&
                   manager.fpsCamera.transform.localPosition.sqrMagnitude < 0.000001f,
            "FPS camera is not following PlayerCameraRoot at eye height.");

        Vector3 originalPosition = hider.transform.position;
        Vector3 originalCameraPosition = manager.fpsCamera.transform.position;
        CharacterController controller = hider.GetComponent<CharacterController>();
        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        hider.transform.position += new Vector3(0.4f, 0f, 0.25f);
        Physics.SyncTransforms();
        Vector3 hiderDelta = hider.transform.position - originalPosition;
        Vector3 cameraDelta =
            manager.fpsCamera.transform.position - originalCameraPosition;
        AddFailure((cameraDelta - hiderDelta).sqrMagnitude < 0.0001f,
            $"FPS camera did not follow Hider exactly. Hider={hiderDelta}, " +
            $"camera={cameraDelta}.");
        hider.transform.position = originalPosition;
        if (controller != null) controller.enabled = controllerWasEnabled;
        Physics.SyncTransforms();

        PlayerCameraMode beforeFailure = manager.CurrentMode;
        bool invalidCopyRejected = !hider.TryBecomePropForTesting(null);
        AddFailure(invalidCopyRejected && manager.CurrentMode == beforeFailure &&
                   !hider.IsDisguised,
            "Rejected copy changed the Human FPS camera.");

        HiderAbilityController abilities =
            hider.GetComponent<HiderAbilityController>();
        PlayerCameraMode beforeClone = manager.CurrentMode;
        bool cloneRejectedWhileHuman =
            abilities == null || !abilities.TryCreateClone();
        AddFailure(cloneRejectedWhileHuman &&
                   manager.CurrentMode == beforeClone,
            "Clone input while Human changed camera mode.");

        EnterAndExitGhost(hider, PlayerCameraMode.HumanFPS, "Human Ghost");
        Debug.Log(
            "[HiderCameraValidation] HUMAN PASS — FPS eye follow, Player Move/Look, " +
            "hidden Capsule, rejected copy, Clone invariance and Ghost return verified.");
    }

    private static void RunDisguiseAbilityGhostAndEliminationChecks()
    {
        PropTransformSystem hider = FindHider();
        PlayerCameraModeManager manager =
            hider != null ? hider.cameraModeManager : null;
        if (hider == null || manager == null)
        {
            AddFailure(false, "Hider references missing during disguise checks.");
            return;
        }

        PropTarget[] props = Object.FindObjectsOfType<PropTarget>(true)
            .Where(item => item.GameplayEnabled &&
                           item.visualParts != null &&
                           item.visualParts.Any(part =>
                               part != null && part.mesh != null))
            .Take(3)
            .ToArray();
        AddFailure(props.Length > 0,
            "No valid PropTarget was available for E-copy checks.");
        if (props.Length == 0) return;

        AddFailure(hider.TryBecomePropForTesting(props[0]),
            $"Could not apply prop '{props[0].name}'.");
        ValidateRuntimeCamera("successful E copy", PlayerCameraMode.PropTPS);
        AddFailure(hider.IsDisguised &&
                   hider.CurrentPropVisualTransform != null,
            "Successful E-copy test did not create a visible disguise.");

        HiderAbilityController abilities =
            hider.GetComponent<HiderAbilityController>();
        PlayerCameraMode beforeClone = manager.CurrentMode;
        bool cloneCreated = abilities != null && abilities.TryCreateClone();
        AddFailure(cloneCreated && manager.CurrentMode == beforeClone,
            "Successful Clone changed the TPS camera.");

        PlayerCameraMode beforeRandom = manager.CurrentMode;
        bool randomChanged = abilities != null && abilities.TryUseRandomProp();
        AddFailure(randomChanged && manager.CurrentMode == PlayerCameraMode.PropTPS,
            "Successful O random-prop did not retain TPS.");
        bool cooldownRejected = abilities == null || !abilities.TryUseRandomProp();
        AddFailure(cooldownRejected && manager.CurrentMode == beforeRandom,
            "Rejected/cooldown O changed camera mode.");

        EnterAndExitGhost(hider, PlayerCameraMode.PropTPS, "Prop Ghost");

        SeekerRaycastWeapon weapon =
            Object.FindObjectOfType<SeekerRaycastWeapon>(true);
        if (weapon != null)
        {
            weapon.TryFireRayFromAI(
                new Ray(weapon.transform.position, Vector3.up), true);
            ValidateRuntimeCamera("AI shot", PlayerCameraMode.PropTPS);
        }
        else
        {
            AddFailure(false, "SeekerRaycastWeapon missing during AI check.");
        }

        // Direct state transitions leave HiderHealth alive, so both starting modes
        // can be validated without ending the round. The final health-driven TPS
        // elimination is still exercised after the Anti-Camp check.
        hider.ApplyHealthEliminationState(true);
        ValidateRuntimeCamera(
            "TPS state elimination", PlayerCameraMode.Spectator);
        hider.ApplyHealthEliminationState(false);
        ValidateRuntimeCamera(
            "clear disguise after TPS", PlayerCameraMode.HumanFPS);
        AddFailure(!hider.IsDisguised,
            "Clear/restore did not return Hider to Human.");

        hider.ApplyHealthEliminationState(true);
        ValidateRuntimeCamera(
            "FPS state elimination", PlayerCameraMode.Spectator);
        hider.ApplyHealthEliminationState(false);
        ValidateRuntimeCamera(
            "FPS elimination restore", PlayerCameraMode.HumanFPS);

        PropTarget finalProp = props.Length > 1 ? props[1] : props[0];
        AddFailure(hider.TryBecomePropForTesting(finalProp),
            "Could not restore a prop before Anti-Camp/final elimination.");
        ValidateRuntimeCamera(
            "final disguised state", PlayerCameraMode.PropTPS);
        Debug.Log(
            "[HiderCameraValidation] DISGUISE PASS — E/O TPS, Clone unchanged, " +
            "Ghost returned TPS, clear returned FPS and both elimination origins " +
            "selected Spectator.");
    }

    private static void EnterAndExitGhost(
        PropTransformSystem hider,
        PlayerCameraMode expectedReturnMode,
        string label)
    {
        MethodInfo enterGhost = typeof(PropTransformSystem).GetMethod(
            "TryEnterGhostCamera",
            BindingFlags.Instance | BindingFlags.NonPublic);
        AddFailure(enterGhost != null, $"{label}: entry method is missing.");
        enterGhost?.Invoke(hider, null);
        AddFailure(hider.IsGhostCameraActive,
            $"{label}: actual Ghost entry path did not activate.");
        ValidateRuntimeCamera(label + " enter", PlayerCameraMode.GhostCamera);
        hider.ForceExitGhostCamera();
        AddFailure(!hider.IsGhostCameraActive,
            $"{label}: Ghost did not exit.");
        ValidateRuntimeCamera(label + " exit", expectedReturnMode);
    }

    private static void PrepareAcceleratedAntiCampCheck()
    {
        HiderAntiCampSystem antiCamp =
            Object.FindObjectOfType<HiderAntiCampSystem>(true);
        if (antiCamp == null)
        {
            AddFailure(false, "HiderAntiCampSystem missing.");
            return;
        }

        FieldInfo allowedCampTime = typeof(HiderAntiCampSystem).GetField(
            "allowedCampTime", BindingFlags.Instance | BindingFlags.NonPublic);
        AddFailure(allowedCampTime != null,
            "Could not access Anti-Camp timing for accelerated check.");
        if (allowedCampTime == null) return;
        allowedCampTime.SetValue(antiCamp, 0.1f);
        antiCamp.ResetAntiCamp();
    }

    private static void ValidateInputBindings(
        PropTransformSystem hider,
        ICollection<string> failures,
        bool runtime)
    {
        PlayerInput playerInput =
            hider != null ? hider.GetComponent<PlayerInput>() : null;
        FirstPersonController movement =
            hider != null ? hider.GetComponent<FirstPersonController>() : null;
        StarterAssetsInputs inputs =
            hider != null ? hider.GetComponent<StarterAssetsInputs>() : null;
        InputActionMap playerMap = runtime
            ? playerInput != null ? playerInput.currentActionMap : null
            : playerInput != null && playerInput.actions != null
                ? playerInput.actions.FindActionMap("Player", false)
                : null;

        Require(playerInput != null && playerInput.enabled,
            "Hider PlayerInput is missing or disabled.", failures);
        Require(playerInput != null && playerInput.defaultActionMap == "Player",
            $"Hider default action map must be Player, got " +
            $"{playerInput?.defaultActionMap}.", failures);
        if (runtime)
        {
            Require(playerInput != null && playerInput.inputIsActive,
                "Runtime Hider PlayerInput is not active.", failures);
            Require(playerMap != null && playerMap.enabled,
                "Runtime Player action map is disabled.", failures);
        }
        InputAction lookAction =
            playerMap != null ? playerMap.FindAction("Look", false) : null;
        InputAction moveAction =
            playerMap != null ? playerMap.FindAction("Move", false) : null;
        Require(lookAction != null && (!runtime || lookAction.enabled),
            "Player/Look action is missing or disabled.", failures);
        Require(moveAction != null && (!runtime || moveAction.enabled),
            "Player/Move action is missing or disabled.", failures);
        Require(movement != null && movement.enabled &&
                !movement.IsControlLocked,
            "Hider movement controller is disabled or locked.", failures);
        Require(inputs != null && inputs.enabled && inputs.cursorInputForLook,
            "StarterAssetsInputs is not ready for Look.", failures);
        if (runtime)
        {
            Require((Application.isBatchMode ||
                     Cursor.lockState == CursorLockMode.Locked) &&
                    !Cursor.visible,
                $"Cursor is not locked/hidden: {Cursor.lockState}, " +
                $"visible={Cursor.visible}.", failures);
        }
    }

    private static void ValidateTechnicalCapsule(
        PropTransformSystem hider,
        ICollection<string> failures,
        bool runtime)
    {
        MeshRenderer renderer = hider != null
            ? hider.GetComponentsInChildren<MeshRenderer>(true)
                .FirstOrDefault(item => item.name == "Capsule")
            : null;
        CapsuleCollider collider =
            renderer != null ? renderer.GetComponent<CapsuleCollider>() : null;
        CharacterController controller =
            hider != null ? hider.GetComponent<CharacterController>() : null;
        Require(renderer != null && !renderer.enabled,
            $"{(runtime ? "Runtime" : "Static")} technical Capsule Renderer " +
            "is missing or enabled.", failures);
        Require(collider != null && collider.enabled,
            "Technical CapsuleCollider was removed or disabled.", failures);
        Require(controller != null && controller.enabled,
            "Hider CharacterController was removed or disabled.", failures);
    }

    private static void ValidateRuntimeCamera(
        string context,
        PlayerCameraMode expectedMode)
    {
        PropTransformSystem hider = FindHider();
        PlayerCameraModeManager manager =
            hider != null ? hider.cameraModeManager : null;
        if (manager == null)
        {
            AddFailure(false, $"{context}: camera manager missing.");
            return;
        }

        AddFailure(manager.EnsureGameplayCameraRendering(
                       "Validation/" + context),
            $"{context}: CameraSafety could not establish a camera.");
        AddFailure(manager.CurrentMode == expectedMode,
            $"{context}: expected {expectedMode}, got {manager.CurrentMode}.");
        AddFailure(CountRenderingSceneCameras() == 1,
            $"{context}: Display 1 camera count=" +
            $"{CountRenderingSceneCameras()}.\n{manager.BuildCameraDiagnostic()}");
        AddFailure(CountEnabledSceneListeners() == 1,
            $"{context}: AudioListener count={CountEnabledSceneListeners()}.");
        AddFailure(IsRendering(manager.ActiveGameplayCamera),
            $"{context}: selected gameplay camera is not rendering.");
        AddFailure(CountMainCameraTags() == 1,
            $"{context}: MainCamera tag count={CountMainCameraTags()}.");

        PropHuntTestRoleSelector selector =
            Object.FindObjectOfType<PropHuntTestRoleSelector>(true);
        AddFailure(selector == null || selector.SeekerCamera == null ||
                   !IsRendering(selector.SeekerCamera),
            $"{context}: SeekerCamera became active.");
    }

    private static void ValidateOutput(
        Camera camera,
        string label,
        ICollection<string> failures)
    {
        Require(camera != null && camera.targetDisplay == 0,
            $"{label} targetDisplay must be 0.", failures);
        Require(camera != null && camera.targetTexture == null,
            $"{label} targetTexture must be null.", failures);
    }

    private static PropTransformSystem FindHider()
    {
        return Object.FindObjectsOfType<PropTransformSystem>(true)
            .FirstOrDefault(item => item.playerRole == PlayerRole.Hider);
    }

    private static bool HasUnstableVisualAncestor(
        Transform item,
        Transform hiderRoot)
    {
        for (Transform current = item != null ? item.parent : null;
             current != null && current != hiderRoot;
             current = current.parent)
        {
            string lower = current.name.ToLowerInvariant();
            if (lower.Contains("humanvisual") ||
                lower.Contains("propvisual") ||
                lower.Contains("disguisevisual"))
            {
                return true;
            }
        }
        return false;
    }

    private static int CountRenderingSceneCameras()
    {
        return Resources.FindObjectsOfTypeAll<Camera>()
            .Count(camera => camera != null &&
                             camera.gameObject.scene.IsValid() &&
                             IsRendering(camera));
    }

    private static int CountEnabledSceneListeners()
    {
        return Resources.FindObjectsOfTypeAll<AudioListener>()
            .Count(listener => listener != null &&
                               listener.gameObject.scene.IsValid() &&
                               listener.enabled &&
                               listener.gameObject.activeInHierarchy);
    }

    private static int CountMainCameraTags()
    {
        return Resources.FindObjectsOfTypeAll<Camera>()
            .Count(camera => camera != null &&
                             camera.gameObject.scene.IsValid() &&
                             camera.CompareTag("MainCamera"));
    }

    private static bool IsRendering(Camera camera)
    {
        return camera != null &&
               camera.enabled &&
               camera.gameObject.activeInHierarchy &&
               camera.targetDisplay == 0 &&
               camera.targetTexture == null;
    }

    private static string BuildFallbackCameraDiagnostic()
    {
        return string.Join("\n", Resources.FindObjectsOfTypeAll<Camera>()
            .Where(camera => camera != null &&
                             camera.gameObject.scene.IsValid())
            .Select(camera =>
                $"{camera.name}: active={camera.gameObject.activeInHierarchy}, " +
                $"enabled={camera.enabled}, display={camera.targetDisplay}, " +
                $"target={(camera.targetTexture != null ? camera.targetTexture.name : "<null>")}"));
    }

    private static void FinishSmoke()
    {
        SessionState.SetBool(SmokeCompletedKey, true);
        EditorApplication.update -= TickSmoke;
        EditorApplication.ExitPlaymode();
    }

    private static void AddFailure(bool condition, string message)
    {
        if (!condition && !Failures.Contains(message))
        {
            Failures.Add(message);
        }
    }

    private static void Require(
        bool condition,
        string message,
        ICollection<string> failures)
    {
        if (!condition) failures.Add(message);
    }
}
