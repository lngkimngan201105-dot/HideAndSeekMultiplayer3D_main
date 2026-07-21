using System;
using UnityEngine;

public class SeekerHealth : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxHealth = 100;
    [SerializeField] private int currentHealth = 100;

    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth;
    public bool IsAlive => currentHealth > 0;

    public event Action<int, int> HealthChanged;

    private void Awake()
    {
        ClampSerializedValues();
    }

    private void OnValidate()
    {
        ClampSerializedValues();
    }

    public void ConfigureMaxHealth(int configuredMaxHealth)
    {
        maxHealth = Mathf.Max(1, configuredMaxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || !IsAlive)
        {
            return;
        }

        SetHealth(currentHealth - amount);
    }

    public void Heal(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        SetHealth(currentHealth + amount);
    }

    public void ResetForRound()
    {
        currentHealth = maxHealth;
        NotifyHealthChanged();
    }

    private void SetHealth(int value)
    {
        int clampedValue = Mathf.Clamp(value, 0, maxHealth);
        if (clampedValue == currentHealth)
        {
            return;
        }

        currentHealth = clampedValue;
        NotifyHealthChanged();
    }

    private void ClampSerializedValues()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
    }

    private void NotifyHealthChanged()
    {
        HealthChanged?.Invoke(currentHealth, maxHealth);
    }
}
