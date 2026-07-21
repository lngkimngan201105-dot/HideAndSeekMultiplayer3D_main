using System;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum SeekerShotResult
{
    Miss,
    World,
    Clone,
    Hider
}

public class SeekerRaycastWeapon : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera shotCamera;
    [SerializeField] private PropHuntTestRoleSelector roleSelector;
    [SerializeField] private PropHuntRoundManager roundManager;
    [SerializeField] private TextMeshProUGUI crosshair;
    [SerializeField] private Renderer[] pulseRenderers = Array.Empty<Renderer>();
    [SerializeField] private SeekerWeaponEnergy weaponEnergy;
    [SerializeField] private SeekerWeaponPresentation weaponPresentation;

    [Header("Pulse Tagger Raycast")]
    [SerializeField, Min(0.1f)] private float range = 50f;
    [SerializeField, Min(1)] private int damage = 20;
    [SerializeField, Min(0f)] private float cooldown = 0.35f;
    [SerializeField] private LayerMask hitMask = Physics.DefaultRaycastLayers;
    [SerializeField] private bool allowDebugWeaponDuringPreparation = true;
    [SerializeField] private bool showWeaponDebugLogs;
    [SerializeField, Range(0.15f, 0.25f)] private float roleSelectionFireBlock = 0.2f;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private MaterialPropertyBlock pulseBlock;
    private float nextShotAt;
    private float fireBlockedUntil;
    private float feedbackUntil;
    private bool weaponActive;
    private bool requireFireRelease;
    private Color crosshairDefaultColor = Color.white;
    private Transform seekerOwnerRoot;
#if ENABLE_INPUT_SYSTEM
    private InputAction fireAction;
#endif

    public float Range => range;
    public int Damage => damage;
    public float Cooldown => cooldown;
    public LayerMask HitMask => hitMask;
    public bool IsWeaponActive => weaponActive;
    public bool AllowDebugWeaponDuringPreparation => allowDebugWeaponDuringPreparation;
    public SeekerWeaponEnergy WeaponEnergy => weaponEnergy;
    public SeekerWeaponPresentation WeaponPresentation => weaponPresentation;
    public float CooldownRemaining => Mathf.Max(0f, nextShotAt - Time.realtimeSinceStartup);
#if ENABLE_INPUT_SYSTEM
    public bool FireInputEnabled => fireAction != null && fireAction.enabled;
#else
    public bool FireInputEnabled => false;
#endif
    public int InputFireCount { get; private set; }
    public SeekerShotResult LastShotResult { get; private set; }
    public event Action<SeekerShotResult, Collider> ShotResolved;

    private void Awake()
    {
        EnsureFeedbackBlock();
        EnsureFireAction();
        ResolveReferences();
        if (crosshair != null) crosshairDefaultColor = crosshair.color;
        ClearFeedback();
    }

    private void OnEnable()
    {
        EnsureFireAction();
#if ENABLE_INPUT_SYSTEM
        fireAction?.Enable();
#endif
    }

    private void OnDisable()
    {
#if ENABLE_INPUT_SYSTEM
        fireAction?.Disable();
#endif
        ClearFeedback();
    }

    private void OnDestroy()
    {
#if ENABLE_INPUT_SYSTEM
        fireAction?.Dispose();
        fireAction = null;
#endif
    }

    private void Update()
    {
        UpdateFeedback();
        if (!weaponActive)
        {
            return;
        }

        if (requireFireRelease)
        {
            if (!IsFireHeld()) requireFireRelease = false;
            return;
        }

        if (!WasFirePressed() || !CanAcceptGameplayFire())
        {
            return;
        }

        if (TryFire()) InputFireCount++;
    }

    public void Configure(
        Camera configuredCamera,
        PropHuntTestRoleSelector configuredRoleSelector,
        PropHuntRoundManager configuredRoundManager,
        TextMeshProUGUI configuredCrosshair,
        Renderer[] configuredPulseRenderers,
        float configuredRange,
        int configuredDamage,
        float configuredCooldown,
        LayerMask configuredHitMask)
    {
        shotCamera = configuredCamera;
        roleSelector = configuredRoleSelector;
        roundManager = configuredRoundManager;
        crosshair = configuredCrosshair;
        pulseRenderers = configuredPulseRenderers ?? Array.Empty<Renderer>();
        range = Mathf.Max(0.1f, configuredRange);
        damage = Mathf.Max(1, configuredDamage);
        cooldown = Mathf.Max(0f, configuredCooldown);
        hitMask = configuredHitMask;
        allowDebugWeaponDuringPreparation = true;
        roleSelectionFireBlock = 0.2f;
        ResolveReferences();
        if (crosshair != null) crosshairDefaultColor = crosshair.color;
        ClearFeedback();
    }

    public void SetWeaponActive(bool active)
    {
        weaponActive = active;
        if (active)
        {
            fireBlockedUntil = Time.realtimeSinceStartup + roleSelectionFireBlock;
            requireFireRelease = IsFireHeld();
        }
        else
        {
            nextShotAt = 0f;
            fireBlockedUntil = 0f;
            requireFireRelease = false;
            ClearFeedback();
        }
    }

    public void ConfigureEnergyAndPresentation(
        SeekerWeaponEnergy configuredEnergy,
        SeekerWeaponPresentation configuredPresentation)
    {
        weaponEnergy = configuredEnergy;
        weaponPresentation = configuredPresentation;
        ResolveReferences();
    }

    public bool TryFire()
    {
        if (shotCamera == null)
        {
            return false;
        }

        Ray ray = shotCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        return TryFireRay(ray, false);
    }

    public bool TryFireRay(Ray ray, bool ignoreCooldownForValidation)
    {
        float now = Time.realtimeSinceStartup;
        if (!weaponActive || (!ignoreCooldownForValidation && now < nextShotAt))
        {
            return false;
        }

        if (weaponEnergy != null && !weaponEnergy.TryConsumeShot())
        {
            return false;
        }

        nextShotAt = now + cooldown;
        weaponPresentation?.PlayShotFeedback();
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            range,
            hitMask,
            QueryTriggerInteraction.Collide);
        if (hits.Length == 0)
        {
            ResolveShot(SeekerShotResult.Miss, null);
            return true;
        }

        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || IsOwnedBySeeker(hit.collider)) continue;

            HiderCloneInstance clone = hit.collider.GetComponentInParent<HiderCloneInstance>();
            if (clone != null)
            {
                weaponPresentation?.SpawnImpact(hit);
                clone.ReceiveHit(gameObject);
                ResolveShot(SeekerShotResult.Clone, hit.collider);
                return true;
            }

            HiderHealth hiderHealth = hit.collider.GetComponentInParent<HiderHealth>();
            if (hiderHealth != null)
            {
                weaponPresentation?.SpawnImpact(hit);
                if (hiderHealth.IsAlive)
                {
                    hiderHealth.TakeDamage(damage, HiderDamageSource.SeekerWeapon);
                }
                ResolveShot(SeekerShotResult.Hider, hit.collider);
                return true;
            }

            if (!hit.collider.isTrigger)
            {
                weaponPresentation?.SpawnImpact(hit);
                ResolveShot(SeekerShotResult.World, hit.collider);
                return true;
            }
        }

        ResolveShot(SeekerShotResult.Miss, null);
        return true;
    }

    private bool CanAcceptGameplayFire()
    {
        if (!enabled || !gameObject.activeInHierarchy || Time.realtimeSinceStartup < fireBlockedUntil)
            return false;
        if (shotCamera == null || !shotCamera.enabled || !shotCamera.gameObject.activeInHierarchy)
            return false;
        if (roleSelector == null || roleSelector.CurrentControlledRole != PropHuntTestRole.Seeker ||
            roleSelector.IsRoleSelectionPanelOpen)
            return false;
        if (!Application.isBatchMode &&
            (Cursor.lockState != CursorLockMode.Locked || Cursor.visible))
            return false;
        if (roundManager != null)
        {
            if (roundManager.CurrentState == PropHuntRoundState.Ended) return false;
            if (roundManager.CurrentState == PropHuntRoundState.Preparation &&
                !allowDebugWeaponDuringPreparation) return false;
        }
        return Time.realtimeSinceStartup >= nextShotAt;
    }

    private bool IsOwnedBySeeker(Collider collider)
    {
        return seekerOwnerRoot != null &&
               (collider.transform == seekerOwnerRoot || collider.transform.IsChildOf(seekerOwnerRoot));
    }

    private void ResolveShot(SeekerShotResult result, Collider hitCollider)
    {
        LastShotResult = result;
        Color feedbackColor;
        switch (result)
        {
            case SeekerShotResult.Clone:
                feedbackColor = new Color(0.2f, 0.95f, 1f, 1f);
                break;
            case SeekerShotResult.Hider:
                feedbackColor = Color.white;
                break;
            default:
                feedbackColor = new Color(0.1f, 0.65f, 0.8f, 1f);
                break;
        }
        ShowFeedback(feedbackColor);
        ShotResolved?.Invoke(result, hitCollider);

        if (showWeaponDebugLogs)
        {
            Debug.Log(
                $"Seeker Shot: Hit={(hitCollider != null ? hitCollider.name : "None")}, " +
                $"Type={result}, Damage={(result == SeekerShotResult.Hider ? damage : 0)}");
        }
    }

    private void ResolveReferences()
    {
        if (shotCamera == null) shotCamera = GetComponent<Camera>();
        if (roleSelector == null) roleSelector = FindObjectOfType<PropHuntTestRoleSelector>(true);
        if (roundManager == null) roundManager = FindObjectOfType<PropHuntRoundManager>(true);
        if (weaponEnergy == null) weaponEnergy = GetComponentInParent<SeekerWeaponEnergy>(true);
        if (weaponPresentation == null) weaponPresentation = GetComponentInParent<SeekerWeaponPresentation>(true);
        SeekerFirstPersonController owner = GetComponentInParent<SeekerFirstPersonController>(true);
        seekerOwnerRoot = owner != null ? owner.transform : transform.root;
    }

    private void ShowFeedback(Color color)
    {
        EnsureFeedbackBlock();
        feedbackUntil = Time.realtimeSinceStartup + 0.12f;
        if (crosshair != null) crosshair.color = color;
        pulseBlock.Clear();
        pulseBlock.SetColor(EmissionColorId, color * 2.4f);
        foreach (Renderer renderer in pulseRenderers)
        {
            if (renderer != null) renderer.SetPropertyBlock(pulseBlock);
        }
    }

    private void UpdateFeedback()
    {
        if (feedbackUntil > 0f && Time.realtimeSinceStartup >= feedbackUntil)
        {
            ClearFeedback();
        }
    }

    private void ClearFeedback()
    {
        EnsureFeedbackBlock();
        feedbackUntil = 0f;
        if (crosshair != null) crosshair.color = crosshairDefaultColor;
        pulseBlock.Clear();
        pulseBlock.SetColor(EmissionColorId, Color.black);
        foreach (Renderer renderer in pulseRenderers)
        {
            if (renderer != null) renderer.SetPropertyBlock(pulseBlock);
        }
    }

    private void EnsureFeedbackBlock()
    {
        if (pulseBlock == null) pulseBlock = new MaterialPropertyBlock();
    }

    private void EnsureFireAction()
    {
#if ENABLE_INPUT_SYSTEM
        if (fireAction != null) return;
        fireAction = new InputAction("SeekerFire", InputActionType.Button, "<Mouse>/leftButton");
        if (isActiveAndEnabled) fireAction.Enable();
#endif
    }

    private bool WasFirePressed()
    {
#if ENABLE_INPUT_SYSTEM
        EnsureFireAction();
        return (fireAction != null && fireAction.WasPressedThisFrame()) ||
               (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
#else
        return false;
#endif
    }

    private bool IsFireHeld()
    {
#if ENABLE_INPUT_SYSTEM
        EnsureFireAction();
        return (fireAction != null && fireAction.IsPressed()) ||
               (Mouse.current != null && Mouse.current.leftButton.isPressed);
#else
        return false;
#endif
    }
}
