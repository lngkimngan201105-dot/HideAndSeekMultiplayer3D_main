using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class FinalUIVisualProofTool
{
    private const string RunningKey = "PropHunt.FinalUIVisualProof.Running";
    private const string OutputFolder = "FinalUIProof";
    private static int phase;
    private static double phaseStartedAt;

    static FinalUIVisualProofTool()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        if (SessionState.GetBool(RunningKey, false) && EditorApplication.isPlaying)
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
    }

    public static void Run()
    {
        Directory.CreateDirectory(OutputFolder);
        EditorSceneManager.OpenScene(
            GameCompletionSetupTool.MainMenuScenePath,
            OpenSceneMode.Single);
        SessionState.SetBool(RunningKey, true);
        phase = 0;
        EditorApplication.EnterPlaymode();
    }

    private static void HandlePlayModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            phaseStartedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(RunningKey, false);
            EditorApplication.update -= Tick;
            Debug.Log(
                "[FinalUIVisualProof] Captured MainMenu.png, HiderWin.png and HiderLose.png.");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }

    private static void Tick()
    {
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - phaseStartedAt;
            if (phase == 0 && elapsed >= 1.5d)
            {
                Screen.SetResolution(1920, 1080, false);
                CaptureResolutionSet("MainMenu");
                phase = 1;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }
            else if (phase == 1 && elapsed >= 1.0d)
            {
                SceneManager.LoadScene("Map_v2");
                phase = 2;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }
            else if (phase == 2 && elapsed >= 2.0d)
            {
                RoundResultController result =
                    Object.FindObjectOfType<RoundResultController>(true);
                if (result == null)
                    throw new InvalidOperationException(
                        "RoundResultController was not found for visual proof.");
                result.ShowHiderWinPreview();
                CaptureResolutionSet("HiderWin");
                phase = 3;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }
            else if (phase == 3 && elapsed >= 1.0d)
            {
                SceneManager.LoadScene("Map_v2");
                phase = 4;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }
            else if (phase == 4 && elapsed >= 2.0d)
            {
                RoundResultController result =
                    Object.FindObjectOfType<RoundResultController>(true);
                result.ShowHiderLosePreview();
                phase = 5;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }
            else if (phase == 5 && elapsed >= 0.25d)
            {
                CaptureResolutionSet("HiderLose");
                phase = 6;
                phaseStartedAt = EditorApplication.timeSinceStartup;
            }
            else if (phase == 6 && elapsed >= 1.0d)
            {
                EditorApplication.ExitPlaymode();
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SessionState.SetBool(RunningKey, false);
            EditorApplication.update -= Tick;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            else if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    private static void CaptureResolutionSet(string baseName)
    {
        CaptureCurrentView(baseName + ".png", 1920, 1080);
        CaptureCurrentView(baseName + "_1600x900.png", 1600, 900);
        CaptureCurrentView(baseName + "_1366x768.png", 1366, 768);
    }

    private static void CaptureCurrentView(
        string fileName,
        int width,
        int height)
    {
        Camera camera = Object.FindObjectsOfType<Camera>(true)
            .FirstOrDefault(item =>
                item.enabled && item.gameObject.activeInHierarchy);
        if (camera == null)
            throw new InvalidOperationException("No active camera is available for UI proof.");

        Canvas[] canvases = Object.FindObjectsOfType<Canvas>(true)
            .Where(item => item.gameObject.activeInHierarchy)
            .ToArray();
        RenderMode[] modes = canvases.Select(item => item.renderMode).ToArray();
        Camera[] worldCameras = canvases.Select(item => item.worldCamera).ToArray();
        float[] distances = canvases.Select(item => item.planeDistance).ToArray();
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture target = new RenderTexture(width, height, 24);
        Texture2D image = new Texture2D(
            width,
            height,
            TextureFormat.RGB24,
            false);

        try
        {
            camera.targetTexture = target;
            foreach (Canvas canvas in canvases)
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
            }
            Canvas.ForceUpdateCanvases();
            RenderTexture.active = target;
            camera.Render();
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply();
            string path = Path.GetFullPath(Path.Combine(OutputFolder, fileName));
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            for (int i = 0; i < canvases.Length; i++)
            {
                canvases[i].renderMode = modes[i];
                canvases[i].worldCamera = worldCameras[i];
                canvases[i].planeDistance = distances[i];
            }
            Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }
}
