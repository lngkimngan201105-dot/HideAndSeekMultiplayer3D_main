using System;
using System.Collections.Generic;
using UnityEngine;

public class HiderRosterManager : MonoBehaviour
{
    [SerializeField] private PropHuntRoundManager roundManager;
    [SerializeField] private List<HiderEliminationController> registeredHiders =
        new List<HiderEliminationController>();

    private readonly List<HiderEliminationController> aliveHiders =
        new List<HiderEliminationController>();
    private bool isResettingRound;

    public int TotalHiderCount => registeredHiders.Count;
    public int AliveHiderCount => aliveHiders.Count;
    public IReadOnlyList<HiderEliminationController> AliveHiders => aliveHiders;

    public event Action<int, int> AliveCountChanged;
    public event Action AllHidersEliminated;

    private void Awake()
    {
        ResolveReferences();
        RebuildAliveList(false);
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (roundManager != null)
        {
            roundManager.RoundStarted -= ResetAllHidersForRound;
            roundManager.RoundStarted += ResetAllHidersForRound;
        }
    }

    private void OnDisable()
    {
        if (roundManager != null)
        {
            roundManager.RoundStarted -= ResetAllHidersForRound;
        }
    }

    public void Configure(
        PropHuntRoundManager configuredRoundManager,
        IEnumerable<HiderEliminationController> configuredHiders)
    {
        if (isActiveAndEnabled && roundManager != null)
        {
            roundManager.RoundStarted -= ResetAllHidersForRound;
        }

        roundManager = configuredRoundManager;
        registeredHiders.Clear();
        if (configuredHiders != null)
        {
            foreach (HiderEliminationController hider in configuredHiders)
            {
                if (IsTrueHider(hider) && !registeredHiders.Contains(hider))
                {
                    registeredHiders.Add(hider);
                }
            }
        }

        RebuildAliveList(false);
        if (isActiveAndEnabled && roundManager != null)
        {
            roundManager.RoundStarted -= ResetAllHidersForRound;
            roundManager.RoundStarted += ResetAllHidersForRound;
        }
    }

    public void RegisterHider(HiderEliminationController hider)
    {
        if (!IsTrueHider(hider) || registeredHiders.Contains(hider))
        {
            return;
        }

        registeredHiders.Add(hider);
        if (hider.Health != null && hider.Health.IsAlive)
        {
            aliveHiders.Add(hider);
        }

        NotifyAliveCountChanged();
    }

    public void UnregisterHider(HiderEliminationController hider)
    {
        if (hider == null)
        {
            return;
        }

        bool changed = registeredHiders.Remove(hider);
        changed |= aliveHiders.Remove(hider);
        if (changed)
        {
            NotifyAliveCountChanged();
        }
    }

    public void NotifyEliminated(HiderEliminationController hider)
    {
        if (!registeredHiders.Contains(hider))
        {
            RegisterHider(hider);
        }

        if (!aliveHiders.Remove(hider) || isResettingRound)
        {
            return;
        }

        NotifyAliveCountChanged();
        if (aliveHiders.Count == 0 && registeredHiders.Count > 0)
        {
            AllHidersEliminated?.Invoke();
        }
    }

    public void NotifyRevivedOrReset(HiderEliminationController hider)
    {
        if (!IsTrueHider(hider))
        {
            return;
        }

        if (!registeredHiders.Contains(hider))
        {
            registeredHiders.Add(hider);
        }

        if (!aliveHiders.Contains(hider))
        {
            aliveHiders.Add(hider);
            if (!isResettingRound)
            {
                NotifyAliveCountChanged();
            }
        }
    }

    public void ResetAllHidersForRound()
    {
        isResettingRound = true;
        for (int i = registeredHiders.Count - 1; i >= 0; i--)
        {
            HiderEliminationController hider = registeredHiders[i];
            if (!IsTrueHider(hider))
            {
                registeredHiders.RemoveAt(i);
                continue;
            }

            hider.Health.ResetForRound();
        }

        isResettingRound = false;
        RebuildAliveList(true);
    }

    private void RebuildAliveList(bool notify)
    {
        aliveHiders.Clear();
        for (int i = registeredHiders.Count - 1; i >= 0; i--)
        {
            HiderEliminationController hider = registeredHiders[i];
            if (!IsTrueHider(hider))
            {
                registeredHiders.RemoveAt(i);
                continue;
            }

            if (hider.Health != null && hider.Health.IsAlive)
            {
                aliveHiders.Add(hider);
            }
        }

        if (notify)
        {
            NotifyAliveCountChanged();
        }
    }

    private void NotifyAliveCountChanged()
    {
        AliveCountChanged?.Invoke(AliveHiderCount, TotalHiderCount);
    }

    private static bool IsTrueHider(HiderEliminationController hider)
    {
        return hider != null && hider.TransformSystem != null &&
               hider.TransformSystem.playerRole == PlayerRole.Hider;
    }

    private void ResolveReferences()
    {
        if (roundManager == null)
        {
            roundManager = FindObjectOfType<PropHuntRoundManager>();
        }
    }
}
