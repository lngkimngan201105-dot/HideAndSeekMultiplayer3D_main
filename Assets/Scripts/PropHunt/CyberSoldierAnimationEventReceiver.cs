using UnityEngine;

[DisallowMultipleComponent]
public sealed class CyberSoldierAnimationEventReceiver : MonoBehaviour
{
    [SerializeField] private AudioSource optionalFootstepAudioSource;
    [SerializeField] private AudioClip optionalFootstepClip;
    [SerializeField] private bool enableOptionalFootstepAudio;

    public int ReceivedFootstepCount { get; private set; }

    public void OnFootstep(AnimationEvent animationEvent)
    {
        ReceivedFootstepCount++;
        if (!enableOptionalFootstepAudio || optionalFootstepAudioSource == null || optionalFootstepClip == null)
            return;

        optionalFootstepAudioSource.PlayOneShot(optionalFootstepClip);
    }

    public void ConfigureInactive()
    {
        optionalFootstepAudioSource = null;
        optionalFootstepClip = null;
        enableOptionalFootstepAudio = false;
    }
}
