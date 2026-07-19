using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public sealed class HiderAntiCampAudioPresentation : MonoBehaviour
{
    [SerializeField] private HiderAntiCampSystem antiCampSystem;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip alertClip;

    private static AudioClip generatedAlertClip;

    public HiderAntiCampSystem AntiCampSystem => antiCampSystem;
    public AudioSource AudioSource => audioSource;
    public int PlayedAlertCount { get; private set; }

    private void Awake()
    {
        ResolveReferences();
        ConfigureAudioSource();
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        if (audioSource != null) audioSource.Stop();
    }

    public void Configure(HiderAntiCampSystem configuredAntiCampSystem, AudioSource configuredAudioSource)
    {
        Unsubscribe();
        antiCampSystem = configuredAntiCampSystem;
        audioSource = configuredAudioSource;
        ConfigureAudioSource();
        if (isActiveAndEnabled) Subscribe();
    }

    private void Subscribe()
    {
        if (antiCampSystem == null) return;
        antiCampSystem.AntiCampAlertTriggered -= HandleAlertTriggered;
        antiCampSystem.AntiCampAlertTriggered += HandleAlertTriggered;
        antiCampSystem.AntiCampAlertCleared -= HandleAlertCleared;
        antiCampSystem.AntiCampAlertCleared += HandleAlertCleared;
    }

    private void Unsubscribe()
    {
        if (antiCampSystem == null) return;
        antiCampSystem.AntiCampAlertTriggered -= HandleAlertTriggered;
        antiCampSystem.AntiCampAlertCleared -= HandleAlertCleared;
    }

    private void HandleAlertTriggered(HiderAntiCampAlertData alert)
    {
        if (audioSource == null) return;
        AudioClip clip = alertClip != null ? alertClip : GetGeneratedAlertClip();
        audioSource.PlayOneShot(clip);
        PlayedAlertCount++;
    }

    private void HandleAlertCleared()
    {
        if (audioSource != null) audioSource.Stop();
    }

    private void ResolveReferences()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (antiCampSystem == null) antiCampSystem = GetComponentInParent<HiderAntiCampSystem>();
    }

    private void ConfigureAudioSource()
    {
        if (audioSource == null) return;
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 1f;
        audioSource.minDistance = 4f;
        audioSource.maxDistance = 35f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
    }

    private static AudioClip GetGeneratedAlertClip()
    {
        if (generatedAlertClip != null) return generatedAlertClip;

        const int sampleRate = 44100;
        const float duration = 0.55f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float time = i / (float)sampleRate;
            float envelope = Mathf.Sin(Mathf.PI * i / sampleCount);
            float pulse = 0.65f + 0.35f * Mathf.Sign(Mathf.Sin(time * 24f));
            samples[i] = Mathf.Sin(2f * Mathf.PI * 740f * time) * envelope * pulse * 0.32f;
        }

        generatedAlertClip = AudioClip.Create(
            "GeneratedAntiCampReveal",
            sampleCount,
            1,
            sampleRate,
            false);
        generatedAlertClip.SetData(samples, 0);
        return generatedAlertClip;
    }
}
