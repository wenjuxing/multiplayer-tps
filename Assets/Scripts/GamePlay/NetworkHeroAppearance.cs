using System;
using System.Threading.Tasks;
using Mirror;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NetworkHeroAppearance : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnRoleIdChanged))]
    string roleId;

    string assembledRoleId;
    string pendingRoleId;
    Task assemblyTask;

    public string RoleId => roleId;

    public void SetRoleIdOnServer(string value)
    {
        if (!NetworkServer.active)
            throw new InvalidOperationException("角色 ID 只能由服务端设置。");
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("roleId 不能为空。", nameof(value));

        roleId = value;
    }

    internal bool IsAssembledFor(string value)
    {
        return string.Equals(assembledRoleId, value, StringComparison.Ordinal);
    }

    internal void MarkAssembled(string value)
    {
        assembledRoleId = value;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (isServer)
            return;

        StartClientAssembly(roleId);
    }

    void OnRoleIdChanged(string oldValue, string newValue)
    {
        if (isClient && !isServer)
            StartClientAssembly(newValue);
    }

    void StartClientAssembly(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            FailClientAssembly("服務端同步的角色 ID 为空。");
            return;
        }

        if (IsAssembledFor(value))
            return;

        if (assemblyTask != null && string.Equals(pendingRoleId, value, StringComparison.Ordinal))
            return;

        pendingRoleId = value;
        SetGameplayEnabled(false);
        _ = AssembleClientAsync(value);
    }

    async Task AssembleClientAsync(string value)
    {
        try
        {
            CharacterFactory factory = CharacterFactory.Instance;
            if (factory == null)
                throw new InvalidOperationException("場景中沒有啟用的 CharacterFactory。");

            assemblyTask = factory.AssembleAppearanceAsync(gameObject, value);
            await assemblyTask;

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

    void FailClientAssembly(string message)
    {
        Debug.LogError($"[NetworkHeroAppearance] {message}", this);
        SetGameplayEnabled(false);
        if (NetworkClient.active)
            NetworkClient.Disconnect();
    }

    void SetGameplayEnabled(bool value)
    {
        if (TryGetComponent(out MoveComp move)) move.enabled = value;
        if (TryGetComponent(out AnimComp_1001 anim)) anim.enabled = value;
        if (TryGetComponent(out Controller_1001 controller)) controller.enabled = value;
        if (TryGetComponent(out WeaponController weapon)) weapon.enabled = value;
        if (TryGetComponent(out CameraComp camera)) camera.enabled = value;
    }
}
