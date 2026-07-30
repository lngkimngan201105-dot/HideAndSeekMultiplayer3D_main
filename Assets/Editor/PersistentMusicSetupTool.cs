using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PersistentMusicSetupTool
{
    public const string ClipPath =
        "Assets/Asset FTTGR/Audio/Music/Performance.wav";
    public const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
    public const string MapScenePath = "Assets/Scenes/Map_v2.unity";
    public const string SetupLogPath = "PersistentMusicSetup.log";

    [MenuItem("Tools/Prop Hunt/Setup Persistent Music")]
    public static void SetupPersistentMusic()
    {
        try
        {
            AudioClip clip = LoadAndConfigureClip();
            StringBuilder report = new StringBuilder();
            report.AppendLine("PERSISTENT MUSIC SETUP PASS");
            report.AppendLine($"Clip: {ClipPath}");
            report.AppendLine(
                $"Metadata: length={clip.length:F3}s, frequency={clip.frequency}Hz, " +
                $"channels={clip.channels}, samples={clip.samples}");

            ConfigureMainMenu(clip, report);
            ConfigureMap(clip, report);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            File.WriteAllText(
                Path.Combine(Directory.GetCurrentDirectory(), SetupLogPath),
                report.ToString());
            Debug.Log(report.ToString());
        }
        catch (Exception exception)
        {
            string failure = "PERSISTENT MUSIC SETUP FAIL\n" + exception;
            File.WriteAllText(
                Path.Combine(Directory.GetCurrentDirectory(), SetupLogPath),
                failure);
            Debug.LogError(failure);
            throw;
        }
    }

    private static AudioClip LoadAndConfigureClip()
    {
        if (!File.Exists(Path.Combine(
                Directory.GetCurrentDirectory(),
                ClipPath.Replace('/', Path.DirectorySeparatorChar))))
        {
            throw new FileNotFoundException(
                "Required music file does not exist.", ClipPath);
        }

        AssetDatabase.ImportAsset(
            ClipPath, ImportAssetOptions.ForceSynchronousImport);
        AudioImporter importer = AssetImporter.GetAtPath(ClipPath) as AudioImporter;
        if (importer == null)
        {
            throw new InvalidOperationException(
                $"AudioImporter was not created for '{ClipPath}'.");
        }

        bool importerChanged = false;
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        if (settings.loadType != AudioClipLoadType.Streaming)
        {
            settings.loadType = AudioClipLoadType.Streaming;
            importerChanged = true;
        }
        if (settings.compressionFormat != AudioCompressionFormat.Vorbis)
        {
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            importerChanged = true;
        }
        if (!Mathf.Approximately(settings.quality, 0.7f))
        {
            settings.quality = 0.7f;
            importerChanged = true;
        }
        if (settings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate)
        {
            settings.sampleRateSetting =
                AudioSampleRateSetting.PreserveSampleRate;
            importerChanged = true;
        }
        if (settings.preloadAudioData)
        {
            settings.preloadAudioData = false;
            importerChanged = true;
        }
        if (importer.defaultSampleSettings.loadType != settings.loadType ||
            importer.defaultSampleSettings.compressionFormat !=
            settings.compressionFormat ||
            !Mathf.Approximately(
                importer.defaultSampleSettings.quality, settings.quality) ||
            importer.defaultSampleSettings.sampleRateSetting !=
            settings.sampleRateSetting ||
            importer.defaultSampleSettings.preloadAudioData !=
            settings.preloadAudioData)
        {
            importer.defaultSampleSettings = settings;
        }
        if (!importer.loadInBackground)
        {
            importer.loadInBackground = true;
            importerChanged = true;
        }
        if (importerChanged)
        {
            importer.SaveAndReimport();
        }

        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
        if (clip == null)
        {
            throw new InvalidOperationException(
                $"AssetDatabase could not load AudioClip '{ClipPath}'.");
        }

        return clip;
    }

    private static void ConfigureMainMenu(
        AudioClip clip, StringBuilder report)
    {
        Scene scene = EditorSceneManager.OpenScene(
            MainMenuScenePath, OpenSceneMode.Single);

        PersistentMusicManager[] managers =
            UnityEngine.Object.FindObjectsOfType<PersistentMusicManager>(true)
                .Where(manager => manager.gameObject.scene == scene)
                .ToArray();

        PersistentMusicManager manager = managers.FirstOrDefault();
        if (manager == null)
        {
            GameObject managerObject = new GameObject("PersistentMusicManager");
            SceneManager.MoveGameObjectToScene(managerObject, scene);
            manager = managerObject.AddComponent<PersistentMusicManager>();
        }

        manager.gameObject.name = "PersistentMusicManager";
        manager.transform.SetParent(null);

        foreach (PersistentMusicManager duplicate in managers.Skip(1))
        {
            if (duplicate != null)
            {
                UnityEngine.Object.DestroyImmediate(duplicate.gameObject);
            }
        }

        AudioSource[] managerSources =
            manager.GetComponents<AudioSource>();
        AudioSource source = managerSources.FirstOrDefault();
        if (source == null)
        {
            source = manager.gameObject.AddComponent<AudioSource>();
        }
        foreach (AudioSource duplicateSource in
                 manager.GetComponents<AudioSource>().Skip(1).ToArray())
        {
            UnityEngine.Object.DestroyImmediate(duplicateSource);
        }

        manager.Configure(clip, source);
        RemoveForbiddenManagerComponents(manager.gameObject);
        DisableDuplicatePerformanceSources(scene, manager, clip);

        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(source);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, MainMenuScenePath);

        report.AppendLine(
            "MainMenu: one root PersistentMusicManager, one configured AudioSource.");
    }

    private static void ConfigureMap(AudioClip clip, StringBuilder report)
    {
        Scene scene = EditorSceneManager.OpenScene(
            MapScenePath, OpenSceneMode.Single);

        foreach (PersistentMusicManager sceneManager in
                 UnityEngine.Object.FindObjectsOfType<PersistentMusicManager>(true)
                     .Where(manager => manager.gameObject.scene == scene)
                     .ToArray())
        {
            UnityEngine.Object.DestroyImmediate(sceneManager.gameObject);
        }

        PersistentMusicBootstrap[] bootstraps =
            UnityEngine.Object.FindObjectsOfType<PersistentMusicBootstrap>(true)
                .Where(bootstrap => bootstrap.gameObject.scene == scene)
                .ToArray();
        PersistentMusicBootstrap bootstrap = bootstraps.FirstOrDefault();
        if (bootstrap == null)
        {
            GameObject bootstrapObject =
                new GameObject("PersistentMusicBootstrap");
            SceneManager.MoveGameObjectToScene(bootstrapObject, scene);
            bootstrap = bootstrapObject.AddComponent<PersistentMusicBootstrap>();
        }

        bootstrap.gameObject.name = "PersistentMusicBootstrap";
        bootstrap.transform.SetParent(null);
        bootstrap.Configure(clip);

        foreach (PersistentMusicBootstrap duplicate in bootstraps.Skip(1))
        {
            if (duplicate != null)
            {
                UnityEngine.Object.DestroyImmediate(duplicate.gameObject);
            }
        }

        DisableDuplicatePerformanceSources(scene, null, clip);
        EditorUtility.SetDirty(bootstrap);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, MapScenePath);

        report.AppendLine(
            "Map_v2: one clip-only bootstrap; no serialized music AudioSource.");
    }

    private static void DisableDuplicatePerformanceSources(
        Scene scene,
        PersistentMusicManager allowedManager,
        AudioClip clip)
    {
        foreach (AudioSource source in
                 UnityEngine.Object.FindObjectsOfType<AudioSource>(true)
                     .Where(source => source.gameObject.scene == scene &&
                                      source.clip == clip)
                     .ToArray())
        {
            if (allowedManager != null &&
                source.gameObject == allowedManager.gameObject)
            {
                continue;
            }

            source.Stop();
            source.playOnAwake = false;
            source.loop = false;
            source.clip = null;
            EditorUtility.SetDirty(source);
        }
    }

    private static void RemoveForbiddenManagerComponents(GameObject managerObject)
    {
        foreach (AudioListener listener in
                 managerObject.GetComponents<AudioListener>())
        {
            UnityEngine.Object.DestroyImmediate(listener);
        }
        foreach (Rigidbody rigidbody in managerObject.GetComponents<Rigidbody>())
        {
            UnityEngine.Object.DestroyImmediate(rigidbody);
        }
    }
}
