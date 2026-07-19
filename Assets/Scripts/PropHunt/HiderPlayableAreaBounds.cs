using UnityEngine;

public class HiderPlayableAreaBounds : MonoBehaviour
{
    [SerializeField] private BoxCollider boundsCollider;

    private Bounds _runtimeBounds;
    private bool _hasRuntimeBounds;

    public bool HasBounds => boundsCollider != null || _hasRuntimeBounds;
    public BoxCollider BoundsCollider => boundsCollider;

    public void Configure(BoxCollider configuredBoundsCollider)
    {
        boundsCollider = configuredBoundsCollider;
        _hasRuntimeBounds = false;
    }

    public bool TryGetBounds(out Bounds bounds)
    {
        if (boundsCollider != null)
        {
            bounds = boundsCollider.bounds;
            return true;
        }

        bounds = _runtimeBounds;
        return _hasRuntimeBounds;
    }

    public bool Contains(Vector3 worldPosition, float margin = 0f)
    {
        Bounds bounds = boundsCollider != null ? boundsCollider.bounds : _runtimeBounds;
        if (boundsCollider == null && !_hasRuntimeBounds)
        {
            return true;
        }

        Vector3 minimum = bounds.min + new Vector3(margin, 0f, margin);
        Vector3 maximum = bounds.max - new Vector3(margin, 0f, margin);
        return worldPosition.x >= minimum.x && worldPosition.x <= maximum.x &&
               worldPosition.y >= minimum.y && worldPosition.y <= maximum.y &&
               worldPosition.z >= minimum.z && worldPosition.z <= maximum.z;
    }

    public bool ContainsXZ(Vector3 worldPosition, float margin = 0f)
    {
        if (!TryGetBounds(out Bounds bounds))
        {
            return true;
        }

        return worldPosition.x >= bounds.min.x + margin &&
               worldPosition.x <= bounds.max.x - margin &&
               worldPosition.z >= bounds.min.z + margin &&
               worldPosition.z <= bounds.max.z - margin;
    }

    public void BuildFromStaticSceneColliders(Transform excludedRoot)
    {
        _hasRuntimeBounds = false;
        foreach (Collider candidate in FindObjectsOfType<Collider>(true))
        {
            if (candidate == null ||
                !candidate.enabled ||
                candidate.isTrigger ||
                candidate.attachedRigidbody != null ||
                (excludedRoot != null &&
                 (candidate.transform == excludedRoot || candidate.transform.IsChildOf(excludedRoot))))
            {
                continue;
            }

            Bounds candidateBounds = candidate.bounds;
            if (candidateBounds.size.sqrMagnitude <= 0.0001f)
            {
                continue;
            }

            if (!_hasRuntimeBounds)
            {
                _runtimeBounds = candidateBounds;
                _hasRuntimeBounds = true;
            }
            else
            {
                _runtimeBounds.Encapsulate(candidateBounds);
            }
        }
    }

    public static HiderPlayableAreaBounds ResolveOrCreate(Transform excludedRoot)
    {
        HiderPlayableAreaBounds existing = FindObjectOfType<HiderPlayableAreaBounds>();
        if (existing != null)
        {
            return existing;
        }

        GameObject boundsObject = new GameObject("HiderPlayableAreaBounds (Runtime)");
        HiderPlayableAreaBounds created = boundsObject.AddComponent<HiderPlayableAreaBounds>();
        created.BuildFromStaticSceneColliders(excludedRoot);
        return created;
    }
}
