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
    [SerializeField] private HiderHealth hiderHealth;
    [SerializeField] private HiderRosterManager hiderRoster;
    [SerializeField] private HiderEliminationController eliminationController;
    [SerializeField] private HiderSpectatorController spectatorController;
    [SerializeField] private PropHuntTestRoleSelector testRoleSelector;

    [Header("Top Round Bar")]
    [SerializeField] private TextMeshProUGUI seekerCountText;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI hiderCountText;

    [Header("Context")]
    [SerializeField] private GameObject hiderContextPanel;
    [SerializeField] private TextMeshProUGUI hiderContextText;

    [Header("Ability Values")]
    [SerializeField] private TextMeshProUGUI cloneChargeText;
    [SerializeField] private TextMeshProUGUI randomChargeText;
    [SerializeField] private TextMeshProUGUI antiCampCountdownText;
    [SerializeField] private Image randomCooldownOverlay;
    [SerializeField] private Image cloneIcon;
    [SerializeField] private Image antiCampIcon;
    [SerializeField] private Image randomPropIcon;

    [Header("HUD Groups")]
    [SerializeField] private GameObject hiderAbilityPanel;
    [SerializeField] private CanvasGroup cloneCardGroup;
    [SerializeField] private CanvasGroup antiCampCardGroup;
    [SerializeField] private CanvasGroup randomCardGroup;

    [Header("Hider Health Bar")]
    [SerializeField] private GameObject hiderHealthBar;
    [SerializeField] private Image hiderHealthFill;
    [SerializeField] private TextMeshProUGUI hiderHealthText;

    [Header("Spectator Status")]
    [SerializeField] private GameObject spectatorStatusPanel;
    [SerializeField] private TextMeshProUGUI spectatorStatusText;

    private static readonly Color NormalIconColor = Color.white;
    private static readonly Color DisabledIconColor = new Color(0.38f, 0.38f, 0.38f, 0.72f);
    private static readonly Color HealthyColor = new Color32(52, 199, 89, 255);
    private static readonly Color WarningColor = new Color32(255, 204, 0, 255);
    private static readonly Color CriticalColor = new Color32(255, 69, 58, 255);

    private void Awake()
    {
        ResolveMissingReferences();
    }

    private void OnEnable()
    {
        ResolveMissingReferences();
        SubscribeToEvents();
        UpdateHealthBar();
        UpdateSpectatorPanel();
    }

    private void OnDisable()
    {
        UnsubscribeFromEvents();
    }

    private void Update()
    {
        UpdateTopRoundBar();
        UpdateContextPanel();
        UpdateAbilityPanel();
        UpdateSpectatorPanel();
    }

    public void Configure(
        PropHuntRoundManager configuredRoundManager,
        PropTransformSystem transformSystem,
        HiderAbilityController configuredAbilityController,
        HiderAntiCampSystem configuredAntiCampSystem,
        HiderHealth configuredHiderHealth,
        HiderRosterManager configuredRoster,
        HiderEliminationController configuredEliminationController,
        HiderSpectatorController configuredSpectatorController,
        PropHuntTestRoleSelector configuredRoleSelector,
        TextMeshProUGUI configuredSeekerCountText,
        TextMeshProUGUI configuredTimerText,
        TextMeshProUGUI configuredHiderCountText,
        GameObject configuredContextPanel,
        TextMeshProUGUI configuredContextText,
        TextMeshProUGUI configuredCloneChargeText,
        TextMeshProUGUI configuredRandomChargeText,
        TextMeshProUGUI configuredAntiCampCountdownText,
        Image configuredRandomCooldownOverlay,
        Image configuredCloneIcon,
        Image configuredAntiCampIcon,
        Image configuredRandomPropIcon,
        GameObject configuredAbilityPanel,
        CanvasGroup configuredCloneCardGroup,
        CanvasGroup configuredAntiCampCardGroup,
        CanvasGroup configuredRandomCardGroup,
        GameObject configuredHealthBar,
        Image configuredHealthFill,
        TextMeshProUGUI configuredHealthText,
        GameObject configuredSpectatorStatusPanel,
        TextMeshProUGUI configuredSpectatorStatusText)
    {
        if (Application.isPlaying)
        {
            UnsubscribeFromEvents();
        }

        roundManager = configuredRoundManager;
        propTransformSystem = transformSystem;
        abilityController = configuredAbilityController;
        antiCampSystem = configuredAntiCampSystem;
        hiderHealth = configuredHiderHealth;
        hiderRoster = configuredRoster;
        eliminationController = configuredEliminationController;
        spectatorController = configuredSpectatorController;
        testRoleSelector = configuredRoleSelector;
        seekerCountText = configuredSeekerCountText;
        timerText = configuredTimerText;
        hiderCountText = configuredHiderCountText;
        hiderContextPanel = configuredContextPanel;
        hiderContextText = configuredContextText;
        cloneChargeText = configuredCloneChargeText;
        randomChargeText = configuredRandomChargeText;
        antiCampCountdownText = configuredAntiCampCountdownText;
        randomCooldownOverlay = configuredRandomCooldownOverlay;
        cloneIcon = configuredCloneIcon;
        antiCampIcon = configuredAntiCampIcon;
        randomPropIcon = configuredRandomPropIcon;
        hiderAbilityPanel = configuredAbilityPanel;
        cloneCardGroup = configuredCloneCardGroup;
        antiCampCardGroup = configuredAntiCampCardGroup;
        randomCardGroup = configuredRandomCardGroup;
        hiderHealthBar = configuredHealthBar;
        hiderHealthFill = configuredHealthFill;
        hiderHealthText = configuredHealthText;
        spectatorStatusPanel = configuredSpectatorStatusPanel;
        spectatorStatusText = configuredSpectatorStatusText;

        if (Application.isPlaying)
        {
            SubscribeToEvents();
            UpdateTopRoundBar();
            UpdateContextPanel();
            UpdateAbilityPanel();
            UpdateHealthBar();
            UpdateSpectatorPanel();
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
        int aliveHiders = hiderRoster != null
            ? hiderRoster.AliveHiderCount
            : roundManager.AliveHiderCount;
        SetText(hiderCountText, $"ĐỒ VẬT {aliveHiders:00}");
        SetText(timerText, roundManager.CurrentState == PropHuntRoundState.Waiting
            ? FormatTime(roundManager.PreparationDuration)
            : FormatTime(roundManager.RemainingTime));
    }

    private void UpdateContextPanel()
    {
        bool isHider = propTransformSystem != null && propTransformSystem.playerRole == PlayerRole.Hider;
        bool hiderRoleActive = testRoleSelector == null || testRoleSelector.IsHiderRoleActive;
        if (!isHider || !hiderRoleActive || propTransformSystem.IsEliminated ||
            propTransformSystem.currentState == PlayerDisguiseState.Spectator)
        {
            SetContextVisible(false, string.Empty, 58f);
            return;
        }

        if (propTransformSystem.currentState == PlayerDisguiseState.Disguised)
        {
            if (propTransformSystem.IsGhostCameraActive)
            {
                SetContextVisible(
                    true,
                    "<color=#F4C430><b>Tab</b></color>  để quay lại Hider",
                    GetContextHeight(1)
                );
                return;
            }

            if (propTransformSystem.IsWallAttached)
            {
                SetContextVisible(
                    true,
                    "<color=#F4C430><b>WASD</b></color>     để leo trên tường\n" +
                    "<color=#F4C430><b>E</b></color>        để tách khỏi tường\n" +
                    "<color=#F4C430><b>Space</b></color>    để nhảy khỏi tường\n" +
                    "<color=#F4C430><b>← / →</b></color>    để xoay hình dạng\n" +
                    "<color=#F4C430><b>Tab</b></color>      để quan sát",
                    GetContextHeight(5)
                );
                return;
            }

            bool canAttachToWall = propTransformSystem.CanAttachToWall();
            string attachLine = canAttachToWall
                ? "<color=#F4C430><b>E</b></color>        để bám vào tường\n"
                : string.Empty;

            SetContextVisible(
                true,
                attachLine +
                "<color=#F4C430><b>R</b></color>        để trở lại người\n" +
                "<color=#F4C430><b>← / →</b></color>    để xoay hình dạng\n" +
                "<color=#F4C430><b>Tab</b></color>      để quan sát",
                GetContextHeight(canAttachToWall ? 4 : 3)
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

    private static float GetContextHeight(int lineCount)
    {
        return 28f + Mathf.Max(1, lineCount) * 28f;
    }

    private void UpdateAbilityPanel()
    {
        bool isHider = propTransformSystem != null && propTransformSystem.playerRole == PlayerRole.Hider;
        bool hiderRoleActive = testRoleSelector == null || testRoleSelector.IsHiderRoleActive;
        bool canShowAbilities = isHider && hiderRoleActive && hiderHealth != null && hiderHealth.IsAlive;
        if (hiderAbilityPanel != null && hiderAbilityPanel.activeSelf != canShowAbilities)
        {
            hiderAbilityPanel.SetActive(canShowAbilities);
        }

        if (!canShowAbilities || abilityController == null)
        {
            return;
        }

        SetText(cloneChargeText, $"x{abilityController.RemainingCloneCharges}");
        SetText(randomChargeText, $"x{abilityController.RemainingRandomPropCharges}");
        SetFill(randomCooldownOverlay, abilityController.RandomPropCooldownNormalized);

        bool cloneEmpty = abilityController.RemainingCloneCharges <= 0;
        bool randomEmpty = abilityController.RemainingRandomPropCharges <= 0;
        SetIconState(cloneIcon, cloneCardGroup, cloneEmpty, 1f);
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

        if (hiderHealth == null && propTransformSystem != null)
        {
            hiderHealth = propTransformSystem.GetComponent<HiderHealth>();
        }

        if (hiderRoster == null) hiderRoster = FindObjectOfType<HiderRosterManager>();
        if (eliminationController == null && propTransformSystem != null)
            eliminationController = propTransformSystem.GetComponent<HiderEliminationController>();
        if (spectatorController == null && propTransformSystem != null)
            spectatorController = propTransformSystem.GetComponent<HiderSpectatorController>();
        if (testRoleSelector == null) testRoleSelector = FindObjectOfType<PropHuntTestRoleSelector>();
    }

    private void SubscribeToEvents()
    {
        if (hiderHealth != null)
        {
            hiderHealth.HealthChanged -= HandleHealthChanged;
            hiderHealth.HealthChanged += HandleHealthChanged;
        }

        if (hiderRoster != null)
        {
            hiderRoster.AliveCountChanged -= HandleAliveCountChanged;
            hiderRoster.AliveCountChanged += HandleAliveCountChanged;
        }

        if (eliminationController != null)
        {
            eliminationController.EliminationStateChanged -= HandleEliminationStateChanged;
            eliminationController.EliminationStateChanged += HandleEliminationStateChanged;
        }

        if (spectatorController != null)
        {
            spectatorController.StatusTextChanged -= HandleSpectatorStatusChanged;
            spectatorController.StatusTextChanged += HandleSpectatorStatusChanged;
        }
    }

    private void UnsubscribeFromEvents()
    {
        if (hiderHealth != null)
        {
            hiderHealth.HealthChanged -= HandleHealthChanged;
        }

        if (hiderRoster != null)
            hiderRoster.AliveCountChanged -= HandleAliveCountChanged;
        if (eliminationController != null)
            eliminationController.EliminationStateChanged -= HandleEliminationStateChanged;
        if (spectatorController != null)
            spectatorController.StatusTextChanged -= HandleSpectatorStatusChanged;
    }

    private void HandleHealthChanged(int currentHealth, int maxHealth)
    {
        UpdateHealthBar(currentHealth, maxHealth);
    }

    private void HandleAliveCountChanged(int aliveCount, int totalCount)
    {
        SetText(hiderCountText, $"ĐỒ VẬT {aliveCount:00}");
    }

    private void HandleEliminationStateChanged(bool eliminated)
    {
        if (eliminated)
        {
            GetComponent<PropHuntZoneHUDController>()?.ClearForElimination();
        }

        UpdateContextPanel();
        UpdateAbilityPanel();
        UpdateSpectatorPanel();
    }

    private void HandleSpectatorStatusChanged(string status)
    {
        SetText(spectatorStatusText, status);
        UpdateSpectatorPanel();
    }

    private void UpdateSpectatorPanel()
    {
        bool hiderRoleActive = testRoleSelector == null || testRoleSelector.IsHiderRoleActive;
        bool eliminated = hiderRoleActive && hiderHealth != null && hiderHealth.IsEliminated;
        if (spectatorStatusPanel != null && spectatorStatusPanel.activeSelf != eliminated)
        {
            spectatorStatusPanel.SetActive(eliminated);
        }

        if (eliminated)
        {
            SetText(
                spectatorStatusText,
                spectatorController != null
                    ? spectatorController.CurrentStatusText
                    : "KHÔNG CÒN HIDER ĐỂ THEO DÕI");
        }
    }

    private void UpdateHealthBar()
    {
        if (hiderHealth == null)
        {
            return;
        }

        UpdateHealthBar(hiderHealth.CurrentHealth, hiderHealth.MaxHealth);
    }

    private void UpdateHealthBar(int currentHealth, int maxHealth)
    {
        int safeMaxHealth = Mathf.Max(1, maxHealth);
        int safeCurrentHealth = Mathf.Clamp(currentHealth, 0, safeMaxHealth);
        float normalizedHealth = safeCurrentHealth / (float)safeMaxHealth;

        SetFill(hiderHealthFill, normalizedHealth);
        if (hiderHealthFill != null)
        {
            hiderHealthFill.color = GetHealthColor(normalizedHealth);
        }

        SetText(hiderHealthText, $"{safeCurrentHealth} / {safeMaxHealth}");
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
