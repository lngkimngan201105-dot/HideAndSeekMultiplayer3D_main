using UnityEngine;

public static class SeekerShotTargetClassifier
{
    public static SeekerShotResult Classify(Collider hitCollider)
    {
        if (hitCollider == null)
        {
            return SeekerShotResult.Miss;
        }

        // Priority is intentional: clone visuals can contain copied prop data and the
        // real Hider can currently be represented by a prop-shaped hierarchy.
        if (hitCollider.GetComponentInParent<HiderCloneInstance>() != null)
        {
            return SeekerShotResult.Clone;
        }

        if (hitCollider.GetComponentInParent<HiderHealth>() != null)
        {
            return SeekerShotResult.Hider;
        }

        PropTarget prop = hitCollider.GetComponentInParent<PropTarget>();
        if (IsGenuinelyCopyable(prop) &&
            prop.GetComponentInParent<HiderHealth>() == null &&
            prop.GetComponentInParent<HiderCloneInstance>() == null)
        {
            return SeekerShotResult.ValidDisguiseProp;
        }

        return SeekerShotResult.World;
    }

    public static bool IsGenuinelyCopyable(PropTarget prop)
    {
        if (prop == null || !prop.GameplayEnabled ||
            prop.visualParts == null || prop.visualParts.Length == 0)
        {
            return false;
        }

        foreach (PropVisualPartData part in prop.visualParts)
        {
            if (part == null || part.mesh == null ||
                part.materials == null || part.materials.Length == 0)
            {
                return false;
            }

            bool hasMaterial = false;
            foreach (Material material in part.materials)
            {
                hasMaterial |= material != null;
            }
            if (!hasMaterial) return false;
        }

        return true;
    }
}
