using System;
using StarterAssets;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum PropHuntTestRole
{
    None,
    Hider,
    Seeker
}

[DefaultExecutionOrder(-300)]
public class PropHuntTestRoleSelector : MonoBehaviour
{
    [Header("Single Player")]
    [SerializeField] private bool singlePlayerHiderMode = true;

    [Header("Role UI")]
    [SerializeField] private GameObject roleSelectionPanel;
    [SerializeField] private Button hiderRoleButton;
    [SerializeField] private Button seekerRoleButton;
    [SerializeField] private GameObject seekerHudRoot;
    [SerializeField] private GameObject hiderHealthBar;
    [SerializeField] private GameObject seekerHealthBar;

    [Header("Hider")]
    [SerializeField] private PropTransformSystem hiderTransformSystem;
    [SerializeField] private HiderHealth hiderHealth;
    [SerializeField] private HiderEliminationController hiderElimination;
    [SerializeField] private HiderAbilityController hiderAbilities;
    [SerializeField] private PropTarget hiderTestPropDefinition;
    [SerializeField] private Transform hiderTestSpawnPoint;

    [Header("Seeker")]
    [SerializeField] private SeekerFirstPersonController seekerController;
    [SerializeField] private SeekerRaycastWeapon seekerWeapon;
    [SerializeField] private SeekerHealth seekerHealth;
    [SerializeField] private SeekerWeaponEnergy seekerWeaponEnergy;
    [SerializeField] private Camera seekerCamera;
    [SerializeField] private Transform seekerSpawnPoint;

    private bool initialSpawnCompleted;

    public PropHuntTestRole CurrentRole { get; private set; } = PropHuntTestRole.None;
    public bool IsHiderRoleActive => CurrentRole == PropHuntTestRole.Hider;
    public bool IsSeekerRoleActive => CurrentRole == PropHuntTestRole.Seeker;
    public GameObject RoleSelectionPanel => roleSelectionPanel;
    public Button HiderRoleButton => hiderRoleButton;
    public Button SeekerRoleButton => seekerRoleButton;
    public Camera SeekerCamera => seekerCamera;
    public GameObject SeekerHudRoot => seekerHudRoot;
    public GameObject HiderHealthBar => hiderHealthBar;
    public GameObject SeekerHealthBar => seekerHealthBar;
    public SeekerHealth SeekerHealth => seekerHealth;
    public SeekerWeaponEnergy SeekerWeaponEnergy => seekerWeaponEnergy;
    public Transform HiderTestSpawnPoint => hiderTestSpawnPoint;
    public Transform SeekerSpawnPoint => seekerSpawnPoint;
    public bool InitialSpawnCompleted => initialSpawnCompleted;
    public bool IsRoleSelectionPanelOpen => roleSelectionPanel != null && roleSelectionPanel.activeInHierarchy;
    public PropHuntTestRole CurrentControlledRole => CurrentRole;
    public bool SinglePlayerHiderMode => singlePlayerHiderMode;
    public event Action<PropHuntTestRole> RoleChanged;

    private void Awake()
    {
        initialSpawnCompleted = false;
        ResolveReferences();
        BindButtons();
        if (singlePlayerHiderMode)
        {
            SetPanelVisible(false);
            hiderTransformSystem?.SetGameplayInputLocked(true);
            SetSeekerGameplayActive(false);
            ApplyHealthBarVisibility(PropHuntTestRole.None);
            SetCameraActive(seekerCamera, false);
        }
        else
        {
            ShowRoleSelection();
        }
    }

    private void Start()
    {
        if (singlePlayerHiderMode)
        {
            SelectInitialHiderRole();
        }
    }

    private void OnEnable()
    {
        BindButtons();
    }

    private void OnDisable()
    {
        UnbindButtons();
    }

    private void Update()
    {
        if (singlePlayerHiderMode)
        {
            return;
        }

        if (IsRoleSelectionPanelOpen)
        {
            CompleteInitialSpawnOnce();
            SetCursor(false);
            LockAllGameplayBehindPanel();
            return;
        }

        if (WasF1Pressed())
        {
            PossessHiderForDebug();
        }
        else if (WasF2Pressed())
        {
            PossessSeekerForDebug();
        }
    }

    public void Configure(
        GameObject configuredPanel,
        Button configuredHiderButton,
        Button configuredSeekerButton,
        GameObject configuredSeekerHudRoot,
        PropTransformSystem configuredHiderTransform,
        HiderHealth configuredHiderHealth,
        HiderEliminationController configuredHiderElimination,
        HiderAbilityController configuredHiderAbilities,
        PropTarget configuredHiderTestProp,
        Transform configuredHiderSpawnPoint,
        SeekerFirstPersonController configuredSeekerController,
        SeekerRaycastWeapon configuredSeekerWeapon,
        SeekerHealth configuredSeekerHealth,
        Camera configuredSeekerCamera,
        Transform configuredSeekerSpawnPoint)
    {
        UnbindButtons();
        roleSelectionPanel = configuredPanel;
        hiderRoleButton = configuredHiderButton;
        seekerRoleButton = configuredSeekerButton;
        seekerHudRoot = configuredSeekerHudRoot;
        hiderTransformSystem = configuredHiderTransform;
        hiderHealth = configuredHiderHealth;
        hiderElimination = configuredHiderElimination;
        hiderAbilities = configuredHiderAbilities;
        hiderTestPropDefinition = configuredHiderTestProp;
        hiderTestSpawnPoint = configuredHiderSpawnPoint;
        seekerController = configuredSeekerController;
        seekerWeapon = configuredSeekerWeapon;
        seekerHealth = configuredSeekerHealth;
        seekerCamera = configuredSeekerCamera;
        seekerSpawnPoint = configuredSeekerSpawnPoint;
        BindButtons();
    }

    public void ConfigureHealthBars(GameObject configuredHiderHealthBar, GameObject configuredSeekerHealthBar)
    {
        hiderHealthBar = configuredHiderHealthBar;
        seekerHealthBar = configuredSeekerHealthBar;
        ApplyHealthBarVisibility(CurrentRole);
    }

    public void ConfigureWeaponEnergy(SeekerWeaponEnergy configuredEnergy)
    {
        seekerWeaponEnergy = configuredEnergy;
    }

    public void ConfigureSinglePlayerHiderMode(bool enabled)
    {
        singlePlayerHiderMode = enabled;
        seekerWeapon?.SetPlayerInputEnabled(!enabled);
        seekerWeaponEnergy?.SetPlayerReloadInputEnabled(!enabled);
        if (!enabled)
        {
            hiderTransformSystem?.cameraModeManager?.ConfigureSinglePlayerHiderCamera(false);
            return;
        }

        SetPanelVisible(false);
        if (Application.isPlaying)
        {
            InitializePlayerAsHider();
        }
        else
        {
            hiderTransformSystem?.cameraModeManager?.ConfigureSinglePlayerHiderCamera(true);
        }
    }

    public void ShowRoleSelection()
    {
        if (singlePlayerHiderMode)
        {
            SelectInitialHiderRole();
            return;
        }

        CurrentRole = PropHuntTestRole.None;
        LockAllGameplayBehindPanel();
        SetPanelVisible(true);
        SetCursor(false);
        ApplyHealthBarVisibility(CurrentRole);
        RoleChanged?.Invoke(CurrentRole);
    }

    public void SelectInitialHiderRole()
    {
        CompleteInitialSpawnOnce();
        PossessHiderForDebug();
    }

    public void InitializePlayerAsHider()
    {
        ResolveReferences();
        CompleteInitialSpawnOnce();
        PossessHiderForDebug();
    }

    public void SelectInitialSeekerRole()
    {
        CompleteInitialSpawnOnce();
        PossessSeekerForDebug();
    }

    public void PossessHiderForDebug()
    {
        if (!initialSpawnCompleted) return;

        RestoreGameplayAfterRoleSelection();
        CurrentRole = PropHuntTestRole.Hider;
        SetSeekerGameplayActive(false);
        hiderElimination?.SetSpectatorSuppressed(false);
        bool hiderAlive = hiderHealth == null || hiderHealth.IsAlive;
        EnsureHiderRuntimeInput();
        hiderTransformSystem?.SetGameplayInputLocked(!hiderAlive);
        if (hiderTransformSystem != null && hiderTransformSystem.cameraModeManager != null)
        {
            PlayerCameraModeManager manager = hiderTransformSystem.cameraModeManager;
            manager.InitializeHiderTps(hiderTransformSystem.transform);
            manager.ConfigureSinglePlayerHiderCamera(singlePlayerHiderMode);
            manager.SetCameraSystemEnabled(true);
            if (hiderAlive)
            {
                manager.SetMode(hiderTransformSystem.IsDisguised
                    ? PlayerCameraMode.PropTPS
                    : PlayerCameraMode.HumanFPS);
            }
            else
            {
                HiderSpectatorController spectator =
                    hiderTransformSystem.GetComponent<HiderSpectatorController>();
                spectator?.EnterSpectator(hiderTransformSystem.transform.position);
            }

            manager.EnsureGameplayCameraRendering("PossessHiderForDebug");
        }
        SetCameraActive(seekerCamera, false);

        SetPanelVisible(false);
        SetCursor(true);
        ApplyHealthBarVisibility(CurrentRole);
        RoleChanged?.Invoke(CurrentRole);
    }

    private void EnsureHiderRuntimeInput()
    {
        if (hiderTransformSystem == null)
        {
            return;
        }

        GameObject hiderObject = hiderTransformSystem.gameObject;
        StarterAssetsInputs inputs = hiderObject.GetComponent<StarterAssetsInputs>();
        if (inputs != null)
        {
            inputs.enabled = true;
            inputs.cursorInputForLook = true;
            inputs.cursorLocked = true;
        }

        FirstPersonController movement =
            hiderObject.GetComponent<FirstPersonController>();
        CharacterController character =
            hiderObject.GetComponent<CharacterController>();
        if (character != null) character.enabled = true;
        if (movement != null) movement.enabled = true;

#if ENABLE_INPUT_SYSTEM
        PlayerInput playerInput = hiderObject.GetComponent<PlayerInput>();
        if (playerInput != null)
        {
            playerInput.enabled = true;
            InputActionMap playerMap =
                playerInput.actions != null
                    ? playerInput.actions.FindActionMap("Player", false)
                    : null;
            // PlayerInput.OnEnable owns its first activation. The bootstrap can run
            // before that lifecycle callback, so only switch maps once input is live.
            if (playerInput.inputIsActive &&
                playerMap != null &&
                playerInput.currentActionMap != playerMap)
            {
                playerInput.SwitchCurrentActionMap(playerMap.name);
            }
            if (playerInput.inputIsActive)
            {
                playerMap?.Enable();
            }
        }
#endif
    }

    public void PossessSeekerForDebug()
    {
        if (singlePlayerHiderMode)
        {
            PossessHiderForDebug();
            return;
        }

        if (!initialSpawnCompleted) return;

        if (hiderTransformSystem != null && hiderTransformSystem.IsGhostCameraActive)
        {
            hiderTransformSystem.ForceExitGhostCamera();
        }

        RestoreGameplayAfterRoleSelection();
        CurrentRole = PropHuntTestRole.Seeker;
        SetPanelVisible(false);
        SetCursor(true);
        hiderTransformSystem?.SetGameplayInputLocked(true);
        hiderElimination?.SetSpectatorSuppressed(true);
        SetCameraActive(seekerCamera, true);
        hiderTransformSystem?.cameraModeManager?.SetCameraSystemEnabled(false);
        SetSeekerGameplayActive(true);
        ApplyHealthBarVisibility(CurrentRole);

        RoleChanged?.Invoke(CurrentRole);
    }

    public void SelectHider()
    {
        if (initialSpawnCompleted) PossessHiderForDebug();
        else SelectInitialHiderRole();
    }

    public void SelectSeeker()
    {
        if (initialSpawnCompleted) PossessSeekerForDebug();
        else SelectInitialSeekerRole();
    }

    private void CompleteInitialSpawnOnce()
    {
        if (initialSpawnCompleted) return;

        if (!singlePlayerHiderMode)
        {
            LockAllGameplayBehindPanel();
        }
        hiderHealth?.ResetForRound();
        seekerHealth?.ResetForRound();
        seekerWeaponEnergy?.ResetForRound();
        hiderTransformSystem?.ResetToHumanForRoleSelection();
        hiderAbilities?.ResetAbilitiesForRound();
        TeleportHiderToSpawn();
        seekerController?.TeleportTo(seekerSpawnPoint);
        if (!singlePlayerHiderMode &&
            hiderTransformSystem != null && hiderTestPropDefinition != null)
        {
            hiderTransformSystem.TryBecomePropForTesting(hiderTestPropDefinition);
        }

        initialSpawnCompleted = true;
        if (!singlePlayerHiderMode)
        {
            ActivateHiderPreviewCamera();
        }
    }

    private void LockAllGameplayBehindPanel()
    {
        hiderTransformSystem?.SetGameplayInputLocked(true);
        SetSeekerGameplayActive(false);
        ApplyHealthBarVisibility(PropHuntTestRole.None);
        ActivateHiderPreviewCamera();
        SetCameraActive(seekerCamera, false);
    }

    private static void RestoreGameplayAfterRoleSelection()
    {
        // The main menu pauses the game. Ensure a role can never inherit that
        // paused state, including after an in-editor script/domain reload.
        Map2RuntimeBootstrap.CloseRuntimeMenusForGameplay();
        Time.timeScale = 1f;
    }

    private void SetSeekerGameplayActive(bool active)
    {
        seekerController?.SetControlActive(active);
        seekerWeapon?.SetWeaponActive(active || singlePlayerHiderMode);
        seekerWeapon?.SetPlayerInputEnabled(active && !singlePlayerHiderMode);
        seekerWeaponEnergy?.SetPlayerReloadInputEnabled(active && !singlePlayerHiderMode);
        if (seekerHudRoot != null) seekerHudRoot.SetActive(active);
    }

    public void ApplyHealthBarVisibility(PropHuntTestRole role)
    {
        bool showHider = role == PropHuntTestRole.Hider && !IsRoleSelectionPanelOpen;
        bool showSeeker = role == PropHuntTestRole.Seeker && !IsRoleSelectionPanelOpen;

        if (hiderHealthBar != null && hiderHealthBar.activeSelf != showHider)
        {
            hiderHealthBar.SetActive(showHider);
        }

        if (seekerHealthBar != null && seekerHealthBar.activeSelf != showSeeker)
        {
            seekerHealthBar.SetActive(showSeeker);
        }
    }

    private void ActivateHiderPreviewCamera()
    {
        if (hiderTransformSystem == null || hiderTransformSystem.cameraModeManager == null) return;

        PlayerCameraModeManager manager = hiderTransformSystem.cameraModeManager;
        manager.SetCameraSystemEnabled(true);
        manager.ApplyResolvedHiderCameraMode();
        manager.EnsureGameplayCameraRendering("ActivateHiderPreviewCamera");
    }

    private void TeleportHiderToSpawn()
    {
        if (hiderTransformSystem == null || hiderTestSpawnPoint == null) return;

        CharacterController controller = hiderTransformSystem.GetComponent<CharacterController>();
        bool restoreController = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        hiderTransformSystem.transform.SetPositionAndRotation(
            hiderTestSpawnPoint.position,
            hiderTestSpawnPoint.rotation);
        if (controller != null) controller.enabled = restoreController;
        Physics.SyncTransforms();
    }

    private void SetPanelVisible(bool visible)
    {
        if (roleSelectionPanel == null) return;

        roleSelectionPanel.SetActive(visible);
        CanvasGroup group = roleSelectionPanel.GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha = 1f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }
        if (hiderRoleButton != null) hiderRoleButton.interactable = visible;
        if (seekerRoleButton != null) seekerRoleButton.interactable = visible;
    }

    private void BindButtons()
    {
        if (hiderRoleButton != null)
        {
            hiderRoleButton.onClick.RemoveListener(SelectInitialHiderRole);
            hiderRoleButton.onClick.AddListener(SelectInitialHiderRole);
        }

        if (seekerRoleButton != null)
        {
            seekerRoleButton.onClick.RemoveListener(SelectInitialSeekerRole);
            seekerRoleButton.onClick.AddListener(SelectInitialSeekerRole);
        }
    }

    private void UnbindButtons()
    {
        if (hiderRoleButton != null) hiderRoleButton.onClick.RemoveListener(SelectInitialHiderRole);
        if (seekerRoleButton != null) seekerRoleButton.onClick.RemoveListener(SelectInitialSeekerRole);
    }

    private void ResolveReferences()
    {
        if (hiderTransformSystem == null)
        {
            foreach (PropTransformSystem player in FindObjectsOfType<PropTransformSystem>(true))
            {
                if (player.playerRole == PlayerRole.Hider)
                {
                    hiderTransformSystem = player;
                    break;
                }
            }
        }

        if (hiderHealth == null && hiderTransformSystem != null)
            hiderHealth = hiderTransformSystem.GetComponent<HiderHealth>();
        if (hiderElimination == null && hiderTransformSystem != null)
            hiderElimination = hiderTransformSystem.GetComponent<HiderEliminationController>();
        if (hiderAbilities == null && hiderTransformSystem != null)
            hiderAbilities = hiderTransformSystem.GetComponent<HiderAbilityController>();
        if (seekerController == null) seekerController = FindObjectOfType<SeekerFirstPersonController>(true);
        if (seekerWeapon == null) seekerWeapon = FindObjectOfType<SeekerRaycastWeapon>(true);
        if (seekerHealth == null) seekerHealth = FindObjectOfType<SeekerHealth>(true);
        if (seekerWeaponEnergy == null) seekerWeaponEnergy = FindObjectOfType<SeekerWeaponEnergy>(true);
        if (seekerCamera == null && seekerController != null)
            seekerCamera = seekerController.GetComponentInChildren<Camera>(true);
    }

    private static void SetCameraActive(Camera camera, bool active)
    {
        if (camera == null) return;
        AudioListener listener = camera.GetComponent<AudioListener>();
        if (active)
        {
            camera.targetDisplay = 0;
            camera.targetTexture = null;
            camera.gameObject.SetActive(true);
            camera.enabled = true;
            camera.gameObject.tag = "MainCamera";
            if (listener != null) listener.enabled = true;
        }
        else
        {
            if (listener != null) listener.enabled = false;
            camera.enabled = false;
            camera.gameObject.tag = "Untagged";
            camera.gameObject.SetActive(false);
        }
    }

    private static void SetCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private static bool WasF1Pressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null) return Keyboard.current.f1Key.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.F1);
#else
        return false;
#endif
    }

    private static bool WasF2Pressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null) return Keyboard.current.f2Key.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.F2);
#else
        return false;
#endif
    }
}
