using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class SeekerWeaponGripController : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private CharacterController movementSource;
    [SerializeField] private Transform rightHand;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform worldGunPivot;
    [SerializeField] private Transform rightHandGrip;
    [SerializeField] private Transform leftHandGrip;
    [SerializeField] private bool enableLeftHandIk = true;
    [SerializeField, Range(0f, 1f)] private float leftHandIkWeight = 1f;

    public Animator Animator => animator;
    public CharacterController MovementSource => movementSource;
    public Transform RightHand => rightHand;
    public Transform LeftHand => leftHand;
    public Transform WorldGunPivot => worldGunPivot;
    public Transform RightHandGrip => rightHandGrip;
    public Transform LeftHandGrip => leftHandGrip;
    public bool LeftHandIkEnabled => enableLeftHandIk;
    public int IkCallbackCount { get; private set; }

    public void Configure(
        Animator configuredAnimator,
        Transform configuredRightHand,
        Transform configuredLeftHand,
        Transform configuredWorldGunPivot,
        Transform configuredRightHandGrip,
        Transform configuredLeftHandGrip)
    {
        animator = configuredAnimator;
        movementSource = configuredAnimator != null
            ? configuredAnimator.GetComponentInParent<CharacterController>()
            : null;
        rightHand = configuredRightHand;
        leftHand = configuredLeftHand;
        worldGunPivot = configuredWorldGunPivot;
        rightHandGrip = configuredRightHandGrip;
        leftHandGrip = configuredLeftHandGrip;
        enableLeftHandIk = true;
        leftHandIkWeight = 1f;
        InitializeAnimatorParameters();
        AlignRightHandGrip();
    }

    private void Reset()
    {
        animator = GetComponent<Animator>();
    }

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (movementSource == null) movementSource = GetComponentInParent<CharacterController>();
        InitializeAnimatorParameters();
    }

    private void OnEnable()
    {
        InitializeAnimatorParameters();
    }

    private void LateUpdate()
    {
        if (worldGunPivot == null || rightHand == null || rightHandGrip == null) return;
        if (worldGunPivot.parent != rightHand)
            worldGunPivot.SetParent(rightHand, true);
        AlignRightHandGrip();
    }

    private void Update()
    {
        if (animator == null || animator.runtimeAnimatorController == null || movementSource == null) return;
        Vector3 velocity = movementSource.velocity;
        float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == "Speed" && parameter.type == AnimatorControllerParameterType.Float)
                animator.SetFloat(parameter.nameHash, horizontalSpeed);
            else if (parameter.name == "MotionSpeed" && parameter.type == AnimatorControllerParameterType.Float)
                animator.SetFloat(parameter.nameHash, horizontalSpeed > 0.01f ? 1f : 0f);
            else if (parameter.name == "Grounded" && parameter.type == AnimatorControllerParameterType.Bool)
                animator.SetBool(parameter.nameHash, movementSource.isGrounded);
        }
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (!enableLeftHandIk || animator == null || leftHand == null || leftHandGrip == null ||
            animator.runtimeAnimatorController == null)
            return;

        IkCallbackCount++;
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, leftHandIkWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, leftHandIkWeight);
        animator.SetIKPosition(AvatarIKGoal.LeftHand, leftHandGrip.position);
        animator.SetIKRotation(AvatarIKGoal.LeftHand, leftHandGrip.rotation);
    }

    private void AlignRightHandGrip()
    {
        if (worldGunPivot == null || rightHand == null || rightHandGrip == null) return;
        worldGunPivot.position += rightHand.position - rightHandGrip.position;
    }

    private void InitializeAnimatorParameters()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == "Speed" && parameter.type == AnimatorControllerParameterType.Float)
                animator.SetFloat(parameter.nameHash, 0f);
            else if (parameter.name == "Grounded" && parameter.type == AnimatorControllerParameterType.Bool)
                animator.SetBool(parameter.nameHash, true);
            else if (parameter.name == "FreeFall" && parameter.type == AnimatorControllerParameterType.Bool)
                animator.SetBool(parameter.nameHash, false);
        }
    }
}
