using System;
using System.Collections.Generic;
using System.Linq;
using StarterAssets;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class HiderEliminationValidationTool
{
    private const string MapV2Path = "Assets/Scenes/Map_v2.unity";
    private const string SmokeSessionKey = "PropHunt.HiderEliminationSmokeRunning";
    private const string SmokeResultKey = "PropHunt.HiderEliminationSmokeResult";
    private static readonly List<string> SmokeFailures = new List<string>();

    static HiderEliminationValidationTool()
    {
        if (SessionState.GetBool(SmokeSessionKey, false))
        {
            EditorApplication.playModeStateChanged -= HandleSmokePlayModeChanged;
            EditorApplication.playModeStateChanged += HandleSmokePlayModeChanged;
        }
    }

    [MenuItem("Tools/Prop Hunt/Validate Hider Elimination Setup")]
    public static void ValidateScene()
    {
        EditorSceneManager.OpenScene(MapV2Path, OpenSceneMode.Single);
        List<string> failures = new List<string>();
        PropTransformSystem[] players = UnityEngine.Object.FindObjectsOfType<PropTransformSystem>(true);
        PropTransformSystem[] hiders = players
            .Where(player => player != null && player.playerRole == PlayerRole.Hider)
            .ToArray();
        HiderRosterManager[] rosters = UnityEngine.Object.FindObjectsOfType<HiderRosterManager>(true);
        PropHuntRoundManager[] roundManagers = UnityEngine.Object.FindObjectsOfType<PropHuntRoundManager>(true);
        HiderSpectatorController[] spectators =
            UnityEngine.Object.FindObjectsOfType<HiderSpectatorController>(true);
        Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>(true);
        AudioListener[] listeners = UnityEngine.Object.FindObjectsOfType<AudioListener>(true);
        int activeListeners = listeners.Count(listener =>
            listener != null && listener.enabled && listener.gameObject.activeInHierarchy);
        PropHuntZoneAnchor[] anchors = UnityEngine.Object.FindObjectsOfType<PropHuntZoneAnchor>(true);

        Require(roundManagers.Length == 1, $"Expected 1 round manager, found {roundManagers.Length}.", failures);
        Require(rosters.Length == 1, $"Expected 1 roster, found {rosters.Length}.", failures);
        Require(hiders.Length > 0, "No true Hider was found.", failures);
        Require(spectators.Length == 1, $"Expected 1 local spectator controller, found {spectators.Length}.", failures);
        Require(anchors.Length == 6, $"Expected 6 zone anchors, found {anchors.Length}.", failures);
        Require(CountNamedObjects("SpectatorStatusPanel") == 1,
            "SpectatorStatusPanel is missing or duplicated.", failures);
        Require(CountNamedObjects("SpectatorStatusText") == 1,
            "SpectatorStatusText is missing or duplicated.", failures);
        Require(CountNamedObjects("HiderCloneContainer") == 1,
            "HiderCloneContainer is missing or duplicated.", failures);

        Transform cloneContainer = SceneManager.GetActiveScene().GetRootGameObjects()
            .FirstOrDefault(root => root.name == "HiderCloneContainer")?.transform;
        foreach (PropTransformSystem hider in hiders)
        {
            Require(hider.GetComponents<HiderHealth>().Length == 1,
                $"{hider.name}: HiderHealth count is not 1.", failures);
            Require(hider.GetComponents<HiderEliminationController>().Length == 1,
                $"{hider.name}: HiderEliminationController count is not 1.", failures);
            Require(cloneContainer == null || !hider.transform.IsChildOf(cloneContainer),
                $"{hider.name}: a clone was registered as a true Hider.", failures);
        }

        if (rosters.Length == 1)
        {
            Require(rosters[0].TotalHiderCount == hiders.Length,
                $"Roster total {rosters[0].TotalHiderCount} does not match true Hiders {hiders.Length}.", failures);
        }

        PropTransformSystem localHider = hiders.FirstOrDefault();
        PlayerCameraModeManager cameraManager = localHider != null ? localHider.cameraModeManager : null;
        Require(cameraManager != null, "Local Hider camera mode manager is missing.", failures);
        if (cameraManager != null)
        {
            Require(cameraManager.fpsCamera != null, "FPS camera reference is missing.", failures);
            Require(cameraManager.tpsCamera != null, "TPS camera reference is missing.", failures);
            Require(cameraManager.spectatorCamera != null, "Spectator camera reference is missing.", failures);
            Require(cameraManager.fpsCamera != cameraManager.tpsCamera &&
                    cameraManager.fpsCamera != cameraManager.spectatorCamera &&
                    cameraManager.tpsCamera != cameraManager.spectatorCamera,
                "Camera role references are not distinct.", failures);
        }

        Require(activeListeners <= 1,
            $"Expected at most 1 active AudioListener, found {activeListeners}.", failures);
        Require(cameras.Select(camera => camera.GetInstanceID()).Distinct().Count() == cameras.Length,
            "Duplicate camera component references detected.", failures);

        PropHuntHUDController hud = UnityEngine.Object.FindObjectOfType<PropHuntHUDController>(true);
        Require(hud != null, "PropHuntHUDController is missing.", failures);
        if (hud != null)
        {
            SerializedObject serializedHud = new SerializedObject(hud);
            Require(serializedHud.FindProperty("hiderRoster")?.objectReferenceValue == rosters.FirstOrDefault(),
                "HUD roster binding is missing.", failures);
            Require(serializedHud.FindProperty("eliminationController")?.objectReferenceValue != null,
                "HUD elimination binding is missing.", failures);
            Require(serializedHud.FindProperty("spectatorController")?.objectReferenceValue != null,
                "HUD spectator binding is missing.", failures);
            Require(serializedHud.FindProperty("spectatorStatusPanel")?.objectReferenceValue != null,
                "HUD spectator panel binding is missing.", failures);
        }

        Debug.Log(
            $"HiderEliminationValidation: Hierarchy summary: " +
            $"Hiders={hiders.Length}, Health={UnityEngine.Object.FindObjectsOfType<HiderHealth>(true).Length}, " +
            $"EliminationControllers={UnityEngine.Object.FindObjectsOfType<HiderEliminationController>(true).Length}, " +
            $"Rosters={rosters.Length}, Spectators={spectators.Length}, Cameras={cameras.Length}, " +
            $"AudioListeners={listeners.Length} (active={activeListeners}), Anchors={anchors.Length}, " +
            $"SpectatorPanels={CountNamedObjects("SpectatorStatusPanel")}.");

        FinishValidation("Scene validation", failures);
    }

    [MenuItem("Tools/Prop Hunt/Run Hider Elimination Play Smoke Test")]
    public static void RunPlayModeSmokeTest()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("HiderEliminationSmoke: Unity is already changing Play Mode.");
            if (Application.isBatchMode) EditorApplication.Exit(2);
            return;
        }

        EditorSceneManager.OpenScene(MapV2Path, OpenSceneMode.Single);
        SmokeFailures.Clear();
        SessionState.EraseString(SmokeResultKey);
        SessionState.SetBool(SmokeSessionKey, true);
        EditorApplication.playModeStateChanged -= HandleSmokePlayModeChanged;
        EditorApplication.playModeStateChanged += HandleSmokePlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    private static void HandleSmokePlayModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(SmokeSessionKey, false))
        {
            return;
        }

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            EditorApplication.delayCall += ExecuteSmokeTestInPlayMode;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            SmokeFailures.Clear();
            string persistedFailures = SessionState.GetString(SmokeResultKey, string.Empty);
            if (!string.IsNullOrEmpty(persistedFailures))
            {
                SmokeFailures.AddRange(persistedFailures.Split(new[] { "\n---\n" },
                    StringSplitOptions.RemoveEmptyEntries));
            }

            SessionState.EraseBool(SmokeSessionKey);
            SessionState.EraseString(SmokeResultKey);
            EditorApplication.playModeStateChanged -= HandleSmokePlayModeChanged;
            bool passed = SmokeFailures.Count == 0;
            if (passed)
            {
                Debug.Log("HiderEliminationSmoke: PASS — eliminate/reset lifecycle completed without exceptions.");
            }
            else
            {
                Debug.LogError("HiderEliminationSmoke: FAIL\n" + string.Join("\n", SmokeFailures));
            }

            if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 3);
        }
    }

    private static void ExecuteSmokeTestInPlayMode()
    {
        try
        {
            HiderEliminationController elimination =
                UnityEngine.Object.FindObjectsOfType<HiderEliminationController>(true)
                    .FirstOrDefault(controller => controller.TransformSystem != null &&
                                                  controller.TransformSystem.playerRole == PlayerRole.Hider);
            Require(elimination != null, "No HiderEliminationController in Play Mode.", SmokeFailures);
            if (elimination == null)
            {
                SessionState.SetString(SmokeResultKey, string.Join("\n---\n", SmokeFailures));
                EditorApplication.ExitPlaymode();
                return;
            }

            HiderHealth health = elimination.Health;
            PropTransformSystem transformSystem = elimination.TransformSystem;
            HiderRosterManager roster = UnityEngine.Object.FindObjectOfType<HiderRosterManager>();
            HiderSpectatorController spectator = elimination.GetComponent<HiderSpectatorController>();
            FirstPersonController movement = elimination.GetComponent<FirstPersonController>();
            HiderAntiCampSystem antiCamp = elimination.GetComponent<HiderAntiCampSystem>();
            HiderZoneStatusController zone = elimination.GetComponent<HiderZoneStatusController>();
            HiderCloneAbility cloneAbility = elimination.GetComponent<HiderCloneAbility>();
            HiderAbilityController ability = elimination.GetComponent<HiderAbilityController>();
            GameObject spectatorPanel = FindNamedObject("SpectatorStatusPanel");
            GameObject abilityPanel = FindNamedObject("HiderAbilityPanel");
            GameObject contextPanel = FindNamedObject("HiderContextPanel");
            GameObject zoneWarningPanel = FindNamedObject("ZoneWarningPanel");
            TextMeshProUGUI healthText = FindNamedObject("HealthText")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI spectatorText =
                FindNamedObject("SpectatorStatusText")?.GetComponent<TextMeshProUGUI>();

            PropHuntTestRoleSelector roleSelector =
                UnityEngine.Object.FindObjectOfType<PropHuntTestRoleSelector>();
            Require(roleSelector != null,
                "No PropHuntTestRoleSelector in Play Mode.", SmokeFailures);
            if (roleSelector == null)
            {
                SessionState.SetString(SmokeResultKey, string.Join("\n---\n", SmokeFailures));
                EditorApplication.ExitPlaymode();
                return;
            }

            // The smoke test validates the local-Hider elimination path. Make that role explicit
            // before subscribing to reset events or checking role-dependent HUD panels.
            roleSelector.SelectInitialHiderRole();
            Canvas.ForceUpdateCanvases();

            int eliminatedEvents = 0;
            int resetEvents = 0;
            int allEliminatedEvents = 0;
            health.Eliminated += _ => eliminatedEvents++;
            health.RevivedOrReset += _ => resetEvents++;
            if (roster != null) roster.AllHidersEliminated += () => allEliminatedEvents++;

            int aliveBefore = roster != null ? roster.AliveHiderCount : -1;
            health.TakeDamage(health.CurrentHealth, HiderDamageSource.Debug);
            health.TakeDamage(10, HiderDamageSource.Debug);
            health.SetHealth(0);

            Renderer[] hiderRenderers = elimination.GetComponentsInChildren<Renderer>(true);
            Renderer[] visibleHiderRenderers = hiderRenderers
                .Where(renderer => renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                .ToArray();
            string rendererStatesAfterElimination = DescribeRenderers(hiderRenderers);

            Require(health.CurrentHealth == 0 && health.IsEliminated && !health.IsAlive,
                "Health did not reach the sole eliminated state.", SmokeFailures);
            Require(eliminatedEvents == 1, $"Eliminated event count was {eliminatedEvents}, expected 1.", SmokeFailures);
            Require(transformSystem.IsEliminated && transformSystem.IsGameplayInputLocked,
                "PropTransformSystem did not mirror eliminated/input-locked state.", SmokeFailures);
            Require(movement == null || !movement.enabled,
                "Movement controller remained enabled after elimination.", SmokeFailures);
            Require(transformSystem.cameraModeManager == null ||
                    transformSystem.cameraModeManager.CurrentMode == PlayerCameraMode.Spectator,
                "Camera mode did not switch to Spectator.", SmokeFailures);
            Require(elimination.GetComponentsInChildren<Collider>(true).All(collider => !collider.enabled),
                "At least one Hider hit collider remained enabled.", SmokeFailures);
            Require(visibleHiderRenderers.Length == 0,
                $"Hider renderer cleanup mismatch. expectedVisible=0, actualVisible={visibleHiderRenderers.Length}, " +
                $"renderers=[{DescribeRenderers(visibleHiderRenderers)}].", SmokeFailures);
            Require(roster == null || roster.AliveHiderCount == Mathf.Max(0, aliveBefore - 1),
                "Roster alive count did not decrement exactly once.", SmokeFailures);
            Require(roster == null || roster.AliveHiderCount > 0 || allEliminatedEvents == 1,
                "AllHidersEliminated did not fire exactly once at zero alive.", SmokeFailures);
            Require(spectator == null || spectator.IsSpectating,
                "Local spectator mode did not activate.", SmokeFailures);
            Require(spectatorPanel == null || spectatorPanel.activeSelf,
                $"SpectatorStatusPanel visibility mismatch after Hider elimination. expected=True, " +
                $"actual={GetActiveState(spectatorPanel)}, role={roleSelector.CurrentControlledRole}, " +
                $"frame={Time.frameCount}.", SmokeFailures);
            Require(spectatorText == null || spectatorText.text == "KHÔNG CÒN HIDER ĐỂ THEO DÕI",
                "Spectator no-target text is incorrect.", SmokeFailures);
            Require(healthText == null || healthText.text == "0 / 100",
                "Health HUD did not remain visible at 0 / 100.", SmokeFailures);
            Require(abilityPanel == null || !abilityPanel.activeSelf,
                "Hider ability panel remained visible after elimination.", SmokeFailures);
            Require(contextPanel == null || !contextPanel.activeSelf,
                "Hider context panel remained visible after elimination.", SmokeFailures);
            Require(zoneWarningPanel == null || !zoneWarningPanel.activeSelf,
                "Zone warning panel remained visible after elimination.", SmokeFailures);
            Require(cloneAbility == null || cloneAbility.ActiveClones.Count == 0,
                "Owned clones were not cleaned up.", SmokeFailures);
            Require(ability == null || !ability.CanUseRandomProp,
                "Random ability remained usable after elimination.", SmokeFailures);
            Require(antiCamp == null || antiCamp.IsEliminated,
                "Anti-camp was not suppressed as eliminated.", SmokeFailures);
            Require(zone == null || (!zone.IsOutsideZone && !zone.IsZoneDamageActive),
                "Zone state was not cleared on elimination.", SmokeFailures);

            health.ResetForRound();

            Require(health.CurrentHealth == health.MaxHealth && health.IsAlive,
                "Health did not reset to full/alive.", SmokeFailures);
            Require(resetEvents == 1, $"Reset event count was {resetEvents}, expected 1.", SmokeFailures);
            Require(!transformSystem.IsEliminated && !transformSystem.IsGameplayInputLocked,
                "PropTransformSystem did not restore gameplay state.", SmokeFailures);
            Require(movement == null || movement.enabled,
                "Movement controller was not restored.", SmokeFailures);
            Require(transformSystem.cameraModeManager == null ||
                    transformSystem.cameraModeManager.CurrentMode == PlayerCameraMode.HumanFPS,
                "Camera mode did not restore to HumanFPS.", SmokeFailures);
            Require(roster == null || roster.AliveHiderCount == aliveBefore,
                "Roster alive count did not restore.", SmokeFailures);
            Require(spectator == null || !spectator.IsSpectating,
                "Spectator mode did not exit on reset.", SmokeFailures);
            Require(spectatorPanel == null || !spectatorPanel.activeSelf,
                "SpectatorStatusPanel did not hide on reset.", SmokeFailures);
            Require(healthText == null || healthText.text == "100 / 100",
                "Health HUD did not restore to 100 / 100.", SmokeFailures);
            Require(abilityPanel == null || abilityPanel.activeSelf,
                $"HiderAbilityPanel visibility mismatch after Hider reset. expected=True, " +
                $"actual={GetActiveState(abilityPanel)}, role={roleSelector.CurrentControlledRole}, " +
                $"frame={Time.frameCount}.", SmokeFailures);
            Require(antiCamp == null || !antiCamp.IsEliminated,
                "Anti-camp eliminated flag did not reset.", SmokeFailures);

            string rendererStatesAfterReset = DescribeRenderers(
                elimination.GetComponentsInChildren<Renderer>(true));

            Debug.Log(
                $"HiderEliminationSmoke: health=0->100, eliminatedEvents={eliminatedEvents}, " +
                $"resetEvents={resetEvents}, alive={aliveBefore}->{(roster != null ? roster.AliveHiderCount : -1)}, " +
                $"allEliminatedEvents={allEliminatedEvents}, spectatorNoTarget=" +
                $"{(spectator != null && spectator.CurrentTarget == null)}, role={roleSelector.CurrentControlledRole}, " +
                $"renderersAfterElimination=[{rendererStatesAfterElimination}], " +
                $"renderersAfterReset=[{rendererStatesAfterReset}], " +
                $"spectatorPanelAfterReset={GetActiveState(spectatorPanel)}, " +
                $"abilityPanelAfterReset={GetActiveState(abilityPanel)}.");
        }
        catch (Exception exception)
        {
            SmokeFailures.Add(exception.ToString());
        }

        SessionState.SetString(SmokeResultKey, string.Join("\n---\n", SmokeFailures));
        EditorApplication.ExitPlaymode();
    }

    private static string GetActiveState(GameObject target)
    {
        return target == null ? "missing" : target.activeSelf.ToString();
    }

    private static string DescribeRenderers(IEnumerable<Renderer> renderers)
    {
        return string.Join(", ", renderers
            .Where(renderer => renderer != null)
            .Select(renderer =>
                $"{GetTransformPath(renderer.transform)}<{renderer.GetType().Name}>:" +
                $"enabled={renderer.enabled},activeSelf={renderer.gameObject.activeSelf}," +
                $"activeInHierarchy={renderer.gameObject.activeInHierarchy}"));
    }

    private static string GetTransformPath(Transform target)
    {
        if (target == null) return "missing";
        List<string> names = new List<string>();
        for (Transform current = target; current != null; current = current.parent)
        {
            names.Add(current.name);
        }

        names.Reverse();
        return string.Join("/", names);
    }

    private static int CountNamedObjects(string objectName)
    {
        return SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Count(transform => transform.name == objectName);
    }

    private static GameObject FindNamedObject(string objectName)
    {
        return SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(transform => transform.name == objectName)
            .Select(transform => transform.gameObject)
            .FirstOrDefault();
    }

    private static void Require(bool condition, string message, ICollection<string> failures)
    {
        if (!condition) failures.Add(message);
    }

    private static void FinishValidation(string label, IReadOnlyCollection<string> failures)
    {
        if (failures.Count == 0)
        {
            Debug.Log($"HiderEliminationValidation: PASS — {label}.");
            return;
        }

        Debug.LogError($"HiderEliminationValidation: FAIL — {label}\n" + string.Join("\n", failures));
        if (Application.isBatchMode) EditorApplication.Exit(2);
    }
}
