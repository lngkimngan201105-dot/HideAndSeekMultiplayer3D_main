using UnityEngine;

[DisallowMultipleComponent]
public sealed class PersistentMusicBootstrap : MonoBehaviour
{
    [SerializeField] private AudioClip musicClip;

    public AudioClip MusicClip => musicClip;

    private void Awake()
    {
        PersistentMusicManager.EnsureInstance(musicClip);
    }

    public void Configure(AudioClip clip)
    {
        musicClip = clip;
    }
}
