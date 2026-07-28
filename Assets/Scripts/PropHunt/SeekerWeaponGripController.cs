using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class SeekerWeaponGripController : MonoBehaviour
{
    [Header("Animator and movement")]
    [SerializeField] private Animator animator;
    [SerializeField] private CharacterController movementSource;
    [SerializeField] private NavMeshAgent aiMovementSource;
    [SerializeField] private SeekerAIController aiController;

    [Header("Weapon binding")]
    [SerializeField] private Transform rightHand;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform chest;
    [SerializeField] private Transform worldGunPivot;
    [SerializeField] private Transform rightHandGrip;
    [SerializeField] private Transform leftHandGrip;
    [SerializeField] private Transform rightHandIkTarget;
    [SerializeField] private Vector3 gripSeparationRestLocal;
    [SerializeField] private Quaternion rightHandTargetRestLocalRotation = Quaternion.identity;
    [SerializeField] private int manualAlignmentVersion;

    [Header("Animation Rigging")]
    [SerializeField] private Rig weaponRig;
    [SerializeField] private TwoBoneIKConstraint rightArmIk;
    [SerializeField] private TwoBoneIKConstraint leftArmIk;
    [SerializeField] private MultiAimConstraint upperBodyAim;
    [SerializeField] private Transform aiAimTarget;
    [SerializeField, Min(0.1f)] private float weightBlendSpeed = 5f;
    [SerializeField, Min(0.1f)] private float aimTargetBlendSpeed = 10f;

    public Animator Animator => animator;
    public CharacterController MovementSource => movementSource;
    public Transform RightHand => rightHand;
    public Transform LeftHand => leftHand;
    public Transform Chest => chest;
    public Transform WorldGunPivot => worldGunPivot;
    public Transform RightHandGrip => rightHandGrip;
    public Transform LeftHandGrip => leftHandGrip;
    public Transform RightHandIkTarget => rightHandIkTarget;
    public bool LeftHandIkEnabled => leftArmIk != null && leftArmIk.enabled;
    public int ManualAlignmentVersion => manualAlignmentVersion;
    public Rig WeaponRig => weaponRig;
    public TwoBoneIKConstraint RightArmIk => rightArmIk;
    public TwoBoneIKConstraint LeftArmIk => leftArmIk;
    public MultiAimConstraint UpperBodyAim => upperBodyAim;
    public Transform AIAimTarget => aiAimTarget;
    public int RigEvaluationCount { get; private set; }
    public Vector3 LastDesiredRightHandTargetPosition { get; private set; }
    public float LastArmRootSeparation { get; private set; }

    public void Configure(
        Animator configuredAnimator,
        Transform configuredRightHand,
        Transform configuredLeftHand,
        Transform configuredChest,
        Transform configuredWorldGunPivot,
        Transform configuredRightHandGrip,
        Transform configuredLeftHandGrip,
        Transform configuredRightHandIkTarget,
        Rig configuredWeaponRig,
        TwoBoneIKConstraint configuredRightArmIk,
        TwoBoneIKConstraint configuredLeftArmIk,
        MultiAimConstraint configuredUpperBodyAim,
        Transform configuredAimTarget,
        int configuredAlignmentVersion)
    {
        animator = configuredAnimator;
        movementSource = configuredAnimator != null
            ? configuredAnimator.GetComponentInParent<CharacterController>()
            : null;
        aiMovementSource = configuredAnimator != null
            ? configuredAnimator.GetComponentInParent<NavMeshAgent>()
            : null;
        aiController = configuredAnimator != null
            ? configuredAnimator.GetComponentInParent<SeekerAIController>()
            : null;
        rightHand = configuredRightHand;
        leftHand = configuredLeftHand;
        chest = configuredChest;
        worldGunPivot = configuredWorldGunPivot;
        rightHandGrip = configuredRightHandGrip;
        leftHandGrip = configuredLeftHandGrip;
        rightHandIkTarget = configuredRightHandIkTarget;
        gripSeparationRestLocal =
            configuredRightHandGrip != null &&
            configuredLeftHandGrip != null &&
            configuredAnimator != null
                ? configuredAnimator.transform.InverseTransformVector(
                    configuredLeftHandGrip.position - configuredRightHandGrip.position)
                : Vector3.zero;
        rightHandTargetRestLocalRotation = configuredRightHandIkTarget != null
            ? configuredRightHandIkTarget.localRotation
            : Quaternion.identity;
        weaponRig = configuredWeaponRig;
        rightArmIk = configuredRightArmIk;
        leftArmIk = configuredLeftArmIk;
        upperBodyAim = configuredUpperBodyAim;
        aiAimTarget = configuredAimTarget;
        manualAlignmentVersion = configuredAlignmentVersion;
        InitializeAnimatorParameters();
        ApplyWeightsImmediately();
    }

    private void Reset()
    {
        animator = GetComponent<Animator>();
    }

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (movementSource == null)
            movementSource = GetComponentInParent<CharacterController>();
        if (aiMovementSource == null)
            aiMovementSource = GetComponentInParent<NavMeshAgent>();
        if (aiController == null)
            aiController = GetComponentInParent<SeekerAIController>();
        InitializeAnimatorParameters();
    }

    private void OnEnable()
    {
        InitializeAnimatorParameters();
        ApplyWeightsImmediately();
    }

    private void Update()
    {
        UpdateAnimatorLocomotion();
        UpdateAimTarget();
        UpdateRigWeights();
    }

    private void LateUpdate()
    {
        if (!HasCompleteRigBinding())
        {
            return;
        }

        // The pivot owns an absolute approved local transform. Runtime never adds
        // offsets, so Animator/Rig evaluation cannot make the gun drift each frame.
        RigEvaluationCount++;
    }

    private void UpdateAnimatorLocomotion()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        Vector3 velocity = aiMovementSource != null && aiMovementSource.enabled
            ? aiMovementSource.velocity
            : movementSource != null ? movementSource.velocity : Vector3.zero;
        float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == "Speed" &&
                parameter.type == AnimatorControllerParameterType.Float)
                animator.SetFloat(parameter.nameHash, horizontalSpeed);
            else if (parameter.name == "MotionSpeed" &&
                     parameter.type == AnimatorControllerParameterType.Float)
                animator.SetFloat(
                    parameter.nameHash,
                    horizontalSpeed > 0.01f ? 1f : 0f);
            else if (parameter.name == "Grounded" &&
                     parameter.type == AnimatorControllerParameterType.Bool)
                animator.SetBool(
                    parameter.nameHash,
                    aiMovementSource != null && aiMovementSource.enabled ||
                    movementSource == null || movementSource.isGrounded);
        }
    }

    private void UpdateAimTarget()
    {
        if (aiAimTarget == null)
        {
            return;
        }

        Vector3 desired = aiController != null
            ? aiController.ResolvePresentationAimPoint()
            : transform.position + transform.forward * 12f + Vector3.up * 1.35f;
        float blend = 1f - Mathf.Exp(-aimTargetBlendSpeed * Time.deltaTime);
        aiAimTarget.position = Vector3.Lerp(aiAimTarget.position, desired, blend);
        UpdateRightHandTarget();
    }

    private void UpdateRightHandTarget()
    {
        if (rightHandIkTarget == null || chest == null || animator == null)
        {
            return;
        }

        Vector3 aimDirection = aiAimTarget.position - chest.position;
        if (aimDirection.sqrMagnitude < 0.0001f)
        {
            aimDirection = animator.transform.forward;
        }
        aimDirection.Normalize();
        Quaternion aimDelta =
            Quaternion.FromToRotation(animator.transform.forward, aimDirection);
        Transform leftArmRoot = leftHand.parent.parent;
        Transform rightArmRoot = rightHand.parent.parent;
        float leftArmReach =
            Vector3.Distance(leftHand.position, leftHand.parent.position) +
            Vector3.Distance(leftHand.parent.position, leftArmRoot.position);
        float rightArmReach =
            Vector3.Distance(rightHand.position, rightHand.parent.position) +
            Vector3.Distance(rightHand.parent.position, rightArmRoot.position);
        Vector3 desiredGripSeparation =
            aimDelta * animator.transform.TransformVector(gripSeparationRestLocal);
        // Solve one translation that keeps both grips inside their respective
        // arm-reach spheres. If P is the right grip, then
        // P + desiredGripSeparation is the left grip.
        Vector3 rightSphereCenter = rightArmRoot.position;
        Vector3 leftSphereCenterForRightGrip =
            leftArmRoot.position - desiredGripSeparation;
        float shortestReach = Mathf.Min(leftArmReach, rightArmReach);
        Vector3 desiredPosition =
            Vector3.Lerp(rightSphereCenter, leftSphereCenterForRightGrip, 0.5f) +
            aimDirection * (shortestReach * 0.08f) -
            animator.transform.up * (shortestReach * 0.18f);
        LastDesiredRightHandTargetPosition = desiredPosition;
        LastArmRootSeparation =
            Vector3.Distance(rightArmRoot.position, leftArmRoot.position);
        Quaternion restWorldRotation =
            animator.transform.rotation * rightHandTargetRestLocalRotation;
        Quaternion desiredRotation = aimDelta * restWorldRotation;
        rightHandIkTarget.SetPositionAndRotation(
            desiredPosition,
            desiredRotation);
    }

    private void UpdateRigWeights()
    {
        if (!HasCompleteRigBinding())
        {
            return;
        }

        ResolveDesiredWeights(
            out float desiredRig,
            out float desiredRightHand,
            out float desiredLeftHand,
            out float desiredAim);
        float step = weightBlendSpeed * Time.deltaTime;
        weaponRig.weight = Mathf.MoveTowards(
            weaponRig.weight, desiredRig, step);
        rightArmIk.weight = Mathf.MoveTowards(
            rightArmIk.weight, desiredRightHand, step);
        leftArmIk.weight = Mathf.MoveTowards(
            leftArmIk.weight, desiredLeftHand, step);
        upperBodyAim.weight = Mathf.MoveTowards(
            upperBodyAim.weight, desiredAim, step);
    }

    private void ApplyWeightsImmediately()
    {
        if (!HasCompleteRigBinding())
        {
            return;
        }

        ResolveDesiredWeights(
            out float rigWeight,
            out float rightWeight,
            out float leftWeight,
            out float aimWeight);
        weaponRig.weight = rigWeight;
        rightArmIk.weight = rightWeight;
        leftArmIk.weight = leftWeight;
        upperBodyAim.weight = aimWeight;
    }

    private void ResolveDesiredWeights(
        out float rigWeight,
        out float rightHandWeight,
        out float leftHandWeight,
        out float aimWeight)
    {
        SeekerAIState state = aiController != null
            ? aiController.CurrentState
            : SeekerAIState.PreparationWait;
        bool eliminated = state == SeekerAIState.Eliminated;
        rigWeight = eliminated ? 0f : 1f;
        rightHandWeight = eliminated ? 0f : 1f;
        leftHandWeight = eliminated
            ? 0f
            : state == SeekerAIState.PreparationWait ? 0.9f : 1f;

        switch (state)
        {
            case SeekerAIState.Attack:
                aimWeight = 1f;
                break;
            case SeekerAIState.Investigate:
            case SeekerAIState.Chase:
            case SeekerAIState.SearchLastKnown:
            case SeekerAIState.Reloading:
                aimWeight = 0.75f;
                break;
            case SeekerAIState.Patrol:
            case SeekerAIState.Observe:
            case SeekerAIState.ReturnToPatrol:
                aimWeight = 0.35f;
                break;
            default:
                aimWeight = 0.15f;
                break;
        }
        if (eliminated) aimWeight = 0f;
    }

    private bool HasCompleteRigBinding()
    {
        return animator != null &&
               rightHand != null &&
               leftHand != null &&
               chest != null &&
               worldGunPivot != null &&
               rightHandGrip != null &&
               leftHandGrip != null &&
               rightHandIkTarget != null &&
               weaponRig != null &&
               rightArmIk != null &&
               leftArmIk != null &&
               upperBodyAim != null &&
               aiAimTarget != null;
    }

    private void InitializeAnimatorParameters()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == "Speed" &&
                parameter.type == AnimatorControllerParameterType.Float)
                animator.SetFloat(parameter.nameHash, 0f);
            else if (parameter.name == "Grounded" &&
                     parameter.type == AnimatorControllerParameterType.Bool)
                animator.SetBool(parameter.nameHash, true);
            else if (parameter.name == "FreeFall" &&
                     parameter.type == AnimatorControllerParameterType.Bool)
                animator.SetBool(parameter.nameHash, false);
        }
    }
}
