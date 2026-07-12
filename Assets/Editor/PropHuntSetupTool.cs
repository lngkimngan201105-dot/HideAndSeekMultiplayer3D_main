using System;
using System.Collections.Generic;
using System.IO;
using StarterAssets;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PropHuntSetupTool
{
    private const string PlayerPrefabPath = "Assets/StarterAssets/FirstPersonController/Prefabs/PlayerCapsule.prefab";
    private const string MapV2Path = "Assets/Scenes/Map_v2.unity";

    private static readonly string[] PropPrefabNames =
    {
        "Cargo_container_v1_LD2close",
        "Wooden_box_v1_LD1square",
        "Bags_on_pallet_v1_2",
        "Generator_v1",
        "Palet_v1_set",
        "Electric_box_v3",
        "Conditioner_v1",
        "Barrel_v3_quadro",
        "Barrel_v3_single",
        "Road_block_v1",
        "Oil_tank_v1",
        "Electric_box_v1"
    };

    [MenuItem("Tools/Prop Hunt/Setup Hider Prop Hunt")]
    public static void Setup()
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        ConfigurePropPrefabs();
        ConfigurePlayerPrefab();
        ConfigureMapScene();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("PropHuntSetupTool: setup complete.");
    }

    private static void ConfigurePropPrefabs()
    {
        foreach (string propName in PropPrefabNames)
        {
            string path = FindPrefabPathByName(propName);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning($"PropHuntSetupTool: prefab '{propName}' was not found.");
                continue;
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
            PropTarget propTarget = prefabRoot.GetComponent<PropTarget>();
            if (propTarget == null)
            {
                propTarget = prefabRoot.AddComponent<PropTarget>();
            }

            propTarget.propId = propName;
            propTarget.displayName = ObjectNames.NicifyVariableName(propName);
            propTarget.visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            propTarget.visualOffset = Vector3.zero;
            propTarget.visualRotationOffset = Vector3.zero;
            propTarget.visualScale = 1f;

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
            PrefabUtility.UnloadPrefabContents(prefabRoot);
            Debug.Log($"PropHuntSetupTool: marked '{propName}' as PropTarget.");
        }
    }

    private static void ConfigurePlayerPrefab()
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        ConfigurePlayerObject(prefabRoot, null);
        PrefabUtility.SaveAsPrefabAsset(prefabRoot, PlayerPrefabPath);
        PrefabUtility.UnloadPrefabContents(prefabRoot);
        Debug.Log("PropHuntSetupTool: configured PlayerCapsule prefab.");
    }

    private static void ConfigureMapScene()
    {
        Scene scene = EditorSceneManager.OpenScene(MapV2Path, OpenSceneMode.Single);

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != "PlayerCapsule")
            {
                continue;
            }

            Camera existingFpsCamera = FindCameraInChildren(root.transform, "mainCamera");
            if (existingFpsCamera == null)
            {
                existingFpsCamera = FindCameraInChildren(root.transform, "MainCamera");
            }

            ConfigurePlayerObject(root, existingFpsCamera);
            PrefabUtility.RecordPrefabInstancePropertyModifications(root);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("PropHuntSetupTool: configured Map_v2 scene.");
    }

    private static void ConfigurePlayerObject(GameObject player, Camera preferredFpsCamera)
    {
        Transform playerTransform = player.transform;
        Transform playerCameraRoot = GetOrCreateChild(playerTransform, "PlayerCameraRoot", new Vector3(0f, 1.375f, 0f));
        Transform humanVisualRoot = GetOrCreateChild(playerTransform, "HumanVisualRoot", Vector3.zero);
        Transform propVisualRoot = GetOrCreateChild(playerTransform, "PropVisualRoot", Vector3.zero);
        Transform tpsCameraRoot = GetOrCreateChild(playerTransform, "TPSCameraRoot", new Vector3(0f, 2.1f, -4f));
        Transform spectatorRoot = GetOrCreateChild(playerTransform, "SpectatorCamera", new Vector3(0f, 4f, -6f));

        Transform capsule = playerTransform.Find("Capsule");
        if (capsule != null && capsule.parent != humanVisualRoot)
        {
            Undo.SetTransformParent(capsule, humanVisualRoot, "Move Capsule to HumanVisualRoot");
            capsule.localPosition = new Vector3(0f, 1f, 0f);
            capsule.localRotation = Quaternion.identity;
            capsule.localScale = Vector3.one;
        }

        Camera fpsCamera = preferredFpsCamera != null ? preferredFpsCamera : FindCameraInChildren(playerCameraRoot, "mainCamera");
        Camera tpsCamera = GetOrCreateCamera(tpsCameraRoot, "TPSCamera", false, new Vector3(20f, 0f, 0f));
        Camera spectatorCamera = GetOrCreateCamera(spectatorRoot, "SpectatorCameraView", false, new Vector3(25f, 0f, 0f));

        PlayerCameraModeManager cameraManager = GetOrAddComponent<PlayerCameraModeManager>(player);
        cameraManager.fpsCamera = fpsCamera;
        cameraManager.tpsCamera = tpsCamera;
        cameraManager.spectatorCamera = spectatorCamera;
        cameraManager.tpsCameraRoot = tpsCameraRoot;
        cameraManager.spectatorCameraRoot = spectatorRoot;

        PropTransformSystem transformSystem = GetOrAddComponent<PropTransformSystem>(player);
        transformSystem.playerRole = PlayerRole.Hider;
        transformSystem.mainCamera = fpsCamera;
        transformSystem.interactionDistance = 3f;
        transformSystem.humanVisualRoot = humanVisualRoot;
        transformSystem.propVisualRoot = propVisualRoot;
        transformSystem.cameraModeManager = cameraManager;
        transformSystem.currentState = PlayerDisguiseState.Human;

        SpectatorCameraController spectatorController = GetOrAddComponent<SpectatorCameraController>(spectatorRoot.gameObject);
        spectatorController.propTransformSystem = transformSystem;
        spectatorController.playerRoot = playerTransform;
        spectatorController.starterAssetsInputs = player.GetComponent<StarterAssetsInputs>();
        spectatorController.maxRadius = 20f;
        spectatorController.maxHeight = 10f;
        spectatorController.invertY = false;

        GetOrCreateInteractionPrompt(playerTransform, out TextMeshProUGUI promptText, out Text legacyPromptText);
        PropInteractionUI interactionUI = GetOrAddComponent<PropInteractionUI>(player);
        interactionUI.propTransformSystem = transformSystem;
        interactionUI.promptText = promptText;
        interactionUI.legacyPromptText = legacyPromptText;
        interactionUI.copyPrompt = "E để copy hình dạng";
        interactionUI.disguisedPrompt = "R để trở lại người\nTab để quan sát";

        FirstPersonController firstPersonController = player.GetComponent<FirstPersonController>();
        if (firstPersonController != null)
        {
            firstPersonController.CinemachineCameraTarget = playerCameraRoot.gameObject;
        }

        propVisualRoot.gameObject.SetActive(false);
        tpsCamera.gameObject.SetActive(false);
        spectatorCamera.gameObject.SetActive(false);

        EditorUtility.SetDirty(player);
        EditorUtility.SetDirty(cameraManager);
        EditorUtility.SetDirty(transformSystem);
        EditorUtility.SetDirty(spectatorController);
        EditorUtility.SetDirty(interactionUI);
        if (promptText != null)
        {
            EditorUtility.SetDirty(promptText);
        }

        if (legacyPromptText != null)
        {
            EditorUtility.SetDirty(legacyPromptText);
        }
    }

    private static void GetOrCreateInteractionPrompt(Transform playerTransform, out TextMeshProUGUI promptText, out Text legacyPromptText)
    {
        promptText = null;
        legacyPromptText = null;

        Transform canvasTransform = playerTransform.Find("PropHuntInteractionCanvas");
        GameObject canvasObject = canvasTransform != null ? canvasTransform.gameObject : new GameObject("PropHuntInteractionCanvas");
        if (canvasTransform == null)
        {
            Undo.SetTransformParent(canvasObject.transform, playerTransform, "Create PropHuntInteractionCanvas");
            canvasObject.transform.localPosition = Vector3.zero;
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one;
        }

        Canvas canvas = GetOrAddComponent<Canvas>(canvasObject);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        GetOrAddComponent<CanvasScaler>(canvasObject);
        GetOrAddComponent<GraphicRaycaster>(canvasObject);

        Transform textTransform = canvasObject.transform.Find("InteractionPromptText");
        GameObject textObject = textTransform != null ? textTransform.gameObject : new GameObject("InteractionPromptText");
        if (textTransform == null)
        {
            Undo.SetTransformParent(textObject.transform, canvasObject.transform, "Create InteractionPromptText");
        }

        RectTransform rectTransform = GetOrAddComponent<RectTransform>(textObject);
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = new Vector2(0f, -80f);
        rectTransform.sizeDelta = new Vector2(700f, 120f);
        rectTransform.localScale = Vector3.one;

        if (HasTextMeshProSettings())
        {
            Text legacyText = textObject.GetComponent<Text>();
            if (legacyText != null)
            {
                Undo.DestroyObjectImmediate(legacyText);
            }

            promptText = GetOrAddComponent<TextMeshProUGUI>(textObject);
            promptText.text = string.Empty;
            promptText.fontSize = 32f;
            promptText.alignment = TextAlignmentOptions.Center;
            promptText.color = Color.white;
            promptText.enableWordWrapping = false;
            promptText.raycastTarget = false;
        }
        else
        {
            TextMeshProUGUI tmpText = textObject.GetComponent<TextMeshProUGUI>();
            if (tmpText != null)
            {
                Undo.DestroyObjectImmediate(tmpText);
            }

            legacyPromptText = GetOrAddComponent<Text>(textObject);
            legacyPromptText.text = string.Empty;
            legacyPromptText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            legacyPromptText.fontSize = 32;
            legacyPromptText.alignment = TextAnchor.MiddleCenter;
            legacyPromptText.color = Color.white;
            legacyPromptText.raycastTarget = false;
            legacyPromptText.horizontalOverflow = HorizontalWrapMode.Overflow;
            legacyPromptText.verticalOverflow = VerticalWrapMode.Overflow;
        }

        textObject.SetActive(false);
    }

    private static bool HasTextMeshProSettings()
    {
        return AssetDatabase.FindAssets("t:TMP_Settings").Length > 0;
    }

    private static Transform GetOrCreateChild(Transform parent, string name, Vector3 localPosition)
    {
        Transform child = parent.Find(name);
        if (child != null)
        {
            return child;
        }

        GameObject childObject = new GameObject(name);
        Undo.SetTransformParent(childObject.transform, parent, $"Create {name}");
        childObject.transform.localPosition = localPosition;
        childObject.transform.localRotation = Quaternion.identity;
        childObject.transform.localScale = Vector3.one;
        return childObject.transform;
    }

    private static Camera GetOrCreateCamera(Transform parent, string name, bool active, Vector3 localEulerAngles)
    {
        Transform existing = parent.Find(name);
        GameObject cameraObject = existing != null ? existing.gameObject : new GameObject(name);
        if (existing == null)
        {
            Undo.SetTransformParent(cameraObject.transform, parent, $"Create {name}");
            cameraObject.transform.localPosition = Vector3.zero;
            cameraObject.transform.localRotation = Quaternion.Euler(localEulerAngles);
            cameraObject.transform.localScale = Vector3.one;
        }

        Camera camera = GetOrAddComponent<Camera>(cameraObject);
        GetOrAddComponent<AudioListener>(cameraObject);
        cameraObject.SetActive(active);
        return camera;
    }

    private static Camera FindCameraInChildren(Transform root, string cameraName)
    {
        foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
        {
            if (camera.name.Equals(cameraName, StringComparison.OrdinalIgnoreCase))
            {
                return camera;
            }
        }

        return null;
    }

    private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        return component != null ? component : gameObject.AddComponent<T>();
    }

    private static string FindPrefabPathByName(string prefabName)
    {
        string[] guids = AssetDatabase.FindAssets($"{prefabName} t:Prefab");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == prefabName)
            {
                return path;
            }
        }

        return null;
    }
}
