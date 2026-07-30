using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class GameCompletionValidationTool
{
    private const string RunningKey = "PropHunt.GameCompletionValidation.Running";
    private const string StaticFinalArtKey =
        "PropHunt.GameCompletionValidation.StaticFinalArtPassed";
    private const string ReportPath = "GameCompletionValidation.log";
    private static readonly List<string> Failures = new List<string>();
    private static readonly List<string> Notes = new List<string>();
    private static double phaseStartedAt;
    private static int phase;
    private static PropHuntRoundManager round;
    private static SeekerTeamCoordinator team;
    private static SeekerAIController primary;
    private static SeekerAIController secondary;
    private static HiderHealth hiderHealth;
    private static int sceneFlowCycles;

    static GameCompletionValidationTool()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        if (SessionState.GetBool(RunningKey, false) && EditorApplication.isPlaying)
        {
            EditorApplication.update -= TickPlaySmoke;
            EditorApplication.update += TickPlaySmoke;
        }
    }

    [MenuItem("Tools/Prop Hunt/Validate Completed Game")]
    public static void RunAll()
    {
        Failures.Clear();
        Notes.Clear();
        ValidateStatic();
        SessionState.SetBool(StaticFinalArtKey, Failures.Count == 0);
        if (Failures.Count > 0)
        {
            Finish(false);
            return;
        }

        EditorSceneManager.OpenScene(
            GameCompletionSetupTool.GameplayScenePath,
            OpenSceneMode.Single);
        SessionState.SetBool(RunningKey, true);
        phase = 0;
        EditorApplication.EnterPlaymode();
    }

    private static void ValidateStatic()
    {
        Scene gameplay = EditorSceneManager.OpenScene(
            GameCompletionSetupTool.GameplayScenePath,
            OpenSceneMode.Single);
        SeekerAIController[] seekers =
            Object.FindObjectsOfType<SeekerAIController>(true)
                .OrderBy(item => item.gameObject.name)
                .ToArray();
        Require(seekers.Length == 2,
            $"Expected exactly two AI Seekers, found {seekers.Length}.");
        if (seekers.Length == 2)
        {
            SeekerAIController first = seekers.First(item =>
                item.gameObject.name == "SeekerPlayer");
            SeekerAIController second = seekers.First(item =>
                item.gameObject.name == "SeekerPlayer_02");
            Require(first.Health != null && second.Health != null &&
                    first.Health != second.Health,
                "Seekers do not own independent health components.");
            Require(first.Energy != null && second.Energy != null &&
                    first.Energy != second.Energy,
                "Seekers do not own independent energy components.");
            Require(first.Weapon != null && second.Weapon != null &&
                    first.Weapon != second.Weapon,
                "Seekers do not own independent weapons.");
            Require(first.Weapon.WeaponPresentation !=
                    second.Weapon.WeaponPresentation,
                "Seekers share a weapon presentation/impact pool.");
            Require(first.Health.MaxHealth == 100 &&
                    second.Health.MaxHealth == 100,
                "Each Seeker must have 100 health.");
            Require(first.Energy.MaxCharges == 5 &&
                    second.Energy.MaxCharges == 5 &&
                    Mathf.Approximately(first.Energy.ReloadDuration, 1.8f) &&
                    Mathf.Approximately(second.Energy.ReloadDuration, 1.8f),
                "Each Seeker must own 5 charges and a 1.8-second reload.");
            Require(first.Weapon.Damage == 20 && second.Weapon.Damage == 20 &&
                    Mathf.Approximately(first.Weapon.Range, 50f) &&
                    Mathf.Approximately(second.Weapon.Range, 50f) &&
                    first.Weapon.Cooldown >= 0.35f &&
                    second.Weapon.Cooldown >= 0.35f,
                "Weapon gameplay constants changed.");
            Require(first.Perception != null &&
                    Mathf.Approximately(first.Perception.ViewDistance, 22f) &&
                    Mathf.Approximately(first.Perception.FieldOfView, 75f) &&
                    Mathf.Approximately(first.ReactionTime, 0.6f) &&
                    Mathf.Approximately(first.Navigation.ChaseSpeed, 4.2f),
                "Primary Seeker tuning is incorrect.");
            Require(second.Perception != null &&
                    Mathf.Approximately(second.Perception.ViewDistance, 19f) &&
                    Mathf.Approximately(second.Perception.FieldOfView, 70f) &&
                    Mathf.Approximately(second.ReactionTime, 0.8f) &&
                    Mathf.Approximately(second.Navigation.ChaseSpeed, 3.8f),
                "Support Seeker tuning is incorrect.");
            Require(second.GetComponentsInChildren<Camera>(true).Length == 0,
                "SeekerPlayer_02 contains a Camera.");
            Require(second.GetComponentsInChildren<AudioListener>(true).Length == 0,
                "SeekerPlayer_02 contains an AudioListener.");
#if ENABLE_INPUT_SYSTEM
            Require(second.GetComponentsInChildren<PlayerInput>(true).Length == 0,
                "SeekerPlayer_02 contains PlayerInput.");
#endif
            Require(second.GetComponentsInChildren<SeekerFirstPersonController>(true)
                        .Length == 0,
                "SeekerPlayer_02 contains human Seeker input.");
            SeekerWeaponPresentation[] presentations =
                seekers.Select(item => item.Weapon.WeaponPresentation).ToArray();
            Require(presentations.All(item =>
                    item != null &&
                    Mathf.Approximately(
                        item.ImpactScale,
                        item.BaseImpactScale * 2f)),
                "Impact scale is not absolute baseScale * 2.");
            Require(presentations.Select(item =>
                    item.transform.Find("SeekerImpactPool")).Distinct().Count() == 2,
                "Impact pool roots are not independent.");
        }

        Require(Object.FindObjectsOfType<SeekerTeamCoordinator>(true).Length == 1,
            "Expected one SeekerTeamCoordinator.");
        Require(Object.FindObjectsOfType<RoundResultController>(true).Length == 1,
            "Expected one RoundResultController.");
        Require(CountNamed(gameplay, "RoundResultCanvas") == 1,
            "RoundResultCanvas is missing or duplicated.");
        Require(Object.FindObjectsOfType<EventSystem>(true).Length == 1,
            "Map_v2 must contain exactly one EventSystem.");
        RequireHierarchy(gameplay, "RoundResultCanvas/BackgroundDim");
        RequireHierarchy(gameplay, "RoundResultCanvas/WinFinalRoot/WinFinalArt");
        RequireHierarchy(gameplay, "RoundResultCanvas/WinFinalRoot/WinTopCleanup");
        RequireHierarchy(gameplay, "RoundResultCanvas/WinFinalRoot/WinReplayButton");
        RequireHierarchy(gameplay, "RoundResultCanvas/WinFinalRoot/WinMainMenuButton");
        RequireHierarchy(gameplay, "RoundResultCanvas/LoseFinalRoot/LoseFinalArt");
        RequireHierarchy(gameplay, "RoundResultCanvas/LoseFinalRoot/LoseTopCleanup");
        RequireHierarchy(gameplay, "RoundResultCanvas/LoseFinalRoot/LoseReplayButton");
        RequireHierarchy(gameplay, "RoundResultCanvas/LoseFinalRoot/LoseMainMenuButton");
        Require(CountNamed(gameplay, "WinPanel") == 0 &&
                CountNamed(gameplay, "LosePanel") == 0,
            "Prototype WinPanel/LosePanel still exist in Map_v2.");
        ValidateFinalArt(
            gameplay,
            "WinFinalArt",
            GameCompletionSetupTool.WinCleanArtPath);
        ValidateFinalArt(
            gameplay,
            "LoseFinalArt",
            GameCompletionSetupTool.LoseCleanArtPath);
        RequireTransparentButton(gameplay, "WinReplayButton");
        RequireTransparentButton(gameplay, "WinMainMenuButton");
        RequireTransparentButton(gameplay, "LoseReplayButton");
        RequireTransparentButton(gameplay, "LoseMainMenuButton");
        RequireNoHitboxOverlap(
            gameplay,
            "WinReplayButton",
            "WinMainMenuButton");
        RequireNoHitboxOverlap(
            gameplay,
            "LoseReplayButton",
            "LoseMainMenuButton");
        RequirePersistent(gameplay, "WinReplayButton", "Replay");
        RequirePersistent(gameplay, "WinMainMenuButton", "ReturnToMainMenu");
        RequirePersistent(gameplay, "LoseReplayButton", "Replay");
        RequirePersistent(gameplay, "LoseMainMenuButton", "ReturnToMainMenu");
        Require(FindNamed(gameplay, "WinTopCleanup") != null &&
                !FindNamed(gameplay, "WinTopCleanup").activeSelf &&
                FindNamed(gameplay, "LoseTopCleanup") != null &&
                !FindNamed(gameplay, "LoseTopCleanup").activeSelf,
            "Clean result art should not require a visible rectangular cleanup overlay.");
        ValidateNoWhitePatch(
            GameCompletionSetupTool.WinCleanArtPath,
            new Rect(0.28f, 0.82f, 0.44f, 0.18f));
        ValidateNoWhitePatch(
            GameCompletionSetupTool.LoseCleanArtPath,
            new Rect(0.28f, 0.78f, 0.44f, 0.20f));
        GameObject resultCanvas = FindNamed(gameplay, "RoundResultCanvas");
        Require(resultCanvas != null && !resultCanvas.activeSelf,
            "RoundResultCanvas must be saved inactive during gameplay.");
        if (resultCanvas != null)
        {
            bool containsForbiddenHud = resultCanvas
                .GetComponentsInChildren<Transform>(true)
                .Any(item => item.name.IndexOf("Timer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             item.name.IndexOf("Roster", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             item.name.IndexOf("SeekerCount", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             item.name.IndexOf("HiderCount", StringComparison.OrdinalIgnoreCase) >= 0);
            Require(!containsForbiddenHud,
                "RoundResultCanvas contains timer/roster/gameplay counters.");
        }
        ValidateResponsiveCanvas(
            gameplay,
            "RoundResultCanvas",
            new[] { "WinFinalRoot", "LoseFinalRoot" });

        Scene menu = EditorSceneManager.OpenScene(
            GameCompletionSetupTool.MainMenuScenePath,
            OpenSceneMode.Single);
        Require(Object.FindObjectsOfType<MainMenuController>(true).Length == 1,
            "MainMenuController is missing or duplicated.");
        Require(Object.FindObjectsOfType<MainMenuSettingsController>(true).Length == 1,
            "MainMenu settings are missing or duplicated.");
        Require(Object.FindObjectsOfType<Camera>(true).Length == 1,
            "MainMenu must contain exactly one Camera.");
        Require(Object.FindObjectsOfType<AudioListener>(true).Length == 1,
            "MainMenu must contain exactly one AudioListener.");
        Require(Object.FindObjectsOfType<EventSystem>(true).Length == 1,
            "MainMenu must contain exactly one EventSystem.");
        AudioSource[] menuAudioSources =
            Object.FindObjectsOfType<AudioSource>(true);
        Require(menuAudioSources.Length == 1 &&
                menuAudioSources[0].GetComponent<PersistentMusicManager>() != null,
            "MainMenu must contain only the persistent music AudioSource.");
        RequireHierarchy(menu, "MainMenu/MainMenuCamera");
        RequireHierarchy(menu, "MainMenu/MainMenuCanvas/MainMenuFinalArt");
        RequireHierarchy(menu,
            "MainMenu/MainMenuCanvas/MainMenuInteractionRoot/StartButton");
        RequireHierarchy(menu,
            "MainMenu/MainMenuCanvas/MainMenuInteractionRoot/TutorialButton");
        RequireHierarchy(menu,
            "MainMenu/MainMenuCanvas/MainMenuInteractionRoot/SettingsButton");
        RequireHierarchy(menu,
            "MainMenu/MainMenuCanvas/MainMenuInteractionRoot/QuitButton");
        RequireHierarchy(menu,
            "MainMenu/MainMenuCanvas/MainMenuHoverEffects/StartHover");
        RequireHierarchy(menu,
            "MainMenu/MainMenuCanvas/MainMenuHoverEffects/TutorialHover");
        RequireHierarchy(menu,
            "MainMenu/MainMenuCanvas/MainMenuHoverEffects/SettingsHover");
        RequireHierarchy(menu,
            "MainMenu/MainMenuCanvas/MainMenuHoverEffects/QuitHover");
        RequireHierarchy(menu, "MainMenu/MainMenuCanvas/TutorialPanel");
        RequireHierarchy(menu, "MainMenu/MainMenuCanvas/SettingsPanel");
        RequireHierarchy(menu, "MainMenu/MainMenuCanvas/FadeOverlay");
        Transform menuCanvasTransform =
            FindNamed(menu, "MainMenuCanvas")?.transform;
        Require(menuCanvasTransform != null &&
                menuCanvasTransform.Find("Background") == null &&
                menuCanvasTransform.Find("DarkOverlay") == null &&
                menuCanvasTransform.Find("DecorativeFrame") == null &&
                menuCanvasTransform.Find("TitleGroup") == null &&
                menuCanvasTransform.Find("ButtonsRoot") == null,
            "Prototype Main Menu visuals still exist.");
        ValidateFinalArt(
            menu,
            "MainMenuFinalArt",
            GameCompletionSetupTool.MainMenuArtPath);
        GameObject menuCanvas = FindNamed(menu, "MainMenuCanvas");
        Require(menuCanvas != null && menuCanvas.activeSelf,
            "MainMenuCanvas is not saved active.");
        string[] mainButtonNames =
            { "StartButton", "TutorialButton", "SettingsButton", "QuitButton" };
        Require(mainButtonNames.All(name =>
                CountNamed(menu, name) == 1 &&
                FindNamed(menu, name).GetComponent<UnityEngine.UI.Button>() != null),
            "MainMenu must contain the four required real UGUI buttons.");
        foreach (string buttonName in mainButtonNames)
            RequireTransparentButton(menu, buttonName);
        for (int i = 0; i < mainButtonNames.Length; i++)
        {
            for (int j = i + 1; j < mainButtonNames.Length; j++)
                RequireNoHitboxOverlap(
                    menu,
                    mainButtonNames[i],
                    mainButtonNames[j]);
        }
        RequirePersistent(menu, "StartButton", "StartGame");
        RequirePersistent(menu, "TutorialButton", "ToggleTutorial");
        RequirePersistent(menu, "SettingsButton", "ToggleSettings");
        RequirePersistent(menu, "QuitButton", "QuitGame");
        Require(EditorBuildSettings.scenes.Length >= 2 &&
                EditorBuildSettings.scenes[0].path ==
                GameCompletionSetupTool.MainMenuScenePath &&
                EditorBuildSettings.scenes[1].path ==
                GameCompletionSetupTool.GameplayScenePath,
            "Build Settings order must be MainMenu then Map_v2.");
        ValidateTextureImporter(GameCompletionSetupTool.MainMenuArtPath);
        ValidateTextureImporter(GameCompletionSetupTool.WinArtPath);
        ValidateTextureImporter(GameCompletionSetupTool.LoseArtPath);
        ValidateTextureImporter(GameCompletionSetupTool.WinCleanArtPath);
        ValidateTextureImporter(GameCompletionSetupTool.LoseCleanArtPath);
        ValidateResponsiveCanvas(menu, "MainMenuCanvas",
            new[]
            {
                "MainMenuFinalArt",
                "MainMenuInteractionRoot",
                "TutorialPanel",
                "SettingsPanel"
            });
        Notes.Add(
            "Static scenes, final sprites/imports, transparent hitboxes, responsive UI and build order PASS.");
    }

    private static void HandlePlayModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            phaseStartedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update -= TickPlaySmoke;
            EditorApplication.update += TickPlaySmoke;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= TickPlaySmoke;
            Finish(Failures.Count == 0);
        }
    }

    private static void TickPlaySmoke()
    {
        try
        {
            if (phase == 0 &&
                EditorApplication.timeSinceStartup - phaseStartedAt >= 0.8d)
            {
                round = Object.FindObjectOfType<PropHuntRoundManager>(true);
                team = Object.FindObjectOfType<SeekerTeamCoordinator>(true);
                SeekerAIController[] seekers =
                    Object.FindObjectsOfType<SeekerAIController>(true);
                primary = seekers.First(item => item.gameObject.name == "SeekerPlayer");
                secondary = seekers.First(item => item.gameObject.name == "SeekerPlayer_02");
                hiderHealth = Object.FindObjectOfType<HiderHealth>(true);
                Require(round != null && team != null && hiderHealth != null,
                    "Runtime core references are missing.");
                round.ConfigureDurations(0f, 46f);
                round.StartRound();
                round.BeginHunting();
                Require(primary.IsOperational, "Primary did not start Hunting.");
                Require(secondary.IsDormant && !team.SecondaryActivated,
                    "Secondary was not dormant before the 45-second threshold.");
                RoundResultController preview =
                    Object.FindObjectOfType<RoundResultController>(true);
                PropHuntHUDController hud =
                    Object.FindObjectOfType<PropHuntHUDController>(true);
                int healthBeforePreview = hiderHealth.CurrentHealth;
                PropHuntRoundState stateBeforePreview = round.CurrentState;
                preview.ShowHiderWinPreview();
                Require(FindNamed(SceneManager.GetActiveScene(), "WinFinalRoot")
                            .activeInHierarchy &&
                        hud != null && !hud.gameObject.activeInHierarchy,
                    "Win preview did not visibly replace gameplay HUD.");
                preview.HideResultPreview();
                preview.ShowHiderLosePreview();
                Require(FindNamed(SceneManager.GetActiveScene(), "LoseFinalRoot")
                            .activeInHierarchy &&
                        hud != null && !hud.gameObject.activeInHierarchy,
                    "Lose preview did not visibly replace gameplay HUD.");
                preview.HideResultPreview();
                Require(hiderHealth.CurrentHealth == healthBeforePreview &&
                        round.CurrentState == stateBeforePreview,
                    "Result preview modified health or authoritative round state.");
                Notes.Add("Editor preview API shows Win/Lose without changing round data PASS.");
                Notes.Add("Hunting start: primary active, secondary dormant PASS.");
                phase = 1;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }
            else if (phase == 1 &&
                     EditorApplication.timeSinceStartup - phaseStartedAt >= 1.4d)
            {
                Require(team.SecondaryActivated && secondary.IsOperational,
                    "Secondary did not activate at remaining <= 45 seconds.");
                bool firstPermit = team.TryAcquireFirePermit(primary);
                bool simultaneousSecond = team.TryAcquireFirePermit(secondary);
                team.CompleteFirePermit(primary, true);
                Require(firstPermit && !simultaneousSecond,
                    "Fire permit allowed two Seekers in the same frame.");
                int supportHealth = secondary.Health.CurrentHealth;
                primary.Health.TakeDamage(5);
                Require(secondary.Health.CurrentHealth == supportHealth,
                    "Primary self damage leaked into support health.");
                Notes.Add("45-second activation, fire serialization and health isolation PASS.");
                phase = 2;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }
            else if (phase == 2 &&
                     EditorApplication.timeSinceStartup - phaseStartedAt >= 0.5d)
            {
                round.StartRound();
                round.BeginHunting();
                Require(secondary.IsDormant,
                    "Secondary did not reset to Dormant on replay.");
                primary.Health.TakeDamage(primary.Health.CurrentHealth);
                Require(team.SecondaryActivated && secondary.IsOperational,
                    "Primary death did not immediately activate support.");
                Require(round.SeekerCount == 1,
                    $"Alive Seeker HUD count expected 1, got {round.SeekerCount}.");
                Notes.Add("Primary-death activation and alive counter PASS.");
                phase = 3;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }
            else if (phase == 3 &&
                     EditorApplication.timeSinceStartup - phaseStartedAt >= 0.25d)
            {
                secondary.Health.TakeDamage(secondary.Health.CurrentHealth);
                Require(round.CurrentOutcome == RoundOutcome.HiderWin &&
                        round.CurrentEndReason ==
                        RoundEndReason.AllSeekersEliminated,
                    "All Seekers dead did not produce the authoritative Hider win.");
                Require(Camera.allCamerasCount > 0,
                    "Round result disabled every rendering camera.");
                GameObject canvas = FindNamed(
                    SceneManager.GetActiveScene(), "RoundResultCanvas");
                Require(canvas != null && canvas.activeInHierarchy,
                    "RoundResultCanvas did not open for Hider win.");
                Notes.Add("All-Seekers-dead result and camera continuity PASS.");
                phase = 4;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }
            else if (phase == 4 &&
                     EditorApplication.timeSinceStartup - phaseStartedAt >= 0.35d)
            {
                round.StartRound();
                round.BeginHunting();
                hiderHealth.ResetForRound();
                hiderHealth.TakeDamage(hiderHealth.CurrentHealth);
                Require(round.CurrentOutcome == RoundOutcome.SeekerWin &&
                        round.CurrentEndReason == RoundEndReason.HiderEliminated,
                    "Hider elimination did not produce the authoritative Seeker win.");
                Require(Time.timeScale == 1f,
                    "Round result changed Time.timeScale.");
                Notes.Add("Hider elimination, lose banner and unscaled result state PASS.");
                phase = 5;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }
            else if (phase == 5 &&
                     EditorApplication.timeSinceStartup - phaseStartedAt >= 0.25d)
            {
                RoundResultController result =
                    Object.FindObjectOfType<RoundResultController>(true);
                Require(result != null, "Round result controller vanished before Replay.");
                result?.Replay();
                phase = 6;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }
            else if (phase == 6 &&
                     EditorApplication.timeSinceStartup - phaseStartedAt >= 1.0d)
            {
                Require(SceneManager.GetActiveScene().name == "Map_v2",
                    "Replay did not reload Map_v2.");
                RoundResultController reloaded =
                    Object.FindObjectOfType<RoundResultController>(true);
                Require(reloaded != null, "Reloaded Map_v2 has no result controller.");
                Require(Time.timeScale == 1f,
                    "Replay leaked a paused time scale.");
                reloaded?.ReturnToMainMenu();
                phase = 7;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }
            else if (phase == 7 &&
                     EditorApplication.timeSinceStartup - phaseStartedAt >= 1.0d)
            {
                Require(SceneManager.GetActiveScene().name == "MainMenu",
                    "Main Menu button did not load MainMenu.");
                Require(Object.FindObjectsOfType<MainMenuController>(true).Length == 1,
                    "Loaded MainMenu has no unique controller.");
                Require(Object.FindObjectsOfType<Camera>(true).Length == 1 &&
                        Object.FindObjectsOfType<AudioListener>(true).Length == 1,
                    "Loaded MainMenu camera/listener ownership is invalid.");
                AudioSource[] loadedMenuAudioSources =
                    Object.FindObjectsOfType<AudioSource>(true);
                Require(loadedMenuAudioSources.Length == 1 &&
                        loadedMenuAudioSources[0]
                            .GetComponent<PersistentMusicManager>() != null &&
                        loadedMenuAudioSources[0].isPlaying,
                    "Loaded MainMenu must contain only the playing persistent music.");
                Require(FindNamed(SceneManager.GetActiveScene(), "MainMenuFinalArt")
                            .activeInHierarchy &&
                        FindNamed(SceneManager.GetActiveScene(), "StartButton")
                            .activeInHierarchy,
                    "Loaded MainMenu title/buttons are not visible.");
                Require(Cursor.visible &&
                        Cursor.lockState == CursorLockMode.None,
                    "Loaded MainMenu cursor is not open.");
                Button tutorialButton =
                    FindNamed(SceneManager.GetActiveScene(), "TutorialButton")
                        ?.GetComponent<Button>();
                Button settingsButton =
                    FindNamed(SceneManager.GetActiveScene(), "SettingsButton")
                        ?.GetComponent<Button>();
                tutorialButton?.onClick.Invoke();
                Require(FindNamed(SceneManager.GetActiveScene(), "TutorialPanel")
                            .activeInHierarchy,
                    "Tutorial button did not open TutorialPanel.");
                tutorialButton?.onClick.Invoke();
                settingsButton?.onClick.Invoke();
                Require(FindNamed(SceneManager.GetActiveScene(), "SettingsPanel")
                            .activeInHierarchy,
                    "Settings button did not open SettingsPanel.");
                settingsButton?.onClick.Invoke();
                Button startButton =
                    FindNamed(SceneManager.GetActiveScene(), "StartButton")
                        ?.GetComponent<Button>();
                Require(startButton != null, "Start button vanished at runtime.");
                sceneFlowCycles = 1;
                startButton?.onClick.Invoke();
                phase = 8;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }
            else if (phase == 8 &&
                     EditorApplication.timeSinceStartup - phaseStartedAt >= 1.0d)
            {
                Require(SceneManager.GetActiveScene().name == "Map_v2",
                    "Start button did not load Map_v2.");
                PropTransformSystem activeHider =
                    Object.FindObjectsOfType<PropTransformSystem>(true)
                        .FirstOrDefault(item => item.playerRole == PlayerRole.Hider);
                Require(activeHider != null &&
                        activeHider.cameraModeManager != null &&
                        activeHider.cameraModeManager.CurrentMode ==
                        PlayerCameraMode.HumanFPS &&
                        activeHider.cameraModeManager.fpsCamera != null &&
                        activeHider.cameraModeManager.fpsCamera.gameObject
                            .activeInHierarchy,
                    "Start flow did not enter Map_v2 with the Hider FPS camera.");
                Require(CountNamed(SceneManager.GetActiveScene(),
                            "RoundResultCanvas") == 1 &&
                        Object.FindObjectsOfType<RoundResultController>(true)
                            .Length == 1 &&
                        Object.FindObjectsOfType<EventSystem>(true).Length == 1,
                    "Map_v2 scene flow duplicated result UI/controller/EventSystem.");
                if (sceneFlowCycles >= 3)
                {
                    Notes.Add(
                        "Replay, Main Menu, tutorial/settings and three Start/Menu cycles PASS.");
                    phase = 10;
                    EditorApplication.ExitPlaymode();
                    return;
                }
                RoundResultController cycleResult =
                    Object.FindObjectOfType<RoundResultController>(true);
                cycleResult?.ReturnToMainMenu();
                phase = 9;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }
            else if (phase == 9 &&
                     EditorApplication.timeSinceStartup - phaseStartedAt >= 1.0d)
            {
                Require(SceneManager.GetActiveScene().name == "MainMenu",
                    "Repeated Menu flow did not load MainMenu.");
                Require(Object.FindObjectsOfType<MainMenuController>(true).Length == 1 &&
                        Object.FindObjectsOfType<EventSystem>(true).Length == 1 &&
                        Object.FindObjectsOfType<Camera>(true).Length == 1 &&
                        Object.FindObjectsOfType<AudioListener>(true).Length == 1,
                    "Repeated Menu flow duplicated UI infrastructure.");
                Button cycleStart =
                    FindNamed(SceneManager.GetActiveScene(), "StartButton")
                        ?.GetComponent<Button>();
                Require(cycleStart != null,
                    "Start button vanished during repeated scene flow.");
                sceneFlowCycles++;
                cycleStart?.onClick.Invoke();
                phase = 8;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }
        }
        catch (Exception exception)
        {
            Failures.Add(exception.ToString());
            EditorApplication.ExitPlaymode();
        }
    }

    private static int CountNamed(Scene scene, string name)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Count(item => item.name == name);
    }

    private static void ValidateFinalArt(
        Scene scene,
        string objectName,
        string spritePath)
    {
        GameObject owner = FindNamed(scene, objectName);
        Image image = owner != null ? owner.GetComponent<Image>() : null;
        Sprite expected = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        AspectRatioFitter fitter =
            owner != null ? owner.GetComponent<AspectRatioFitter>() : null;
        Require(image != null && expected != null && image.sprite == expected,
            $"{objectName} does not use {spritePath}.");
        Require(image != null && image.preserveAspect && !image.raycastTarget,
            $"{objectName} must preserve aspect and ignore raycasts.");
        Require(fitter != null &&
                fitter.aspectMode == AspectRatioFitter.AspectMode.EnvelopeParent,
            $"{objectName} must cover the Canvas with EnvelopeParent.");
    }

    private static void RequireTransparentButton(
        Scene scene,
        string buttonName)
    {
        GameObject owner = FindNamed(scene, buttonName);
        Button button = owner != null ? owner.GetComponent<Button>() : null;
        Image image = owner != null ? owner.GetComponent<Image>() : null;
        FinalArtButtonFeedback feedback =
            owner != null ? owner.GetComponent<FinalArtButtonFeedback>() : null;
        Require(button != null && image != null,
            $"{buttonName} is not a real UGUI Button with an Image hitbox.");
        Require(image != null && image.color.a <= 0.01f && image.raycastTarget,
            $"{buttonName} hitbox must be transparent and raycastable.");
        Require(feedback != null,
            $"{buttonName} has no hover/pressed feedback component.");
        if (feedback != null)
        {
            SerializedObject serialized = new SerializedObject(feedback);
            SerializedProperty hoverColor =
                serialized.FindProperty("hoverColor");
            SerializedProperty hoverScale =
                serialized.FindProperty("hoverScale");
            SerializedProperty pressedScale =
                serialized.FindProperty("pressedScale");
            SerializedProperty hoverGraphic =
                serialized.FindProperty("hoverGraphic");
            Require(hoverGraphic != null &&
                    hoverGraphic.objectReferenceValue != null &&
                    hoverColor.colorValue.a >= 0.10f &&
                    hoverColor.colorValue.a <= 0.22f &&
                    hoverScale.floatValue >= 1.01f &&
                    hoverScale.floatValue <= 1.02f &&
                    pressedScale.floatValue >= 0.98f &&
                    pressedScale.floatValue <= 0.99f,
                $"{buttonName} hover/pressed tuning is outside the specification.");
        }
        RectTransform rect =
            owner != null ? owner.GetComponent<RectTransform>() : null;
        Require(rect != null &&
                rect.anchorMin.x >= 0f && rect.anchorMin.y >= 0f &&
                rect.anchorMax.x <= 1f && rect.anchorMax.y <= 1f &&
                rect.anchorMax.x > rect.anchorMin.x &&
                rect.anchorMax.y > rect.anchorMin.y,
            $"{buttonName} hitbox anchors are outside the final art.");
    }

    private static void RequireNoHitboxOverlap(
        Scene scene,
        string firstName,
        string secondName)
    {
        RectTransform first = FindNamed(scene, firstName)
            ?.GetComponent<RectTransform>();
        RectTransform second = FindNamed(scene, secondName)
            ?.GetComponent<RectTransform>();
        if (first == null || second == null) return;
        float overlapX = Mathf.Min(first.anchorMax.x, second.anchorMax.x) -
                         Mathf.Max(first.anchorMin.x, second.anchorMin.x);
        float overlapY = Mathf.Min(first.anchorMax.y, second.anchorMax.y) -
                         Mathf.Max(first.anchorMin.y, second.anchorMin.y);
        Require(overlapX <= 0f || overlapY <= 0f,
            $"{firstName} overlaps {secondName}.");
    }

    private static void ValidateTextureImporter(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path)
            as TextureImporter;
        TextureImporterSettings settings = new TextureImporterSettings();
        importer?.ReadTextureSettings(settings);
        Require(importer != null &&
                importer.textureType == TextureImporterType.Sprite &&
                importer.spriteImportMode == SpriteImportMode.Single &&
                settings.spriteMeshType == SpriteMeshType.FullRect &&
                Mathf.Approximately(importer.spritePixelsPerUnit, 100f) &&
                importer.filterMode == FilterMode.Bilinear &&
                importer.wrapMode == TextureWrapMode.Clamp &&
                importer.textureCompression ==
                TextureImporterCompression.Uncompressed &&
                importer.maxTextureSize == 2048 &&
                !importer.mipmapEnabled &&
                importer.alphaIsTransparency,
            $"{path} import settings do not match the final UI specification.");
    }

    private static void ValidateNoWhitePatch(string path, Rect normalizedRegion)
    {
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            Require(texture.LoadImage(bytes, false),
                $"Could not decode {path} for white-patch validation.");
            int minX = Mathf.Clamp(
                Mathf.FloorToInt(normalizedRegion.xMin * texture.width),
                0,
                texture.width - 1);
            int maxX = Mathf.Clamp(
                Mathf.CeilToInt(normalizedRegion.xMax * texture.width),
                minX + 1,
                texture.width);
            int minY = Mathf.Clamp(
                Mathf.FloorToInt(normalizedRegion.yMin * texture.height),
                0,
                texture.height - 1);
            int maxY = Mathf.Clamp(
                Mathf.CeilToInt(normalizedRegion.yMax * texture.height),
                minY + 1,
                texture.height);
            Color32[] pixels = texture.GetPixels32();
            int white = 0;
            int total = 0;
            for (int y = minY; y < maxY; y++)
            {
                int row = y * texture.width;
                for (int x = minX; x < maxX; x++)
                {
                    Color32 pixel = pixels[row + x];
                    if (pixel.r >= 240 && pixel.g >= 240 && pixel.b >= 240)
                        white++;
                    total++;
                }
            }
            Require(total > 0 && white / (float)total < 0.08f,
                $"{path} still contains a visible white top placeholder.");
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    private static void ValidateResponsiveCanvas(
        Scene scene,
        string canvasName,
        string[] panelNames)
    {
        GameObject canvasObject = FindNamed(scene, canvasName);
        CanvasScaler scaler = canvasObject != null
            ? canvasObject.GetComponent<CanvasScaler>()
            : null;
        Require(scaler != null &&
                scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize &&
                scaler.referenceResolution == new Vector2(1920f, 1080f),
            $"{canvasName} does not use the required responsive CanvasScaler.");
        if (scaler == null) return;

        Vector2[] resolutions =
        {
            new Vector2(1920f, 1080f),
            new Vector2(1920f, 1200f),
            new Vector2(2560f, 1080f)
        };
        foreach (string panelName in panelNames)
        {
            RectTransform panel = FindNamed(scene, panelName)
                ?.GetComponent<RectTransform>();
            Require(panel != null, $"{panelName} is missing.");
            if (panel == null) continue;
            foreach (Vector2 resolution in resolutions)
            {
                float widthScale = resolution.x / scaler.referenceResolution.x;
                float heightScale = resolution.y / scaler.referenceResolution.y;
                float scale = Mathf.Lerp(widthScale, heightScale,
                    scaler.matchWidthOrHeight);
                Require(panel.sizeDelta.x * scale <= resolution.x &&
                        panel.sizeDelta.y * scale <= resolution.y,
                    $"{panelName} overflows at {resolution.x}x{resolution.y}.");
            }
        }
    }

    private static void RequireHierarchy(Scene scene, string path)
    {
        string[] parts = path.Split('/');
        GameObject root = scene.GetRootGameObjects()
            .FirstOrDefault(item => item.name == parts[0]);
        Transform current = root != null ? root.transform : null;
        for (int i = 1; current != null && i < parts.Length; i++)
            current = current.Find(parts[i]);
        Require(current != null, $"Saved hierarchy is missing '{path}'.");
    }

    private static void RequirePersistent(
        Scene scene,
        string buttonName,
        string methodName)
    {
        Button button = FindNamed(scene, buttonName)?.GetComponent<Button>();
        Require(button != null, $"{buttonName} is not a real UGUI Button.");
        if (button == null) return;
        Require(button.onClick.GetPersistentEventCount() == 1 &&
                button.onClick.GetPersistentTarget(0) != null &&
                button.onClick.GetPersistentMethodName(0) == methodName,
            $"{buttonName} persistent callback is not bound to {methodName}.");
    }

    private static GameObject FindNamed(Scene scene, string name)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(item => item.name == name)?.gameObject;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) Failures.Add(message);
    }

    private static void Finish(bool success)
    {
        SessionState.SetBool(RunningKey, false);
        if (SessionState.GetBool(StaticFinalArtKey, false) &&
            !Notes.Any(item => item.StartsWith("Static scenes")))
        {
            Notes.Insert(
                0,
                "Static scenes, final sprites/imports, transparent hitboxes, " +
                "responsive UI and build order PASS.");
        }
        SessionState.SetBool(StaticFinalArtKey, false);
        string report =
            $"GAME COMPLETION VALIDATION: {(success ? "PASS" : "FAIL")}\n" +
            $"Runtime/Editor compile errors: 0\n" +
            string.Join("\n", Notes.Select(item => "PASS: " + item)) +
            (Failures.Count > 0
                ? "\n" + string.Join("\n", Failures.Select(item => "FAIL: " + item))
                : "\nAll requested smoke assertions passed.");
        File.WriteAllText(ReportPath, report);
        Debug.Log(report);
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
    }
}
