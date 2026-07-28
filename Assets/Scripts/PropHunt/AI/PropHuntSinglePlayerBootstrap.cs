using System;
using UnityEngine;

[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public sealed class PropHuntSinglePlayerBootstrap : MonoBehaviour
{
    [SerializeField] private PropHuntTestRoleSelector roleSelector;
    [SerializeField] private PropTransformSystem hider;
    [SerializeField] private SeekerFirstPersonController seekerHumanController;
    [SerializeField] private CharacterController seekerCharacterController;
    [SerializeField] private SeekerRaycastWeapon seekerWeapon;
    [SerializeField] private SeekerWeaponEnergy seekerEnergy;
    [SerializeField] private Camera seekerCamera;
    [SerializeField] private GameObject seekerFpsVisual;
    [SerializeField] private GameObject seekerWorldVisual;
    [SerializeField] private GameObject seekerHud;

    private void Awake()
    {
        ResolveReferences();
        ApplySinglePlayerOwnership();
    }

    private void Start()
    {
        ApplySinglePlayerOwnership();
    }

    public void Configure(
        PropHuntTestRoleSelector configuredSelector,
        PropTransformSystem configuredHider,
        SeekerFirstPersonController configuredHumanController,
        CharacterController configuredCharacterController,
        SeekerRaycastWeapon configuredWeapon,
        SeekerWeaponEnergy configuredEnergy,
        Camera configuredSeekerCamera,
        GameObject configuredFpsVisual,
        GameObject configuredWorldVisual,
        GameObject configuredSeekerHud)
    {
        roleSelector = configuredSelector;
        hider = configuredHider;
        seekerHumanController = configuredHumanController;
        seekerCharacterController = configuredCharacterController;
        seekerWeapon = configuredWeapon;
        seekerEnergy = configuredEnergy;
        seekerCamera = configuredSeekerCamera;
        seekerFpsVisual = configuredFpsVisual;
        seekerWorldVisual = configuredWorldVisual;
        seekerHud = configuredSeekerHud;
    }

    public void ApplySinglePlayerOwnership()
    {
        ResolveReferences();
        if (hider == null || hider.cameraModeManager == null || roleSelector == null)
        {
            throw new InvalidOperationException(
                "Single-player Hider ownership/camera setup is incomplete.\n" +
                $"Hider={(hider != null ? GetHierarchyPath(hider.transform) : "<missing>")}\n" +
                $"PlayerCameraModeManager={(hider != null && hider.cameraModeManager != null ? "resolved" : "<missing>")}\n" +
                $"RoleSelector={(roleSelector != null ? GetHierarchyPath(roleSelector.transform) : "<missing>")}");
        }

        // Bind Hider ownership, movement, PlayerInput and both Hider camera targets
        // before the state-appropriate gameplay camera is activated.
        roleSelector.ConfigureSinglePlayerHiderMode(true);
        roleSelector.InitializePlayerAsHider();
        PlayerCameraModeManager cameraManager = hider.cameraModeManager;
        cameraManager.InitializeHiderTps(hider.transform);
        cameraManager.ConfigureSinglePlayerHiderCamera(true);

        seekerHumanController?.SetControlActive(false);
        if (seekerHumanController != null) seekerHumanController.enabled = false;
        if (seekerCharacterController != null) seekerCharacterController.enabled = false;
        seekerWeapon?.SetPlayerInputEnabled(false);
        seekerWeapon?.SetWeaponActive(true);
        seekerEnergy?.SetPlayerReloadInputEnabled(false);

        if (seekerCamera != null)
        {
            seekerCamera.gameObject.tag = "Untagged";
            AudioListener listener = seekerCamera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
            seekerCamera.gameObject.SetActive(false);
        }

        if (seekerFpsVisual != null) seekerFpsVisual.SetActive(false);
        if (seekerWorldVisual != null) seekerWorldVisual.SetActive(true);
        if (seekerHud != null) seekerHud.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        cameraManager.SetCameraSystemEnabled(true);
        cameraManager.ApplyResolvedHiderCameraMode();
        if (!cameraManager.EnsureGameplayCameraRendering("SinglePlayerBootstrap"))
        {
            throw new InvalidOperationException(
                "Single-player bootstrap could not activate a Hider camera.\n" +
                cameraManager.BuildCameraDiagnostic());
        }
    }

    private void ResolveReferences()
    {
        if (roleSelector == null) roleSelector = FindObjectOfType<PropHuntTestRoleSelector>(true);
        if (hider == null)
        {
            foreach (PropTransformSystem player in FindObjectsOfType<PropTransformSystem>(true))
            {
                if (player.playerRole == PlayerRole.Hider)
                {
                    hider = player;
                    break;
                }
            }
        }
        if (seekerHumanController == null) seekerHumanController = GetComponent<SeekerFirstPersonController>();
        if (seekerCharacterController == null) seekerCharacterController = GetComponent<CharacterController>();
        if (seekerWeapon == null) seekerWeapon = GetComponentInChildren<SeekerRaycastWeapon>(true);
        if (seekerEnergy == null) seekerEnergy = GetComponent<SeekerWeaponEnergy>();
        if (seekerCamera == null) seekerCamera = GetComponentInChildren<Camera>(true);
    }

    private static string GetHierarchyPath(Transform item)
    {
        if (item == null) return "<null>";
        string path = item.name;
        while (item.parent != null)
        {
            item = item.parent;
            path = item.name + "/" + path;
        }
        return path;
    }
}
