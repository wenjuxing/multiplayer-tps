// 同步网络角色的外观并在客户端装配
using System;
using System.Threading.Tasks;
using Mirror;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkHeroAppearance : NetworkBehaviour
{
    // 服务端同步的角色 ID
    [SyncVar(hook = nameof(OnRoleIdChanged))]
    string roleId;

    // 已完成装配的角色 ID
    string assembledRoleId;
    // 正在装配的角色 ID
    string pendingRoleId;
    Task assemblyTask;

    public string RoleId => roleId;

    // 在服务端设置并同步角色 ID
    public void SetRoleIdOnServer(string value)
    {
        // 检查服务端状态和角色 ID
        if (!NetworkServer.active)
            throw new InvalidOperationException("角色 ID 只能由服务端设置。");
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("roleId 不能为空。", nameof(value));

        roleId = value;
    }

    // 判断指定角色外观是否已装配
    internal bool IsAssembledFor(string value)
    {
        return string.Equals(assembledRoleId, value, StringComparison.Ordinal);
    }

    // 记录装配完成的角色 ID
    internal void MarkAssembled(string value)
    {
        assembledRoleId = value;
    }

    // 为纯客户端启动初次外观装配
    public override void OnStartClient()
    {
        base.OnStartClient();
        if (isServer)
            return;

        StartClientAssembly(roleId);
    }

    // 收到新角色 ID 时更新客户端外观
    void OnRoleIdChanged(string oldValue, string newValue)
    {
        if (isClient && !isServer)
            StartClientAssembly(newValue);
    }

    // 按角色 ID 启动客户端装配
    void StartClientAssembly(string value)
    {
        // 空角色 ID 按装配失败处理
        if (string.IsNullOrWhiteSpace(value))
        {
            FailClientAssembly("服務端同步的角色 ID 为空。");
            return;
        }

        // 跳过已完成或正在进行的同一个装配
        if (IsAssembledFor(value))
            return;

        if (assemblyTask != null && string.Equals(pendingRoleId, value, StringComparison.Ordinal))
            return;

        // 装配期间暂停角色的玩法组件
        pendingRoleId = value;
        SetGameplayEnabled(false);
        _ = AssembleClientAsync(value);
    }

    // 异步装配角色并在完成后恢复玩法组件
    async Task AssembleClientAsync(string value)
    {
        try
        {
            // 通过角色工厂完成外观装配
            CharacterFactory factory = CharacterFactory.Instance;
            if (factory == null)
                throw new InvalidOperationException("場景中沒有啟用的 CharacterFactory。");

            assemblyTask = factory.AssembleAppearanceAsync(gameObject, value);
            await assemblyTask;

            // 角色 ID 已变更时跳过此次结果
            if (!string.Equals(roleId, value, StringComparison.Ordinal))
                return;

            SetGameplayEnabled(true);
        }
        catch (Exception exception)
        {
            FailClientAssembly($"客戶端裝配角色 {value} 失败：{exception}");
        }
        finally
        {
            assemblyTask = null;
        }
    }

    // 记录装配失败并断开客户端连接
    void FailClientAssembly(string message)
    {
        Debug.LogError($"[NetworkHeroAppearance] {message}", this);
        SetGameplayEnabled(false);
        if (NetworkClient.active)
            NetworkClient.Disconnect();
    }

    // 统一启用或停用角色的玩法组件
    void SetGameplayEnabled(bool value)
    {
        if (TryGetComponent(out MoveComp move)) move.enabled = value;
        if (TryGetComponent(out AnimComp_1001 anim)) anim.enabled = value;
        if (TryGetComponent(out Controller_1001 controller)) controller.enabled = value;
        if (TryGetComponent(out WeaponController weapon)) weapon.enabled = value;
        if (TryGetComponent(out CameraComp camera)) camera.enabled = value;
    }
}
