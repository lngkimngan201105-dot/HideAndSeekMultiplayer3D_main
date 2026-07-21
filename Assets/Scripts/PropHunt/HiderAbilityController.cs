using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum HiderAbilityType
{
    Clone,
    RandomProp
}

public class HiderAbilityController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PropTransformSystem propTransformSystem;
    [SerializeField] private PropHuntRoundManager roundManager;
    [SerializeField] private HiderCloneAbility cloneAbility;

    [Header("Random prop (O)")]
    [SerializeField, Min(0)] private int randomPropMaxCharges = 5;
    [SerializeField, Min(0f)] private float randomPropCooldown = 3f;
    [SerializeField] private List<PropTarget> randomPropDefinitions = new List<PropTarget>();

    public int RemainingCloneCharges => cloneAbility != null
        ? cloneAbility.RemainingCloneCharges
        : 0;
    public int RemainingRandomPropCharges { get; private set; }
    public float RandomPropCooldownRemaining { get; private set; }
    public float RandomPropCooldownNormalized => randomPropCooldown > 0f
        ? Mathf.Clamp01(RandomPropCooldownRemaining / randomPropCooldown)
        : 0f;

    public bool CanCreateClone => cloneAbility != null && cloneAbility.CanCreateClone;
    public bool CanUseRandomProp => CanUseDisguisedAbility() &&
                                    RemainingRandomPropCharges > 0 &&
                                    RandomPropCooldownRemaining <= 0f &&
                                    !_isChangingProp;

    public event Action AbilitiesChanged;
    public event Action<HiderAbilityType> AbilityUsed;

    private bool _isChangingProp;

    private void Awake()
    {
        ResolveReferences();
        ResetAbilitiesForRound();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (cloneAbility != null)
        {
            cloneAbility.CloneStateChanged += HandleCloneStateChanged;
        }
    }

    private void OnDisable()
    {
        if (cloneAbility != null)
        {
            cloneAbility.CloneStateChanged -= HandleCloneStateChanged;
        }
    }

    private void Update()
    {
        float oldRandomCooldown = RandomPropCooldownRemaining;
        RandomPropCooldownRemaining = Mathf.Max(
            0f,
            RandomPropCooldownRemaining - Time.deltaTime
        );

        if (WasKeyPressed(KeyCode.X))
        {
            TryCreateClone();
        }

        if (WasKeyPressed(KeyCode.O))
        {
            TryUseRandomProp();
        }

        if (!Mathf.Approximately(oldRandomCooldown, RandomPropCooldownRemaining))
        {
            AbilitiesChanged?.Invoke();
        }
    }

    public void Configure(
        PropTransformSystem transformSystem,
        PropHuntRoundManager configuredRoundManager,
        HiderCloneAbility configuredCloneAbility,
        IEnumerable<PropTarget> propDefinitions)
    {
        propTransformSystem = transformSystem;
        roundManager = configuredRoundManager;

        if (isActiveAndEnabled && cloneAbility != null)
        {
            cloneAbility.CloneStateChanged -= HandleCloneStateChanged;
        }

        cloneAbility = configuredCloneAbility;
        if (isActiveAndEnabled && cloneAbility != null)
        {
            cloneAbility.CloneStateChanged += HandleCloneStateChanged;
        }

        randomPropDefinitions.Clear();
        if (propDefinitions == null)
        {
            return;
        }

        foreach (PropTarget definition in propDefinitions)
        {
            if (definition != null && !randomPropDefinitions.Contains(definition))
            {
                randomPropDefinitions.Add(definition);
            }
        }
    }

    public bool TryCreateClone()
    {
        if (cloneAbility == null || !cloneAbility.TryCreateClone())
        {
            return false;
        }

        AbilityUsed?.Invoke(HiderAbilityType.Clone);
        AbilitiesChanged?.Invoke();
        return true;
    }

    public bool TryUseRandomProp()
    {
        if (!CanUseRandomProp || propTransformSystem == null)
        {
            return false;
        }

        List<PropTarget> validDefinitions = BuildValidRandomPropList();
        if (validDefinitions.Count == 0)
        {
            Debug.LogWarning("HiderAbilityController: no valid visualParts prop is available for random disguise.");
            return false;
        }

        _isChangingProp = true;
        PropTarget selected = validDefinitions[UnityEngine.Random.Range(0, validDefinitions.Count)];
        bool applied = propTransformSystem.ApplyPropDefinition(selected, true);
        _isChangingProp = false;

        if (!applied)
        {
            Debug.LogWarning($"HiderAbilityController: random prop change to '{selected.displayName}' failed; charge was preserved.");
            return false;
        }

        RemainingRandomPropCharges--;
        RandomPropCooldownRemaining = randomPropCooldown;
        AbilityUsed?.Invoke(HiderAbilityType.RandomProp);
        AbilitiesChanged?.Invoke();
        return true;
    }

    public void ResetAbilitiesForRound()
    {
        if (cloneAbility != null)
        {
            cloneAbility.ResetCloneAbilityForRound();
        }

        RemainingRandomPropCharges = randomPropMaxCharges;
        RandomPropCooldownRemaining = 0f;
        _isChangingProp = false;

        HiderAntiCampSystem antiCampSystem = GetComponent<HiderAntiCampSystem>();
        if (antiCampSystem != null)
        {
            antiCampSystem.ResetAntiCamp();
        }

        if (propTransformSystem != null && propTransformSystem.cameraModeManager != null)
        {
            propTransformSystem.cameraModeManager.SetPropCameraFar(false);
        }

        AbilitiesChanged?.Invoke();
    }

    private bool CanUseDisguisedAbility()
    {
        return propTransformSystem != null &&
               propTransformSystem.playerRole == PlayerRole.Hider &&
               propTransformSystem.currentState == PlayerDisguiseState.Disguised &&
               !propTransformSystem.IsGameplayInputLocked &&
               !propTransformSystem.IsEliminated &&
               !propTransformSystem.IsGhostCameraActive &&
               IsRoundPhasePlayable();
    }

    private bool IsRoundPhasePlayable()
    {
        return roundManager == null || roundManager.IsAbilityPhaseActive();
    }

    private List<PropTarget> BuildValidRandomPropList()
    {
        List<PropTarget> valid = new List<PropTarget>();
        bool hasAlternative = false;

        foreach (PropTarget definition in randomPropDefinitions)
        {
            if (!IsValidPropDefinition(definition))
            {
                continue;
            }

            valid.Add(definition);
            if (definition.propId != propTransformSystem.currentPropId)
            {
                hasAlternative = true;
            }
        }

        if (hasAlternative)
        {
            valid.RemoveAll(definition => definition.propId == propTransformSystem.currentPropId);
        }

        return valid;
    }

    private static bool IsValidPropDefinition(PropTarget definition)
    {
        if (definition == null ||
            !definition.GameplayEnabled ||
            definition.visualParts == null ||
            definition.visualParts.Length == 0)
        {
            return false;
        }

        bool hasRenderablePart = false;
        foreach (PropVisualPartData part in definition.visualParts)
        {
            if (part == null || part.mesh == null ||
                part.mesh.name.IndexOf("Combined Mesh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                part.materials == null || part.materials.Length == 0)
            {
                return false;
            }

            bool hasMaterial = false;
            foreach (Material material in part.materials)
            {
                hasMaterial |= material != null;
            }

            Vector3 scaledSize = Vector3.Scale(part.mesh.bounds.size, part.localScale);
            scaledSize = new Vector3(
                Mathf.Abs(scaledSize.x),
                Mathf.Abs(scaledSize.y),
                Mathf.Abs(scaledSize.z)
            );
            if (!hasMaterial ||
                scaledSize.x > 20f ||
                scaledSize.y > 20f ||
                scaledSize.z > 20f)
            {
                return false;
            }

            hasRenderablePart = true;
        }

        return hasRenderablePart;
    }

    private void ResolveReferences()
    {
        if (propTransformSystem == null)
        {
            propTransformSystem = GetComponent<PropTransformSystem>();
        }

        if (cloneAbility == null)
        {
            cloneAbility = GetComponent<HiderCloneAbility>();
        }

        if (roundManager == null)
        {
            roundManager = FindObjectOfType<PropHuntRoundManager>();
        }
    }

    private void HandleCloneStateChanged()
    {
        AbilitiesChanged?.Invoke();
    }

    private static bool WasKeyPressed(KeyCode keyCode)
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (keyCode == KeyCode.X) return Keyboard.current.xKey.wasPressedThisFrame;
            if (keyCode == KeyCode.O) return Keyboard.current.oKey.wasPressedThisFrame;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(keyCode);
#else
        return false;
#endif
    }
}
