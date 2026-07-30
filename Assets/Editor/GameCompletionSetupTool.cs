using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif
using Object = UnityEngine.Object;

public static class GameCompletionSetupTool
{
    public const string GameplayScenePath = "Assets/Scenes/Map_v2.unity";
    public const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
    public const string MainMenuArtPath =
        "Assets/UI/FinalArt/MainMenu_Final.png";
    public const string WinArtPath =
        "Assets/UI/FinalArt/HiderWin_Final.png";
    public const string LoseArtPath =
        "Assets/UI/FinalArt/HiderLose_Final.png";
    public const string WinCleanArtPath =
        "Assets/UI/FinalArt/HiderWin_Clean.png";
    public const string LoseCleanArtPath =
        "Assets/UI/FinalArt/HiderLose_Clean.png";
    private static readonly Color Cyan = new Color32(29, 224, 226, 255);
    private static readonly Color Gold = new Color32(247, 187, 55, 255);
    private static readonly Color Black = new Color32(4, 10, 14, 255);
    private static readonly Color Panel = new Color32(10, 25, 31, 242);

    [MenuItem("Tools/Prop Hunt/Complete Game (Two Seekers + Results + Main Menu)")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before completing the game.");

        PrepareFinalArtAssets();
        SetupGameplayScene();
        SetupMainMenuScene();
        ConfigureBuildSettings();
        AssetDatabase.SaveAssets();
        Debug.Log(
            "[GameCompletionSetup] PASS — two independent AI Seekers, fair team " +
            "coordination, authoritative result UI, MainMenu, and build order configured.");
    }

    [MenuItem("Tools/Prop Hunt/Setup Final UI")]
    public static void SetupFinalUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before setting up final UI.");

        PrepareFinalArtAssets();
        Scene gameplay = EditorSceneManager.OpenScene(
            GameplayScenePath, OpenSceneMode.Single);
        PropHuntRoundManager round =
            Object.FindObjectOfType<PropHuntRoundManager>(true);
        SeekerTeamCoordinator team =
            Object.FindObjectOfType<SeekerTeamCoordinator>(true);
        PropTransformSystem hider =
            Object.FindObjectsOfType<PropTransformSystem>(true)
                .FirstOrDefault(item => item.playerRole == PlayerRole.Hider);
        if (round == null || team == null || hider == null)
            throw new InvalidOperationException(
                "Map_v2 is missing RoundManager, SeekerTeamCoordinator or Hider.");
        BuildRoundResultUI(gameplay, round, team, hider);
        EditorSceneManager.MarkSceneDirty(gameplay);
        EditorSceneManager.SaveScene(gameplay, GameplayScenePath);

        SetupMainMenuScene();
        ConfigureBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            "[Setup Final UI] Saved Assets/Scenes/MainMenu.unity and " +
            "Assets/Scenes/Map_v2.unity with persistent callbacks.");
    }

    [MenuItem("Tools/Prop Hunt/Open Main Menu")]
    public static void OpenMainMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Open Main Menu is disabled while Play Mode is active.");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
        Selection.activeGameObject = FindNamed(
            SceneManager.GetActiveScene(), "MainMenuCanvas");
    }

    [MenuItem("Tools/Prop Hunt/Open Final Main Menu")]
    public static void OpenFinalMainMenu()
    {
        OpenMainMenu();
    }

    [MenuItem("Tools/Prop Hunt/Result UI/Preview Hider Win Final")]
    public static void PreviewHiderWin()
    {
        RoundResultController controller = RequirePlayModeResultController();
        controller?.ShowHiderWinPreview();
    }

    [MenuItem("Tools/Prop Hunt/Result UI/Preview Hider Lose Final")]
    public static void PreviewHiderLose()
    {
        RoundResultController controller = RequirePlayModeResultController();
        controller?.ShowHiderLosePreview();
    }

    [MenuItem("Tools/Prop Hunt/Result UI/Hide Result Preview")]
    public static void HideResultPreview()
    {
        RoundResultController controller = RequirePlayModeResultController();
        controller?.HideResultPreview();
    }

    private static void PrepareFinalArtAssets()
    {
        string[] required = { MainMenuArtPath, WinArtPath, LoseArtPath };
        string[] missing = required.Where(path => !File.Exists(path)).ToArray();
        if (missing.Length > 0)
        {
            throw new FileNotFoundException(
                "Setup Final UI stopped. Missing required final art:\n" +
                string.Join("\n", missing));
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        string[] imported =
        {
            MainMenuArtPath,
            WinArtPath,
            LoseArtPath,
            WinCleanArtPath,
            LoseCleanArtPath
        };
        foreach (string path in imported)
            ConfigureFinalArtImporter(path);
    }

    private static void ConfigureFinalArtImporter(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException(
                $"Final art importer is unavailable: {path}");

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        bool changed =
            importer.textureType != TextureImporterType.Sprite ||
            importer.spriteImportMode != SpriteImportMode.Single ||
            settings.spriteMeshType != SpriteMeshType.FullRect ||
            !Mathf.Approximately(importer.spritePixelsPerUnit, 100f) ||
            importer.filterMode != FilterMode.Bilinear ||
            importer.wrapMode != TextureWrapMode.Clamp ||
            importer.textureCompression != TextureImporterCompression.Uncompressed ||
            importer.maxTextureSize != 2048 ||
            importer.mipmapEnabled ||
            !importer.alphaIsTransparency ||
            importer.npotScale != TextureImporterNPOTScale.None;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.npotScale = TextureImporterNPOTScale.None;
        if (changed) importer.SaveAndReimport();
    }

    private static void SetupGameplayScene()
    {
        Scene scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
        SeekerAIController primary = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<SeekerAIController>(true))
            .FirstOrDefault(item => item.gameObject.name == "SeekerPlayer");
        if (primary == null)
            throw new InvalidOperationException(
                "Run 'Setup Single Player AI Seeker' once before Game Completion setup.");

        PropHuntRoundManager round = Object.FindObjectOfType<PropHuntRoundManager>(true);
        PropTransformSystem hider = Object.FindObjectsOfType<PropTransformSystem>(true)
            .FirstOrDefault(item => item.playerRole == PlayerRole.Hider);
        if (round == null || hider == null)
            throw new InvalidOperationException("Round manager or Hider is missing.");

        HiderHealth hiderHealth = hider.GetComponent<HiderHealth>();
        HiderAntiCampSystem antiCamp = hider.GetComponent<HiderAntiCampSystem>();
        HiderPerceptionSignature signature =
            hider.GetComponent<HiderPerceptionSignature>();
        if (hiderHealth == null || antiCamp == null || signature == null)
            throw new InvalidOperationException("Hider AI-facing components are incomplete.");

        GameObject secondaryObject = FindNamed(scene, "SeekerPlayer_02");
        bool createdSecondary = secondaryObject == null;
        if (createdSecondary)
        {
            secondaryObject = Object.Instantiate(primary.gameObject);
            secondaryObject.name = "SeekerPlayer_02";
            SceneManager.MoveGameObjectToScene(secondaryObject, scene);
            StripHumanOnlyComponents(secondaryObject);
        }

        SeekerAIController secondary =
            RequireComponent<SeekerAIController>(secondaryObject);
        Transform spawn = EnsureRoot(scene, "SeekerSpawnPoint_02").transform;
        if (createdSecondary)
        {
            spawn.SetPositionAndRotation(
                primary.transform.position + primary.transform.right * 8f,
                primary.transform.rotation);
            secondary.transform.SetPositionAndRotation(
                spawn.position,
                spawn.rotation);
        }

        GameObject systems = EnsureRoot(scene, "SeekerTeamSystems");
        SeekerTeamCoordinator team =
            GetOrAddUnique<SeekerTeamCoordinator>(systems);
        ConfigureAI(primary, false, round, hiderHealth, antiCamp, signature);
        ConfigureAI(secondary, true, round, hiderHealth, antiCamp, signature);
        team.Configure(round, antiCamp, spawn, primary, secondary);
        round.ConfigureSeekerTeam(team);

        BuildRoundResultUI(scene, round, team, hider);
        EditorUtility.SetDirty(round);
        EditorUtility.SetDirty(team);
        EditorUtility.SetDirty(primary);
        EditorUtility.SetDirty(secondary);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, GameplayScenePath);
    }

    private static void ConfigureAI(
        SeekerAIController controller,
        bool support,
        PropHuntRoundManager round,
        HiderHealth hiderHealth,
        HiderAntiCampSystem antiCamp,
        HiderPerceptionSignature signature)
    {
        GameObject owner = controller.gameObject;
        SeekerHealth health = RequireComponent<SeekerHealth>(owner);
        SeekerWeaponEnergy energy = RequireComponent<SeekerWeaponEnergy>(owner);
        SeekerAINavigation navigation = RequireComponent<SeekerAINavigation>(owner);
        SeekerAIPerception perception = RequireComponent<SeekerAIPerception>(owner);
        SeekerAICombat combat = RequireComponent<SeekerAICombat>(owner);
        SeekerAISuspicionSystem suspicion = RequireComponent<SeekerAISuspicionSystem>(owner);
        SeekerRaycastWeapon weapon =
            owner.GetComponentInChildren<SeekerRaycastWeapon>(true);
        Transform eye = FindDescendant(owner.transform, "SeekerAIEye");
        Transform muzzle = FindDescendant(owner.transform, "MuzzlePoint_World");
        if (weapon == null || eye == null || muzzle == null)
            throw new InvalidOperationException($"{owner.name} weapon/eye/muzzle is missing.");

        controller.Configure(
            round, hiderHealth, antiCamp, health, weapon, energy,
            navigation, perception, combat, suspicion);
        perception.Configure(eye, signature);
        perception.ConfigureTuning(support ? 19f : 22f, support ? 70f : 75f);
        navigation.ConfigureTuning(2.3f, support ? 3.8f : 4.2f);
        combat.Configure(weapon, energy, muzzle, perception);
        combat.ConfigureAimError(support ? 3f : 2f, support ? 4.5f : 4f);
        controller.ConfigureTuning(support ? 0.8f : 0.6f, support ? 7f : 8f);
        weapon.SetPlayerInputEnabled(false);
        energy.SetPlayerReloadInputEnabled(false);
        MarkHierarchyDirty(owner);
    }

    private static void StripHumanOnlyComponents(GameObject secondary)
    {
        DestroyComponents<PropHuntSinglePlayerBootstrap>(secondary);
        DestroyComponents<SeekerFirstPersonController>(secondary);
        DestroyComponents<StarterAssets.FirstPersonController>(secondary);
        DestroyComponents<CharacterController>(secondary);
#if ENABLE_INPUT_SYSTEM
        DestroyComponents<PlayerInput>(secondary);
#endif
        DestroyComponents<Camera>(secondary);
        DestroyComponents<AudioListener>(secondary);

        foreach (Canvas canvas in secondary.GetComponentsInChildren<Canvas>(true))
            canvas.gameObject.SetActive(false);
        foreach (Transform item in secondary.GetComponentsInChildren<Transform>(true))
        {
            if (item.CompareTag("MainCamera")) item.tag = "Untagged";
            if (item.name.IndexOf("FPS", StringComparison.OrdinalIgnoreCase) >= 0 &&
                item.name.IndexOf("World", StringComparison.OrdinalIgnoreCase) < 0)
                item.gameObject.SetActive(false);
        }
    }

    private static void BuildRoundResultUI(
        Scene scene,
        PropHuntRoundManager round,
        SeekerTeamCoordinator team,
        PropTransformSystem hider)
    {
        GameObject systems = EnsureRoot(scene, "GameCompletionSystems");
        RoundResultController controller =
            GetOrAddUnique<RoundResultController>(systems);
        GameObject canvasObject = EnsureRoot(scene, "RoundResultCanvas");
        Canvas canvas = GetOrAddUnique<Canvas>(canvasObject);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = GetOrAddUnique<CanvasScaler>(canvasObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        GetOrAddUnique<GraphicRaycaster>(canvasObject);

        DestroyDirectChild(canvasObject.transform, "WinPanel");
        DestroyDirectChild(canvasObject.transform, "LosePanel");

        Image dim = EnsureImage(canvasObject.transform, "BackgroundDim");
        Stretch(dim.rectTransform);
        dim.color = Color.black;
        dim.raycastTarget = false;

        GameObject win = EnsureChild(canvasObject.transform, "WinFinalRoot");
        Stretch(RequireRect(win));
        GameObject lose = EnsureChild(canvasObject.transform, "LoseFinalRoot");
        Stretch(RequireRect(lose));

        Sprite winSprite = LoadFinalSprite(WinCleanArtPath);
        Sprite loseSprite = LoadFinalSprite(LoseCleanArtPath);
        EnsureFullScreenArt(win.transform, "WinFinalArt", winSprite);
        DisableCleanupOverlay(
            win.transform,
            "WinTopCleanup",
            new Vector2(0.296f, 0.905f),
            new Vector2(0.704f, 0.988f));
        Button replay = EnsureTransparentButton(
            win.transform,
            "WinReplayButton",
            new Vector2(0.280f, 0.145f),
            new Vector2(0.495f, 0.255f));
        Button menu = EnsureTransparentButton(
            win.transform,
            "WinMainMenuButton",
            new Vector2(0.525f, 0.145f),
            new Vector2(0.745f, 0.255f));
        ConfigureEmbeddedHover(replay, Cyan);
        ConfigureEmbeddedHover(menu, Gold);

        EnsureFullScreenArt(lose.transform, "LoseFinalArt", loseSprite);
        DisableCleanupOverlay(
            lose.transform,
            "LoseTopCleanup",
            new Vector2(0.295f, 0.820f),
            new Vector2(0.705f, 0.930f));
        Button loseReplay = EnsureTransparentButton(
            lose.transform,
            "LoseReplayButton",
            new Vector2(0.308f, 0.177f),
            new Vector2(0.498f, 0.274f));
        Button loseMenu = EnsureTransparentButton(
            lose.transform,
            "LoseMainMenuButton",
            new Vector2(0.522f, 0.177f),
            new Vector2(0.711f, 0.274f));
        ConfigureEmbeddedHover(loseReplay, Gold);
        ConfigureEmbeddedHover(loseMenu, Cyan);

        PropHuntHUDController hud =
            Object.FindObjectOfType<PropHuntHUDController>(true);
        GameObject hudRoot = hud != null ? hud.gameObject : null;
        controller.Configure(
            round,
            team,
            hider,
            hider.GetComponent<HiderAbilityController>(),
            hudRoot,
            canvasObject,
            win,
            lose,
            null,
            null,
            replay,
            menu,
            loseReplay,
            loseMenu);
        BindPersistent(replay, controller.Replay);
        BindPersistent(menu, controller.ReturnToMainMenu);
        BindPersistent(loseReplay, controller.Replay);
        BindPersistent(loseMenu, controller.ReturnToMainMenu);

        dim.transform.SetAsFirstSibling();
        win.transform.SetSiblingIndex(1);
        lose.transform.SetSiblingIndex(2);
        win.SetActive(false);
        lose.SetActive(false);
        canvasObject.SetActive(false);
        EditorUtility.SetDirty(controller);
    }

    private static void BuildPrototypeRoundResultUI(
        Scene scene,
        PropHuntRoundManager round,
        SeekerTeamCoordinator team,
        PropTransformSystem hider)
    {
        GameObject systems = EnsureRoot(scene, "GameCompletionSystems");
        RoundResultController controller =
            GetOrAddUnique<RoundResultController>(systems);
        GameObject canvasObject = EnsureRoot(scene, "RoundResultCanvas");
        Canvas canvas = GetOrAddUnique<Canvas>(canvasObject);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = GetOrAddUnique<CanvasScaler>(canvasObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        GetOrAddUnique<GraphicRaycaster>(canvasObject);

        Image dim = EnsureImage(canvasObject.transform, "BackgroundDim");
        Stretch(dim.rectTransform);
        dim.color = new Color(0f, 0f, 0f, 0.72f);

        GameObject win = EnsurePanel(canvasObject.transform, "WinPanel",
            new Vector2(840f, 490f));
        GameObject lose = EnsurePanel(canvasObject.transform, "LosePanel",
            new Vector2(840f, 490f));
        if (win.transform.Find("WinFrame") == null)
            ClearChildren(win.transform);
        if (lose.transform.Find("LoseFrame") == null)
            ClearChildren(lose.transform);
        TMP_FontAsset font = ResolveFont();

        Image winFrame = EnsureImage(win.transform, "WinFrame");
        SetRect(winFrame.rectTransform, Vector2.zero, new Vector2(790f, 440f));
        winFrame.color = new Color(0f, 0f, 0f, 0.02f);
        winFrame.raycastTarget = false;
        Outline winOutline = GetOrAddUnique<Outline>(winFrame.gameObject);
        winOutline.effectColor = Cyan;
        winOutline.effectDistance = new Vector2(3f, -3f);
        TextMeshProUGUI winIcon = EnsureText(
            win.transform, "WinIcon", "V", 52, Gold,
            new Vector2(0f, 170f), new Vector2(100f, 70f), font);
        winIcon.enableAutoSizing = false;
        winIcon.fontStyle = FontStyles.Bold;
        EnsureText(win.transform, "WinTitle", "ĐỒ VẬT CHIẾN THẮNG", 50, Cyan,
            new Vector2(0f, 90f), new Vector2(760f, 80f), font)
            .enableWordWrapping = false;
        TextMeshProUGUI winSubtitle = EnsureText(
            win.transform, "WinDescription", "Bạn đã sống sót đến hết thời gian",
            27, Color.white, new Vector2(0f, 20f), new Vector2(720f, 58f), font);
        Button replay = EnsureDarkButton(
            win.transform, "WinReplayButton", "CHƠI LẠI",
            new Vector2(-175f, -120f), Cyan, font);
        Button menu = EnsureDarkButton(
            win.transform, "WinMainMenuButton", "VỀ MENU",
            new Vector2(175f, -120f), Gold, font);

        Image loseFrame = EnsureImage(lose.transform, "LoseFrame");
        SetRect(loseFrame.rectTransform, Vector2.zero, new Vector2(790f, 440f));
        loseFrame.color = new Color(0f, 0f, 0f, 0.02f);
        loseFrame.raycastTarget = false;
        Outline loseOutline = GetOrAddUnique<Outline>(loseFrame.gameObject);
        loseOutline.effectColor = new Color32(255, 92, 59, 255);
        loseOutline.effectDistance = new Vector2(3f, -3f);
        EnsureText(lose.transform, "WarningIcon", "!", 56,
            new Color32(255, 92, 59, 255), new Vector2(0f, 170f),
            new Vector2(100f, 70f), font);
        EnsureText(lose.transform, "LoseTitle", "ĐỒ VẬT THẤT BẠI", 50,
            new Color32(255, 92, 59, 255), new Vector2(0f, 90f),
            new Vector2(760f, 80f), font).enableWordWrapping = false;
        TextMeshProUGUI loseSubtitle = EnsureText(
            lose.transform, "LoseDescription", "Bạn đã bị thợ săn hạ gục", 27,
            Color.white, new Vector2(0f, 20f), new Vector2(720f, 58f), font);
        Button loseReplay = EnsureDarkButton(
            lose.transform, "LoseReplayButton", "CHƠI LẠI",
            new Vector2(-175f, -120f), Cyan, font);
        Button loseMenu = EnsureDarkButton(
            lose.transform, "LoseMainMenuButton", "VỀ MENU",
            new Vector2(175f, -120f), Gold, font);
        // Keep lose labels at panel depth so they render reliably on the first
        // result frame in both overlay and camera-backed canvases.
        DestroyDirectChild(loseReplay.transform, "Label");
        DestroyDirectChild(loseMenu.transform, "Label");
        Text loseReplayLabel = EnsureLegacyText(
            lose.transform, "LoseReplayLabel", "CHƠI LẠI", 25, Color.white,
            new Vector2(-175f, -120f), new Vector2(280f, 54f));
        loseReplayLabel.fontStyle = FontStyle.Bold;
        loseReplayLabel.raycastTarget = false;
        Text loseMenuLabel = EnsureLegacyText(
            lose.transform, "LoseMainMenuLabel", "VỀ MENU", 25, Color.white,
            new Vector2(175f, -120f), new Vector2(280f, 54f));
        loseMenuLabel.fontStyle = FontStyle.Bold;
        loseMenuLabel.raycastTarget = false;

        PropHuntHUDController hud = Object.FindObjectOfType<PropHuntHUDController>(true);
        GameObject hudRoot = hud != null ? hud.gameObject : null;
        controller.Configure(
            round, team, hider, hider.GetComponent<HiderAbilityController>(),
            hudRoot, canvasObject, win, lose, winSubtitle, loseSubtitle,
            replay, menu, loseReplay, loseMenu);
        BindPersistent(replay, controller.Replay);
        BindPersistent(menu, controller.ReturnToMainMenu);
        BindPersistent(loseReplay, controller.Replay);
        BindPersistent(loseMenu, controller.ReturnToMainMenu);
        canvasObject.SetActive(false);
        EditorUtility.SetDirty(controller);
    }

    private static void SetupMainMenuScene()
    {
        SetupPrototypeMainMenuScene();
        Scene scene = SceneManager.GetActiveScene();
        GameObject canvasObject = FindNamed(scene, "MainMenuCanvas");
        if (canvasObject == null)
            throw new InvalidOperationException("MainMenuCanvas was not created.");

        DestroyDirectChild(canvasObject.transform, "Background");
        DestroyDirectChild(canvasObject.transform, "DarkOverlay");
        DestroyDirectChild(canvasObject.transform, "DecorativeFrame");
        DestroyDirectChild(canvasObject.transform, "TitleGroup");
        DestroyDirectChild(canvasObject.transform, "ButtonsRoot");

        Sprite menuSprite = LoadFinalSprite(MainMenuArtPath);
        Image finalArt = EnsureFullScreenArt(
            canvasObject.transform,
            "MainMenuFinalArt",
            menuSprite);
        GameObject interactionRoot = EnsureChild(
            canvasObject.transform,
            "MainMenuInteractionRoot");
        Stretch(RequireRect(interactionRoot));
        GameObject hoverRoot = EnsureChild(
            canvasObject.transform,
            "MainMenuHoverEffects");
        Stretch(RequireRect(hoverRoot));

        Button start = EnsureTransparentButton(
            interactionRoot.transform,
            "StartButton",
            new Vector2(0.353f, 0.426f),
            new Vector2(0.658f, 0.552f));
        Button tutorial = EnsureTransparentButton(
            interactionRoot.transform,
            "TutorialButton",
            new Vector2(0.353f, 0.308f),
            new Vector2(0.658f, 0.414f));
        Button settings = EnsureTransparentButton(
            interactionRoot.transform,
            "SettingsButton",
            new Vector2(0.353f, 0.190f),
            new Vector2(0.658f, 0.295f));
        Button quit = EnsureTransparentButton(
            interactionRoot.transform,
            "QuitButton",
            new Vector2(0.353f, 0.069f),
            new Vector2(0.658f, 0.178f));

        Color goldHover = new Color(1f, 0.68f, 0.10f, 0.18f);
        Color cyanHover = new Color(0f, 0.90f, 1f, 0.16f);
        Image startHover = EnsureHoverOverlay(
            hoverRoot.transform,
            "StartHover",
            new Vector2(0.353f, 0.426f),
            new Vector2(0.658f, 0.552f),
            goldHover);
        Image tutorialHover = EnsureHoverOverlay(
            hoverRoot.transform,
            "TutorialHover",
            new Vector2(0.353f, 0.308f),
            new Vector2(0.658f, 0.414f),
            cyanHover);
        Image settingsHover = EnsureHoverOverlay(
            hoverRoot.transform,
            "SettingsHover",
            new Vector2(0.353f, 0.190f),
            new Vector2(0.658f, 0.295f),
            cyanHover);
        Image quitHover = EnsureHoverOverlay(
            hoverRoot.transform,
            "QuitHover",
            new Vector2(0.353f, 0.069f),
            new Vector2(0.658f, 0.178f),
            cyanHover);
        ConfigureFeedback(start, startHover, goldHover);
        ConfigureFeedback(tutorial, tutorialHover, cyanHover);
        ConfigureFeedback(settings, settingsHover, cyanHover);
        ConfigureFeedback(quit, quitHover, cyanHover);

        GameObject tutorialPanel = FindNamed(scene, "TutorialPanel");
        GameObject settingsPanel = FindNamed(scene, "SettingsPanel");
        GameObject fadeObject = FindNamed(scene, "FadeOverlay");
        Button tutorialClose = tutorialPanel != null
            ? tutorialPanel.transform.Find("CloseButton")?.GetComponent<Button>()
            : null;
        Button settingsClose = settingsPanel != null
            ? settingsPanel.transform.Find("CloseButton")?.GetComponent<Button>()
            : null;
        CanvasGroup fade = fadeObject != null
            ? fadeObject.GetComponent<CanvasGroup>()
            : null;
        MainMenuController menu =
            GetOrAddUnique<MainMenuController>(canvasObject);
        menu.Configure(
            start,
            tutorial,
            settings,
            quit,
            tutorialPanel,
            settingsPanel,
            fade,
            tutorialClose,
            settingsClose);
        BindPersistent(start, menu.StartGame);
        BindPersistent(tutorial, menu.ToggleTutorial);
        BindPersistent(settings, menu.ToggleSettings);
        BindPersistent(quit, menu.QuitGame);

        finalArt.transform.SetAsFirstSibling();
        interactionRoot.transform.SetSiblingIndex(1);
        hoverRoot.transform.SetSiblingIndex(2);
        if (tutorialPanel != null) tutorialPanel.transform.SetSiblingIndex(3);
        if (settingsPanel != null) settingsPanel.transform.SetSiblingIndex(4);
        if (fadeObject != null) fadeObject.transform.SetAsLastSibling();
        canvasObject.SetActive(true);
        EditorUtility.SetDirty(menu);
        EditorUtility.SetDirty(canvasObject);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, MainMenuScenePath);
    }

    private static void SetupPrototypeMainMenuScene()
    {
        Scene scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) != null
            ? EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject mainRoot = EnsureRoot(scene, "MainMenu");
        GameObject cameraObject = FindNamed(scene, "MainMenuCamera") ??
                                  FindNamed(scene, "Main Camera") ??
                                  new GameObject("MainMenuCamera");
        cameraObject.name = "MainMenuCamera";
        if (cameraObject.scene != scene)
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
        cameraObject.transform.SetParent(mainRoot.transform, false);
        Camera camera = GetOrAddUnique<Camera>(cameraObject);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Black;
        camera.orthographic = true;
        cameraObject.tag = "MainCamera";
        GetOrAddUnique<AudioListener>(cameraObject);
        cameraObject.transform.SetPositionAndRotation(
            new Vector3(0f, 0f, -10f), Quaternion.identity);

        GameObject eventObject = FindNamed(scene, "EventSystem") ??
                                 new GameObject("EventSystem");
        if (eventObject.scene != scene)
            SceneManager.MoveGameObjectToScene(eventObject, scene);
        eventObject.transform.SetParent(mainRoot.transform, false);
        GetOrAddUnique<EventSystem>(eventObject);
#if ENABLE_INPUT_SYSTEM
        GetOrAddUnique<InputSystemUIInputModule>(eventObject);
#else
        GetOrAddUnique<StandaloneInputModule>(eventObject);
#endif

        GameObject canvasObject = FindNamed(scene, "MainMenuCanvas") ??
                                  new GameObject("MainMenuCanvas");
        if (canvasObject.scene != scene)
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
        canvasObject.transform.SetParent(mainRoot.transform, false);
        canvasObject.SetActive(true);
        Canvas canvas = GetOrAddUnique<Canvas>(canvasObject);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = GetOrAddUnique<CanvasScaler>(canvasObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        GetOrAddUnique<GraphicRaycaster>(canvasObject);
        TMP_FontAsset font = ResolveFont();

        DestroyDirectChild(canvasObject.transform, "IndustrialBackground");
        DestroyDirectChild(canvasObject.transform, "CyanAccentBand");
        DestroyDirectChild(canvasObject.transform, "GoldAccentBand");
        DestroyDirectChild(canvasObject.transform, "MainPanel");

        GameObject backgroundObject = EnsureChild(canvasObject.transform, "Background");
        RawImage background = GetOrAddUnique<RawImage>(backgroundObject);
        Stretch(background.rectTransform);
        background.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/Inguz Media Studio/Free 2D Impact FX/Demo Scene/Wallpaper.png");
        background.color = new Color(0.32f, 0.55f, 0.58f, 1f);
        background.raycastTarget = false;

        Image darkOverlay = EnsureImage(canvasObject.transform, "DarkOverlay");
        Stretch(darkOverlay.rectTransform);
        darkOverlay.color = new Color(0.005f, 0.035f, 0.055f, 0.73f);
        darkOverlay.raycastTarget = false;

        Image decorativeFrame = EnsureImage(
            canvasObject.transform, "DecorativeFrame");
        SetRect(decorativeFrame.rectTransform, Vector2.zero,
            new Vector2(1740f, 900f));
        decorativeFrame.color = new Color(0f, 0f, 0f, 0.01f);
        decorativeFrame.raycastTarget = false;
        Outline frameOutline = GetOrAddUnique<Outline>(decorativeFrame.gameObject);
        frameOutline.effectColor = Cyan;
        frameOutline.effectDistance = new Vector2(3f, -3f);

        GameObject titleGroup = EnsureChild(canvasObject.transform, "TitleGroup");
        SetRect(RequireRect(titleGroup), new Vector2(-430f, 145f),
            new Vector2(790f, 460f));
        if (titleGroup.transform.Find("MainTitle") == null)
            ClearChildren(titleGroup.transform);
        TextMeshProUGUI mainTitle = EnsureText(
            titleGroup.transform, "MainTitle",
            "HIDE AND SEEK\nMULTIPLAYER 3D", 70, Cyan,
            new Vector2(0f, 70f), new Vector2(760f, 230f), font);
        mainTitle.fontStyle = FontStyles.Bold;
        mainTitle.lineSpacing = -5f;
        mainTitle.enableWordWrapping = false;
        EnsureText(titleGroup.transform, "Subtitle",
            "Trốn thật khéo - Săn thật nhanh", 28, Gold,
            new Vector2(0f, -85f), new Vector2(700f, 60f), font);

        GameObject buttonsRoot = EnsureChild(canvasObject.transform, "ButtonsRoot");
        SetRect(RequireRect(buttonsRoot), new Vector2(450f, -20f),
            new Vector2(520f, 520f));
        if (buttonsRoot.transform.Find("StartButton") == null)
            ClearChildren(buttonsRoot.transform);
        Button start = EnsureDarkButton(buttonsRoot.transform, "StartButton",
            "BẮT ĐẦU", new Vector2(0f, 150f), Cyan, font);
        Button tutorial = EnsureDarkButton(buttonsRoot.transform, "TutorialButton",
            "HƯỚNG DẪN", new Vector2(0f, 50f), Gold, font);
        Button settings = EnsureDarkButton(buttonsRoot.transform, "SettingsButton",
            "CÀI ĐẶT", new Vector2(0f, -50f), Cyan, font);
        Button quit = EnsureDarkButton(buttonsRoot.transform, "QuitButton",
            "THOÁT", new Vector2(0f, -150f),
            new Color32(255, 92, 59, 255), font);

        GameObject tutorialPanel = EnsurePanel(canvasObject.transform,
            "TutorialPanel", new Vector2(980f, 720f));
        EnsureText(tutorialPanel.transform, "Title", "HƯỚNG DẪN", 46, Cyan,
            new Vector2(0f, 275f), new Vector2(860f, 70f), font);
        EnsureText(tutorialPanel.transform, "Bindings",
            "WASD  Di chuyển     CHUỘT  Quan sát\n" +
            "SPACE  Nhảy          E  Sao chép vật thể\n" +
            "O  Đổi vật thể ngẫu nhiên     X  Tạo Clone\n" +
            "TAB  Ghost Camera     E  Hủy ngụy trang",
            27, Color.white, Vector2.zero, new Vector2(820f, 360f), font);
        EnsureText(tutorialPanel.transform, "CloseHint",
            "WASD + chuột để sống sót và đánh lạc hướng AI", 20, Cyan,
            new Vector2(0f, -235f), new Vector2(760f, 38f), font);
        Button tutorialClose = EnsureButton(
            tutorialPanel.transform, "CloseButton", "ĐÓNG",
            new Vector2(0f, -300f), Gold, font);

        GameObject settingsPanel = EnsurePanel(canvasObject.transform,
            "SettingsPanel", new Vector2(980f, 720f));
        EnsureText(settingsPanel.transform, "Title", "CÀI ĐẶT", 46, Cyan,
            new Vector2(0f, 275f), new Vector2(860f, 70f), font);
        TextMeshProUGUI volumeValue;
        Slider volume = EnsureSlider(settingsPanel.transform, "MasterVolume",
            "ÂM LƯỢNG", new Vector2(0f, 125f), 0f, 1f, 1f, font,
            out volumeValue);
        Toggle fullscreen = EnsureToggle(settingsPanel.transform, "Fullscreen",
            "TOÀN MÀN HÌNH", new Vector2(0f, 10f), font);
        TextMeshProUGUI sensitivityValue;
        Slider sensitivity = EnsureSlider(settingsPanel.transform,
            "MouseSensitivity", "ĐỘ NHẠY CHUỘT", new Vector2(0f, -105f),
            0.2f, 3f, 1f, font, out sensitivityValue);
        EnsureText(settingsPanel.transform, "CloseHint",
            "Các thay đổi được lưu tự động", 20, Cyan,
            new Vector2(0f, -235f), new Vector2(760f, 38f), font);
        Button settingsClose = EnsureButton(
            settingsPanel.transform, "CloseButton", "ĐÓNG",
            new Vector2(0f, -300f), Gold, font);

        Image fadeImage = EnsureImage(canvasObject.transform, "FadeOverlay");
        Stretch(fadeImage.rectTransform);
        fadeImage.color = Color.black;
        CanvasGroup fade = GetOrAddUnique<CanvasGroup>(fadeImage.gameObject);
        fade.alpha = 0f;
        fade.blocksRaycasts = false;

        MainMenuController menu = GetOrAddUnique<MainMenuController>(canvasObject);
        menu.Configure(start, tutorial, settings, quit,
            tutorialPanel, settingsPanel, fade, tutorialClose, settingsClose);
        BindPersistent(start, menu.StartGame);
        BindPersistent(tutorial, menu.ToggleTutorial);
        BindPersistent(settings, menu.ToggleSettings);
        BindPersistent(quit, menu.QuitGame);
        BindPersistent(tutorialClose, menu.ToggleTutorial);
        BindPersistent(settingsClose, menu.ToggleSettings);
        MainMenuSettingsController menuSettings =
            GetOrAddUnique<MainMenuSettingsController>(settingsPanel);
        menuSettings.Configure(
            volume, fullscreen, sensitivity, volumeValue, sensitivityValue);
        tutorialPanel.SetActive(false);
        settingsPanel.SetActive(false);
        fadeImage.transform.SetAsLastSibling();
        EditorUtility.SetDirty(menu);
        EditorUtility.SetDirty(menuSettings);
        EditorUtility.SetDirty(canvasObject);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, MainMenuScenePath);
    }

    private static Sprite LoadFinalSprite(string path)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            throw new InvalidOperationException(
                $"Required final art is not imported as a Sprite: {path}");
        return sprite;
    }

    private static Image EnsureFullScreenArt(
        Transform parent,
        string name,
        Sprite sprite)
    {
        GameObject root = EnsureChild(parent, name);
        RectTransform rect = RequireRect(root);
        Stretch(rect);
        Image image = GetOrAddUnique<Image>(root);
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = Color.white;
        image.raycastTarget = false;
        AspectRatioFitter fitter = GetOrAddUnique<AspectRatioFitter>(root);
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
        EditorUtility.SetDirty(image);
        EditorUtility.SetDirty(fitter);
        return image;
    }

    private static Button EnsureTransparentButton(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        GameObject root = EnsureChild(parent, name);
        SetAnchorRect(RequireRect(root), anchorMin, anchorMax);
        DestroyDirectChild(root.transform, "Label");
        Image image = GetOrAddUnique<Image>(root);
        image.sprite = null;
        image.color = new Color(1f, 1f, 1f, 0f);
        image.raycastTarget = true;
        Button button = GetOrAddUnique<Button>(root);
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        EditorUtility.SetDirty(image);
        EditorUtility.SetDirty(button);
        return button;
    }

    private static Image EnsureHoverOverlay(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Color hoverColor)
    {
        GameObject root = EnsureChild(parent, name);
        SetAnchorRect(RequireRect(root), anchorMin, anchorMax);
        Image image = GetOrAddUnique<Image>(root);
        image.sprite = null;
        Color hidden = hoverColor;
        hidden.a = 0f;
        image.color = hidden;
        image.raycastTarget = false;
        Outline outline = GetOrAddUnique<Outline>(root);
        outline.effectColor = new Color(
            hoverColor.r,
            hoverColor.g,
            hoverColor.b,
            Mathf.Max(hoverColor.a, 0.14f));
        outline.effectDistance = new Vector2(3f, -3f);
        outline.useGraphicAlpha = true;
        EditorUtility.SetDirty(image);
        EditorUtility.SetDirty(outline);
        return image;
    }

    private static void ConfigureFeedback(
        Button button,
        Image hover,
        Color hoverColor)
    {
        FinalArtButtonFeedback feedback =
            GetOrAddUnique<FinalArtButtonFeedback>(button.gameObject);
        feedback.Configure(
            hover,
            hover.rectTransform,
            hoverColor,
            1.015f,
            0.985f);
        EditorUtility.SetDirty(feedback);
    }

    private static void ConfigureEmbeddedHover(Button button, Color color)
    {
        Image hover = EnsureHoverOverlay(
            button.transform,
            "Hover",
            Vector2.zero,
            Vector2.one,
            new Color(color.r, color.g, color.b, 0.16f));
        FinalArtButtonFeedback feedback =
            GetOrAddUnique<FinalArtButtonFeedback>(button.gameObject);
        feedback.Configure(
            hover,
            button.GetComponent<RectTransform>(),
            new Color(color.r, color.g, color.b, 0.16f),
            1.015f,
            0.985f);
        EditorUtility.SetDirty(feedback);
    }

    private static RawImage EnsureTopCleanup(
        Transform parent,
        string name,
        Texture texture,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Rect sourceUv,
        Color accent)
    {
        GameObject root = EnsureChild(parent, name);
        SetAnchorRect(RequireRect(root), anchorMin, anchorMax);
        Image incompatible = root.GetComponent<Image>();
        if (incompatible != null) Object.DestroyImmediate(incompatible);
        RawImage patch = GetOrAddUnique<RawImage>(root);
        patch.texture = texture;
        patch.uvRect = sourceUv;
        patch.color = new Color(0.62f, 0.72f, 0.78f, 1f);
        patch.raycastTarget = false;

        Image tint = EnsureImage(root.transform, "CleanupTint");
        Stretch(tint.rectTransform);
        tint.color = new Color(0.01f, 0.055f, 0.09f, 0.38f);
        tint.raycastTarget = false;
        Image glow = EnsureImage(root.transform, "CleanupGlow");
        SetAnchorRect(
            glow.rectTransform,
            new Vector2(0.08f, 0.03f),
            new Vector2(0.92f, 0.075f));
        glow.color = new Color(accent.r, accent.g, accent.b, 0.25f);
        glow.raycastTarget = false;
        EditorUtility.SetDirty(patch);
        return patch;
    }

    private static void DisableCleanupOverlay(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        GameObject root = EnsureChild(parent, name);
        SetAnchorRect(RequireRect(root), anchorMin, anchorMax);
        root.SetActive(false);
    }

    private static void SetAnchorRect(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static Slider EnsureSlider(
        Transform parent, string name, string label, Vector2 position,
        float minimum, float maximum, float value, TMP_FontAsset font,
        out TextMeshProUGUI valueText)
    {
        GameObject root = EnsureChild(parent, name);
        RectTransform rect = RequireRect(root);
        SetRect(rect, position, new Vector2(690f, 78f));
        EnsureText(root.transform, "Label", label, 23, Color.white,
            new Vector2(-220f, 22f), new Vector2(260f, 36f), font);
        valueText = EnsureText(root.transform, "Value", string.Empty, 22, Cyan,
            new Vector2(285f, 22f), new Vector2(100f, 36f), font);
        Image background = EnsureImage(root.transform, "Background");
        SetRectIfUnset(background.rectTransform, new Vector2(55f, -17f),
            new Vector2(560f, 12f));
        background.color = new Color32(51, 66, 72, 255);
        Image fill = EnsureImage(background.transform, "Fill");
        Stretch(fill.rectTransform);
        fill.color = Cyan;
        Image handle = EnsureImage(root.transform, "Handle");
        SetRectIfUnset(handle.rectTransform, new Vector2(55f, -17f),
            new Vector2(24f, 32f));
        handle.color = Gold;
        Slider slider = GetOrAddUnique<Slider>(root);
        slider.minValue = minimum;
        slider.maxValue = maximum;
        slider.value = value;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    private static Toggle EnsureToggle(
        Transform parent, string name, string label, Vector2 position,
        TMP_FontAsset font)
    {
        GameObject root = EnsureChild(parent, name);
        RectTransform rect = RequireRect(root);
        SetRect(rect, position, new Vector2(690f, 70f));
        Image background = EnsureImage(root.transform, "Background");
        SetRectIfUnset(background.rectTransform, new Vector2(-275f, 0f),
            new Vector2(42f, 42f));
        background.color = new Color32(42, 58, 64, 255);
        Image checkmark = EnsureImage(background.transform, "Checkmark");
        Stretch(checkmark.rectTransform);
        checkmark.color = Cyan;
        EnsureText(root.transform, "Label", label, 23, Color.white,
            new Vector2(-65f, 0f), new Vector2(350f, 44f), font);
        Toggle toggle = GetOrAddUnique<Toggle>(root);
        toggle.targetGraphic = background;
        toggle.graphic = checkmark;
        return toggle;
    }

    private static Button EnsureButton(
        Transform parent, string name, string label, Vector2 position,
        Color color, TMP_FontAsset font)
    {
        GameObject root = EnsureChild(parent, name);
        RectTransform rect = RequireRect(root);
        SetRect(rect, position, new Vector2(300f, 64f));
        Image image = GetOrAddUnique<Image>(root);
        image.color = color;
        Button button = GetOrAddUnique<Button>(root);
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.82f);
        colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
        button.colors = colors;
        EnsureText(root.transform, "Label", label, 25, Black,
            Vector2.zero, new Vector2(280f, 54f), font);
        return button;
    }

    private static Button EnsureDarkButton(
        Transform parent, string name, string label, Vector2 position,
        Color accent, TMP_FontAsset font)
    {
        Button button = EnsureButton(
            parent, name, label, position, new Color32(8, 27, 35, 248), font);
        SetRect(button.GetComponent<RectTransform>(), position,
            new Vector2(380f, 72f));
        Image image = button.GetComponent<Image>();
        image.color = new Color32(8, 27, 35, 248);
        Outline outline = GetOrAddUnique<Outline>(button.gameObject);
        outline.effectColor = accent;
        outline.effectDistance = new Vector2(2f, -2f);
        TextMeshProUGUI text = button.transform.Find("Label")
            ?.GetComponent<TextMeshProUGUI>();
        if (text != null)
        {
            text.color = Color.white;
            text.fontSize = 25f;
            text.fontStyle = FontStyles.Bold;
        }
        return button;
    }

    private static GameObject EnsurePanel(
        Transform parent, string name, Vector2 size)
    {
        GameObject root = EnsureChild(parent, name);
        RectTransform rect = RequireRect(root);
        SetRect(rect, Vector2.zero, size);
        Image image = GetOrAddUnique<Image>(root);
        image.color = Panel;
        Outline outline = GetOrAddUnique<Outline>(root);
        outline.effectColor = Cyan;
        outline.effectDistance = new Vector2(2f, -2f);
        return root;
    }

    private static TextMeshProUGUI EnsureText(
        Transform parent, string name, string value, float size, Color color,
        Vector2 position, Vector2 dimensions, TMP_FontAsset font)
    {
        GameObject root = EnsureChild(parent, name);
        RectTransform rect = RequireRect(root);
        SetRect(rect, position, dimensions);
        TextMeshProUGUI text = GetOrAddUnique<TextMeshProUGUI>(root);
        text.text = value;
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
        return text;
    }

    private static Text EnsureLegacyText(
        Transform parent, string name, string value, int size, Color color,
        Vector2 position, Vector2 dimensions)
    {
        GameObject root = EnsureChild(parent, name);
        TextMeshProUGUI tmp = root.GetComponent<TextMeshProUGUI>();
        if (tmp != null) Object.DestroyImmediate(tmp);
        RectTransform rect = RequireRect(root);
        SetRect(rect, position, dimensions);
        Text text = GetOrAddUnique<Text>(root);
        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        return text;
    }

    private static Image EnsureImage(Transform parent, string name)
    {
        GameObject root = EnsureChild(parent, name);
        return GetOrAddUnique<Image>(root);
    }

    private static GameObject EnsureChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) return child.gameObject;
        GameObject result = new GameObject(name, typeof(RectTransform));
        result.transform.SetParent(parent, false);
        return result;
    }

    private static GameObject EnsureRoot(Scene scene, string name)
    {
        GameObject existing = FindNamed(scene, name);
        if (existing != null) return existing;
        GameObject created = new GameObject(name);
        SceneManager.MoveGameObjectToScene(created, scene);
        return created;
    }

    private static GameObject FindNamed(Scene scene, string name)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(item => item.name == name)?.gameObject;
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name == name);
    }

    private static RectTransform RequireRect(GameObject owner)
    {
        RectTransform rect = owner.GetComponent<RectTransform>();
        if (rect != null) return rect;
        return owner.AddComponent<RectTransform>();
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void SetRectIfUnset(
        RectTransform rect, Vector2 position, Vector2 size)
    {
        SetRect(rect, position, size);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(parent.GetChild(i).gameObject);
    }

    private static void DestroyDirectChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) Object.DestroyImmediate(child.gameObject);
    }

    private static void BindPersistent(Button button, UnityAction action)
    {
        if (button == null || action == null) return;
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(button.onClick, i);
        UnityEventTools.AddPersistentListener(button.onClick, action);
        EditorUtility.SetDirty(button);
    }

    private static RoundResultController RequirePlayModeResultController()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning(
                "Result UI preview chỉ hoạt động trong Play Mode của Map_v2.");
            return null;
        }

        RoundResultController controller =
            Object.FindObjectOfType<RoundResultController>(true);
        if (controller == null)
            Debug.LogError("RoundResultController was not found in the active scene.");
        return controller;
    }

    private static TMP_FontAsset ResolveFont()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/UI/HiderHUD/HiderVietnameseDynamic.asset");
        return font != null ? font : TMP_Settings.defaultFontAsset;
    }

    private static T RequireComponent<T>(GameObject owner) where T : Component
    {
        T component = owner.GetComponent<T>();
        if (component == null)
            throw new InvalidOperationException($"{owner.name} is missing {typeof(T).Name}.");
        return component;
    }

    private static T GetOrAddUnique<T>(GameObject owner) where T : Component
    {
        T[] components = owner.GetComponents<T>();
        T result = components.FirstOrDefault();
        if (result == null) result = owner.AddComponent<T>();
        for (int i = 1; i < components.Length; i++)
            Object.DestroyImmediate(components[i]);
        return result;
    }

    private static void DestroyComponents<T>(GameObject root) where T : Component
    {
        foreach (T component in root.GetComponentsInChildren<T>(true))
            Object.DestroyImmediate(component);
    }

    private static void MarkHierarchyDirty(GameObject root)
    {
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
            if (component != null) EditorUtility.SetDirty(component);
    }

    private static void ConfigureBuildSettings()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(MainMenuScenePath, true),
            new EditorBuildSettingsScene(GameplayScenePath, true)
        };
    }
}
