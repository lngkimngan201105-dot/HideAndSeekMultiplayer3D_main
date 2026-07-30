using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MainMenuSettingsController : MonoBehaviour
{
    public const string MasterVolumeKey = "MasterVolume";
    public const string FullscreenKey = "Fullscreen";
    public const string MouseSensitivityKey = "MouseSensitivity";

    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Slider mouseSensitivitySlider;
    [SerializeField] private TextMeshProUGUI volumeValueText;
    [SerializeField] private TextMeshProUGUI sensitivityValueText;

    private void Awake()
    {
        LoadAndApply();
        WireControls();
    }

    public void Configure(
        Slider configuredVolume,
        Toggle configuredFullscreen,
        Slider configuredSensitivity,
        TextMeshProUGUI configuredVolumeText,
        TextMeshProUGUI configuredSensitivityText)
    {
        masterVolumeSlider = configuredVolume;
        fullscreenToggle = configuredFullscreen;
        mouseSensitivitySlider = configuredSensitivity;
        volumeValueText = configuredVolumeText;
        sensitivityValueText = configuredSensitivityText;
        LoadAndApply();
        WireControls();
    }

    private void LoadAndApply()
    {
        float volume = PlayerPrefs.GetFloat(MasterVolumeKey, 1f);
        bool fullscreen = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) != 0;
        float sensitivity = PlayerPrefs.GetFloat(MouseSensitivityKey, 1f);
        if (masterVolumeSlider != null) masterVolumeSlider.SetValueWithoutNotify(volume);
        if (fullscreenToggle != null) fullscreenToggle.SetIsOnWithoutNotify(fullscreen);
        if (mouseSensitivitySlider != null)
            mouseSensitivitySlider.SetValueWithoutNotify(sensitivity);
        ApplyVolume(volume);
        ApplyFullscreen(fullscreen);
        ApplySensitivity(sensitivity);
    }

    private void WireControls()
    {
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.onValueChanged.RemoveListener(ApplyVolume);
            masterVolumeSlider.onValueChanged.AddListener(ApplyVolume);
        }
        if (fullscreenToggle != null)
        {
            fullscreenToggle.onValueChanged.RemoveListener(ApplyFullscreen);
            fullscreenToggle.onValueChanged.AddListener(ApplyFullscreen);
        }
        if (mouseSensitivitySlider != null)
        {
            mouseSensitivitySlider.onValueChanged.RemoveListener(ApplySensitivity);
            mouseSensitivitySlider.onValueChanged.AddListener(ApplySensitivity);
        }
    }

    private void ApplyVolume(float value)
    {
        value = Mathf.Clamp01(value);
        AudioListener.volume = value;
        PlayerPrefs.SetFloat(MasterVolumeKey, value);
        if (volumeValueText != null) volumeValueText.text = $"{Mathf.RoundToInt(value * 100f)}%";
        PlayerPrefs.Save();
    }

    private void ApplyFullscreen(bool value)
    {
        Screen.fullScreen = value;
        PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
        PlayerPrefs.Save();
    }

    private void ApplySensitivity(float value)
    {
        value = Mathf.Clamp(value, 0.2f, 3f);
        PlayerPrefs.SetFloat(MouseSensitivityKey, value);
        if (sensitivityValueText != null)
            sensitivityValueText.text = value.ToString("0.0");
        PlayerPrefs.Save();
    }
}
