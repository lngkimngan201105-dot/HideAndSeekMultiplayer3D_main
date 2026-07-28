using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class SeekerWeaponRigPlayModeValidationTool
{
    private const string RunningKey = "PropHunt.SeekerWeaponRigValidation.Running";
    private const string ResultKey = "PropHunt.SeekerWeaponRigValidation.Result";
    private static double validateAt;

    static SeekerWeaponRigPlayModeValidationTool()
    {
        if (SessionState.GetBool(RunningKey, false))
            Subscribe();
    }

    [MenuItem("Tools/Prop Hunt/Validate Seeker Weapon Rig In Play Mode")]
    public static void RunInteractive()
    {
        Start(false);
    }

    public static void RunCommandLineVerification()
    {
        Start(true);
    }

    private static void Start(bool commandLine)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Unity is already entering Play Mode.");
        SeekerPresentationSetupTool.SetupTwiceAndValidate();
        SessionState.SetBool(RunningKey, true);
        SessionState.SetBool(RunningKey + ".CommandLine", commandLine);
        SessionState.EraseString(ResultKey);
        Subscribe();
        EditorApplication.EnterPlaymode();
    }

    private static void Subscribe()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeState;
        EditorApplication.playModeStateChanged += HandlePlayModeState;
    }

    private static void HandlePlayModeState(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(RunningKey, false))
            return;

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            validateAt = EditorApplication.timeSinceStartup + 1.0;
            EditorApplication.update -= ValidateWhenReady;
            EditorApplication.update += ValidateWhenReady;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= ValidateWhenReady;
            string result = SessionState.GetString(ResultKey, string.Empty);
            bool commandLine =
                SessionState.GetBool(RunningKey + ".CommandLine", false);
            SessionState.EraseBool(RunningKey);
            SessionState.EraseBool(RunningKey + ".CommandLine");
            SessionState.EraseString(ResultKey);
            EditorApplication.playModeStateChanged -= HandlePlayModeState;
            if (string.IsNullOrEmpty(result))
                Debug.Log(
                    "[SeekerWeaponRigValidation] PLAY MODE PASS — " +
                    "PreparationWait/Idle, Walk_N, Run_N, Attack aim and 45-degree Rotate " +
                    "retain two-hand grip with stable absolute pivot.");
            else
                Debug.LogError(
                    "[SeekerWeaponRigValidation] PLAY MODE FAIL\n" + result);
            if (commandLine && Application.isBatchMode)
                EditorApplication.Exit(string.IsNullOrEmpty(result) ? 0 : 1);
        }
    }

    private static void ValidateWhenReady()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorApplication.update -= ValidateWhenReady;
            return;
        }
        if (EditorApplication.timeSinceStartup < validateAt)
            return;

        EditorApplication.update -= ValidateWhenReady;
        try
        {
            ValidateRuntimeRig();
            SessionState.SetString(ResultKey, string.Empty);
        }
        catch (Exception exception)
        {
            SessionState.SetString(ResultKey, exception.ToString());
        }
        EditorApplication.ExitPlaymode();
    }

    private static void ValidateRuntimeRig()
    {
        SeekerWeaponGripController grip =
            Object.FindObjectOfType<SeekerWeaponGripController>(true);
        if (grip == null)
            throw new InvalidOperationException(
                "SeekerWeaponGripController is missing in Play Mode.");
        Animator animator = grip.Animator;
        RigBuilder builder = animator != null ? animator.GetComponent<RigBuilder>() : null;
        if (animator == null || builder == null || !builder.graph.IsValid())
            throw new InvalidOperationException(
                "Animator or live RigBuilder PlayableGraph is invalid.");
        if (grip.RigEvaluationCount <= 0)
            throw new InvalidOperationException(
                "SeekerWeaponGripController did not evaluate after entering Play Mode.");
        if (Object.FindObjectsOfType<RigBuilder>(true).Length != 1 ||
            Object.FindObjectsOfType<Rig>(true).Length != 1 ||
            Object.FindObjectsOfType<TwoBoneIKConstraint>(true).Length != 2 ||
            Object.FindObjectsOfType<MultiAimConstraint>(true).Length != 1)
            throw new InvalidOperationException(
                "Runtime rig component counts are duplicated or incomplete.");

        Transform pivot = grip.WorldGunPivot;
        Vector3 pivotPosition = pivot.localPosition;
        Quaternion pivotRotation = pivot.localRotation;
        Vector3 pivotScale = pivot.localScale;
        CyberSoldierAnimationEventReceiver footstepReceiver =
            animator.GetComponent<CyberSoldierAnimationEventReceiver>();
        if (footstepReceiver == null)
            throw new InvalidOperationException(
                "CyberSoldierAnimationEventReceiver is missing.");
        bool footstepWarning = false;
        Application.LogCallback logCallback = (condition, _, type) =>
        {
            if (type == LogType.Warning &&
                condition.IndexOf("OnFootstep", StringComparison.OrdinalIgnoreCase) >= 0 &&
                condition.IndexOf("receiver", StringComparison.OrdinalIgnoreCase) >= 0)
                footstepWarning = true;
        };
        Application.logMessageReceived += logCallback;
        int footstepsBefore = footstepReceiver.ReceivedFootstepCount;
        footstepReceiver.OnFootstep(new AnimationEvent());
        Application.logMessageReceived -= logCallback;
        if (footstepWarning ||
            footstepReceiver.ReceivedFootstepCount != footstepsBefore + 1)
            throw new InvalidOperationException(
                "OnFootstep emitted a missing-receiver warning or was not received.");
        TwoBoneIKConstraintData leftData = grip.LeftArmIk.data;
        float leftReach =
            Vector3.Distance(leftData.root.position, leftData.mid.position) +
            Vector3.Distance(leftData.mid.position, leftData.tip.position);
        TwoBoneIKConstraintData rightData = grip.RightArmIk.data;
        Debug.Log(
            "[SeekerWeaponRigValidation] Runtime diagnostic " +
            $"graph={builder.graph.IsValid()}, layerConstraints={builder.layers[0].constraints?.Length ?? -1}, " +
            $"culling={animator.cullingMode}, " +
            $"rigWeight={grip.WeaponRig.weight:F3}, leftEnabled={grip.LeftArmIk.enabled}, " +
            $"leftWeight={grip.LeftArmIk.weight:F3}, leftReach={leftReach:F4}m, " +
            $"rootToTarget={Vector3.Distance(leftData.root.position, leftData.target.position):F4}m, " +
            $"tipToTarget={Vector3.Distance(leftData.tip.position, leftData.target.position):F4}m, " +
            $"rightRootToTarget={Vector3.Distance(rightData.root.position, rightData.target.position):F4}m, " +
            $"rightTipToTarget={Vector3.Distance(rightData.tip.position, rightData.target.position):F4}m, " +
            $"gripSeparation={Vector3.Distance(grip.RightHandGrip.position, grip.LeftHandGrip.position):F4}m, " +
            $"rightGripToIk={Vector3.Distance(grip.RightHandGrip.position, grip.RightHandIkTarget.position):F4}m, " +
            $"armRootSeparation={grip.LastArmRootSeparation:F4}m, " +
            $"targetToDesired={Vector3.Distance(grip.RightHandIkTarget.position, grip.LastDesiredRightHandTargetPosition):F4}m.");

        ValidatePose("PreparationWait/Idle", grip, pivotPosition, pivotRotation, pivotScale);
        EvaluateLocomotionPose("Walk_N", 2f, animator, builder, grip,
            pivotPosition, pivotRotation, pivotScale);
        EvaluateLocomotionPose("Run_N", 6f, animator, builder, grip,
            pivotPosition, pivotRotation, pivotScale);

        SeekerAIController ai = grip.GetComponentInParent<SeekerAIController>();
        if (ai == null)
            throw new InvalidOperationException("SeekerAIController is missing.");
        ai.enabled = false;
        FieldInfo stateField = typeof(SeekerAIController).GetField(
            "<CurrentState>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (stateField == null)
            throw new InvalidOperationException(
                "Could not access CurrentState for isolated Attack pose validation.");
        stateField.SetValue(ai, SeekerAIState.Attack);
        grip.AIAimTarget.position = ai.ResolvePresentationAimPoint();
        for (int i = 0; i < 20; i++)
        {
            grip.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
            builder.Evaluate(0.05f);
        }
        ValidatePose("Attack", grip, pivotPosition, pivotRotation, pivotScale);
        MethodInfo applyWeightsImmediately =
            typeof(SeekerWeaponGripController).GetMethod(
                "ApplyWeightsImmediately",
                BindingFlags.Instance | BindingFlags.NonPublic);
        if (applyWeightsImmediately == null)
            throw new InvalidOperationException(
                "Could not resolve the rig weight validation hook.");
        applyWeightsImmediately.Invoke(grip, null);
        builder.Evaluate(0.05f);
        if (grip.UpperBodyAim.weight < 0.99f ||
            grip.LeftArmIk.weight < 0.99f ||
            grip.RightArmIk.weight < 0.99f)
            throw new InvalidOperationException(
                $"Attack weights are incomplete: aim={grip.UpperBodyAim.weight:F3}, " +
                $"left={grip.LeftArmIk.weight:F3}, right={grip.RightArmIk.weight:F3}.");

        Transform muzzle = FindNamed(SceneManager.GetActiveScene(), "MuzzlePoint_World");
        Vector3 toAim = grip.AIAimTarget.position - muzzle.position;
        float attackAimAngle = Vector3.Angle(muzzle.forward, toAim);
        if (attackAimAngle > 15f)
            throw new InvalidOperationException(
                $"Attack muzzle is {attackAimAngle:F2} degrees away from SeekerAIAimTarget.");

        Transform seeker = ai.transform;
        seeker.rotation = Quaternion.AngleAxis(45f, Vector3.up) * seeker.rotation;
        grip.AIAimTarget.position = ai.ResolvePresentationAimPoint();
        for (int i = 0; i < 20; i++)
        {
            grip.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
            builder.Evaluate(0.05f);
        }
        ValidatePose("Rotate 45 degrees", grip,
            pivotPosition, pivotRotation, pivotScale);

        Debug.Log(
            "[SeekerWeaponRigValidation] Metrics " +
            $"rightGrip={Vector3.Distance(grip.RightHand.position, grip.RightHandGrip.position):F6}m, " +
            $"leftGrip={Vector3.Distance(grip.LeftHand.position, grip.LeftHandGrip.position):F6}m, " +
            $"attackMuzzleAngle={attackAimAngle:F3}deg, " +
            $"pivotPos={pivotPosition:F6}, pivotEuler={pivotRotation.eulerAngles:F3}, " +
            $"pivotScale={pivotScale:F6}.");
    }

    private static void EvaluateLocomotionPose(
        string label,
        float speed,
        Animator animator,
        RigBuilder builder,
        SeekerWeaponGripController grip,
        Vector3 pivotPosition,
        Quaternion pivotRotation,
        Vector3 pivotScale)
    {
        for (int i = 0; i < 10; i++)
        {
            grip.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
            animator.SetFloat("Speed", speed);
            animator.SetFloat("MotionSpeed", 1f);
            builder.Evaluate(0.1f);
        }
        ValidatePose(label, grip, pivotPosition, pivotRotation, pivotScale);
    }

    private static void ValidatePose(
        string label,
        SeekerWeaponGripController grip,
        Vector3 pivotPosition,
        Quaternion pivotRotation,
        Vector3 pivotScale)
    {
        float rightDistance =
            Vector3.Distance(grip.RightHand.position, grip.RightHandGrip.position);
        float leftDistance =
            Vector3.Distance(grip.LeftHand.position, grip.LeftHandGrip.position);
        if (rightDistance > 0.01f)
            throw new InvalidOperationException(
                $"{label}: RightHand is {rightDistance:F4}m from RightHandGrip.");
        if (leftDistance > 0.08f)
            throw new InvalidOperationException(
                $"{label}: LeftHand is {leftDistance:F4}m from LeftHandGrip.");
        if (Vector3.Distance(pivotPosition, grip.WorldGunPivot.localPosition) > 0.0001f ||
            Quaternion.Angle(pivotRotation, grip.WorldGunPivot.localRotation) > 0.01f ||
            Vector3.Distance(pivotScale, grip.WorldGunPivot.localScale) > 0.0001f)
            throw new InvalidOperationException(
                $"{label}: absolute SeekerWorldGunPivot transform drifted.");
        if (grip.WorldGunPivot.parent != grip.RightHand)
            throw new InvalidOperationException(
                $"{label}: world gun pivot is no longer parented to RightHand.");
        Debug.Log(
            $"[SeekerWeaponRigValidation] {label} PASS — " +
            $"right={rightDistance:F6}m, left={leftDistance:F6}m.");
    }

    private static Transform FindNamed(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            if (transform.name == name)
                return transform;
        throw new InvalidOperationException($"Scene transform not found: {name}");
    }
}
