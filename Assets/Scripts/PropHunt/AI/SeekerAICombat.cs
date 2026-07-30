using UnityEngine;

[DisallowMultipleComponent]
public sealed class SeekerAICombat : MonoBehaviour
{
    [SerializeField] private SeekerRaycastWeapon weapon;
    [SerializeField] private SeekerWeaponEnergy energy;
    [SerializeField] private Transform worldMuzzle;
    [SerializeField] private SeekerAIPerception perception;
    [SerializeField, Min(0.35f)] private float minimumShotInterval = 0.35f;
    [SerializeField, Min(0.35f)] private float maximumShotInterval = 0.55f;
    [SerializeField, Range(1f, 45f)] private float bodyAimTolerance = 9f;
    [SerializeField, Range(1f, 60f)] private float muzzleAimTolerance = 18f;
    [SerializeField, Range(0f, 15f)] private float nearAimError = 2f;
    [SerializeField, Range(0f, 15f)] private float farAimError = 3.5f;

    private float nextDecisionShotAt;
    private SeekerTeamCoordinator teamCoordinator;
    private SeekerAIController owner;

    public float MinimumShotInterval => minimumShotInterval;
    public float MaximumShotInterval => maximumShotInterval;
    public float BodyAimTolerance => bodyAimTolerance;
    public float MuzzleAimTolerance => muzzleAimTolerance;
    public Vector2 AimErrorRange => new Vector2(nearAimError, farAimError);

    public void Configure(
        SeekerRaycastWeapon configuredWeapon,
        SeekerWeaponEnergy configuredEnergy,
        Transform configuredWorldMuzzle,
        SeekerAIPerception configuredPerception)
    {
        weapon = configuredWeapon;
        energy = configuredEnergy;
        worldMuzzle = configuredWorldMuzzle;
        perception = configuredPerception;
    }

    public void ConfigureTeam(
        SeekerTeamCoordinator configuredCoordinator,
        SeekerAIController configuredOwner)
    {
        teamCoordinator = configuredCoordinator;
        owner = configuredOwner;
    }

    public void ConfigureAimError(float nearError, float farError)
    {
        nearAimError = Mathf.Max(0f, nearError);
        farAimError = Mathf.Max(nearAimError, farError);
    }

    public bool TryFireAtHider(HiderPerceptionSignature target)
    {
        return target != null && TryFireAtPoint(target.GetAimPoint(), target.transform);
    }

    public bool TryFireAtCollider(Collider target)
    {
        if (target == null) return false;
        return TryFireAtPoint(target.bounds.center, target.transform);
    }

    public void EnsureReload()
    {
        if (energy != null && energy.CurrentCharges <= 0 && !energy.IsReloading)
        {
            energy.TryStartReloadFromAI();
        }
    }

    public bool TryEarlyReload(bool hasImmediateTarget)
    {
        if (hasImmediateTarget || energy == null || energy.IsReloading ||
            energy.CurrentCharges <= 0 ||
            energy.CurrentCharges >= energy.MaxCharges)
        {
            return false;
        }

        return energy.TryStartReloadFromAI();
    }

    private bool TryFireAtPoint(Vector3 targetPoint, Transform acceptedTarget)
    {
        if (weapon == null || energy == null || worldMuzzle == null ||
            energy.IsReloading || Time.time < nextDecisionShotAt)
        {
            return false;
        }

        if (energy.CurrentCharges <= 0)
        {
            EnsureReload();
            return false;
        }

        if (perception != null &&
            !perception.HasUnblockedLine(worldMuzzle.position, targetPoint, acceptedTarget))
        {
            return false;
        }

        Vector3 direction = targetPoint - worldMuzzle.position;
        float distance = direction.magnitude;
        if (distance < 0.001f || !IsFacingTarget(targetPoint))
        {
            return false;
        }

        bool hasPermit = teamCoordinator == null ||
                         teamCoordinator.TryAcquireFirePermit(owner);
        if (!hasPermit) return false;

        float error = distance <= 10f ? nearAimError : farAimError;
        direction = Quaternion.Euler(
            Random.Range(-error, error),
            Random.Range(-error, error),
            0f) * direction.normalized;

        bool fired = weapon.TryFireFromAI(worldMuzzle.position, direction);
        teamCoordinator?.CompleteFirePermit(owner, fired);
        if (fired)
        {
            nextDecisionShotAt = Time.time + Random.Range(minimumShotInterval, maximumShotInterval);
            EnsureReload();
        }
        return fired;
    }

    private bool IsFacingTarget(Vector3 targetPoint)
    {
        Vector3 bodyDirection = targetPoint - transform.position;
        bodyDirection.y = 0f;
        if (bodyDirection.sqrMagnitude > 0.001f &&
            Vector3.Angle(transform.forward, bodyDirection) > bodyAimTolerance)
        {
            return false;
        }

        Vector3 muzzleDirection = targetPoint - worldMuzzle.position;
        return muzzleDirection.sqrMagnitude <= 0.001f ||
               Vector3.Angle(worldMuzzle.forward, muzzleDirection) <=
               muzzleAimTolerance;
    }
}
