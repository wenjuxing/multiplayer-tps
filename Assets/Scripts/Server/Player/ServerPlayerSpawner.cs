// 装配服务端玩家并清理尚未交给网络的对象。
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

internal sealed class ServerPlayerSpawner {
    private const string DefaultRoleId = "hero_01";
    // 每个连接的标记区分旧请求与重新发起的请求。
    private readonly Dictionary<int, object> pendingPlayers = new Dictionary<int, object>();

    // 同一个连接同一时间只接受一次玩家创建请求。
    internal void AddPlayer(int connectionId, GameObject playerPrefab, ICharacterAssemblyBindings bindings,
        Func<bool> canRegister, Func<Transform> getStartPosition, Func<GameObject, string, bool> registerPlayer) {
        object request = new object();
        if (!pendingPlayers.TryAdd(connectionId, request)) {
            Debug.LogWarning($"[ServerPlayerSpawner] connection {connectionId} 的玩家正在创建中，忽略重复请求。");
            return;
        }

        _ = CreateAndRegisterAsync(connectionId, request, playerPrefab, bindings, canRegister, getStartPosition, registerPlayer);
    }

    // 装配默认角色并在连接仍有效时交给网络入口。
    private async Task CreateAndRegisterAsync(int connectionId, object request, GameObject playerPrefab,
        ICharacterAssemblyBindings bindings, Func<bool> canRegister, Func<Transform> getStartPosition,
        Func<GameObject, string, bool> registerPlayer) {
        GameObject player = null;
        try {
            // 等待当前会话的角色和武器装配。
            CharacterFactory factory = CharacterFactory.Instance;
            if (factory == null)
                throw new InvalidOperationException("场景中没有启用的 CharacterFactory。");

            player = await factory.CreateAsync(playerPrefab, DefaultRoleId, bindings);
            if (player == null || !IsCurrent(connectionId, request) || !canRegister())
                return;

            // 确认仍需生成后才分配出生点。
            Transform startPosition = getStartPosition();
            if (startPosition != null)
                player.transform.SetPositionAndRotation(startPosition.position, startPosition.rotation);

            if (registerPlayer(player, DefaultRoleId))
                player = null;
        } catch (Exception exception) {
            Debug.LogError($"[ServerPlayerSpawner] 添加玩家失败，connId={connectionId}\n{exception}");
        } finally {
            // 回收未登记的玩家，旧请求只清除自己的标记。
            if (player != null)
                UnityEngine.Object.Destroy(player);
            if (IsCurrent(connectionId, request))
                pendingPlayers.Remove(connectionId);
        }
    }

    // 比较当前请求标记与正在完成的请求。
    private bool IsCurrent(int connectionId, object request) {
        return pendingPlayers.TryGetValue(connectionId, out object current) && current == request;
    }

    // 使断开的连接不再接受旧装配结果。
    internal void Cancel(int connectionId) {
        pendingPlayers.Remove(connectionId);
    }

    // 停止会话时让所有在途请求失效。
    internal void CancelAll() {
        pendingPlayers.Clear();
    }
}
