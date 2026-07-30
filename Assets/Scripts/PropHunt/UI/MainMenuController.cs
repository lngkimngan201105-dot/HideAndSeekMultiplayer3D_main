using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MainMenuController : MonoBehaviour
{
    [SerializeField] private string gameplaySceneName = "Map_v2";
    [SerializeField] private Button startButton;
    [SerializeField] private Button tutorialButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private GameObject tutorialPanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private CanvasGroup fadeOverlay;
    [SerializeField] private Button tutorialCloseButton;
    [SerializeField] private Button settingsCloseButton;
    private bool loading;

    private void Awake()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        WireButtons();
        if (tutorialPanel != null) tutorialPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (fadeOverlay != null)
        {
            fadeOverlay.alpha = 0f;
            fadeOverlay.blocksRaycasts = false;
        }
    }

    public void Configure(
        Button configuredStart,
        Button configuredTutorial,
        Button configuredSettings,
        Button configuredQuit,
        GameObject configuredTutorialPanel,
        GameObject configuredSettingsPanel,
        CanvasGroup configuredFade,
        Button configuredTutorialClose = null,
        Button configuredSettingsClose = null)
    {
        startButton = configuredStart;
        tutorialButton = configuredTutorial;
        settingsButton = configuredSettings;
        quitButton = configuredQuit;
        tutorialPanel = configuredTutorialPanel;
        settingsPanel = configuredSettingsPanel;
        fadeOverlay = configuredFade;
        tutorialCloseButton = configuredTutorialClose;
        settingsCloseButton = configuredSettingsClose;
        WireButtons();
    }

    public void StartGame()
    {
        if (loading) return;
        loading = true;
        SetButtonsInteractable(false);
        if (fadeOverlay != null)
        {
            fadeOverlay.alpha = 1f;
            fadeOverlay.blocksRaycasts = true;
        }
        SceneManager.LoadScene(gameplaySceneName);
    }

    public void ToggleTutorial()
    {
        if (tutorialPanel != null)
            tutorialPanel.SetActive(!tutorialPanel.activeSelf);
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    public void ToggleSettings()
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(!settingsPanel.activeSelf);
        if (tutorialPanel != null) tutorialPanel.SetActive(false);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void WireButtons()
    {
        Wire(startButton, StartGame);
        Wire(tutorialButton, ToggleTutorial);
        Wire(settingsButton, ToggleSettings);
        Wire(quitButton, QuitGame);
        Wire(tutorialCloseButton, ToggleTutorial);
        Wire(settingsCloseButton, ToggleSettings);
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (startButton != null) startButton.interactable = interactable;
        if (tutorialButton != null) tutorialButton.interactable = interactable;
        if (settingsButton != null) settingsButton.interactable = interactable;
        if (quitButton != null) quitButton.interactable = interactable;
    }

    private static void Wire(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;
        if (button.onClick.GetPersistentEventCount() > 0) return;
        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }
}
