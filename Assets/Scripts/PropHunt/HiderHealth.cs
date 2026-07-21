using System;
using UnityEngine;

public enum HiderDamageSource
{
    Unknown,
    Zone,
    SeekerWeapon,
    Environment,
    Debug
}

public class HiderHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField, Min(1)] private int maxHealth = 100;
    [SerializeField] private int currentHealth = 100;
    [SerializeField] private bool isEliminated;

    [Header("Gameplay References")]
    [SerializeField] private PropTransformSystem propTransformSystem;
    [SerializeField] private PropHuntRoundManager roundManager;

    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth;
    public bool IsDead => isEliminated;
    public bool IsAlive => !isEliminated;
    public bool IsEliminated => isEliminated;
    public HiderDamageSource LastDamageSource { get; private set; } = HiderDamageSource.Unknown;

    public event Action<int, int> HealthChanged;
    public event Action<int, HiderDamageSource> DamageTaken;
    public event Action<HiderHealth> Eliminated;
    public event Action<HiderHealth> RevivedOrReset;

    private void Awake()
    {
        ResolveMissingReferences();
        ClampSerializedValues();
    }

    private void OnValidate()
    {
        ClampSerializedValues();
    }

    public void Configure(PropTransformSystem transformSystem, PropHuntRoundManager configuredRoundManager)
    {
        propTransformSystem = transformSystem;
        roundManager = configuredRoundManager;
        ClampSerializedValues();
    }

    public void TakeDamage(int amount)
    {
        TakeDamage(amount, HiderDamageSource.Unknown);
    }

    public void TakeDamage(int amount, HiderDamageSource source)
    {
        if (amount <= 0 || IsEliminated)
        {
            return;
        }

        int previousHealth = currentHealth;
        LastDamageSource = source;
        SetHealthInternal(currentHealth - amount, false);
        int appliedDamage = previousHealth - currentHealth;
        if (appliedDamage > 0)
        {
            DamageTaken?.Invoke(appliedDamage, source);
        }
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || IsEliminated)
        {
            return;
        }

        SetHealthInternal(currentHealth + amount, false);
    }

    public void SetHealth(int value)
    {
        SetHealthInternal(value, value > 0);
    }

    public void ResetHealth()
    {
        ResetForRound();
    }

    public void ResetForRound()
    {
        LastDamageSource = HiderDamageSource.Unknown;
        bool wasEliminated = isEliminated;
        isEliminated = false;
        currentHealth = maxHealth;
        NotifyHealthChanged();
        RevivedOrReset?.Invoke(this);

        if (wasEliminated)
        {
            Debug.Log($"HiderHealth: '{name}' revived/reset to {currentHealth}/{maxHealth}.");
        }
    }

    private void SetHealthInternal(int value, bool allowRevive)
    {
        int clampedValue = Mathf.Clamp(value, 0, maxHealth);
        bool shouldRevive = allowRevive && clampedValue > 0 && isEliminated;
        if (currentHealth == clampedValue && !shouldRevive)
        {
            return;
        }

        currentHealth = clampedValue;
        if (shouldRevive)
        {
            isEliminated = false;
        }

        NotifyHealthChanged();

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            if (!isEliminated)
            {
                isEliminated = true;
                Eliminated?.Invoke(this);
            }

            return;
        }

        if (shouldRevive)
        {
            RevivedOrReset?.Invoke(this);
        }
    }

    private void ResolveMissingReferences()
    {
        if (propTransformSystem == null)
        {
            propTransformSystem = GetComponent<PropTransformSystem>();
        }

        if (roundManager == null)
        {
            roundManager = FindObjectOfType<PropHuntRoundManager>();
        }
    }

    private void ClampSerializedValues()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        isEliminated = currentHealth <= 0;
    }

    private void NotifyHealthChanged()
    {
        HealthChanged?.Invoke(currentHealth, maxHealth);
    }
}
