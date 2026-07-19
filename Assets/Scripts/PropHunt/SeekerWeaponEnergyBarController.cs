using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SeekerWeaponEnergyBarController : MonoBehaviour
{
    [SerializeField] private SeekerWeaponEnergy energy;
    [SerializeField] private Image[] segmentFills = Array.Empty<Image>();
    [SerializeField] private TextMeshProUGUI inactiveFallbackText;

    public SeekerWeaponEnergy Energy => energy;
    public Image[] SegmentFills => segmentFills;
    public TextMeshProUGUI InactiveFallbackText => inactiveFallbackText;

    private void OnEnable()
    {
        Bind();
        Refresh();
    }

    private void OnDisable()
    {
        Unbind();
    }

    public void Configure(
        SeekerWeaponEnergy configuredEnergy,
        Image[] configuredSegmentFills,
        TextMeshProUGUI configuredInactiveFallbackText)
    {
        Unbind();
        energy = configuredEnergy;
        segmentFills = configuredSegmentFills ?? Array.Empty<Image>();
        inactiveFallbackText = configuredInactiveFallbackText;
        if (inactiveFallbackText != null)
        {
            inactiveFallbackText.text = string.Empty;
            inactiveFallbackText.gameObject.SetActive(false);
        }
        if (isActiveAndEnabled) Bind();
        Refresh();
    }

    public void Refresh()
    {
        if (energy == null) return;
        float totalFilledSegments = energy.NormalizedEnergy * energy.MaxCharges;
        for (int index = 0; index < segmentFills.Length; index++)
        {
            if (segmentFills[index] != null)
                segmentFills[index].fillAmount = Mathf.Clamp01(totalFilledSegments - index);
        }
    }

    private void Bind()
    {
        if (energy == null) return;
        energy.EnergyChanged -= OnEnergyChanged;
        energy.ReloadProgressChanged -= OnReloadProgressChanged;
        energy.ReloadStateChanged -= OnReloadStateChanged;
        energy.EnergyChanged += OnEnergyChanged;
        energy.ReloadProgressChanged += OnReloadProgressChanged;
        energy.ReloadStateChanged += OnReloadStateChanged;
    }

    private void Unbind()
    {
        if (energy == null) return;
        energy.EnergyChanged -= OnEnergyChanged;
        energy.ReloadProgressChanged -= OnReloadProgressChanged;
        energy.ReloadStateChanged -= OnReloadStateChanged;
    }

    private void OnEnergyChanged(int current, int maximum) => Refresh();
    private void OnReloadProgressChanged(float progress) => Refresh();
    private void OnReloadStateChanged(bool reloading) => Refresh();
}
