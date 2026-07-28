using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public sealed class SeekerAIAnimatorDriver : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private NavMeshAgent agent;

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");
    private static readonly int GroundedId = Animator.StringToHash("Grounded");

    public void Configure(Animator configuredAnimator, NavMeshAgent configuredAgent)
    {
        animator = configuredAnimator;
        agent = configuredAgent;
        if (animator != null)
        {
            animator.applyRootMotion = false;
        }
    }

    private void Update()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return;
        }

        float speed = agent != null && agent.enabled ? agent.velocity.magnitude : 0f;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == SpeedId &&
                parameter.type == AnimatorControllerParameterType.Float)
                animator.SetFloat(SpeedId, speed);
            else if (parameter.nameHash == MotionSpeedId &&
                     parameter.type == AnimatorControllerParameterType.Float)
                animator.SetFloat(MotionSpeedId, speed > 0.05f ? 1f : 0f);
            else if (parameter.nameHash == GroundedId &&
                     parameter.type == AnimatorControllerParameterType.Bool)
                animator.SetBool(GroundedId, true);
        }
    }
}
