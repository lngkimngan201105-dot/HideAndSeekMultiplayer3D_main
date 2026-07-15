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

public static class HiderCompleteHUDSetupTool
{
    private const string MapV2Path = "Assets/Scenes/Map_v2.unity";
    private const string HudAssetFolder = "Assets/UI/HiderHUD";
    private const string SpeedSpritePath = HudAssetFolder + "/SpeedBoost.png";
    private const string AntiCampSpritePath = HudAssetFolder + "/AntiCamp.png";
    private const string RandomPropSpritePath = HudAssetFolder + "/RandomProp.png";
    private const string BevelSpritePath = HudAssetFolder + "/BeveledPanel.png";
    private const string GeneratedFontPath = HudAssetFolder + "/HiderVietnameseDynamic.asset";

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
    private static readonly Color SpeedCoverColor = Hex("51449B");
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
        ConfigureAbilitySpriteImport(SpeedSpritePath);
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

        PropHuntRoundManager roundManager = GetOrCreateRoundManager();
        ConfigureHiderGameplay(hider, roundManager, out HiderAbilityController abilityController, out HiderAntiCampSystem antiCampSystem);
        CreateOrUpdateHud(hider, roundManager, abilityController, antiCampSystem);

        PropTransformSystem[] players = UnityEngine.Object.FindObjectsOfType<PropTransformSystem>(true);
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
        EditorUtility.SetDirty(hider);
        EditorUtility.SetDirty(abilityController);
        EditorUtility.SetDirty(antiCampSystem);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), MapV2Path);
        AssetDatabase.SaveAssets();

        Debug.Log("HiderCompleteHUDSetupTool: Canvas active.");
        Debug.Log("HiderCompleteHUDSetupTool: TopRoundBar created.");
        Debug.Log("HiderCompleteHUDSetupTool: HiderContextPanel created.");
        Debug.Log("HiderCompleteHUDSetupTool: Ability sprites assigned.");
        Debug.Log("HiderCompleteHUDSetupTool: Speed card created.");
        Debug.Log("HiderCompleteHUDSetupTool: Anti-camp card created.");
        Debug.Log("HiderCompleteHUDSetupTool: Random prop card created.");
        Debug.Log(_fontAsset != null
            ? "HiderCompleteHUDSetupTool: TMP font assigned."
            : "HiderCompleteHUDSetupTool: TMP font unavailable; outline skipped safely.");
        Debug.Log("HiderCompleteHUDSetupTool: Scene saved.");
        Debug.Log("HiderCompleteHUDSetupTool:\nHUD setup complete.");
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

    private static void ConfigureHiderGameplay(
        PropTransformSystem hider,
        PropHuntRoundManager roundManager,
        out HiderAbilityController abilityController,
        out HiderAntiCampSystem antiCampSystem)
    {
        GameObject player = hider.gameObject;
        AudioSource audioSource = player.GetComponents<AudioSource>().FirstOrDefault();
        if (audioSource == null)
        {
            audioSource = Undo.AddComponent<AudioSource>(player);
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 1f;
        audioSource.minDistance = 4f;
        audioSource.maxDistance = 35f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;

        abilityController = GetOrAddComponent<HiderAbilityController>(player);
        antiCampSystem = GetOrAddComponent<HiderAntiCampSystem>(player);
        PropTarget[] propDefinitions = UnityEngine.Object.FindObjectsOfType<PropTarget>(true)
            .Where(IsValidOriginalPropDefinition)
            .ToArray();

        hider.roundManager = roundManager;
        abilityController.Configure(
            hider,
            roundManager,
            player.GetComponent<FirstPersonController>(),
            propDefinitions
        );
        antiCampSystem.Configure(hider, roundManager, audioSource);

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
    }

    private static bool IsValidOriginalPropDefinition(PropTarget prop)
    {
        if (prop == null || prop.visualParts == null || prop.visualParts.Length == 0)
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

    private static void CreateOrUpdateHud(
        PropTransformSystem hider,
        PropHuntRoundManager roundManager,
        HiderAbilityController abilityController,
        HiderAntiCampSystem antiCampSystem)
    {
        GameObject canvasObject = FindOrCreateUniqueRoot("PropHuntHUDCanvas");
        ClearChildren(canvasObject.transform);
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
            out CanvasGroup speedGroup,
            out CanvasGroup antiCampGroup,
            out CanvasGroup randomGroup,
            out TextMeshProUGUI speedChargeText,
            out TextMeshProUGUI randomChargeText,
            out TextMeshProUGUI antiCampCountdownText,
            out Image speedCooldown,
            out Image randomCooldown,
            out Image speedIcon,
            out Image antiCampIcon,
            out Image randomIcon
        );

        PropHuntHUDController hud = GetOrAddComponent<PropHuntHUDController>(canvasObject);
        hud.Configure(
            roundManager,
            hider,
            abilityController,
            antiCampSystem,
            seekerCountText,
            timerText,
            hiderCountText,
            contextPanel,
            contextText,
            speedChargeText,
            randomChargeText,
            antiCampCountdownText,
            speedCooldown,
            randomCooldown,
            speedIcon,
            antiCampIcon,
            randomIcon,
            abilityPanel,
            speedGroup,
            antiCampGroup,
            randomGroup
        );

        EditorUtility.SetDirty(canvasObject);
        EditorUtility.SetDirty(hud);
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
        out CanvasGroup speedGroup,
        out CanvasGroup antiCampGroup,
        out CanvasGroup randomGroup,
        out TextMeshProUGUI speedCharge,
        out TextMeshProUGUI randomCharge,
        out TextMeshProUGUI antiCampCountdown,
        out Image speedCooldown,
        out Image randomCooldown,
        out Image speedIcon,
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

        speedIcon = CreateAbilityCard(
            abilityPanel.transform,
            "SpeedBoostCard",
            SpeedSpritePath,
            true,
            SpeedCoverColor,
            out speedGroup,
            out speedCharge,
            out speedCooldown,
            out _
        );
        antiCampIcon = CreateAbilityCard(
            abilityPanel.transform,
            "AntiCampCard",
            AntiCampSpritePath,
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
            RandomCoverColor,
            out randomGroup,
            out randomCharge,
            out randomCooldown,
            out _
        );
    }

    private static Image CreateAbilityCard(
        Transform parent,
        string name,
        string spritePath,
        bool showCharge,
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
        if (showCharge)
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
            text.outlineColor = color;
            text.outlineWidth = width;
            return;
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

    private static void ClearChildren(Transform parent)
    {
        for (int index = parent.childCount - 1; index >= 0; index--)
        {
            Undo.DestroyObjectImmediate(parent.GetChild(index).gameObject);
        }
    }

    private static GameObject CreateChild(Transform parent, string name)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(child, $"Create {name}");
        Undo.SetTransformParent(child.transform, parent, $"Parent {name}");
        child.transform.localScale = Vector3.one;
        return child;
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
