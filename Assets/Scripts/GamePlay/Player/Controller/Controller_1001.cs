// 更新角色 1001 的移动动画和左手持枪姿势
using Mirror;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class Controller_1001 : NetworkBehaviour
{
    // 左手约束、跟随目标和持枪姿势的配置
    [Header("左手 IK")]
    [SerializeField] Rig leftHandIKRig;
    [SerializeField] Transform leftHandIKTarget;
    [SerializeField] Transform leftHandIdleWalkPose;
    [SerializeField] Transform leftHandRunPose;
    // 左手约束启用和持枪姿势切换的过渡时间
    [SerializeField, Min(0f)] float leftHandIKBlendTime = 0.12f;
    [SerializeField, Min(0f)] float leftHandPoseBlendTime = 0.15f;

    PlayerContext ctx;
    private TwoBoneIKConstraint leftHandConstraint;
    // 当前奔跑持枪姿势的混合比例
    float runPoseBlend;

    // 获取角色组件并初始化左手姿势
    void Awake()
    {
        // 保存移动和动画组件引用
        ctx = new PlayerContext
        {
            Move = GetComponent<MoveComp>(),
            Anim = GetComponent<AnimComp_1001>(),
        };

        // 优先从指定的动画约束层查找左手约束
        if (leftHandIKRig != null)
            leftHandConstraint = leftHandIKRig.GetComponentInChildren<TwoBoneIKConstraint>(true);
        else
            leftHandConstraint = GetComponentInChildren<TwoBoneIKConstraint>(true);

        // 清空约束权重，把左手目标放到站立持枪姿势
        if (leftHandConstraint != null)
            leftHandConstraint.weight = 0f;

        if (leftHandIKTarget != null && leftHandIdleWalkPose != null)
        {
            leftHandIKTarget.localPosition = leftHandIdleWalkPose.localPosition;
            leftHandIKTarget.localRotation = leftHandIdleWalkPose.localRotation;
        }
    }

    // 绑定装配完成的左手约束和持枪姿势
    public void InitializeRigReferences(
        TwoBoneIKConstraint constraint,
        Transform target,
        Transform idleWalkPose,
        Transform runPose)
    {
        // 保存装配后的引用并清空姿势混合
        leftHandConstraint = constraint;
        leftHandIKTarget = target;
        leftHandIdleWalkPose = idleWalkPose;
        leftHandRunPose = runPose;
        runPoseBlend = 0f;

        // 重置约束权重和左手目标位置
        if (leftHandConstraint != null)
            leftHandConstraint.weight = 0f;

        if (leftHandIKTarget != null && leftHandIdleWalkPose != null)
        {
            leftHandIKTarget.localPosition = leftHandIdleWalkPose.localPosition;
            leftHandIKTarget.localRotation = leftHandIdleWalkPose.localRotation;
        }
    }

    // 将移动状态写入动画并更新左手姿势
    void Update()
    {
        if (ctx.Move == null || ctx.Anim == null) return;

        // 由本地拥有者更新移动和奔跑动画参数
        if (isOwned)
        {
            ctx.Anim.SetSpeed(ctx.Move.AnimationSpeed);
            ctx.Anim.SetRunning(ctx.Move.IsRunning);
        }

        // 平滑更新左手约束强度和持枪姿势
        UpdateLeftHandIKWeight();
        UpdateLeftHandIKPose();
    }

    // 逐步将左手约束权重提高到完全生效
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

    // 在站立行走和奔跑姿势之间平滑切换
    void UpdateLeftHandIKPose()
    {
        if (leftHandIKTarget == null ||
            leftHandIdleWalkPose == null ||
            leftHandRunPose == null)
            return;

        // 根据本地或同步的奔跑状态计算混合比例
        bool isRunning = isOwned ? ctx.Anim.IsRunning : ctx.Anim.AnimatorIsRunning;
        float targetBlend = isRunning ? 1f : 0f;
        float blendSpeed = 1f / Mathf.Max(leftHandPoseBlendTime, 0.001f);
        runPoseBlend = Mathf.MoveTowards(
            runPoseBlend,
            targetBlend,
            blendSpeed * Time.deltaTime);

        // 按混合比例更新左手目标的位置和朝向
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
