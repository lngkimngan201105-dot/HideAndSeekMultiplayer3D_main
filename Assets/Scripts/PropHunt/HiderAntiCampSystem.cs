using UnityEngine;

public enum HiderAntiCampState
{
    Safe,
    Warning,
    Revealed
}

public class HiderAntiCampSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PropTransformSystem propTransformSystem;
    [SerializeField] private PropHuntRoundManager roundManager;

    [Header("Anti-camp")]
    [SerializeField, Min(0f)] private float allowedCampTime = 30f;
    [SerializeField, Min(0.1f)] private float escapeCountdownDuration = 5f;
    [SerializeField, Min(0.1f)] private float escapeRadius = 3f;
    [SerializeField, Min(0.1f)] private float repeatSoundInterval = 10f;
    [SerializeField] private AudioSource revealAudioSource;
    [SerializeField] private AudioClip revealSound;

    public float CampTime { get; private set; }
    public float CountdownRemaining { get; private set; }
    public int CountdownDisplay => IsCountdownActive
        ? Mathf.Clamp(Mathf.CeilToInt(CountdownRemaining), 1, Mathf.CeilToInt(escapeCountdownDuration))
        : 0;
    public bool IsCountdownActive => CurrentState == HiderAntiCampState.Warning;
    public bool IsRevealed => CurrentState == HiderAntiCampState.Revealed;
    public HiderAntiCampState CurrentState { get; private set; } = HiderAntiCampState.Safe;

    private Vector2 _campOrigin;
    private float _nextRevealAt;
    private bool _hasCampOrigin;

    private void Awake()
    {
        ResolveReferences();
        ConfigureAudioSource();
        ResetAntiCamp();
    }

    private void OnEnable()
    {
        if (roundManager != null)
        {
            roundManager.RoundStateChanged += HandleRoundStateChanged;
        }
    }

    private void OnDisable()
    {
        if (roundManager != null)
        {
            roundManager.RoundStateChanged -= HandleRoundStateChanged;
        }
    }

    private void Update()
    {
        if (!ShouldTrackAntiCamp())
        {
            CountdownRemaining = 0f;
            CurrentState = HiderAntiCampState.Safe;
            return;
        }

        if (!_hasCampOrigin)
        {
            SetNewCampOrigin(GetHorizontalPosition());
        }

        Vector2 currentPosition = GetHorizontalPosition();
        float horizontalDistance = Vector2.Distance(currentPosition, _campOrigin);
        if (horizontalDistance >= escapeRadius)
        {
            SetNewCampOrigin(currentPosition);
            StopRevealSound();
            return;
        }

        CampTime += Time.deltaTime;
        if (CampTime < allowedCampTime)
        {
            CountdownRemaining = 0f;
            CurrentState = HiderAntiCampState.Safe;
            return;
        }

        float warningElapsed = CampTime - allowedCampTime;
        if (warningElapsed < escapeCountdownDuration)
        {
            CountdownRemaining = escapeCountdownDuration - warningElapsed;
            CurrentState = HiderAntiCampState.Warning;
            return;
        }

        CountdownRemaining = 0f;
        CurrentState = HiderAntiCampState.Revealed;
        if (CampTime >= _nextRevealAt)
        {
            PlayRevealSound();
            _nextRevealAt = CampTime + repeatSoundInterval;
        }
    }

    public void Configure(
        PropTransformSystem transformSystem,
        PropHuntRoundManager configuredRoundManager,
        AudioSource configuredAudioSource)
    {
        if (roundManager != null && isActiveAndEnabled)
        {
            roundManager.RoundStateChanged -= HandleRoundStateChanged;
        }

        propTransformSystem = transformSystem;
        roundManager = configuredRoundManager;
        revealAudioSource = configuredAudioSource;
        ConfigureAudioSource();

        if (roundManager != null && isActiveAndEnabled)
        {
            roundManager.RoundStateChanged += HandleRoundStateChanged;
        }

        ResetAntiCamp();
    }

    public void ResetAntiCamp()
    {
        _hasCampOrigin = false;
        CampTime = 0f;
        CountdownRemaining = 0f;
        _nextRevealAt = allowedCampTime + escapeCountdownDuration;
        CurrentState = HiderAntiCampState.Safe;
        StopRevealSound();
    }

    private void HandleRoundStateChanged(PropHuntRoundState state)
    {
        if (state == PropHuntRoundState.Hunting)
        {
            SetNewCampOrigin(GetHorizontalPosition());
        }
        else
        {
            ResetAntiCamp();
        }
    }

    private void SetNewCampOrigin(Vector2 horizontalPosition)
    {
        _campOrigin = horizontalPosition;
        _hasCampOrigin = true;
        CampTime = 0f;
        CountdownRemaining = 0f;
        _nextRevealAt = allowedCampTime + escapeCountdownDuration;
        CurrentState = HiderAntiCampState.Safe;
    }

    private bool ShouldTrackAntiCamp()
    {
        return propTransformSystem != null &&
               propTransformSystem.playerRole == PlayerRole.Hider &&
               propTransformSystem.currentState == PlayerDisguiseState.Disguised &&
               !propTransformSystem.IsEliminated &&
               roundManager != null &&
               roundManager.CurrentState == PropHuntRoundState.Hunting;
    }

    private Vector2 GetHorizontalPosition()
    {
        Vector3 position = transform.position;
        return new Vector2(position.x, position.z);
    }

    private void ResolveReferences()
    {
        if (propTransformSystem == null)
        {
            propTransformSystem = GetComponent<PropTransformSystem>();
        }

        if (roundManager == null)
        {
            roundManager = FindObjectOfType<PropHuntRoundManager>();
        }

        if (revealAudioSource == null)
        {
            revealAudioSource = GetComponent<AudioSource>();
        }
    }

    private void ConfigureAudioSource()
    {
        if (revealAudioSource == null)
        {
            return;
        }

        revealAudioSource.playOnAwake = false;
        revealAudioSource.loop = false;
        revealAudioSource.spatialBlend = 1f;
        revealAudioSource.minDistance = 4f;
        revealAudioSource.maxDistance = 35f;
        revealAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
    }

    private void PlayRevealSound()
    {
        if (revealAudioSource == null)
        {
            return;
        }

        AudioClip clip = revealSound != null ? revealSound : GetGeneratedRevealClip();
        revealAudioSource.PlayOneShot(clip);
    }

    private void StopRevealSound()
    {
        if (revealAudioSource != null)
        {
            revealAudioSource.Stop();
        }
    }

    private static AudioClip _generatedRevealClip;

    private static AudioClip GetGeneratedRevealClip()
    {
        if (_generatedRevealClip != null)
        {
            return _generatedRevealClip;
        }

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

        _generatedRevealClip = AudioClip.Create("GeneratedAntiCampReveal", sampleCount, 1, sampleRate, false);
        _generatedRevealClip.SetData(samples, 0);
        return _generatedRevealClip;
    }
}
