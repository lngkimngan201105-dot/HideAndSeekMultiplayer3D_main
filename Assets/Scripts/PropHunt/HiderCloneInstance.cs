using UnityEngine;

public class HiderCloneInstance : MonoBehaviour
{
    [SerializeField] private HiderCloneAbility owner;
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private Collider hitCollider;
    [SerializeField] private bool wasCreatedOnWall;
    [SerializeField] private Vector3 capturedWallNormal;

    public HiderCloneAbility Owner => owner;
    public bool HasBeenHit { get; private set; }
    public bool WasCreatedOnWall => wasCreatedOnWall;
    public Vector3 CapturedWallNormal => capturedWallNormal;

    private bool isBeingDestroyed;

    public void Initialize(
        HiderCloneAbility cloneOwner,
        GameObject cloneVisual,
        Collider cloneHitCollider,
        bool createdOnWall,
        Vector3 wallNormal)
    {
        owner = cloneOwner;
        visualRoot = cloneVisual;
        hitCollider = cloneHitCollider;
        wasCreatedOnWall = createdOnWall;
        capturedWallNormal = wallNormal;
    }

    public void ReceiveHit(GameObject attacker = null)
    {
        if (HasBeenHit || isBeingDestroyed)
        {
            return;
        }

        HasBeenHit = true;
        if (owner != null)
        {
            owner.HandleCloneHit(this);
        }

        DestroyClone();
    }

    public void DestroyClone()
    {
        if (isBeingDestroyed)
        {
            return;
        }

        isBeingDestroyed = true;
        if (owner != null)
        {
            owner.NotifyCloneDestroyed(this);
        }

        if (hitCollider != null)
        {
            hitCollider.enabled = false;
        }

        if (visualRoot != null)
        {
            visualRoot.SetActive(false);
        }

        Destroy(gameObject);
    }

#if UNITY_EDITOR
    [ContextMenu("Debug/Simulate Clone Hit")]
    private void DebugSimulateCloneHit()
    {
        ReceiveHit(null);
    }
#endif
}
