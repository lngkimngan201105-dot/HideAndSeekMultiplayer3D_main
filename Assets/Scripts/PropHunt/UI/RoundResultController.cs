using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class RoundResultController : MonoBehaviour
{
    [SerializeField] private PropHuntRoundManager roundManager;
    [SerializeField] private SeekerTeamCoordinator seekerTeam;
    [SerializeField] private PropTransformSystem hider;
    [SerializeField] private HiderAbilityController abilities;
    [SerializeField] private GameObject gameplayHudRoot;
    [SerializeField] private GameObject resultCanvasRoot;
    [SerializeField] private GameObject winPanel;
    [SerializeField] private GameObject losePanel;
    [SerializeField] private TextMeshProUGUI winSubtitle;
    [SerializeField] private TextMeshProUGUI loseSubtitle;
    [SerializeField] private Button replayButton;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button loseReplayButton;
    [SerializeField] private Button loseMainMenuButton;
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    private bool transitionInProgress;
    private bool previewActive;

    private void Awake()
    {
        ResolveReferences();
        WireButtons();
        HideResult();
    }

    private void OnEnable()
    {
        ResolveReferences();
        WireButtons();
        if (roundManager != null)
        {
            roundManager.RoundEnded -= HandleRoundEnded;
            roundManager.RoundEnded += HandleRoundEnded;
            roundManager.RoundStarted -= HandleRoundStarted;
            roundManager.RoundStarted += HandleRoundStarted;
        }
    }

    private void OnDisable()
    {
        if (roundManager != null)
        {
            roundManager.RoundEnded -= HandleRoundEnded;
            roundManager.RoundStarted -= HandleRoundStarted;
        }
    }

    public void Configure(
        PropHuntRoundManager configuredRound,
        SeekerTeamCoordinator configuredTeam,
        PropTransformSystem configuredHider,
        HiderAbilityController configuredAbilities,
        GameObject configuredHud,
        GameObject configuredCanvas,
        GameObject configuredWin,
        GameObject configuredLose,
        TextMeshProUGUI configuredWinSubtitle,
        TextMeshProUGUI configuredLoseSubtitle,
        Button configuredReplay,
        Button configuredMenu,
        Button configuredLoseReplay,
        Button configuredLoseMenu)
    {
        roundManager = configuredRound;
        seekerTeam = configuredTeam;
        hider = configuredHider;
        abilities = configuredAbilities;
        gameplayHudRoot = configuredHud;
        resultCanvasRoot = configuredCanvas;
        winPanel = configuredWin;
        losePanel = configuredLose;
        winSubtitle = configuredWinSubtitle;
        loseSubtitle = configuredLoseSubtitle;
        replayButton = configuredReplay;
        mainMenuButton = configuredMenu;
        loseReplayButton = configuredLoseReplay;
        loseMainMenuButton = configuredLoseMenu;
        WireButtons();
    }

    public void Replay()
    {
        if (transitionInProgress) return;
        transitionInProgress = true;
        SetButtonsInteractable(false);
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ReturnToMainMenu()
    {
        if (transitionInProgress) return;
        transitionInProgress = true;
        SetButtonsInteractable(false);
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private void HandleRoundEnded(RoundOutcome outcome, RoundEndReason reason)
    {
        previewActive = false;
        transitionInProgress = false;
        SetButtonsInteractable(true);
        Time.timeScale = 1f;
        seekerTeam?.StopTeamForRoundEnd();
        if (hider != null)
        {
            hider.ForceExitGhostCamera();
            hider.SetGameplayInputLocked(true);
        }
        if (abilities != null) abilities.enabled = false;
        if (gameplayHudRoot != null) gameplayHudRoot.SetActive(false);
        if (resultCanvasRoot != null) resultCanvasRoot.SetActive(true);

        bool won = outcome == RoundOutcome.HiderWin;
        if (winPanel != null) winPanel.SetActive(won);
        if (losePanel != null) losePanel.SetActive(!won);
        if (winSubtitle != null)
        {
            winSubtitle.text = reason == RoundEndReason.AllSeekersEliminated
                ? "Bạn đã loại toàn bộ thợ săn"
                : "Bạn đã sống sót đến hết thời gian";
        }
        if (loseSubtitle != null)
            loseSubtitle.text = "Bạn đã bị thợ săn hạ gục";
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void HandleRoundStarted()
    {
        previewActive = false;
        transitionInProgress = false;
        SetButtonsInteractable(true);
        Time.timeScale = 1f;
        if (abilities != null) abilities.enabled = true;
        if (hider != null) hider.SetGameplayInputLocked(false);
        if (gameplayHudRoot != null) gameplayHudRoot.SetActive(true);
        HideResult();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void HideResult()
    {
        if (resultCanvasRoot != null) resultCanvasRoot.SetActive(false);
        if (winPanel != null) winPanel.SetActive(false);
        if (losePanel != null) losePanel.SetActive(false);
    }

    public void ShowHiderWinPreview()
    {
        ShowPreview(true);
    }

    public void ShowHiderLosePreview()
    {
        ShowPreview(false);
    }

    public void HideResultPreview()
    {
        if (!previewActive) return;
        previewActive = false;
        HideResult();
        if (gameplayHudRoot != null) gameplayHudRoot.SetActive(true);
        if (abilities != null) abilities.enabled = true;
        if (hider != null) hider.SetGameplayInputLocked(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void ShowPreview(bool hiderWon)
    {
        previewActive = true;
        transitionInProgress = false;
        SetButtonsInteractable(true);
        if (hider != null)
        {
            hider.ForceExitGhostCamera();
            hider.SetGameplayInputLocked(true);
        }
        if (abilities != null) abilities.enabled = false;
        if (gameplayHudRoot != null) gameplayHudRoot.SetActive(false);
        if (resultCanvasRoot != null) resultCanvasRoot.SetActive(true);
        if (winPanel != null) winPanel.SetActive(hiderWon);
        if (losePanel != null) losePanel.SetActive(!hiderWon);
        if (winSubtitle != null)
            winSubtitle.text = "Bạn đã sống sót đến hết thời gian";
        if (loseSubtitle != null)
            loseSubtitle.text = "Bạn đã bị thợ săn hạ gục";
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ResolveReferences()
    {
        if (roundManager == null)
            roundManager = FindObjectOfType<PropHuntRoundManager>(true);
        if (seekerTeam == null)
            seekerTeam = FindObjectOfType<SeekerTeamCoordinator>(true);
        if (hider == null)
        {
            foreach (PropTransformSystem item in
                     FindObjectsOfType<PropTransformSystem>(true))
            {
                if (item.playerRole != PlayerRole.Hider) continue;
                hider = item;
                break;
            }
        }
        if (abilities == null && hider != null)
            abilities = hider.GetComponent<HiderAbilityController>();
    }

    private void WireButtons()
    {
        Wire(replayButton, Replay);
        Wire(loseReplayButton, Replay);
        Wire(mainMenuButton, ReturnToMainMenu);
        Wire(loseMainMenuButton, ReturnToMainMenu);
    }

    private static void Wire(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;
        if (button.onClick.GetPersistentEventCount() > 0) return;
        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (replayButton != null) replayButton.interactable = interactable;
        if (mainMenuButton != null) mainMenuButton.interactable = interactable;
        if (loseReplayButton != null) loseReplayButton.interactable = interactable;
        if (loseMainMenuButton != null) loseMainMenuButton.interactable = interactable;
    }
}
