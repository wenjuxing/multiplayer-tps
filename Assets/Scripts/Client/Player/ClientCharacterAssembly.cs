// 为独立客户端装配网络角色并管理装配期间的玩法组件。
using System;
using System.Threading.Tasks;
using UnityEngine;

internal sealed class ClientCharacterAssembly {
    private readonly GameObject player;
    private readonly ICharacterAssemblyBindings bindings;
    private readonly Func<string, bool> isAssembled;
    private readonly Func<string, bool> isCurrentRole;
    private readonly Action disconnect;
    // 请求标记区分同一玩家上交错完成的多次装配。
    private object activeRequest;
    // 只有在途请求有角色 ID，取消时与请求标记一起清空。
    private string pendingRoleId;

    // 接收网络角色实例、装配阶段接入和生命周期回调。
    internal ClientCharacterAssembly(GameObject player, ICharacterAssemblyBindings bindings,
        Func<string, bool> isAssembled, Func<string, bool> isCurrentRole, Action disconnect) {
        this.player = player;
        this.bindings = bindings;
        this.isAssembled = isAssembled;
        this.isCurrentRole = isCurrentRole;
        this.disconnect = disconnect;
    }

    // 按角色 ID 启动一次客户端装配。
    internal void Start(string roleId) {
        // 空角色 ID 沿用现有失败处理。
        if (string.IsNullOrWhiteSpace(roleId)) {
            Cancel();
            Fail("服务端同步的角色 ID 为空。");
            return;
        }

        if (isAssembled(roleId)) {
            // 当前角色已经可用时，取消尚未提交的其他装配。
            if (activeRequest != null) {
                Cancel();
                SetGameplayEnabled(true);
            }
            return;
        }

        if (string.Equals(pendingRoleId, roleId, StringComparison.Ordinal)) {
            return;
        }

        // 等待资源期间暂停该玩家的玩法组件。
        object request = new object();
        activeRequest = request;
        pendingRoleId = roleId;
        SetGameplayEnabled(false);
        _ = AssembleAsync(roleId, request);
    }

    // 等待角色装配并仅为当前请求恢复玩法组件。
    private async Task AssembleAsync(string roleId, object request) {
        try {
            // 在加载完成后修改角色前再次校验请求。
            CharacterFactory factory = CharacterFactory.Instance;
            if (factory == null)
                throw new InvalidOperationException("场景中没有启用的 CharacterFactory。");

            await factory.AssembleAppearanceAsync(player, roleId, bindings, () => IsCurrent(roleId, request));

            if (IsCurrent(roleId, request))
                SetGameplayEnabled(true);
        } catch (Exception exception) {
            if (IsCurrent(roleId, request))
                Fail($"客户端装配角色 {roleId} 失败：{exception}");
        } finally {
            // 旧请求结束时保留较新的请求标记。
            if (activeRequest == request)
                Cancel();
        }
    }

    // 判断请求仍属于存活的玩家及当前同步角色。
    private bool IsCurrent(string roleId, object request) {
        return activeRequest == request && player != null && isCurrentRole(roleId);
    }

    // 取消当前请求，阻止其结果进入离开的玩家实例。
    internal void Cancel() {
        activeRequest = null;
        pendingRoleId = null;
    }

    // 记录装配错误并通知网络入口处理断开。
    private void Fail(string message) {
        Debug.LogError($"[NetworkHeroAppearance] {message}", player);
        SetGameplayEnabled(false);
        disconnect();
    }

    // 统一切换装配依赖的五个角色组件。
    private void SetGameplayEnabled(bool value) {
        if (player.TryGetComponent(out MoveComp move)) move.enabled = value;
        if (player.TryGetComponent(out AnimComp_1001 anim)) anim.enabled = value;
        if (player.TryGetComponent(out Controller_1001 controller)) controller.enabled = value;
        if (player.TryGetComponent(out WeaponController weapon)) weapon.enabled = value;
        if (player.TryGetComponent(out CameraComp camera)) camera.enabled = value;
    }
}
