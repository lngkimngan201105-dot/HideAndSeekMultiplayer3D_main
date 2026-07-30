using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SeekerAIPerception : MonoBehaviour
{
    [SerializeField] private Transform eye;
    [SerializeField] private HiderPerceptionSignature hider;
    [SerializeField, Min(0.1f)] private float viewDistance = 22f;
    [SerializeField, Range(1f, 179f)] private float fieldOfView = 75f;
    [SerializeField] private LayerMask sightMask = Physics.DefaultRaycastLayers;

    private bool hasValidPriorSight;

    public bool HasLineOfSight { get; private set; }
    public bool CanIdentifyHider { get; private set; }
    public float DistanceToHider { get; private set; } = float.PositiveInfinity;
    public Vector3 LastVisiblePosition { get; private set; }
    public HiderPerceptionSignature Hider => hider;
    public float ViewDistance => viewDistance;
    public float FieldOfView => fieldOfView;
    public Transform Eye => eye;

    public void Configure(Transform configuredEye, HiderPerceptionSignature configuredHider)
    {
        eye = configuredEye;
        hider = configuredHider;
        hasValidPriorSight = false;
    }

    public void ConfigureTuning(float configuredViewDistance, float configuredFieldOfView)
    {
        viewDistance = Mathf.Max(0.1f, configuredViewDistance);
        fieldOfView = Mathf.Clamp(configuredFieldOfView, 1f, 179f);
    }

    public void Observe()
    {
        HasLineOfSight = false;
        CanIdentifyHider = false;
        DistanceToHider = float.PositiveInfinity;
        if (hider == null || !hider.gameObject.activeInHierarchy)
        {
            return;
        }

        Vector3 origin = eye != null ? eye.position : transform.position + Vector3.up * 1.55f;
        Vector3 target = hider.GetAimPoint();
        Vector3 delta = target - origin;
        DistanceToHider = delta.magnitude;
        if (DistanceToHider > viewDistance || DistanceToHider < 0.001f)
        {
            return;
        }

        Vector3 viewForward = eye != null ? eye.forward : transform.forward;
        if (Vector3.Angle(viewForward, delta) > fieldOfView * 0.5f)
        {
            return;
        }

        HasLineOfSight = HasUnblockedLine(origin, target, hider.transform);
        if (!HasLineOfSight)
        {
            return;
        }

        bool directEvidence = hider.IsHuman || hider.IsMoving ||
                              hider.ChangedRecently || hider.IsRevealed;
        if (directEvidence)
        {
            hasValidPriorSight = true;
        }

        CanIdentifyHider = directEvidence || hasValidPriorSight;
        if (CanIdentifyHider)
        {
            LastVisiblePosition = hider.transform.position;
        }
    }

    public bool HasUnblockedLine(Vector3 origin, Vector3 target, Transform acceptedTarget)
    {
        Vector3 delta = target - origin;
        float distance = delta.magnitude;
        if (distance < 0.001f)
        {
            return true;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            delta / distance,
            distance + 0.1f,
            sightMask,
            QueryTriggerInteraction.Collide);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.isTrigger)
            {
                continue;
            }

            Transform item = hit.collider.transform;
            if (item == transform || item.IsChildOf(transform))
            {
                continue;
            }

            if (acceptedTarget != null &&
                (item == acceptedTarget || item.IsChildOf(acceptedTarget)))
            {
                return true;
            }

            return false;
        }

        return false;
    }

    public void ForgetPriorSight()
    {
        hasValidPriorSight = false;
        CanIdentifyHider = false;
    }
}
