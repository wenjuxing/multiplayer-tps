// 在固定的角色装配阶段连接 Mirror 动画和玩家组件。
using System;
using Mirror;
using UnityEngine;
using UnityEngine.Animations.Rigging;

internal sealed class NetworkCharacterAssembly : ICharacterAssemblyBindings {
    internal static NetworkCharacterAssembly Instance { get; } = new NetworkCharacterAssembly();

    // 校验网络玩家组件并按装配顺序刷新网络动画。
    public void PrepareAnimator(GameObject player, Animator animator) {
        // 校验原装配路径依赖的角色控制组件。
        if (!player.TryGetComponent(out NetworkAnimator networkAnimator) ||
            !player.TryGetComponent<Controller_1001>(out _) || !player.TryGetComponent<WeaponController>(out _))
            throw new InvalidOperationException("Player 根预制体缺少 NetworkAnimator 或角色控制组件。");

        networkAnimator.animator = animator;
        networkAnimator.enabled = false;
        networkAnimator.enabled = true;
        BindShootTrigger(networkAnimator, player.GetComponent<AnimComp_1001>());
    }

    // 使用当前的网络动画发射开火触发器，供两端共用动画逻辑调用。
    internal static void BindShootTrigger(NetworkAnimator networkAnimator, AnimCompBase animation) {
        animation.BindShootTrigger(trigger => {
            if (networkAnimator == null)
                return false;

            networkAnimator.SetTrigger(trigger);
            return true;
        });
    }

    // 将已装配的持枪姿势交给玩家的 IK 接入组件。
    public void BindRig(GameObject player, TwoBoneIKConstraint constraint, WeaponVisualBinding weapon) {
        player.GetComponent<Controller_1001>().InitializeRigReferences(
            constraint, weapon.LeftHandIKTarget, weapon.LeftHandIdleWalkPose, weapon.LeftHandRunPose);
    }

    // 配置移动和开火所需枪口并登记装配就绪。
    public void Complete(GameObject player, string roleId, Transform muzzle) {
        // 训练或验证预制体可以省略移动和外观同步组件。
        if (player.TryGetComponent(out MoveComp move))
            move.BindMuzzle(muzzle);
        player.GetComponent<WeaponController>().InitializeWeapon(muzzle);

        if (player.TryGetComponent(out NetworkHeroAppearance appearance))
            appearance.MarkAssembled(roleId);
    }
}
