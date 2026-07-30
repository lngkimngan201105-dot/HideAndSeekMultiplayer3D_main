using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class PersistentMusicValidationTool
{
    private const string RunningKey =
        "PropHunt.PersistentMusicValidation.Running";
    private const string StageKey =
        "PropHunt.PersistentMusicValidation.Stage";
    private const string FailedKey =
        "PropHunt.PersistentMusicValidation.Failed";
    private const string NotesKey =
        "PropHunt.PersistentMusicValidation.Notes";
    private const string ReportPath = "PersistentMusicValidation.log";
    private const string RequestPath = "PersistentMusicValidation.run";

    private static int phase;
    private static double phaseStartedAt;
    private static int managerInstanceId;
    private static int previousTimeSamples;

    static PersistentMusicValidationTool()
    {
        EditorApplication.update -= TryRunRequestedValidation;
        EditorApplication.update += TryRunRequestedValidation;
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;

        if (SessionState.GetBool(RunningKey, false) &&
            EditorApplication.isPlaying)
        {
            EditorApplication.update -= TickPlayMode;
            EditorApplication.update += TickPlayMode;
        }
    }

    [MenuItem("Tools/Prop Hunt/Validate Persistent Music")]
    public static void RunAll()
    {
        if (Application.isPlaying || EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
        {
            Debug.LogWarning(
                "PersistentMusicValidation: wait for Edit Mode and compilation.");
            return;
        }

        SessionState.SetBool(RunningKey, false);
        SessionState.SetInt(StageKey, 0);
        SessionState.SetBool(FailedKey, false);
        SessionState.SetString(NotesKey, string.Empty);

        try
        {
            PersistentMusicSetupTool.SetupPersistentMusic();
            string firstMenuHash =
                HashFile(PersistentMusicSetupTool.MainMenuScenePath);
            string firstMapHash =
                HashFile(PersistentMusicSetupTool.MapScenePath);
            AppendNote("Setup pass 1: PASS.");

            PersistentMusicSetupTool.SetupPersistentMusic();
            string secondMenuHash =
                HashFile(PersistentMusicSetupTool.MainMenuScenePath);
            string secondMapHash =
                HashFile(PersistentMusicSetupTool.MapScenePath);
            Require(firstMenuHash == secondMenuHash &&
                    firstMapHash == secondMapHash,
                "Setup pass 2 changed a scene; setup is not idempotent.");
            AppendNote("Setup pass 2: PASS; MainMenu and Map_v2 hashes unchanged.");

            ValidateStaticSetup();
            AppendNote("Static scene/import validation: PASS.");
            EditorSceneManager.OpenScene(
                PersistentMusicSetupTool.MainMenuScenePath,
                OpenSceneMode.Single);
            SessionState.SetBool(RunningKey, true);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception exception)
        {
            RecordFailure("Static validation failed: " + exception);
            Finish();
        }
    }

    private static void TryRunRequestedValidation()
    {
        if (Application.isPlaying || EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
        {
            return;
        }

        string request = Path.Combine(
            Directory.GetCurrentDirectory(), RequestPath);
        if (!File.Exists(request))
        {
            return;
        }

        File.Delete(request);
        RunAll();
    }

    private static void ValidateStaticSetup()
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(
            PersistentMusicSetupTool.ClipPath);
        Require(clip != null,
            $"Missing AudioClip: {PersistentMusicSetupTool.ClipPath}");

        AudioImporter importer =
            AssetImporter.GetAtPath(PersistentMusicSetupTool.ClipPath)
                as AudioImporter;
        Require(importer != null, "Performance.wav has no AudioImporter.");
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        AppendNote(
            $"Clip metadata: length={clip.length:F3}s, " +
            $"sampleRate={clip.frequency}Hz, channels={clip.channels}, " +
            $"samples={clip.samples}, loadType={settings.loadType}, " +
            $"compression={settings.compressionFormat}, quality={settings.quality:F2}.");

        Scene menu = EditorSceneManager.OpenScene(
            PersistentMusicSetupTool.MainMenuScenePath,
            OpenSceneMode.Single);
        PersistentMusicManager[] menuManagers =
            Object.FindObjectsOfType<PersistentMusicManager>(true)
                .Where(item => item.gameObject.scene == menu)
                .ToArray();
        Require(menuManagers.Length == 1,
            $"MainMenu expected one manager, found {menuManagers.Length}.");
        PersistentMusicManager manager = menuManagers[0];
        AudioSource[] managerSources = manager.GetComponents<AudioSource>();
        Require(manager.transform.parent == null &&
                manager.gameObject.name == "PersistentMusicManager",
            "MainMenu manager is not the expected root hierarchy object.");
        Require(managerSources.Length == 1,
            $"Manager expected one AudioSource, found {managerSources.Length}.");
        AudioSource source = managerSources[0];
        Require(source.clip == clip, "Manager uses the wrong AudioClip.");
        Require(source.loop && source.playOnAwake,
            "Manager AudioSource must loop and Play On Awake.");
        Require(Mathf.Approximately(source.spatialBlend, 0f),
            "Manager music is not 2D.");
        Require(Mathf.Approximately(source.volume,
                    PersistentMusicManager.DefaultMusicVolume),
            "Configured source volume is not 0.25.");
        Require(Mathf.Approximately(source.pitch, 1f) &&
                !source.mute &&
                !source.bypassEffects &&
                !source.bypassListenerEffects &&
                source.bypassReverbZones &&
                source.priority == 128 &&
                Mathf.Approximately(source.dopplerLevel, 0f),
            "Manager AudioSource settings are incomplete.");
        Require(manager.GetComponent<AudioListener>() == null &&
                manager.GetComponent<Rigidbody>() == null,
            "Manager must not own an AudioListener or Rigidbody.");
        Require(CountPerformanceSources(menu, clip) == 1,
            "MainMenu contains a duplicate Performance.wav source.");

        Scene map = EditorSceneManager.OpenScene(
            PersistentMusicSetupTool.MapScenePath,
            OpenSceneMode.Single);
        PersistentMusicBootstrap[] bootstraps =
            Object.FindObjectsOfType<PersistentMusicBootstrap>(true)
                .Where(item => item.gameObject.scene == map)
                .ToArray();
        Require(Object.FindObjectsOfType<PersistentMusicManager>(true)
                    .Count(item => item.gameObject.scene == map) == 0,
            "Map_v2 serializes a PersistentMusicManager.");
        Require(bootstraps.Length == 1 &&
                bootstraps[0].MusicClip == clip &&
                bootstraps[0].GetComponent<AudioSource>() == null,
            "Map_v2 bootstrap is missing, duplicated, or owns AudioSource.");
        Require(CountPerformanceSources(map, clip) == 0,
            "Map_v2 serializes a duplicate Performance.wav source.");

        string roundSource = File.ReadAllText(Path.Combine(
            Directory.GetCurrentDirectory(),
            "Assets/Scripts/PropHunt/UI/RoundResultController.cs"));
        Require(!roundSource.Contains("musicSource.Stop()"),
            "RoundResultController stops the persistent music.");
    }

    private static void HandlePlayModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(RunningKey, false))
        {
            return;
        }

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            phase = 0;
            phaseStartedAt = EditorApplication.timeSinceStartup;
            managerInstanceId = 0;
            previousTimeSamples = 0;
            EditorApplication.update -= TickPlayMode;
            EditorApplication.update += TickPlayMode;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= TickPlayMode;
            if (SessionState.GetBool(FailedKey, false))
            {
                Finish();
                return;
            }

            int stage = SessionState.GetInt(StageKey, 0);
            if (stage == 0)
            {
                try
                {
                    SessionState.SetInt(StageKey, 1);
                    EditorSceneManager.OpenScene(
                        PersistentMusicSetupTool.MapScenePath,
                        OpenSceneMode.Single);
                    EditorApplication.EnterPlaymode();
                }
                catch (Exception exception)
                {
                    RecordFailure(
                        "Could not start direct Map_v2 test: " + exception);
                    Finish();
                }
            }
            else
            {
                Finish();
            }
        }
    }

    private static void TickPlayMode()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - phaseStartedAt < 0.8d)
            {
                return;
            }

            if (SessionState.GetInt(StageKey, 0) == 0)
            {
                TickMainMenuFlow();
            }
            else
            {
                TickDirectMapFlow();
            }
        }
        catch (Exception exception)
        {
            RecordFailure("Play Mode validation failed: " + exception);
            EditorApplication.update -= TickPlayMode;
            EditorApplication.ExitPlaymode();
        }
    }

    private static void TickMainMenuFlow()
    {
        if (phase == 0)
        {
            Require(SceneManager.GetActiveScene().name == "MainMenu",
                "Play test did not start in MainMenu.");
            PersistentMusicManager manager = RequireRuntimeMusic();
            managerInstanceId = manager.GetInstanceID();
            previousTimeSamples = manager.MusicSource.timeSamples;
            Require(previousTimeSamples > 0,
                "Performance.wav did not begin in MainMenu.");
            AppendNote(
                $"MainMenu Play test: PASS (instance={managerInstanceId}, " +
                $"timeSamples={previousTimeSamples}).");
            phase = 1;
            phaseStartedAt = EditorApplication.timeSinceStartup;
            Object.FindObjectOfType<MainMenuController>().StartGame();
            return;
        }

        if (phase == 1)
        {
            Require(SceneManager.GetActiveScene().name == "Map_v2",
                "Start Game did not load Map_v2.");
            ValidateContinuousInstance("MainMenu -> Map_v2");
            AppendNote("Scene transition test: PASS; instance and sample clock continued.");
            RoundResultController result =
                Object.FindObjectOfType<RoundResultController>(true);
            Require(result != null, "RoundResultController is missing.");
            result.ShowHiderWinPreview();
            phase = 2;
            phaseStartedAt = EditorApplication.timeSinceStartup;
            return;
        }

        if (phase == 2)
        {
            ValidateContinuousInstance("Hider win banner");
            Object.FindObjectOfType<RoundResultController>(true)
                .ShowHiderLosePreview();
            phase = 3;
            phaseStartedAt = EditorApplication.timeSinceStartup;
            return;
        }

        if (phase == 3)
        {
            ValidateContinuousInstance("Hider lose banner");
            AppendNote("Win/Lose banner test: PASS; music remained active.");
            RoundResultController result =
                Object.FindObjectOfType<RoundResultController>(true);
            phase = 4;
            phaseStartedAt = EditorApplication.timeSinceStartup;
            result.Replay();
            return;
        }

        if (phase == 4)
        {
            Require(SceneManager.GetActiveScene().name == "Map_v2",
                "Replay did not reload Map_v2.");
            ValidateContinuousInstance("Replay");
            AppendNote("Replay test: PASS; no restart and no duplicate.");
            RoundResultController result =
                Object.FindObjectOfType<RoundResultController>(true);
            phase = 5;
            phaseStartedAt = EditorApplication.timeSinceStartup;
            result.ReturnToMainMenu();
            return;
        }

        Require(SceneManager.GetActiveScene().name == "MainMenu",
            "Return to Menu did not load MainMenu.");
        ValidateContinuousInstance("Return to MainMenu");
        AppendNote("Return Menu test: PASS; no restart and no duplicate.");
        AppendNote("Runtime compile: 0 errors.");
        EditorApplication.update -= TickPlayMode;
        EditorApplication.ExitPlaymode();
    }

    private static void TickDirectMapFlow()
    {
        Require(SceneManager.GetActiveScene().name == "Map_v2",
            "Direct scene test did not start in Map_v2.");
        PersistentMusicManager manager = RequireRuntimeMusic();
        Require(manager.MusicSource.timeSamples > 0,
            "Direct Map_v2 bootstrap created music but it is not advancing.");
        Require(Object.FindObjectsOfType<PersistentMusicBootstrap>(true).Length == 1,
            "Direct Map_v2 has no unique bootstrap.");
        AppendNote(
            "Direct Map_v2 Play test: PASS; bootstrap created exactly one " +
            "playing manager.");
        AppendNote("Editor compile: 0 errors.");
        EditorApplication.update -= TickPlayMode;
        EditorApplication.ExitPlaymode();
    }

    private static PersistentMusicManager RequireRuntimeMusic()
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(
            PersistentMusicSetupTool.ClipPath);
        PersistentMusicManager[] managers =
            Resources.FindObjectsOfTypeAll<PersistentMusicManager>()
                .Where(item => item != null &&
                               item.gameObject.scene.IsValid() &&
                               item.gameObject.activeInHierarchy)
                .ToArray();
        Require(managers.Length == 1,
            $"Runtime expected one manager, found {managers.Length}.");

        AudioSource[] performanceSources =
            Resources.FindObjectsOfTypeAll<AudioSource>()
                .Where(item => item != null &&
                               item.gameObject.scene.IsValid() &&
                               item.clip == clip)
                .ToArray();
        Require(performanceSources.Length == 1,
            $"Runtime expected one Performance source, found " +
            $"{performanceSources.Length}.");
        Require(performanceSources[0].isPlaying &&
                performanceSources[0].loop &&
                Mathf.Approximately(performanceSources[0].spatialBlend, 0f),
            "Runtime Performance source is not playing as looping 2D music.");
        return managers[0];
    }

    private static void ValidateContinuousInstance(string context)
    {
        PersistentMusicManager manager = RequireRuntimeMusic();
        Require(manager.GetInstanceID() == managerInstanceId,
            $"{context}: manager instance changed.");
        int currentSamples = manager.MusicSource.timeSamples;
        Require(currentSamples > previousTimeSamples,
            $"{context}: timeSamples did not continue " +
            $"({previousTimeSamples} -> {currentSamples}).");
        previousTimeSamples = currentSamples;
    }

    private static int CountPerformanceSources(Scene scene, AudioClip clip)
    {
        return Object.FindObjectsOfType<AudioSource>(true)
            .Count(source => source.gameObject.scene == scene &&
                             source.clip == clip);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AppendNote(string note)
    {
        string existing = SessionState.GetString(NotesKey, string.Empty);
        SessionState.SetString(
            NotesKey,
            string.IsNullOrEmpty(existing) ? note : existing + "\n" + note);
    }

    private static void RecordFailure(string failure)
    {
        SessionState.SetBool(FailedKey, true);
        AppendNote("FAIL: " + failure);
        Debug.LogError(failure);
    }

    private static void Finish()
    {
        bool success = !SessionState.GetBool(FailedKey, false);
        string notes = SessionState.GetString(NotesKey, string.Empty);
        StringBuilder report = new StringBuilder();
        report.AppendLine(success
            ? "PERSISTENT MUSIC VALIDATION PASS"
            : "PERSISTENT MUSIC VALIDATION FAIL");
        report.AppendLine(notes);
        File.WriteAllText(
            Path.Combine(Directory.GetCurrentDirectory(), ReportPath),
            report.ToString());

        SessionState.SetBool(RunningKey, false);
        SessionState.SetBool(FailedKey, false);
        SessionState.SetInt(StageKey, 0);
        SessionState.SetString(NotesKey, string.Empty);

        try
        {
            EditorSceneManager.OpenScene(
                PersistentMusicSetupTool.MainMenuScenePath,
                OpenSceneMode.Single);
        }
        catch
        {
            // Preserve the validation result even if restoring the scene fails.
        }

        if (success)
        {
            Debug.Log(report.ToString());
        }
        else
        {
            Debug.LogError(report.ToString());
        }

        if (Application.isBatchMode)
        {
            EditorApplication.Exit(success ? 0 : 1);
        }
    }

    private static string HashFile(string assetPath)
    {
        string fullPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            assetPath.Replace('/', Path.DirectorySeparatorChar));
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(fullPath))
        {
            return BitConverter.ToString(sha.ComputeHash(stream));
        }
    }
}
