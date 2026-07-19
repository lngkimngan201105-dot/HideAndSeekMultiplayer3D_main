using System;
using System.Collections.Generic;
using UnityEngine;

public class HiderCloneAbility : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PropTransformSystem propTransformSystem;
    [SerializeField] private PropHuntRoundManager roundManager;
    [SerializeField] private HiderRevealController revealController;
    [SerializeField] private Transform cloneContainer;

    [Header("Clone (X)")]
    [SerializeField, Min(0)] private int cloneMaxCharges = 5;

    public int RemainingCloneCharges { get; private set; }
    public IReadOnlyList<HiderCloneInstance> ActiveClones => activeClones;
    public bool CanCreateClone =>
        !isCreatingClone &&
        RemainingCloneCharges > 0 &&
        propTransformSystem != null &&
        propTransformSystem.playerRole == PlayerRole.Hider &&
        propTransformSystem.IsDisguised &&
        !propTransformSystem.IsGameplayInputLocked &&
        !propTransformSystem.IsEliminated &&
        !propTransformSystem.IsGhostCameraActive &&
        !propTransformSystem.IsChangingModel &&
        propTransformSystem.CurrentPropVisualTransform != null &&
        (roundManager == null || roundManager.IsAbilityPhaseActive());

    public event Action CloneStateChanged;

    private readonly List<HiderCloneInstance> activeClones = new List<HiderCloneInstance>();
    private bool isCreatingClone;
    private int nextCloneId = 1;

    private void Awake()
    {
        ResolveReferences();
        RemainingCloneCharges = cloneMaxCharges;
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (roundManager != null)
        {
            roundManager.RoundStateChanged += HandleRoundStateChanged;
        }

        if (propTransformSystem != null)
        {
            propTransformSystem.EliminationChanged += HandleEliminationChanged;
        }
    }

    private void OnDisable()
    {
        if (roundManager != null)
        {
            roundManager.RoundStateChanged -= HandleRoundStateChanged;
        }

        if (propTransformSystem != null)
        {
            propTransformSystem.EliminationChanged -= HandleEliminationChanged;
        }

        DestroyAllOwnedClones();
        if (revealController != null)
        {
            revealController.StopReveal();
        }
    }

    public void Configure(
        PropTransformSystem transformSystem,
        PropHuntRoundManager configuredRoundManager,
        HiderRevealController configuredRevealController,
        Transform configuredCloneContainer)
    {
        if (isActiveAndEnabled)
        {
            UnsubscribeFromCurrentReferences();
        }

        propTransformSystem = transformSystem;
        roundManager = configuredRoundManager;
        revealController = configuredRevealController;
        cloneContainer = configuredCloneContainer;

        if (isActiveAndEnabled)
        {
            SubscribeToCurrentReferences();
        }
    }

    public bool TryCreateClone()
    {
        if (!CanCreateClone)
        {
            LogCreationFailure(GetCreationFailureReason());
            return false;
        }

        isCreatingClone = true;
        GameObject cloneRoot = null;
        try
        {
            Transform container = ResolveCloneContainer();
            if (container == null)
            {
                LogCreationFailure("HiderCloneContainer is unavailable.");
                return false;
            }

            Transform sourceRoot = propTransformSystem.propVisualRoot;
            cloneRoot = new GameObject($"HiderClone_{nextCloneId:000}");
            cloneRoot.transform.SetParent(container, false);
            cloneRoot.transform.SetPositionAndRotation(sourceRoot.position, sourceRoot.rotation);
            cloneRoot.transform.localScale = sourceRoot.lossyScale;

            if (!propTransformSystem.TryCreateDetachedVisualCopy(
                    cloneRoot.transform,
                    out GameObject cloneVisual))
            {
                LogCreationFailure("Current prop visual could not be copied safely.");
                return false;
            }

            if (!TryCreateHitbox(cloneRoot, cloneVisual, out BoxCollider hitbox))
            {
                LogCreationFailure("Clone renderer bounds are invalid.");
                return false;
            }

            bool createdOnWall = propTransformSystem.IsWallAttached;
            Vector3 wallNormal = createdOnWall ? propTransformSystem.WallNormal : Vector3.zero;
            if (createdOnWall && !IsWallSnapshotValid(cloneVisual, wallNormal))
            {
                LogCreationFailure("The captured wall pose is no longer valid.");
                return false;
            }

            HiderCloneInstance instance = cloneRoot.AddComponent<HiderCloneInstance>();
            instance.Initialize(this, cloneVisual, hitbox, createdOnWall, wallNormal);
            activeClones.Add(instance);

            RemainingCloneCharges--;
            nextCloneId++;
            cloneRoot = null;
            CloneStateChanged?.Invoke();
            Debug.Log(
                $"Hider Clone:\nCreated clone {activeClones.Count}.\n" +
                $"Remaining charges={RemainingCloneCharges}.\n" +
                $"WallAttached={createdOnWall.ToString().ToLowerInvariant()}."
            );
            return true;
        }
        finally
        {
            if (cloneRoot != null)
            {
                Destroy(cloneRoot);
            }

            isCreatingClone = false;
        }
    }

    public void ResetCloneAbilityForRound()
    {
        DestroyAllOwnedClones();
        RemainingCloneCharges = cloneMaxCharges;
        nextCloneId = 1;
        isCreatingClone = false;
        if (revealController != null)
        {
            revealController.StopReveal();
        }

        CloneStateChanged?.Invoke();
    }

    public void DestroyAllOwnedClones()
    {
        HiderCloneInstance[] clones = activeClones.ToArray();
        activeClones.Clear();
        foreach (HiderCloneInstance clone in clones)
        {
            if (clone != null)
            {
                clone.DestroyClone();
            }
        }

        CloneStateChanged?.Invoke();
    }

    internal void HandleCloneHit(HiderCloneInstance clone)
    {
        activeClones.Remove(clone);
        if (revealController != null)
        {
            revealController.RevealForSeconds(5f);
        }

        CloneStateChanged?.Invoke();
        Debug.Log("Hider Clone:\nClone hit.\nReal Hider revealed for 5 seconds.");
    }

    internal void NotifyCloneDestroyed(HiderCloneInstance clone)
    {
        if (activeClones.Remove(clone))
        {
            CloneStateChanged?.Invoke();
        }
    }

    private static bool TryCreateHitbox(
        GameObject cloneRoot,
        GameObject cloneVisual,
        out BoxCollider hitbox)
    {
        hitbox = null;
        Renderer[] renderers = cloneVisual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return false;
        }

        bool hasBounds = false;
        Vector3 localMin = Vector3.zero;
        Vector3 localMax = Vector3.zero;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            for (int x = 0; x <= 1; x++)
            {
                for (int y = 0; y <= 1; y++)
                {
                    for (int z = 0; z <= 1; z++)
                    {
                        Vector3 corner = new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z
                        );
                        Vector3 localPoint = cloneRoot.transform.InverseTransformPoint(corner);
                        if (!hasBounds)
                        {
                            localMin = localMax = localPoint;
                            hasBounds = true;
                        }
                        else
                        {
                            localMin = Vector3.Min(localMin, localPoint);
                            localMax = Vector3.Max(localMax, localPoint);
                        }
                    }
                }
            }
        }

        if (!hasBounds)
        {
            return false;
        }

        Vector3 size = localMax - localMin;
        size = new Vector3(
            Mathf.Max(0.22f, size.x),
            Mathf.Max(0.22f, size.y),
            Mathf.Max(0.22f, size.z)
        );

        hitbox = cloneRoot.AddComponent<BoxCollider>();
        hitbox.center = (localMin + localMax) * 0.5f;
        hitbox.size = size;
        hitbox.isTrigger = true;
        return true;
    }

    private bool IsWallSnapshotValid(GameObject cloneVisual, Vector3 wallNormal)
    {
        if (wallNormal.sqrMagnitude < 0.5f || propTransformSystem.AttachedWallCollider == null)
        {
            return false;
        }

        Renderer[] renderers = cloneVisual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return false;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        float signedDistance = Vector3.Dot(
            bounds.center - propTransformSystem.WallHitPoint,
            wallNormal.normalized
        );
        return signedDistance >= -Mathf.Max(0.25f, bounds.extents.magnitude * 0.35f);
    }

    private Transform ResolveCloneContainer()
    {
        if (cloneContainer != null)
        {
            return cloneContainer;
        }

        GameObject existing = GameObject.Find("HiderCloneContainer");
        if (existing == null)
        {
            existing = new GameObject("HiderCloneContainer");
        }

        existing.transform.SetParent(null);
        cloneContainer = existing.transform;
        return cloneContainer;
    }

    private string GetCreationFailureReason()
    {
        if (isCreatingClone) return "A clone is already being created.";
        if (RemainingCloneCharges <= 0) return "No clone charges remain.";
        if (propTransformSystem == null) return "PropTransformSystem is missing.";
        if (propTransformSystem.playerRole != PlayerRole.Hider) return "Player is not a Hider.";
        if (!propTransformSystem.IsDisguised) return "Hider is not disguised.";
        if (propTransformSystem.IsEliminated) return "Hider is eliminated.";
        if (propTransformSystem.IsGhostCameraActive) return "Ghost Camera is active.";
        if (propTransformSystem.IsChangingModel) return "Prop visual is changing.";
        if (propTransformSystem.CurrentPropVisualTransform == null) return "Current prop visual is invalid.";
        if (roundManager != null && !roundManager.IsAbilityPhaseActive()) return "Round phase is not playable.";
        return "Clone requirements are not satisfied.";
    }

    private static void LogCreationFailure(string reason)
    {
        Debug.LogWarning(
            $"Hider Clone:\nCreation failed.\nCharge was not consumed.\nReason={reason}"
        );
    }

    private void HandleRoundStateChanged(PropHuntRoundState state)
    {
        if (state == PropHuntRoundState.Preparation)
        {
            ResetCloneAbilityForRound();
        }
        else if (state == PropHuntRoundState.Ended || state == PropHuntRoundState.Waiting)
        {
            DestroyAllOwnedClones();
            if (revealController != null)
            {
                revealController.StopReveal();
            }
        }
    }

    private void HandleEliminationChanged(bool eliminated)
    {
        if (!eliminated)
        {
            return;
        }

        DestroyAllOwnedClones();
        if (revealController != null)
        {
            revealController.StopReveal();
        }
    }

    private void ResolveReferences()
    {
        if (propTransformSystem == null)
        {
            propTransformSystem = GetComponent<PropTransformSystem>();
        }

        if (roundManager == null)
        {
            roundManager = FindObjectOfType<PropHuntRoundManager>();
        }

        if (revealController == null)
        {
            revealController = GetComponent<HiderRevealController>();
        }
    }

    private void SubscribeToCurrentReferences()
    {
        if (roundManager != null)
        {
            roundManager.RoundStateChanged += HandleRoundStateChanged;
        }

        if (propTransformSystem != null)
        {
            propTransformSystem.EliminationChanged += HandleEliminationChanged;
        }
    }

    private void UnsubscribeFromCurrentReferences()
    {
        if (roundManager != null)
        {
            roundManager.RoundStateChanged -= HandleRoundStateChanged;
        }

        if (propTransformSystem != null)
        {
            propTransformSystem.EliminationChanged -= HandleEliminationChanged;
        }
    }
}
