// 将网络角色的本地动画输入与两端持枪姿势接入同一帧更新。
using Mirror;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class Controller_1001 : NetworkBehaviour {
    // 左手约束、跟随目标和持枪姿势的配置
    [Header("左手 IK")]
    [SerializeField] private Rig leftHandIKRig;
    [SerializeField] private Transform leftHandIKTarget;
    [SerializeField] private Transform leftHandIdleWalkPose;
    [SerializeField] private Transform leftHandRunPose;
    // 左手约束启用和持枪姿势切换的过渡时间
    [SerializeField, Min(0f)] private float leftHandIKBlendTime = 0.12f;
    [SerializeField, Min(0f)] private float leftHandPoseBlendTime = 0.15f;

    private readonly CharacterGripPose grip = new CharacterGripPose();
    private MoveComp move;
    private AnimComp_1001 animation;

    // 从玩家组件准备动画发送和初始持枪姿势。
    private void Awake() {
        TryGetComponent(out move);
        TryGetComponent(out animation);
        if (animation != null && TryGetComponent(out NetworkAnimator networkAnimator))
            NetworkCharacterAssembly.BindShootTrigger(networkAnimator, animation);

        // 优先在配置的 Rig 层查找左手约束。
        TwoBoneIKConstraint constraint = leftHandIKRig != null
            ? leftHandIKRig.GetComponentInChildren<TwoBoneIKConstraint>(true)
            : GetComponentInChildren<TwoBoneIKConstraint>(true);
        grip.Bind(constraint, leftHandIKTarget, leftHandIdleWalkPose, leftHandRunPose);
    }

    // 记录装配结果并把左手姿势交给共用求值逻辑。
    public void InitializeRigReferences(TwoBoneIKConstraint constraint, Transform target, Transform idleWalkPose,
        Transform runPose) {
        leftHandIKTarget = target;
        leftHandIdleWalkPose = idleWalkPose;
        leftHandRunPose = runPose;
        grip.Bind(constraint, target, idleWalkPose, runPose);
    }

    // 先应用拥有者移动动画，再根据当前奔跑状态更新持枪姿势。
    private void Update() {
        if (move == null || animation == null)
            return;

        if (isOwned)
            move.ApplyLocalAnimation(animation);

        // 远端和服务器从同步的 Animator 读取姿势所需状态。
        bool running = isOwned ? animation.IsRunning : animation.AnimatorIsRunning;
        grip.Tick(running, leftHandIKBlendTime, leftHandPoseBlendTime);
    }
}
