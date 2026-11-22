// 同步网络角色的外观并在客户端装配
using System;
using Mirror;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkHeroAppearance : NetworkBehaviour
{
    // 服务端同步的角色 ID
    [SyncVar(hook = nameof(OnRoleIdChanged))]
    private string roleId;

    // 已完成装配的角色 ID
    private string assembledRoleId;
    // 只有独立客户端才需要的角色装配流程。
    private ClientCharacterAssembly clientAssembly;

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

    // 停止客户端时让在途角色装配失效。
    public override void OnStopClient() {
        clientAssembly?.Cancel();
        base.OnStopClient();
    }

    // 将同步角色 ID 交给独立客户端装配入口。
    private void StartClientAssembly(string value) {
        if (clientAssembly == null) {
            clientAssembly = new ClientCharacterAssembly(gameObject, NetworkCharacterAssembly.Instance,
                IsAssembledFor, IsCurrentClientRole, DisconnectClient);
        }

        clientAssembly.Start(value);
    }

    // 只允许当前客户端实例的当前角色提交结果。
    private bool IsCurrentClientRole(string value) {
        return this != null && isClient && !isServer && NetworkClient.active &&
            string.Equals(roleId, value, StringComparison.Ordinal);
    }

    // 角色装配失败时断开本客户端连接。
    private static void DisconnectClient() {
        if (NetworkClient.active)
            NetworkClient.Disconnect();
    }
}
