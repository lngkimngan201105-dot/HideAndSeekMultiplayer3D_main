using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum SeekerWeaponEnergyState
{
    Ready,
    Reloading
}

public class SeekerWeaponEnergy : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxCharges = 5;
    [SerializeField, Min(0.1f)] private float reloadDuration = 1.8f;
    [SerializeField] private int currentCharges = 5;
    [SerializeField] private SeekerWeaponEnergyState state = SeekerWeaponEnergyState.Ready;
    [SerializeField] private PropHuntTestRoleSelector roleSelector;
    [SerializeField] private SeekerRaycastWeapon weapon;
    [SerializeField] private bool acceptPlayerReloadInput = true;

    private float reloadElapsed;
    private float reloadProgress = 1f;
    private int reloadStartCharges = 5;
#if ENABLE_INPUT_SYSTEM
    private InputAction reloadAction;
#endif

    public int MaxCharges => maxCharges;
    public int CurrentCharges => currentCharges;
    public SeekerWeaponEnergyState State => state;
    public bool IsReloading => state == SeekerWeaponEnergyState.Reloading;
    public float ReloadDuration => reloadDuration;
    public float ReloadProgress => reloadProgress;
    public float NormalizedEnergy => IsReloading
        ? Mathf.Lerp((float)reloadStartCharges / maxCharges, 1f, reloadProgress)
        : (float)currentCharges / maxCharges;
    public bool HasActiveReload => state == SeekerWeaponEnergyState.Reloading;
    public int CompletedReloadCount { get; private set; }
#if ENABLE_INPUT_SYSTEM
    public bool ReloadInputEnabled => reloadAction != null && reloadAction.enabled;
#else
    public bool ReloadInputEnabled => false;
#endif

    public event Action<int, int> EnergyChanged;
    public event Action<float> ReloadProgressChanged;
    public event Action<bool> ReloadStateChanged;

    private void Awake()
    {
        ResolveReferences();
        currentCharges = Mathf.Clamp(currentCharges, 0, maxCharges);
        if (state != SeekerWeaponEnergyState.Reloading)
        {
            state = SeekerWeaponEnergyState.Ready;
            reloadProgress = currentCharges >= maxCharges ? 1f : 0f;
        }
        EnsureReloadAction();
    }

    private void OnEnable()
    {
        EnsureReloadAction();
#if ENABLE_INPUT_SYSTEM
        reloadAction?.Enable();
#endif
    }

    private void OnDisable()
    {
#if ENABLE_INPUT_SYSTEM
        reloadAction?.Disable();
#endif
        // A disabled component cannot advance its timer. Leave a recoverable Ready state
        // instead of preserving Reloading forever with no input available.
        CancelReload(true);
    }

    private void OnDestroy()
    {
#if ENABLE_INPUT_SYSTEM
        reloadAction?.Dispose();
        reloadAction = null;
#endif
    }

    private void Update()
    {
        TickReload(Time.unscaledDeltaTime);
        if (acceptPlayerReloadInput && WasReloadPressed()) TryStartReload();
    }

    public void Configure(PropHuntTestRoleSelector configuredRoleSelector, SeekerRaycastWeapon configuredWeapon)
    {
        roleSelector = configuredRoleSelector;
        weapon = configuredWeapon;
        maxCharges = 5;
        reloadDuration = 1.8f;
        currentCharges = Mathf.Clamp(currentCharges, 0, maxCharges);
        if (!IsReloading) reloadProgress = currentCharges >= maxCharges ? 1f : 0f;
    }

    public bool TryConsumeShot()
    {
        if (state != SeekerWeaponEnergyState.Ready || currentCharges <= 0) return false;

        currentCharges--;
        reloadProgress = currentCharges >= maxCharges ? 1f : 0f;
        EnergyChanged?.Invoke(currentCharges, maxCharges);
        return true;
    }

    public bool TryStartReload()
    {
        return TryStartReloadInternal(false);
    }

    public bool TryStartReloadFromAI()
    {
        return TryStartReloadInternal(true);
    }

    public void SetPlayerReloadInputEnabled(bool enabled)
    {
        acceptPlayerReloadInput = enabled;
    }

    private bool TryStartReloadInternal(bool aiRequest)
    {
        if (!CanStartReload(aiRequest)) return false;

        state = SeekerWeaponEnergyState.Reloading;
        reloadElapsed = 0f;
        reloadProgress = 0f;
        reloadStartCharges = currentCharges;
        ReloadStateChanged?.Invoke(true);
        ReloadProgressChanged?.Invoke(0f);
        return true;
    }

    public void ResetForRound()
    {
        bool wasReloading = IsReloading;
        state = SeekerWeaponEnergyState.Ready;
        reloadElapsed = 0f;
        reloadProgress = 1f;
        reloadStartCharges = maxCharges;
        currentCharges = maxCharges;
        if (wasReloading) ReloadStateChanged?.Invoke(false);
        ReloadProgressChanged?.Invoke(1f);
        EnergyChanged?.Invoke(currentCharges, maxCharges);
    }

    public void CancelReloadForRoundEnd()
    {
        CancelReload(true);
    }

#if UNITY_EDITOR
    public void AdvanceReloadForValidation(float seconds)
    {
        TickReload(Mathf.Max(0f, seconds));
    }
#endif

    private bool CanStartReload(bool aiRequest)
    {
        if (!enabled || !gameObject.activeInHierarchy || state != SeekerWeaponEnergyState.Ready ||
            currentCharges >= maxCharges)
            return false;
        if (!aiRequest &&
            (roleSelector == null || roleSelector.CurrentControlledRole != PropHuntTestRole.Seeker ||
             roleSelector.IsRoleSelectionPanelOpen))
            return false;
        if (aiRequest)
        {
            return weapon != null && weapon.IsWeaponActive;
        }

        return weapon != null && weapon.enabled &&
               weapon.gameObject.activeInHierarchy && weapon.IsWeaponActive;
    }

    private void TickReload(float deltaTime)
    {
        if (state != SeekerWeaponEnergyState.Reloading) return;

        reloadElapsed = Mathf.Min(reloadDuration, reloadElapsed + Mathf.Max(0f, deltaTime));
        reloadProgress = Mathf.Clamp01(reloadElapsed / reloadDuration);
        ReloadProgressChanged?.Invoke(reloadProgress);
        if (reloadElapsed >= reloadDuration) CompleteReload();
    }

    private void CompleteReload()
    {
        if (state != SeekerWeaponEnergyState.Reloading) return;

        currentCharges = maxCharges;
        state = SeekerWeaponEnergyState.Ready;
        reloadElapsed = reloadDuration;
        reloadProgress = 1f;
        reloadStartCharges = maxCharges;
        CompletedReloadCount++;
        EnergyChanged?.Invoke(currentCharges, maxCharges);
        ReloadProgressChanged?.Invoke(1f);
        ReloadStateChanged?.Invoke(false);
    }

    private void CancelReload(bool notify)
    {
        if (state != SeekerWeaponEnergyState.Reloading) return;
        state = SeekerWeaponEnergyState.Ready;
        reloadElapsed = 0f;
        reloadProgress = currentCharges >= maxCharges ? 1f : 0f;
        reloadStartCharges = currentCharges;
        if (notify) ReloadStateChanged?.Invoke(false);
        ReloadProgressChanged?.Invoke(reloadProgress);
    }

    private void ResolveReferences()
    {
        if (roleSelector == null) roleSelector = FindObjectOfType<PropHuntTestRoleSelector>(true);
        if (weapon == null) weapon = GetComponentInChildren<SeekerRaycastWeapon>(true);
    }

    private void EnsureReloadAction()
    {
#if ENABLE_INPUT_SYSTEM
        if (reloadAction != null) return;
        reloadAction = new InputAction("SeekerReload", InputActionType.Button, "<Keyboard>/r");
        if (isActiveAndEnabled) reloadAction.Enable();
#endif
    }

    private bool WasReloadPressed()
    {
#if ENABLE_INPUT_SYSTEM
        EnsureReloadAction();
        return reloadAction != null && reloadAction.WasPressedThisFrame();
#else
        return false;
#endif
    }

    private void OnValidate()
    {
        maxCharges = Mathf.Max(1, maxCharges);
        reloadDuration = Mathf.Max(0.1f, reloadDuration);
        currentCharges = Mathf.Clamp(currentCharges, 0, maxCharges);
        if (!IsReloading) reloadProgress = currentCharges >= maxCharges ? 1f : 0f;
    }
}
