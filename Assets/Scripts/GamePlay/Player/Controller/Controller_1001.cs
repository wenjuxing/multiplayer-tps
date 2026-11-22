using Mirror;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class Controller_1001 : NetworkBehaviour
{
    [Header("左手 IK")]
    [SerializeField] Rig leftHandIKRig;
    [SerializeField] Transform leftHandIKTarget;
    [SerializeField] Transform leftHandIdleWalkPose;
    [SerializeField] Transform leftHandRunPose;
    [SerializeField, Min(0f)] float leftHandIKBlendTime = 0.12f;
    [SerializeField, Min(0f)] float leftHandPoseBlendTime = 0.15f;

    PlayerContext ctx;
    private TwoBoneIKConstraint leftHandConstraint;
    float runPoseBlend;

    void Awake()
    {
        ctx = new PlayerContext
        {
            Move = GetComponent<MoveComp>(),
            Anim = GetComponent<AnimComp_1001>(),
        };

        if (leftHandIKRig != null)
            leftHandConstraint = leftHandIKRig.GetComponentInChildren<TwoBoneIKConstraint>(true);
        else
            leftHandConstraint = GetComponentInChildren<TwoBoneIKConstraint>(true);

        if (leftHandConstraint != null)
            leftHandConstraint.weight = 0f;

        if (leftHandIKTarget != null && leftHandIdleWalkPose != null)
        {
            leftHandIKTarget.localPosition = leftHandIdleWalkPose.localPosition;
            leftHandIKTarget.localRotation = leftHandIdleWalkPose.localRotation;
        }
    }

    public void InitializeRigReferences(
        TwoBoneIKConstraint constraint,
        Transform target,
        Transform idleWalkPose,
        Transform runPose)
    {
        leftHandConstraint = constraint;
        leftHandIKTarget = target;
        leftHandIdleWalkPose = idleWalkPose;
        leftHandRunPose = runPose;
        runPoseBlend = 0f;

        if (leftHandConstraint != null)
            leftHandConstraint.weight = 0f;

        if (leftHandIKTarget != null && leftHandIdleWalkPose != null)
        {
            leftHandIKTarget.localPosition = leftHandIdleWalkPose.localPosition;
            leftHandIKTarget.localRotation = leftHandIdleWalkPose.localRotation;
        }
    }

    void Update()
    {
        if (ctx.Move == null || ctx.Anim == null) return;

        if (isOwned)
        {
            ctx.Anim.SetSpeed(ctx.Move.AnimationSpeed);
            ctx.Anim.SetRunning(ctx.Move.IsRunning);
        }

        UpdateLeftHandIKWeight();
        UpdateLeftHandIKPose();
    }

    void UpdateLeftHandIKWeight()
    {
        if (leftHandConstraint == null) return;

        const float targetWeight = 1f;
        float blendSpeed = 1f / Mathf.Max(leftHandIKBlendTime, 0.001f);
        leftHandConstraint.weight = Mathf.MoveTowards(
            leftHandConstraint.weight,
            targetWeight,
            blendSpeed * Time.deltaTime);
    }

    void UpdateLeftHandIKPose()
    {
        if (leftHandIKTarget == null ||
            leftHandIdleWalkPose == null ||
            leftHandRunPose == null)
            return;

        bool isRunning = isOwned ? ctx.Anim.IsRunning : ctx.Anim.AnimatorIsRunning;
        float targetBlend = isRunning ? 1f : 0f;
        float blendSpeed = 1f / Mathf.Max(leftHandPoseBlendTime, 0.001f);
        runPoseBlend = Mathf.MoveTowards(
            runPoseBlend,
            targetBlend,
            blendSpeed * Time.deltaTime);

        leftHandIKTarget.localPosition = Vector3.Lerp(
            leftHandIdleWalkPose.localPosition,
            leftHandRunPose.localPosition,
            runPoseBlend);

        leftHandIKTarget.localRotation = Quaternion.Slerp(
            leftHandIdleWalkPose.localRotation,
            leftHandRunPose.localRotation,
            runPoseBlend);
    }
}
