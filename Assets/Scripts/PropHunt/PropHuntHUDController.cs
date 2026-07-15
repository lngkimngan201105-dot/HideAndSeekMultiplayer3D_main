using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PropHuntHUDController : MonoBehaviour
{
    [Header("Gameplay References")]
    [SerializeField] private PropHuntRoundManager roundManager;
    [SerializeField] private PropTransformSystem propTransformSystem;
    [SerializeField] private HiderAbilityController abilityController;
    [SerializeField] private HiderAntiCampSystem antiCampSystem;

    [Header("Top Round Bar")]
    [SerializeField] private TextMeshProUGUI seekerCountText;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI hiderCountText;

    [Header("Context")]
    [SerializeField] private GameObject hiderContextPanel;
    [SerializeField] private TextMeshProUGUI hiderContextText;

    [Header("Ability Values")]
    [SerializeField] private TextMeshProUGUI speedChargeText;
    [SerializeField] private TextMeshProUGUI randomChargeText;
    [SerializeField] private TextMeshProUGUI antiCampCountdownText;
    [SerializeField] private Image speedCooldownOverlay;
    [SerializeField] private Image randomCooldownOverlay;
    [SerializeField] private Image speedIcon;
    [SerializeField] private Image antiCampIcon;
    [SerializeField] private Image randomPropIcon;

    [Header("HUD Groups")]
    [SerializeField] private GameObject hiderAbilityPanel;
    [SerializeField] private CanvasGroup speedCardGroup;
    [SerializeField] private CanvasGroup antiCampCardGroup;
    [SerializeField] private CanvasGroup randomCardGroup;

    private static readonly Color NormalIconColor = Color.white;
    private static readonly Color DisabledIconColor = new Color(0.38f, 0.38f, 0.38f, 0.72f);

    private void Awake()
    {
        ResolveMissingReferences();
    }

    private void Update()
    {
        UpdateTopRoundBar();
        UpdateContextPanel();
        UpdateAbilityPanel();
    }

    public void Configure(
        PropHuntRoundManager configuredRoundManager,
        PropTransformSystem transformSystem,
        HiderAbilityController configuredAbilityController,
        HiderAntiCampSystem configuredAntiCampSystem,
        TextMeshProUGUI configuredSeekerCountText,
        TextMeshProUGUI configuredTimerText,
        TextMeshProUGUI configuredHiderCountText,
        GameObject configuredContextPanel,
        TextMeshProUGUI configuredContextText,
        TextMeshProUGUI configuredSpeedChargeText,
        TextMeshProUGUI configuredRandomChargeText,
        TextMeshProUGUI configuredAntiCampCountdownText,
        Image configuredSpeedCooldownOverlay,
        Image configuredRandomCooldownOverlay,
        Image configuredSpeedIcon,
        Image configuredAntiCampIcon,
        Image configuredRandomPropIcon,
        GameObject configuredAbilityPanel,
        CanvasGroup configuredSpeedCardGroup,
        CanvasGroup configuredAntiCampCardGroup,
        CanvasGroup configuredRandomCardGroup)
    {
        roundManager = configuredRoundManager;
        propTransformSystem = transformSystem;
        abilityController = configuredAbilityController;
        antiCampSystem = configuredAntiCampSystem;
        seekerCountText = configuredSeekerCountText;
        timerText = configuredTimerText;
        hiderCountText = configuredHiderCountText;
        hiderContextPanel = configuredContextPanel;
        hiderContextText = configuredContextText;
        speedChargeText = configuredSpeedChargeText;
        randomChargeText = configuredRandomChargeText;
        antiCampCountdownText = configuredAntiCampCountdownText;
        speedCooldownOverlay = configuredSpeedCooldownOverlay;
        randomCooldownOverlay = configuredRandomCooldownOverlay;
        speedIcon = configuredSpeedIcon;
        antiCampIcon = configuredAntiCampIcon;
        randomPropIcon = configuredRandomPropIcon;
        hiderAbilityPanel = configuredAbilityPanel;
        speedCardGroup = configuredSpeedCardGroup;
        antiCampCardGroup = configuredAntiCampCardGroup;
        randomCardGroup = configuredRandomCardGroup;

        if (Application.isPlaying)
        {
            UpdateTopRoundBar();
            UpdateContextPanel();
            UpdateAbilityPanel();
        }
    }

    private void UpdateTopRoundBar()
    {
        if (roundManager == null)
        {
            SetText(seekerCountText, "THỢ SĂN 02");
            SetText(timerText, "00:40");
            SetText(hiderCountText, "ĐỒ VẬT 05");
            return;
        }

        SetText(seekerCountText, $"THỢ SĂN {roundManager.SeekerCount:00}");
        SetText(hiderCountText, $"ĐỒ VẬT {roundManager.AliveHiderCount:00}");
        SetText(timerText, roundManager.CurrentState == PropHuntRoundState.Waiting
            ? FormatTime(roundManager.PreparationDuration)
            : FormatTime(roundManager.RemainingTime));
    }

    private void UpdateContextPanel()
    {
        bool isHider = propTransformSystem != null && propTransformSystem.playerRole == PlayerRole.Hider;
        if (!isHider || propTransformSystem.IsEliminated ||
            propTransformSystem.currentState == PlayerDisguiseState.Spectator)
        {
            SetContextVisible(false, string.Empty, 58f);
            return;
        }

        if (propTransformSystem.currentState == PlayerDisguiseState.Disguised)
        {
            SetContextVisible(
                true,
                "<color=#F4C430><b>R</b></color>    để trở lại người\n" +
                "<color=#F4C430><b>Tab</b></color>  để đổi góc nhìn",
                92f
            );
            return;
        }

        bool lookingAtValidProp = propTransformSystem.currentState == PlayerDisguiseState.Human &&
                                  propTransformSystem.TryGetLookedAtProp(out _);
        SetContextVisible(
            lookingAtValidProp,
            "<color=#F4C430><b>E</b></color>  để copy hình dạng",
            58f
        );
    }

    private void SetContextVisible(bool visible, string content, float height)
    {
        if (hiderContextPanel != null)
        {
            RectTransform rect = hiderContextPanel.transform as RectTransform;
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(290f, height);
            }

            if (hiderContextPanel.activeSelf != visible)
            {
                hiderContextPanel.SetActive(visible);
            }
        }

        SetText(hiderContextText, content);
    }

    private void UpdateAbilityPanel()
    {
        bool isHider = propTransformSystem != null && propTransformSystem.playerRole == PlayerRole.Hider;
        if (hiderAbilityPanel != null && hiderAbilityPanel.activeSelf != isHider)
        {
            hiderAbilityPanel.SetActive(isHider);
        }

        if (!isHider || abilityController == null)
        {
            return;
        }

        SetText(speedChargeText, $"x{abilityController.RemainingSpeedBoostCharges}");
        SetText(randomChargeText, $"x{abilityController.RemainingRandomPropCharges}");
        SetFill(speedCooldownOverlay, abilityController.SpeedCooldownNormalized);
        SetFill(randomCooldownOverlay, abilityController.RandomPropCooldownNormalized);

        bool speedEmpty = abilityController.RemainingSpeedBoostCharges <= 0;
        bool randomEmpty = abilityController.RemainingRandomPropCharges <= 0;
        SetIconState(speedIcon, speedCardGroup, speedEmpty, 1f);
        SetIconState(randomPropIcon, randomCardGroup, randomEmpty, 1f);

        bool countdown = antiCampSystem != null && antiCampSystem.IsCountdownActive;
        bool revealed = antiCampSystem != null && antiCampSystem.IsRevealed;
        SetText(antiCampCountdownText, countdown ? antiCampSystem.CountdownDisplay.ToString() : string.Empty);
        if (antiCampCountdownText != null)
        {
            antiCampCountdownText.gameObject.SetActive(countdown);
        }

        float antiCampAlpha = countdown
            ? Mathf.Lerp(0.58f, 1f, (Mathf.Sin(Time.unscaledTime * 10f) + 1f) * 0.5f)
            : 1f;
        if (revealed)
        {
            antiCampAlpha = 1f;
        }

        SetIconState(antiCampIcon, antiCampCardGroup, false, antiCampAlpha);
    }

    private void ResolveMissingReferences()
    {
        if (roundManager == null) roundManager = FindObjectOfType<PropHuntRoundManager>();
        if (propTransformSystem == null) propTransformSystem = FindObjectOfType<PropTransformSystem>();
        if (abilityController == null && propTransformSystem != null)
        {
            abilityController = propTransformSystem.GetComponent<HiderAbilityController>();
        }

        if (antiCampSystem == null && propTransformSystem != null)
        {
            antiCampSystem = propTransformSystem.GetComponent<HiderAntiCampSystem>();
        }
    }

    private static string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int minutes = Mathf.FloorToInt(seconds / 60f);
        int remainingSeconds = Mathf.FloorToInt(seconds % 60f);
        return $"{minutes:00}:{remainingSeconds:00}";
    }

    private static void SetText(TextMeshProUGUI target, string value)
    {
        if (target != null) target.text = value;
    }

    private static void SetFill(Image target, float value)
    {
        if (target != null) target.fillAmount = Mathf.Clamp01(value);
    }

    private static void SetIconState(Image icon, CanvasGroup group, bool disabled, float alpha)
    {
        if (icon != null)
        {
            icon.color = disabled ? DisabledIconColor : NormalIconColor;
        }

        if (group != null)
        {
            group.alpha = disabled ? DisabledIconColor.a : alpha;
            group.blocksRaycasts = false;
            group.interactable = false;
        }
    }
}
