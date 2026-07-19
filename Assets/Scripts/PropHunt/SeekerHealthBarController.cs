using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SeekerHealthBarController : MonoBehaviour
{
    [SerializeField] private SeekerHealth seekerHealth;
    [SerializeField] private Image seekerHealthFill;
    [SerializeField] private TextMeshProUGUI seekerHealthText;

    private static readonly Color HealthyColor = new Color32(52, 199, 89, 255);
    private static readonly Color WarningColor = new Color32(255, 204, 0, 255);
    private static readonly Color CriticalColor = new Color32(255, 69, 58, 255);

    public SeekerHealth HealthSource => seekerHealth;
    public Image HealthFill => seekerHealthFill;
    public TextMeshProUGUI HealthText => seekerHealthText;
    public int HealthEventCallbackCount { get; private set; }

    private void Awake()
    {
        ResolveHealthReference();
    }

    private void OnEnable()
    {
        ResolveHealthReference();
        Subscribe();
        Refresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public void Configure(
        SeekerHealth configuredHealth,
        Image configuredFill,
        TextMeshProUGUI configuredText)
    {
        if (Application.isPlaying)
        {
            Unsubscribe();
        }

        seekerHealth = configuredHealth;
        seekerHealthFill = configuredFill;
        seekerHealthText = configuredText;

        if (Application.isPlaying && isActiveAndEnabled)
        {
            Subscribe();
        }

        Refresh();
    }

    public void Refresh()
    {
        int maximum = seekerHealth != null ? Mathf.Max(1, seekerHealth.MaxHealth) : 100;
        int current = seekerHealth != null ? Mathf.Clamp(seekerHealth.CurrentHealth, 0, maximum) : maximum;
        float normalized = current / (float)maximum;

        if (seekerHealthFill != null)
        {
            seekerHealthFill.fillAmount = normalized;
            seekerHealthFill.color = GetHealthColor(normalized);
        }

        if (seekerHealthText != null)
        {
            seekerHealthText.text = $"{current} / {maximum}";
        }
    }

    private void ResolveHealthReference()
    {
        if (seekerHealth == null)
        {
            seekerHealth = FindObjectOfType<SeekerHealth>(true);
        }
    }

    private void Subscribe()
    {
        if (seekerHealth == null)
        {
            return;
        }

        seekerHealth.HealthChanged -= HandleHealthChanged;
        seekerHealth.HealthChanged += HandleHealthChanged;
    }

    private void Unsubscribe()
    {
        if (seekerHealth != null)
        {
            seekerHealth.HealthChanged -= HandleHealthChanged;
        }
    }

    private void HandleHealthChanged(int currentHealth, int maxHealth)
    {
        HealthEventCallbackCount++;
        Refresh();
    }

    private static Color GetHealthColor(float normalizedHealth)
    {
        if (normalizedHealth <= 0.3f)
        {
            return CriticalColor;
        }

        if (normalizedHealth <= 0.6f)
        {
            return Color.Lerp(CriticalColor, WarningColor, Mathf.InverseLerp(0.3f, 0.6f, normalizedHealth));
        }

        return Color.Lerp(WarningColor, HealthyColor, Mathf.InverseLerp(0.6f, 1f, normalizedHealth));
    }
}
