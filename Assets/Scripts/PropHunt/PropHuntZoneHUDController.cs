using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PropHuntZoneHUDController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PropHuntShrinkingZone shrinkingZone;
    [SerializeField] private HiderZoneStatusController localHiderZoneStatus;
    [SerializeField] private PropTransformSystem localHider;
    [SerializeField] private GameObject warningPanel;
    [SerializeField] private Image warningBackground;
    [SerializeField] private TextMeshProUGUI warningText;
    [SerializeField] private Image damageFlash;

    [Header("Damage Flash")]
    [SerializeField, Range(0f, 0.5f)] private float flashPeakAlpha = 0.22f;
    [SerializeField, Range(0.1f, 1f)] private float flashFadeDuration = 0.38f;

    private float _flashElapsed = float.PositiveInfinity;

    private void Awake()
    {
        ResolveReferences();
        ClearVisuals();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToStatus();
        ClearVisuals();
    }

    private void OnDisable()
    {
        UnsubscribeFromStatus();
        ClearVisuals();
    }

    private void Update()
    {
        UpdateWarning();
        UpdateDamageFlash();
    }

    public void Configure(
        PropHuntShrinkingZone configuredZone,
        HiderZoneStatusController configuredLocalStatus,
        PropTransformSystem configuredLocalHider,
        GameObject configuredWarningPanel,
        Image configuredWarningBackground,
        TextMeshProUGUI configuredWarningText,
        Image configuredDamageFlash)
    {
        if (Application.isPlaying)
        {
            UnsubscribeFromStatus();
        }

        shrinkingZone = configuredZone;
        localHiderZoneStatus = configuredLocalStatus;
        localHider = configuredLocalHider;
        warningPanel = configuredWarningPanel;
        warningBackground = configuredWarningBackground;
        warningText = configuredWarningText;
        damageFlash = configuredDamageFlash;
        ClearVisuals();

        if (Application.isPlaying)
        {
            SubscribeToStatus();
        }
    }

    public void ClearForElimination()
    {
        ClearVisuals();
    }

    private void SubscribeToStatus()
    {
        if (localHiderZoneStatus == null)
        {
            return;
        }

        localHiderZoneStatus.ZoneDamageApplied -= HandleZoneDamageApplied;
        localHiderZoneStatus.ZoneDamageApplied += HandleZoneDamageApplied;
    }

    private void UnsubscribeFromStatus()
    {
        if (localHiderZoneStatus != null)
        {
            localHiderZoneStatus.ZoneDamageApplied -= HandleZoneDamageApplied;
        }
    }

    private void HandleZoneDamageApplied(int damage)
    {
        _flashElapsed = 0f;
        SetFlashAlpha(flashPeakAlpha);
    }

    private void UpdateWarning()
    {
        if (localHider == null || localHider.playerRole != PlayerRole.Hider || localHider.IsEliminated)
        {
            SetWarning(false, string.Empty, false);
            return;
        }

        if (localHiderZoneStatus != null && localHiderZoneStatus.IsOutsideZone)
        {
            if (localHiderZoneStatus.IsZoneDamageActive)
            {
                SetWarning(true, "BẠN ĐANG Ở NGOÀI VÙNG AN TOÀN", true);
            }
            else
            {
                SetWarning(
                    true,
                    $"QUAY LẠI VÙNG AN TOÀN: {localHiderZoneStatus.GraceTimeRemaining:F1}s",
                    true);
            }

            return;
        }

        if (shrinkingZone != null && shrinkingZone.IsZoneActive && shrinkingZone.IsWarningPhase)
        {
            int seconds = Mathf.Clamp(Mathf.CeilToInt(shrinkingZone.WarningSecondsRemaining), 1, 10);
            SetWarning(true, $"VÒNG BO SẼ THU SAU {seconds} GIÂY", false);
            return;
        }

        SetWarning(false, string.Empty, false);
    }

    private void SetWarning(bool visible, string content, bool danger)
    {
        if (warningPanel != null && warningPanel.activeSelf != visible)
        {
            warningPanel.SetActive(visible);
        }

        if (warningText != null)
        {
            warningText.text = content;
            warningText.color = danger
                ? new Color32(255, 225, 225, 255)
                : Color.white;
        }

        if (warningBackground != null)
        {
            warningBackground.color = danger
                ? new Color(0.35f, 0.02f, 0.02f, 0.78f)
                : new Color(0f, 0f, 0f, 0.68f);
        }
    }

    private void UpdateDamageFlash()
    {
        if (damageFlash == null || float.IsPositiveInfinity(_flashElapsed))
        {
            return;
        }

        _flashElapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(_flashElapsed / flashFadeDuration);
        SetFlashAlpha(Mathf.Lerp(flashPeakAlpha, 0f, progress));
        if (progress >= 1f)
        {
            _flashElapsed = float.PositiveInfinity;
        }
    }

    private void SetFlashAlpha(float alpha)
    {
        if (damageFlash == null)
        {
            return;
        }

        Color color = damageFlash.color;
        color.a = Mathf.Clamp01(alpha);
        damageFlash.color = color;
    }

    private void ClearVisuals()
    {
        SetWarning(false, string.Empty, false);
        _flashElapsed = float.PositiveInfinity;
        SetFlashAlpha(0f);
    }

    private void ResolveReferences()
    {
        if (localHiderZoneStatus == null) localHiderZoneStatus = FindObjectOfType<HiderZoneStatusController>();
        if (localHider == null && localHiderZoneStatus != null)
        {
            localHider = localHiderZoneStatus.HiderTransformSystem;
        }

        if (shrinkingZone == null) shrinkingZone = FindObjectOfType<PropHuntShrinkingZone>();
    }
}
