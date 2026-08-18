using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SeekerAISuspicionSystem : MonoBehaviour
{
    private readonly struct SuspicionCandidate
    {
        public SuspicionCandidate(Collider collider, float score, string reason)
        {
            Collider = collider;
            Score = score;
            Reason = reason;
        }

        public Collider Collider { get; }
        public float Score { get; }
        public string Reason { get; }
    }

    private struct PropMemory
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public float Score;
        public float UpdatedAt;
    }

    [SerializeField, Min(0.1f)] private float investigationRadius = 7f;
    [SerializeField, Min(0f)] private float suspicionThreshold = 45f;
    [SerializeField, Min(0f)] private float scoreDecayPerSecond = 8f;

    private readonly Queue<SuspicionCandidate> investigationTargets =
        new Queue<SuspicionCandidate>();
    private readonly Dictionary<int, PropMemory> propMemory =
        new Dictionary<int, PropMemory>();

    public int RemainingTargets => investigationTargets.Count;
    public int ShotBudget { get; private set; }
    public float SuspicionThreshold => suspicionThreshold;
    public float LastSelectedScore { get; private set; }
    public string LastSelectedReason { get; private set; } = string.Empty;

    public void BuildInvestigation(Vector3 snapshotPosition)
    {
        BuildCandidates(snapshotPosition, investigationRadius, true);
    }

    public void BuildSearch(Vector3 lastKnownPosition)
    {
        BuildCandidates(lastKnownPosition, investigationRadius, false);
    }

    public void SurveyVisibleProps(
        Transform eye,
        SeekerAIPerception perception)
    {
        if (perception == null) return;
        Vector3 origin = eye != null
            ? eye.position
            : transform.position + Vector3.up * 1.55f;
        Vector3 forward = eye != null ? eye.forward : transform.forward;
        HashSet<int> seen = new HashSet<int>();

        foreach (Collider candidate in Physics.OverlapSphere(
                     origin,
                     perception.ViewDistance,
                     Physics.DefaultRaycastLayers,
                     QueryTriggerInteraction.Collide))
        {
            PropTarget prop = candidate != null
                ? candidate.GetComponentInParent<PropTarget>()
                : null;
            if (prop == null ||
                candidate.GetComponentInParent<HiderHealth>() != null ||
                !SeekerShotTargetClassifier.IsGenuinelyCopyable(prop) ||
                !seen.Add(prop.GetInstanceID()))
            {
                continue;
            }

            Vector3 target = candidate.bounds.center;
            Vector3 delta = target - origin;
            if (Vector3.Angle(forward, delta) > perception.FieldOfView * 0.5f ||
                !perception.HasUnblockedLine(origin, target, prop.transform))
            {
                continue;
            }

            RememberObservedProp(prop.transform);
        }
    }

    public bool TryFindVisibleHighSuspicion(
        Transform eye,
        SeekerAIPerception perception,
        float range,
        float fieldOfView,
        out Collider target)
    {
        target = null;
        Vector3 origin = eye != null
            ? eye.position
            : transform.position + Vector3.up * 1.55f;
        Vector3 forward = eye != null ? eye.forward : transform.forward;
        SuspicionCandidate best = default;
        bool found = false;
        HashSet<int> seen = new HashSet<int>();

        foreach (Collider candidate in Physics.OverlapSphere(
                     origin,
                     range,
                     Physics.DefaultRaycastLayers,
                     QueryTriggerInteraction.Collide))
        {
            if (!TryScoreCandidate(candidate, origin, false, out SuspicionCandidate scored) ||
                !seen.Add(GetCandidateIdentity(candidate)))
            {
                continue;
            }

            Vector3 delta = candidate.bounds.center - origin;
            if (Vector3.Angle(forward, delta) > fieldOfView * 0.5f ||
                perception == null ||
                !perception.HasUnblockedLine(origin, candidate.bounds.center,
                    candidate.transform))
            {
                continue;
            }

            if (!found || scored.Score > best.Score)
            {
                best = scored;
                found = true;
            }
        }

        if (!found || best.Score < suspicionThreshold) return false;
        target = best.Collider;
        LastSelectedScore = best.Score;
        LastSelectedReason = best.Reason;
        return true;
    }

    public bool TryTakeNext(out Collider target)
    {
        while (investigationTargets.Count > 0)
        {
            SuspicionCandidate candidate = investigationTargets.Dequeue();
            target = candidate.Collider;
            if (target != null)
            {
                LastSelectedScore = candidate.Score;
                LastSelectedReason = candidate.Reason;
                return true;
            }
        }

        target = null;
        return false;
    }

    public void Clear()
    {
        investigationTargets.Clear();
        ShotBudget = 0;
        LastSelectedScore = 0f;
        LastSelectedReason = string.Empty;
    }

    private void BuildCandidates(
        Vector3 snapshotPosition,
        float radius,
        bool antiCamp)
    {
        investigationTargets.Clear();
        List<SuspicionCandidate> candidates = new List<SuspicionCandidate>();
        HashSet<int> seen = new HashSet<int>();

        foreach (Collider candidate in Physics.OverlapSphere(
                     snapshotPosition,
                     radius,
                     Physics.DefaultRaycastLayers,
                     QueryTriggerInteraction.Collide))
        {
            if (candidate == null || candidate.transform.IsChildOf(transform) ||
                !seen.Add(GetCandidateIdentity(candidate)) ||
                !TryScoreCandidate(
                    candidate,
                    snapshotPosition,
                    antiCamp,
                    out SuspicionCandidate scored) ||
                scored.Score < suspicionThreshold)
            {
                continue;
            }
            candidates.Add(scored);
        }

        ShotBudget = Random.Range(1, 4);
        foreach (SuspicionCandidate candidate in
                 candidates.OrderByDescending(item => item.Score))
        {
            if (investigationTargets.Count >= ShotBudget) break;
            investigationTargets.Enqueue(candidate);
        }
    }

    private bool TryScoreCandidate(
        Collider candidate,
        Vector3 evidenceCenter,
        bool antiCamp,
        out SuspicionCandidate scored)
    {
        scored = default;
        if (candidate == null ||
            candidate.GetComponentInParent<HiderHealth>() != null)
        {
            return false;
        }

        SeekerShotResult classification =
            SeekerShotTargetClassifier.Classify(candidate);
        if (classification == SeekerShotResult.Clone)
        {
            scored = new SuspicionCandidate(
                candidate,
                100f,
                "visible Clone signature");
            return true;
        }

        if (classification != SeekerShotResult.ValidDisguiseProp)
        {
            return false;
        }

        Transform identity = candidate.GetComponentInParent<PropTarget>().transform;
        int id = identity.GetInstanceID();
        float distance = Vector3.Distance(candidate.bounds.center, evidenceCenter);
        float score = antiCamp ? 35f : 40f;
        string reason = antiCamp
            ? "copyable prop inside the Anti-Camp alert region"
            : "copyable prop close to the last-known position";

        if (propMemory.TryGetValue(id, out PropMemory previous))
        {
            float age = Mathf.Max(0f, Time.time - previous.UpdatedAt);
            score += Mathf.Max(0f, previous.Score - age * scoreDecayPerSecond);
            if (Vector3.Distance(previous.Position, identity.position) > 0.12f)
            {
                score += 60f;
                reason += ", moved since the previous observation";
            }
            if (Quaternion.Angle(previous.Rotation, identity.rotation) > 12f)
            {
                score += 25f;
                reason += ", changed orientation";
            }
            if ((previous.Scale - identity.lossyScale).sqrMagnitude > 0.01f)
            {
                score += 20f;
                reason += ", changed size";
            }
        }

        score += Mathf.Clamp((investigationRadius - distance) * 2f, 0f, 12f);
        float upAngle = Vector3.Angle(identity.up, Vector3.up);
        if (upAngle > 20f)
        {
            score += 15f;
            reason += ", unusually tilted";
        }

        propMemory[id] = new PropMemory
        {
            Position = identity.position,
            Rotation = identity.rotation,
            Scale = identity.lossyScale,
            Score = Mathf.Min(score, 100f),
            UpdatedAt = Time.time
        };
        scored = new SuspicionCandidate(candidate, score, reason);
        return true;
    }

    private void RememberObservedProp(Transform identity)
    {
        int id = identity.GetInstanceID();
        float retainedScore = 0f;
        if (propMemory.TryGetValue(id, out PropMemory previous))
        {
            float age = Mathf.Max(0f, Time.time - previous.UpdatedAt);
            retainedScore = Mathf.Max(0f, previous.Score - age * scoreDecayPerSecond);
            if (Vector3.Distance(previous.Position, identity.position) > 0.12f)
                retainedScore += 60f;
            if (Quaternion.Angle(previous.Rotation, identity.rotation) > 12f)
                retainedScore += 25f;
            if ((previous.Scale - identity.lossyScale).sqrMagnitude > 0.01f)
                retainedScore += 20f;
        }

        propMemory[id] = new PropMemory
        {
            Position = identity.position,
            Rotation = identity.rotation,
            Scale = identity.lossyScale,
            Score = Mathf.Min(retainedScore, 100f),
            UpdatedAt = Time.time
        };
    }

    private static int GetCandidateIdentity(Collider candidate)
    {
        HiderCloneInstance clone = candidate.GetComponentInParent<HiderCloneInstance>();
        if (clone != null) return clone.GetInstanceID();
        PropTarget prop = candidate.GetComponentInParent<PropTarget>();
        return prop != null ? prop.GetInstanceID() : candidate.GetInstanceID();
    }
}
