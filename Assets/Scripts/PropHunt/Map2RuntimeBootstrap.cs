using Assets.Scripts;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

public class Map2RuntimeBootstrap : MonoBehaviour
{
    private const string TargetSceneName = "Map_v2";

    private AudioSource _musicSource;
    private AudioClip _menuMusic;
    private AudioClip _matchMusic;
    private GameObject _menuRoot;
    private PropHuntRoundManager _roundManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != TargetSceneName || FindObjectOfType<Map2RuntimeBootstrap>() != null)
        {
            return;
        }

        GameObject bootstrap = new GameObject("Map2_RuntimeBootstrap");
        bootstrap.AddComponent<Map2RuntimeBootstrap>();
    }

    private void Start()
    {
        if (SceneManager.GetActiveScene().name != TargetSceneName)
        {
            Destroy(gameObject);
            return;
        }

        _roundManager = FindObjectOfType<PropHuntRoundManager>();
        EnsureEventSystem();
        EnsureRuntimeNavMeshBuilder();
        RepairInvisibleMapPieces();
        EnsureSeekerAI();
        BuildMenu();
        ConfigureMusic();
        ShowMenu();
    }

    private void OnDestroy()
    {
        if (_menuMusic != null)
        {
            Destroy(_menuMusic);
        }

        if (_matchMusic != null)
        {
            Destroy(_matchMusic);
        }
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        eventSystem.AddComponent<InputSystemUIInputModule>();
#else
        eventSystem.AddComponent<StandaloneInputModule>();
#endif
    }

    private void EnsureRuntimeNavMeshBuilder()
    {
        if (FindObjectOfType<RuntimeNavMeshBuilder>() != null)
        {
            return;
        }

        GameObject builder = new GameObject("RuntimeNavMeshBuilder");
        builder.AddComponent<RuntimeNavMeshBuilder>();
    }

    private void RepairInvisibleMapPieces()
    {
        foreach (MeshRenderer renderer in FindObjectsOfType<MeshRenderer>(true))
        {
            GameObject target = renderer.gameObject;
            bool looksLikeHiddenFloor =
                target.name.StartsWith("Road_set_v1_s_floor") &&
                (!target.activeSelf || !renderer.enabled);
            bool isManualGroundFix = target.name.StartsWith("GroundBlock_Fix");

            if (!looksLikeHiddenFloor && !isManualGroundFix)
            {
                continue;
            }

            target.SetActive(true);
            renderer.enabled = true;

            BoxCollider box = target.GetComponent<BoxCollider>();
            if (box != null && box.size.y <= 0.01f)
            {
                box.size = new Vector3(box.size.x, 0.15f, box.size.z);
            }
        }
    }

    private void EnsureSeekerAI()
    {
        if (FindObjectOfType<SeekerAIController>() != null)
        {
            return;
        }

        PropTransformSystem existingSeeker = null;
        foreach (PropTransformSystem player in FindObjectsOfType<PropTransformSystem>(true))
        {
            if (player.playerRole == PlayerRole.Seeker)
            {
                existingSeeker = player;
                break;
            }
        }

        GameObject seekerObject = existingSeeker != null
            ? existingSeeker.gameObject
            : CreateSeekerObject();

        if (seekerObject == null)
        {
            return;
        }

        NavMeshAgent agent = seekerObject.GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            agent = seekerObject.AddComponent<NavMeshAgent>();
        }

        if (seekerObject.GetComponent<SeekerAIController>() == null)
        {
            seekerObject.AddComponent<SeekerAIController>();
        }
    }

    private GameObject CreateSeekerObject()
    {
        Vector3 spawnPosition = FindSeekerSpawnPosition();
        GameObject seeker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        seeker.name = "AI_Seeker";
        seeker.transform.position = spawnPosition;
        seeker.transform.localScale = new Vector3(0.9f, 1f, 0.9f);

        Renderer renderer = seeker.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material material = new Material(Shader.Find("Standard"));
            material.color = new Color(0.9f, 0.16f, 0.1f);
            renderer.sharedMaterial = material;
        }

        return seeker;
    }

    private Vector3 FindSeekerSpawnPosition()
    {
        PropTransformSystem hider = FindObjectOfType<PropTransformSystem>();
        Vector3 origin = hider != null
            ? hider.transform.position + new Vector3(8f, 0f, 8f)
            : Vector3.zero;

        if (NavMesh.SamplePosition(origin, out NavMeshHit hit, 25f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        return origin + Vector3.up;
    }

    private void BuildMenu()
    {
        Canvas canvas = CreateCanvas("Map2MainMenuCanvas", 200);
        _menuRoot = canvas.gameObject;

        Image backdrop = _menuRoot.AddComponent<Image>();
        backdrop.color = new Color(0.02f, 0.025f, 0.03f, 0.86f);

        RectTransform panel = CreatePanel(canvas.transform, new Vector2(520f, 360f));
        CreateText(panel, "HIDE AND SEEK", 42, FontStyle.Bold, new Vector2(0f, 112f), new Vector2(460f, 70f));
        CreateText(panel, "MAP 2", 24, FontStyle.Bold, new Vector2(0f, 58f), new Vector2(460f, 42f));

        CreateButton(panel, "PLAY", new Vector2(0f, -20f), StartMatch);
        CreateButton(panel, "QUIT", new Vector2(0f, -104f), QuitGame);
    }

    private void ConfigureMusic()
    {
        GameObject audioObject = new GameObject("Map2MusicPlayer");
        audioObject.transform.SetParent(transform, false);
        _musicSource = audioObject.AddComponent<AudioSource>();
        _musicSource.loop = true;
        _musicSource.playOnAwake = false;
        _musicSource.spatialBlend = 0f;
        _musicSource.volume = 0.45f;

        _menuMusic = GenerateMusicClip("Generated_Map2_MenuMusic", 96f, 0.28f);
        _matchMusic = GenerateMusicClip("Generated_Map2_MatchMusic", 132f, 0.34f);
    }

    private void ShowMenu()
    {
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        PlayMusic(_menuMusic, 0.5f);
        if (_menuRoot != null)
        {
            _menuRoot.SetActive(true);
        }
    }

    private void StartMatch()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (_menuRoot != null)
        {
            _menuRoot.SetActive(false);
        }

        if (_roundManager != null)
        {
            _roundManager.RestartRound();
        }

        PlayMusic(_matchMusic, 0.38f);
    }

    private void QuitGame()
    {
        Time.timeScale = 1f;
        Application.Quit();
    }

    private void PlayMusic(AudioClip clip, float volume)
    {
        if (_musicSource == null || clip == null)
        {
            return;
        }

        _musicSource.Stop();
        _musicSource.clip = clip;
        _musicSource.volume = volume;
        _musicSource.Play();
    }

    private static Canvas CreateCanvas(string name, int sortingOrder)
    {
        GameObject root = new GameObject(name);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private static RectTransform CreatePanel(Transform parent, Vector2 size)
    {
        GameObject panelObject = new GameObject("MainMenuPanel");
        panelObject.transform.SetParent(parent, false);
        RectTransform rect = panelObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;

        Image image = panelObject.AddComponent<Image>();
        image.color = new Color(0.06f, 0.075f, 0.085f, 0.96f);
        return rect;
    }

    private static Text CreateText(Transform parent, string content, int size, FontStyle style, Vector2 position, Vector2 dimensions)
    {
        GameObject textObject = new GameObject(content.Replace(" ", string.Empty));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        Text text = textObject.AddComponent<Text>();
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (text.font == null)
        {
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        return text;
    }

    private static void CreateButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = new GameObject(label + "Button");
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(260f, 58f);

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.86f, 0.22f, 0.14f, 1f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        CreateText(buttonObject.transform, label, 24, FontStyle.Bold, Vector2.zero, rect.sizeDelta);
    }

    private static AudioClip GenerateMusicClip(string name, float bpm, float volume)
    {
        const int sampleRate = 44100;
        const float duration = 8f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float beat = bpm / 60f;

        for (int i = 0; i < sampleCount; i++)
        {
            float time = i / (float)sampleRate;
            float pulse = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(time * beat * Mathf.PI)), 10f);
            float bass = Mathf.Sin(2f * Mathf.PI * 55f * time) * (0.45f + pulse * 0.5f);
            float tone = Mathf.Sin(2f * Mathf.PI * 220f * time) * 0.16f;
            float high = Mathf.Sin(2f * Mathf.PI * 440f * time + Mathf.Sin(time * 2f)) * 0.08f;
            samples[i] = (bass + tone + high) * volume;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
