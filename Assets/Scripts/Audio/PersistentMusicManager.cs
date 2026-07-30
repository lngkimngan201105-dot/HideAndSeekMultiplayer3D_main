using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class PersistentMusicManager : MonoBehaviour
{
    public const string MusicVolumeKey = "MusicVolume";
    public const float DefaultMusicVolume = 0.25f;

    public static PersistentMusicManager Instance { get; private set; }

    [SerializeField] private AudioClip musicClip;
    [SerializeField] private AudioSource musicSource;

    public AudioClip MusicClip => musicClip;
    public AudioSource MusicSource => musicSource;
    public float MusicVolume => musicSource != null
        ? musicSource.volume
        : PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
    }

    private void Awake()
    {
        InitializeAsSingleton(musicClip);
    }

    private bool InitializeAsSingleton(AudioClip fallbackClip)
    {
        if (Instance != null && Instance != this)
        {
            AudioSource duplicateSource = GetComponent<AudioSource>();
            if (duplicateSource != null)
            {
                duplicateSource.Stop();
            }

            Destroy(gameObject);
            return false;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (musicSource == null)
        {
            musicSource = GetComponent<AudioSource>();
        }

        if (musicClip == null)
        {
            musicClip = fallbackClip;
        }
        if (musicClip == null && musicSource != null)
        {
            musicClip = musicSource.clip;
        }

        ApplySourceSettings();
        SetSourceVolume(PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume));
        StartPlaybackIfNeeded();
        return true;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public static PersistentMusicManager EnsureInstance(AudioClip clip)
    {
        if (Instance != null)
        {
            Instance.AssignClipIfMissing(clip);
            Instance.StartPlaybackIfNeeded();
            return Instance;
        }

        PersistentMusicManager existing =
            FindObjectOfType<PersistentMusicManager>(true);
        if (existing != null)
        {
            existing.AssignClipIfMissing(clip);
            if (!existing.gameObject.activeSelf)
            {
                existing.gameObject.SetActive(true);
            }
            else
            {
                existing.InitializeAsSingleton(clip);
            }

            return existing;
        }

        GameObject managerObject = new GameObject("PersistentMusicManager");
        managerObject.SetActive(false);
        AudioSource source = managerObject.AddComponent<AudioSource>();
        PersistentMusicManager manager =
            managerObject.AddComponent<PersistentMusicManager>();
        manager.Configure(clip, source);
        managerObject.SetActive(true);
        return manager;
    }

    public void Configure(AudioClip clip, AudioSource source)
    {
        musicClip = clip;
        musicSource = source != null ? source : GetComponent<AudioSource>();
        ApplySourceSettings();

        if (Application.isPlaying)
        {
            SetSourceVolume(
                PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume));
            StartPlaybackIfNeeded();
        }
        else
        {
            SetSourceVolume(DefaultMusicVolume);
        }
    }

    public void SetMusicVolume(float volume)
    {
        float clampedVolume = Mathf.Clamp01(volume);
        SetSourceVolume(clampedVolume);
        PlayerPrefs.SetFloat(MusicVolumeKey, clampedVolume);
        PlayerPrefs.Save();
    }

    private void AssignClipIfMissing(AudioClip clip)
    {
        if (musicClip == null)
        {
            musicClip = clip;
        }

        if (musicSource == null)
        {
            musicSource = GetComponent<AudioSource>();
        }

        if (musicSource != null && musicSource.clip == null)
        {
            musicSource.clip = musicClip;
        }
    }

    private void ApplySourceSettings()
    {
        if (musicSource == null)
        {
            return;
        }

        musicSource.clip = musicClip;
        musicSource.playOnAwake = true;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        musicSource.pitch = 1f;
        musicSource.mute = false;
        musicSource.bypassEffects = false;
        musicSource.bypassListenerEffects = false;
        musicSource.bypassReverbZones = true;
        musicSource.priority = 128;
        musicSource.dopplerLevel = 0f;
    }

    private void SetSourceVolume(float volume)
    {
        if (musicSource != null)
        {
            musicSource.volume = Mathf.Clamp01(volume);
        }
    }

    private void StartPlaybackIfNeeded()
    {
        if (musicSource != null && musicSource.clip != null &&
            !musicSource.isPlaying)
        {
            musicSource.Play();
        }
    }
}
