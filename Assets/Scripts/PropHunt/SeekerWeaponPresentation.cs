using System;
using UnityEngine;
using UnityEngine.Serialization;

public class SeekerWeaponPresentation : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip shotAudioClip;
    [SerializeField] private AudioClip reloadAudioClip;
    [SerializeField, Range(0f, 1f)] private float shotVolume = 0.45f;
    [SerializeField, Range(0f, 1f)] private float reloadVolume = 0.55f;

    [Header("Muzzle")]
    [SerializeField] private ParticleSystem muzzleFlash;
    [SerializeField] private Light muzzleLight;
    [SerializeField, Min(0.01f)] private float muzzleLightDuration = 0.06f;

    [Header("Impact Pool")]
    [SerializeField] private GameObject impactPrefab;
    [SerializeField] private Transform impactPoolRoot;
    [SerializeField] private Camera impactCamera;
    [SerializeField, Range(6, 10)] private int impactPoolSize = 8;
    private const float ImpactScaleMultiplier = 2f;

    [FormerlySerializedAs("impactScale")]
    [SerializeField, Min(0.001f)] private float baseImpactScale = 0.192f;
    [SerializeField] private int impactLayer;
    [SerializeField] private bool showImpactDebugLogs;

    [Header("State")]
    [SerializeField] private SeekerWeaponEnergy energy;

    private GameObject[] impactPool = Array.Empty<GameObject>();
    private float[] impactMinimumAliveUntil = Array.Empty<float>();
    private float[] impactSpawnedAt = Array.Empty<float>();
    private bool[] impactReturnLogPending = Array.Empty<bool>();
    private int nextImpactIndex;
    private float muzzleLightUntil;

    public AudioSource AudioSource => audioSource;
    public AudioClip ShotAudioClip => shotAudioClip;
    public AudioClip ReloadAudioClip => reloadAudioClip;
    public ParticleSystem MuzzleFlash => muzzleFlash;
    public Light MuzzleLight => muzzleLight;
    public GameObject ImpactPrefab => impactPrefab;
    public int ImpactPoolSize => impactPoolSize;
    public Camera ImpactCamera => impactCamera;
    public int ImpactLayer => impactLayer;
    public float BaseImpactScale => baseImpactScale;
    public float ImpactScale => baseImpactScale * ImpactScaleMultiplier;
    public int ShotFeedbackCount { get; private set; }
    public int ReloadFeedbackCount { get; private set; }
    public int ImpactFeedbackCount { get; private set; }
    public int LastImpactParticleCount { get; private set; }
    public Vector3 LastImpactPosition { get; private set; }
    public int LastImpactLayer { get; private set; } = -1;
    public string LastImpactColliderPath { get; private set; } = string.Empty;
    public bool LastImpactCameraRenderedLayer { get; private set; }
    public Quaternion LastImpactRotation { get; private set; } = Quaternion.identity;
    public Vector3 LastImpactScale { get; private set; } = Vector3.zero;
    public int LastImpactParticleSystemCount { get; private set; }
    public int LastImpactSpriteRendererCount { get; private set; }
    public int LastImpactAnimatorCount { get; private set; }
    public int ActiveImpactCount
    {
        get
        {
            int count = 0;
            foreach (GameObject instance in impactPool)
                if (instance != null && instance.activeSelf) count++;
            return count;
        }
    }

    private void Awake()
    {
        BuildImpactPool();
        StopAllTransientEffects();
    }

    private void OnEnable()
    {
        BindEnergy();
    }

    private void OnDisable()
    {
        UnbindEnergy();
        StopAllTransientEffects();
    }

    private void Update()
    {
        if (muzzleLight != null && muzzleLight.enabled && Time.unscaledTime >= muzzleLightUntil)
        {
            muzzleLight.enabled = false;
        }

        for (int i = 0; i < impactPool.Length; i++)
        {
            GameObject instance = impactPool[i];
            if (instance == null || !instance.activeSelf || Time.unscaledTime < impactMinimumAliveUntil[i])
                continue;

            bool isAlive = false;
            foreach (ParticleSystem particle in instance.GetComponentsInChildren<ParticleSystem>(true))
                isAlive |= particle.IsAlive(true);
            if (!isAlive)
            {
                instance.SetActive(false);
                if (impactReturnLogPending[i])
                {
                    Debug.Log($"[SeekerImpact] return {instance.name} to pool after {Time.unscaledTime - impactSpawnedAt[i]:F3}s; IsAlive={isAlive}.");
                    impactReturnLogPending[i] = false;
                }
            }
        }
    }

    public void Configure(
        AudioSource configuredAudioSource,
        AudioClip configuredShotClip,
        AudioClip configuredReloadClip,
        ParticleSystem configuredMuzzleFlash,
        Light configuredMuzzleLight,
        GameObject configuredImpactPrefab,
        Transform configuredImpactPoolRoot,
        Camera configuredImpactCamera,
        SeekerWeaponEnergy configuredEnergy)
    {
        UnbindEnergy();
        audioSource = configuredAudioSource;
        shotAudioClip = configuredShotClip;
        reloadAudioClip = configuredReloadClip;
        muzzleFlash = configuredMuzzleFlash;
        muzzleLight = configuredMuzzleLight;
        impactPrefab = configuredImpactPrefab;
        impactPoolRoot = configuredImpactPoolRoot;
        impactCamera = configuredImpactCamera;
        impactPoolSize = 8;
        baseImpactScale = 0.192f;
        impactLayer = 0;
        energy = configuredEnergy;
        if (isActiveAndEnabled) BindEnergy();
    }

    public void PlayShotFeedback()
    {
        ShotFeedbackCount++;
        if (audioSource != null && shotAudioClip != null)
            audioSource.PlayOneShot(shotAudioClip, shotVolume);

        if (muzzleFlash != null)
        {
            muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            muzzleFlash.Play(true);
        }

        if (muzzleLight != null)
        {
            muzzleLight.enabled = true;
            muzzleLightUntil = Time.unscaledTime + muzzleLightDuration;
        }
    }

    public void SpawnImpact(RaycastHit hit)
    {
        if (hit.collider == null || impactPrefab == null) return;
        if (impactPool.Length == 0) BuildImpactPool();
        if (impactPool.Length == 0) return;

        GameObject instance = impactPool[nextImpactIndex];
        int index = nextImpactIndex;
        nextImpactIndex = (nextImpactIndex + 1) % impactPool.Length;
        if (instance == null) return;

        instance.SetActive(false);
        Vector3 impactPosition = hit.point + hit.normal * 0.02f;
        Vector3 cameraDirection = impactCamera != null
            ? impactCamera.transform.position - impactPosition
            : -hit.normal;
        if (cameraDirection.sqrMagnitude < 0.000001f) cameraDirection = -hit.normal;
        instance.transform.SetPositionAndRotation(
            impactPosition,
            Quaternion.LookRotation(cameraDirection.normalized, Vector3.up));
        instance.transform.localScale = Vector3.one * (baseImpactScale * ImpactScaleMultiplier);
        SetLayerRecursively(instance, impactLayer);
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
            renderer.sortingOrder = 50;
        }
        instance.SetActive(true);
        ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        float lifetime = 0.25f;
        int particleCount = 0;
        foreach (ParticleSystem particle in particles)
        {
            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particle.Play(true);
            particle.Simulate(0.02f, true, false, false);
            // ParticleSystem.Simulate pauses playback. Resume so the 8x8 impact
            // spritesheet advances instead of remaining on its nearly transparent first tile.
            particle.Play(true);
            particleCount += particle.particleCount;
            ParticleSystem.MainModule main = particle.main;
            lifetime = Mathf.Max(lifetime, main.duration + main.startLifetime.constantMax + 0.1f);
        }
        impactMinimumAliveUntil[index] = Time.unscaledTime + 0.05f;
        impactSpawnedAt[index] = Time.unscaledTime;
        impactReturnLogPending[index] |= showImpactDebugLogs;
        LastImpactParticleCount = particleCount;
        LastImpactPosition = impactPosition;
        LastImpactLayer = instance.layer;
        LastImpactColliderPath = GetHierarchyPath(hit.collider.transform);
        LastImpactCameraRenderedLayer = impactCamera != null &&
                                        (impactCamera.cullingMask & (1 << instance.layer)) != 0;
        LastImpactRotation = instance.transform.rotation;
        LastImpactScale = instance.transform.localScale;
        LastImpactParticleSystemCount = particles.Length;
        LastImpactSpriteRendererCount = instance.GetComponentsInChildren<SpriteRenderer>(true).Length;
        LastImpactAnimatorCount = instance.GetComponentsInChildren<Animator>(true).Length;
        ImpactFeedbackCount++;
        if (showImpactDebugLogs)
        {
#if UNITY_EDITOR
            string exactPrefabPath = UnityEditor.AssetDatabase.GetAssetPath(impactPrefab);
#else
            string exactPrefabPath = impactPrefab.name;
#endif
            Debug.Log(
                $"[SeekerImpact] collider={LastImpactColliderPath}, point={hit.point:F3}, normal={hit.normal:F3}, " +
                $"prefab={exactPrefabPath}, instance={instance.name}, active={instance.activeInHierarchy}, " +
                $"layer={instance.layer}, cameraRenders={LastImpactCameraRenderedLayer}, position={impactPosition:F3}, " +
                $"rotation={instance.transform.rotation.eulerAngles:F2}, localScale={instance.transform.localScale:F3}, " +
                $"particleSystems={LastImpactParticleSystemCount}, spriteRenderers={LastImpactSpriteRendererCount}, " +
                $"animators={LastImpactAnimatorCount}, particleCount={particleCount}, naturalLifetime={lifetime:F3}s.");
            foreach (ParticleSystem particle in particles)
            {
                ParticleSystem.MainModule particleMain = particle.main;
                ParticleSystem.TextureSheetAnimationModule textureSheet = particle.textureSheetAnimation;
                ParticleSystemRenderer particleRenderer = particle.GetComponent<ParticleSystemRenderer>();
                ParticleSystem.Particle[] sample = new ParticleSystem.Particle[Mathf.Max(1, particle.particleCount)];
                int sampled = particle.GetParticles(sample);
                string particleState = sampled > 0
                    ? $"samplePosition={sample[0].position:F3}, sampleSize={sample[0].GetCurrentSize(particle):F3}, " +
                      $"sampleColor={sample[0].GetCurrentColor(particle)}, remaining={sample[0].remainingLifetime:F3}"
                    : "sample=<none>";
                Material material = particleRenderer != null ? particleRenderer.sharedMaterial : null;
                Color tint = material != null && material.HasProperty("_TintColor")
                    ? material.GetColor("_TintColor")
                    : Color.clear;
                Texture texture = material != null && material.HasProperty("_MainTex")
                    ? material.GetTexture("_MainTex")
                    : null;
                Debug.Log(
                    $"[SeekerImpactVisualState] system={particle.name}, playing={particle.isPlaying}, emitting={particle.isEmitting}, " +
                    $"alive={particle.IsAlive(true)}, simulationSpace={particleMain.simulationSpace}, scalingMode={particleMain.scalingMode}, " +
                    $"startSize={particleMain.startSize.constantMax:F3}, startLifetime={particleMain.startLifetime.constantMax:F3}, " +
                    $"rendererEnabled={particleRenderer != null && particleRenderer.enabled}, renderMode={particleRenderer?.renderMode}, " +
                    $"alignment={particleRenderer?.alignment}, bounds={particleRenderer?.bounds}, material={material?.name}, " +
                    $"shader={material?.shader?.name}, supported={material?.shader != null && material.shader.isSupported}, " +
                    $"renderQueue={material?.renderQueue}, tint={tint}, texture={texture?.name}({texture?.width}x{texture?.height}), " +
                    $"textureSheet={textureSheet.enabled}, tiles={textureSheet.numTilesX}x{textureSheet.numTilesY}, {particleState}.");
            }
        }
    }

    public void StopAllTransientEffects()
    {
        if (muzzleFlash != null)
            muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (muzzleLight != null) muzzleLight.enabled = false;
        muzzleLightUntil = 0f;
        foreach (GameObject instance in impactPool)
        {
            if (instance != null) instance.SetActive(false);
        }
    }

    private void BuildImpactPool()
    {
        if (!Application.isPlaying || impactPrefab == null || impactPool.Length > 0) return;
        impactPool = new GameObject[impactPoolSize];
        impactMinimumAliveUntil = new float[impactPoolSize];
        impactSpawnedAt = new float[impactPoolSize];
        impactReturnLogPending = new bool[impactPoolSize];
        Transform parent = impactPoolRoot != null ? impactPoolRoot : transform;
        for (int i = 0; i < impactPoolSize; i++)
        {
            GameObject instance = Instantiate(impactPrefab, parent);
            instance.name = $"Impact02_Pooled_{i + 1:00}";
            instance.transform.localScale = Vector3.one * (baseImpactScale * ImpactScaleMultiplier);
            SetLayerRecursively(instance, impactLayer);
            RemovePhysicsAndScripts(instance);
            foreach (ParticleSystem particle in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particle.main;
                main.loop = false;
                main.playOnAwake = false;
                main.useUnscaledTime = true;
            }
            instance.SetActive(false);
            impactPool[i] = instance;
        }
    }

    private static void RemovePhysicsAndScripts(GameObject instance)
    {
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) Destroy(collider);
        foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
        foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(behaviour);
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;
    }

#if UNITY_EDITOR
    public void SetImpactDebugLoggingForValidation(bool enabled)
    {
        showImpactDebugLogs = enabled;
    }

    public void CompleteImpactParticlesForValidation()
    {
        foreach (GameObject instance in impactPool)
        {
            if (instance == null || !instance.activeSelf) continue;
            foreach (ParticleSystem particle in instance.GetComponentsInChildren<ParticleSystem>(true))
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
#endif

    private static string GetHierarchyPath(Transform item)
    {
        if (item == null) return "<missing>";
        string path = item.name;
        while (item.parent != null)
        {
            item = item.parent;
            path = item.name + "/" + path;
        }
        return path;
    }

    private void BindEnergy()
    {
        if (energy == null) return;
        energy.ReloadStateChanged -= OnReloadStateChanged;
        energy.ReloadStateChanged += OnReloadStateChanged;
    }

    private void UnbindEnergy()
    {
        if (energy != null) energy.ReloadStateChanged -= OnReloadStateChanged;
    }

    private void OnReloadStateChanged(bool isReloading)
    {
        if (!isReloading) return;
        ReloadFeedbackCount++;
        if (audioSource != null && reloadAudioClip != null)
            audioSource.PlayOneShot(reloadAudioClip, reloadVolume);
    }
}
