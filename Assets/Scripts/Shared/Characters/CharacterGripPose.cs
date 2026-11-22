// 按奔跑状态平滑求值客户端和服务端共用的持枪姿势。
using UnityEngine;
using UnityEngine.Animations.Rigging;

internal sealed class CharacterGripPose {
    private TwoBoneIKConstraint constraint;
    private Transform target;
    private Transform idleWalkPose;
    private Transform runPose;
    // 站立和奔跑持枪姿势之间的混合比例。
    private float runPoseBlend;

    // 绑定武器提供的持枪姿势并重置混合状态。
    internal void Bind(TwoBoneIKConstraint constraint, Transform target, Transform idleWalkPose, Transform runPose) {
        this.constraint = constraint;
        this.target = target;
        this.idleWalkPose = idleWalkPose;
        this.runPose = runPose;
        runPoseBlend = 0f;

        // 初始化时让约束权重从零开始，左手位于站立姿势。
        if (constraint != null)
            constraint.weight = 0f;

        if (target != null && idleWalkPose != null)
            target.SetLocalPositionAndRotation(idleWalkPose.localPosition, idleWalkPose.localRotation);
    }

    // 更新约束权重及站立与奔跑之间的插值姿势。
    internal void Tick(bool running, float weightBlendTime, float poseBlendTime) {
        // 逐步启用左手约束。
        if (constraint != null) {
            float weightBlendSpeed = 1f / Mathf.Max(weightBlendTime, 0.001f);
            constraint.weight = Mathf.MoveTowards(constraint.weight, 1f, weightBlendSpeed * Time.deltaTime);
        }

        // 按奔跑状态选择持枪姿势，保持原过渡速度。
        if (target == null || idleWalkPose == null || runPose == null)
            return;

        float poseBlendSpeed = 1f / Mathf.Max(poseBlendTime, 0.001f);
        runPoseBlend = Mathf.MoveTowards(runPoseBlend, running ? 1f : 0f, poseBlendSpeed * Time.deltaTime);
        target.SetLocalPositionAndRotation(
            Vector3.Lerp(idleWalkPose.localPosition, runPose.localPosition, runPoseBlend),
            Quaternion.Slerp(idleWalkPose.localRotation, runPose.localRotation, runPoseBlend));
    }
}
