using System;
using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

public class HiderEliminationController : MonoBehaviour
{
    [Header("Core References")]
    [SerializeField] private HiderHealth health;
    [SerializeField] private PropTransformSystem transformSystem;
    [SerializeField] private FirstPersonController movementController;
    [SerializeField] private HiderCloneAbility cloneAbility;
    [SerializeField] private HiderRevealController revealController;
    [SerializeField] private HiderAntiCampSystem antiCampSystem;
    [SerializeField] private HiderZoneStatusController zoneStatusController;
    [SerializeField] private HiderRosterManager rosterManager;
    [SerializeField] private HiderSpectatorController spectatorController;

    [Header("Objects disabled while eliminated")]
    [SerializeField] private Collider[] playerHitColliders;
    [SerializeField] private Renderer[] playerRenderers;

    private readonly Dictionary<Collider, bool> colliderStates = new Dictionary<Collider, bool>();
    private readonly Dictionary<Renderer, bool> rendererStates = new Dictionary<Renderer, bool>();
    private bool isProcessing;
    private bool eliminationApplied;
    private bool spectatorSuppressed;

    public HiderHealth Health => health;
    public PropTransformSystem TransformSystem => transformSystem;
    public bool IsProcessing => isProcessing;
    public bool IsEliminated => health != null && health.IsEliminated;
    public bool IsSpectatorSuppressed => spectatorSuppressed;
    public event Action<bool> EliminationStateChanged;

    private void Awake()
    {
        ResolveReferences();
        CacheDisableStates();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToHealth();
        rosterManager?.RegisterHider(this);

        if (health != null && health.IsEliminated)
        {
            ApplyElimination();
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromHealth();
    }

    private void OnDestroy()
    {
        rosterManager?.UnregisterHider(this);
    }

    public void Configure(
        HiderHealth configuredHealth,
        PropTransformSystem configuredTransformSystem,
        FirstPersonController configuredMovementController,
        HiderCloneAbility configuredCloneAbility,
        HiderRevealController configuredRevealController,
        HiderAntiCampSystem configuredAntiCampSystem,
        HiderZoneStatusController configuredZoneStatusController,
        HiderRosterManager configuredRosterManager,
        HiderSpectatorController configuredSpectatorController,
        Collider[] configuredHitColliders,
        Renderer[] configuredRenderers)
    {
        bool wasActive = isActiveAndEnabled;
        if (wasActive)
        {
            UnsubscribeFromHealth();
        }

        health = configuredHealth;
        transformSystem = configuredTransformSystem;
        movementController = configuredMovementController;
        cloneAbility = configuredCloneAbility;
        revealController = configuredRevealController;
        antiCampSystem = configuredAntiCampSystem;
        zoneStatusController = configuredZoneStatusController;
        rosterManager = configuredRosterManager;
        spectatorController = configuredSpectatorController;
        playerHitColliders = configuredHitColliders ?? Array.Empty<Collider>();
        playerRenderers = configuredRenderers ?? Array.Empty<Renderer>();
        CacheDisableStates();

        if (wasActive)
        {
            SubscribeToHealth();
            rosterManager?.RegisterHider(this);
        }
    }

    public Vector3 GetSpectatorFocusPosition(float heightOffset = 1.8f)
    {
        Bounds bounds = default;
        bool hasBounds = false;
        Renderer[] renderers = transformSystem != null && transformSystem.CurrentVisualRoot != null
            ? transformSystem.CurrentVisualRoot.GetComponentsInChildren<Renderer>(false)
            : Array.Empty<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds ? bounds.center : transform.position + Vector3.up * heightOffset;
    }

    public void ApplyElimination()
    {
        if (isProcessing || eliminationApplied || health == null || !health.IsEliminated)
        {
            return;
        }

        isProcessing = true;
        CacheDisableStates();
        if (!spectatorSuppressed)
        {
            spectatorController?.PrepareDeathView(transform.position);
        }

        transformSystem?.SetGameplayInputLocked(true);
        transformSystem?.ForceExitGhostCamera();
        transformSystem?.ForceDetachForElimination();

        if (movementController != null)
        {
            movementController.SetControlLocked(true);
            movementController.enabled = false;
        }

        cloneAbility?.DestroyAllOwnedClones();
        revealController?.StopReveal();
        antiCampSystem?.SetEliminatedState(true);
        zoneStatusController?.SetEliminatedState(true);
        transformSystem?.ApplyHealthEliminationState(true, !spectatorSuppressed);

        SetCollidersEnabled(false);
        SetRenderersEnabled(false);
        rosterManager?.NotifyEliminated(this);
        if (!spectatorSuppressed)
        {
            spectatorController?.EnterSpectator(transform.position);
        }

        eliminationApplied = true;
        isProcessing = false;
        EliminationStateChanged?.Invoke(true);
    }

    public void ResetEliminationForRound()
    {
        if (isProcessing)
        {
            return;
        }

        isProcessing = true;
        spectatorController?.ExitSpectator();
        transformSystem?.ForceExitGhostCamera();
        transformSystem?.ForceDetachForElimination();
        cloneAbility?.DestroyAllOwnedClones();
        revealController?.StopReveal();
        antiCampSystem?.SetEliminatedState(false);
        antiCampSystem?.ResetAntiCamp();
        zoneStatusController?.SetEliminatedState(false);
        zoneStatusController?.ResetForRound();
        transformSystem?.ApplyHealthEliminationState(false);

        RestoreColliders();
        RestoreRenderers();
        if (movementController != null)
        {
            movementController.SetControlLocked(false);
            movementController.enabled = true;
        }

        transformSystem?.SetGameplayInputLocked(false);
        eliminationApplied = false;
        rosterManager?.NotifyRevivedOrReset(this);
        isProcessing = false;
        EliminationStateChanged?.Invoke(false);
    }

    public void SetSpectatorSuppressed(bool suppressed)
    {
        spectatorSuppressed = suppressed;
        if (suppressed)
        {
            spectatorController?.ExitSpectator();
        }
    }

    private void HandleEliminated(HiderHealth eliminatedHealth)
    {
        if (eliminatedHealth == health)
        {
            ApplyElimination();
        }
    }

    private void HandleRevivedOrReset(HiderHealth resetHealth)
    {
        if (resetHealth == health)
        {
            ResetEliminationForRound();
        }
    }

    private void SubscribeToHealth()
    {
        if (health == null)
        {
            return;
        }

        health.Eliminated -= HandleEliminated;
        health.Eliminated += HandleEliminated;
        health.RevivedOrReset -= HandleRevivedOrReset;
        health.RevivedOrReset += HandleRevivedOrReset;
    }

    private void UnsubscribeFromHealth()
    {
        if (health == null)
        {
            return;
        }

        health.Eliminated -= HandleEliminated;
        health.RevivedOrReset -= HandleRevivedOrReset;
    }

    private void ResolveReferences()
    {
        if (health == null) health = GetComponent<HiderHealth>();
        if (transformSystem == null) transformSystem = GetComponent<PropTransformSystem>();
        if (movementController == null) movementController = GetComponent<FirstPersonController>();
        if (cloneAbility == null) cloneAbility = GetComponent<HiderCloneAbility>();
        if (revealController == null) revealController = GetComponent<HiderRevealController>();
        if (antiCampSystem == null) antiCampSystem = GetComponent<HiderAntiCampSystem>();
        if (zoneStatusController == null) zoneStatusController = GetComponent<HiderZoneStatusController>();
        if (rosterManager == null) rosterManager = FindObjectOfType<HiderRosterManager>();
        if (spectatorController == null) spectatorController = GetComponent<HiderSpectatorController>();
        if (playerHitColliders == null || playerHitColliders.Length == 0)
        {
            playerHitColliders = GetComponentsInChildren<Collider>(true);
        }

        if (playerRenderers == null || playerRenderers.Length == 0)
        {
            playerRenderers = GetComponentsInChildren<Renderer>(true);
        }
    }

    private void CacheDisableStates()
    {
        colliderStates.Clear();
        foreach (Collider collider in playerHitColliders ?? Array.Empty<Collider>())
        {
            if (collider != null && !colliderStates.ContainsKey(collider))
            {
                colliderStates.Add(collider, collider.enabled);
            }
        }

        rendererStates.Clear();
        foreach (Renderer renderer in playerRenderers ?? Array.Empty<Renderer>())
        {
            if (renderer != null && !rendererStates.ContainsKey(renderer))
            {
                rendererStates.Add(renderer, renderer.enabled);
            }
        }
    }

    private void SetCollidersEnabled(bool enabled)
    {
        foreach (Collider collider in colliderStates.Keys)
        {
            if (collider != null) collider.enabled = enabled;
        }
    }

    private void SetRenderersEnabled(bool enabled)
    {
        foreach (Renderer renderer in rendererStates.Keys)
        {
            if (renderer != null) renderer.enabled = enabled;
        }
    }

    private void RestoreColliders()
    {
        foreach (KeyValuePair<Collider, bool> state in colliderStates)
        {
            if (state.Key != null) state.Key.enabled = state.Value;
        }
    }

    private void RestoreRenderers()
    {
        foreach (KeyValuePair<Renderer, bool> state in rendererStates)
        {
            if (state.Key != null) state.Key.enabled = state.Value;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Debug/Eliminate")]
    private void DebugEliminate()
    {
        health?.TakeDamage(health.CurrentHealth, HiderDamageSource.Debug);
    }

    [ContextMenu("Debug/Reset")]
    private void DebugReset()
    {
        health?.ResetForRound();
    }
#endif
}
