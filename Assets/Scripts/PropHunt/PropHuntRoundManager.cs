using System;
using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

public enum PropHuntRoundState
{
    Waiting,
    Preparation,
    Hunting,
    Ended
}

public enum PropHuntRoundWinner
{
    None,
    Hiders,
    Seekers
}

public class PropHuntRoundManager : MonoBehaviour
{
    [Header("Round timing")]
    [SerializeField, Min(0f)] private float preparationDuration = 30f;
    [SerializeField, Min(0f)] private float huntingDuration = 180f;
    [SerializeField] private bool autoStartRoundInLocalMode = true;

    [Header("Local preview counts")]
    [SerializeField] private bool usePreviewCounts = false;
    [SerializeField, Min(0)] private int previewSeekerCount = 2;

    [Header("Participants")]
    [SerializeField] private List<PropTransformSystem> players = new List<PropTransformSystem>();
    [SerializeField] private List<HiderAbilityController> hiderAbilities = new List<HiderAbilityController>();

    public int AliveHiderCount { get; private set; }
    public int SeekerCount { get; private set; }
    public float RemainingTime { get; private set; }
    public PropHuntRoundState CurrentState { get; private set; } = PropHuntRoundState.Waiting;
    public PropHuntRoundWinner CurrentWinner { get; private set; } = PropHuntRoundWinner.None;
    public float PreparationDuration => preparationDuration;
    public float HuntingDuration => huntingDuration;

    public event Action RoundDataChanged;
    public event Action RoundStarted;
    public event Action<PropHuntRoundState> RoundStateChanged;

    public void ConfigureDurations(float configuredPreparationDuration, float configuredHuntingDuration)
    {
        preparationDuration = Mathf.Max(0f, configuredPreparationDuration);
        huntingDuration = Mathf.Max(0f, configuredHuntingDuration);
    }

    private void Awake()
    {
        CacheLocalParticipantsIfNeeded();
        RefreshPlayerCounts();
    }

    private void Start()
    {
        if (autoStartRoundInLocalMode && CurrentState == PropHuntRoundState.Waiting)
        {
            StartRound();
        }
    }

    private void Update()
    {
        RefreshPlayerCounts();

        if ((CurrentState != PropHuntRoundState.Preparation &&
             CurrentState != PropHuntRoundState.Hunting) || RemainingTime <= 0f)
        {
            return;
        }

        float previousTime = RemainingTime;
        RemainingTime = Mathf.Max(0f, RemainingTime - Time.deltaTime);
        if (Mathf.FloorToInt(previousTime) != Mathf.FloorToInt(RemainingTime))
        {
            RoundDataChanged?.Invoke();
        }

        if (RemainingTime > 0f)
        {
            return;
        }

        if (CurrentState == PropHuntRoundState.Preparation)
        {
            BeginHunting();
        }
        else
        {
            EndRound();
        }
    }

    public void ConfigureLocalParticipants(IEnumerable<PropTransformSystem> configuredPlayers)
    {
        players.Clear();
        if (configuredPlayers != null)
        {
            foreach (PropTransformSystem player in configuredPlayers)
            {
                if (player != null && !players.Contains(player))
                {
                    players.Add(player);
                }
            }
        }

        hiderAbilities.Clear();
        foreach (PropTransformSystem player in players)
        {
            HiderAbilityController ability = player.GetComponent<HiderAbilityController>();
            if (ability != null && !hiderAbilities.Contains(ability))
            {
                hiderAbilities.Add(ability);
            }
        }

        RefreshPlayerCounts();
    }

    public void BeginPreparation()
    {
        StartRound();
    }

    public void StartRound()
    {
        CurrentWinner = PropHuntRoundWinner.None;
        RemainingTime = preparationDuration;
        SetState(PropHuntRoundState.Preparation);
        SetSeekerMovementAllowed(false);
        ResetHiderAbilities();
        RoundStarted?.Invoke();
        RefreshPlayerCounts();
        RoundDataChanged?.Invoke();
    }

    public void RestartRound()
    {
        StartRound();
    }

    public void BeginHunting()
    {
        RemainingTime = huntingDuration;
        SetState(PropHuntRoundState.Hunting);
        SetSeekerMovementAllowed(true);
        RefreshPlayerCounts();
        RoundDataChanged?.Invoke();
    }

    public void EndRound()
    {
        EndRoundWithWinner(CountActualAliveHiders() > 0
            ? PropHuntRoundWinner.Hiders
            : PropHuntRoundWinner.Seekers);
    }

    public void EndRoundWithWinner(PropHuntRoundWinner winner)
    {
        if (CurrentState == PropHuntRoundState.Ended)
        {
            return;
        }

        CurrentWinner = winner;
        RemainingTime = 0f;
        SetState(PropHuntRoundState.Ended);
        SetSeekerMovementAllowed(true);
        RoundDataChanged?.Invoke();
    }

    public void RefreshPlayerCounts()
    {
        int actualHiders = 0;
        int actualSeekers = 0;

        for (int i = players.Count - 1; i >= 0; i--)
        {
            PropTransformSystem player = players[i];
            if (player == null)
            {
                players.RemoveAt(i);
                continue;
            }

            if (player.playerRole == PlayerRole.Hider)
            {
                HiderHealth health = player.GetComponent<HiderHealth>();
                if (health == null || health.IsAlive)
                {
                    actualHiders++;
                }
            }
            else if (player.playerRole == PlayerRole.Seeker)
            {
                actualSeekers++;
            }
        }

        int displayedHiders = actualHiders;
        int displayedSeekers = usePreviewCounts ? previewSeekerCount : actualSeekers;

        if (AliveHiderCount == displayedHiders && SeekerCount == displayedSeekers)
        {
            return;
        }

        AliveHiderCount = displayedHiders;
        SeekerCount = displayedSeekers;
        RoundDataChanged?.Invoke();
    }

    public bool IsAbilityPhaseActive()
    {
        return CurrentState == PropHuntRoundState.Preparation ||
               CurrentState == PropHuntRoundState.Hunting;
    }

    private void SetState(PropHuntRoundState newState)
    {
        if (CurrentState == newState)
        {
            return;
        }

        CurrentState = newState;
        RoundStateChanged?.Invoke(newState);
        RoundDataChanged?.Invoke();
    }

    private void ResetHiderAbilities()
    {
        foreach (HiderAbilityController ability in hiderAbilities)
        {
            if (ability != null)
            {
                ability.ResetAbilitiesForRound();
            }
        }
    }

    private int CountActualAliveHiders()
    {
        int count = 0;
        foreach (PropTransformSystem player in players)
        {
            HiderHealth health = player != null ? player.GetComponent<HiderHealth>() : null;
            if (player != null && player.playerRole == PlayerRole.Hider &&
                (health == null || health.IsAlive))
            {
                count++;
            }
        }

        return count;
    }

    private void SetSeekerMovementAllowed(bool allowed)
    {
        foreach (PropTransformSystem player in players)
        {
            if (player == null || player.playerRole != PlayerRole.Seeker)
            {
                continue;
            }

            FirstPersonController controller = player.GetComponent<FirstPersonController>();
            if (controller != null)
            {
                controller.enabled = allowed;
            }
        }
    }

    private void CacheLocalParticipantsIfNeeded()
    {
        if (players.Count == 0)
        {
            players.AddRange(FindObjectsOfType<PropTransformSystem>(true));
        }

        if (hiderAbilities.Count == 0)
        {
            hiderAbilities.AddRange(FindObjectsOfType<HiderAbilityController>(true));
        }
    }
}
