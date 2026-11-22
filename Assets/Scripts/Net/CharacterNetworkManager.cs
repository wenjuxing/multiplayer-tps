// 管理联网玩家生成和会话资源的生命周期
using System.Collections.Generic;
using System.Threading.Tasks;
using Mirror;
using UnityEngine;

public sealed class CharacterNetworkManager : NetworkManager
{
    // 玩家根预制体的资源路径
    private const string PlayerPrefabResourcePath = "Prefabs/Player";

    // 正在创建玩家的连接，用于拦截重复请求
    readonly HashSet<int> pendingPlayers = new HashSet<int>();

    // 当前客户端会话使用的射击特效
    internal WeaponShotEffects ShotEffects { get; } = new WeaponShotEffects();

    // 初始化网络管理器并补齐玩家预制体
    public override void Awake()
    {
        base.Awake();
        // 仅为当前管理器补加载未配置的预制体
        if (singleton != this || playerPrefab != null)
            return;

        playerPrefab = Resources.Load<GameObject>(PlayerPrefabResourcePath);
        if (playerPrefab == null)
            Debug.LogError($"[CharacterNetworkManager] 无法加载玩家根预制体 Resources/{PlayerPrefabResourcePath}。", this);
    }

    // 接收连接的玩家生成请求
    public override void OnServerAddPlayer(NetworkConnectionToClient connection)
    {
        // 跳过已有玩家或正在创建玩家的连接
        if (connection.identity != null)
        {
            Debug.LogError($"[CharacterNetworkManager] connection {connection.connectionId} 已有玩家对象。");
            return;
        }

        if (!pendingPlayers.Add(connection.connectionId))
        {
            Debug.LogWarning($"[CharacterNetworkManager] connection {connection.connectionId} 的玩家正在创建中，忽略重复请求。");
            return;
        }

        _ = CreateAndAddPlayerAsync(connection);
    }

    // 装配角色并将玩家绑定到仍有效的连接
    async Task CreateAndAddPlayerAsync(NetworkConnectionToClient connection)
    {
        GameObject player = null;
        try
        {
            // 通过角色工厂装配默认角色
            CharacterFactory factory = CharacterFactory.Instance;
            if (factory == null)
                throw new System.InvalidOperationException("场景中没有启用的 CharacterFactory。");

            player = await factory.CreateAsync("hero_01");
            if (player == null)
                return;

            // 装配完成后重新检查服务端和玩家连接
            if (!NetworkServer.active || connection == null ||
                !NetworkServer.connections.TryGetValue(connection.connectionId, out NetworkConnectionToClient activeConnection) ||
                activeConnection != connection || connection.identity != null)
            {
                Destroy(player);
                player = null;
                return;
            }

            // 有出生点时设置玩家初始位置和朝向
            Transform startPosition = GetStartPosition();
            if (startPosition != null)
            {
                player.transform.SetPositionAndRotation(startPosition.position, startPosition.rotation);
            }

            // 验证外观同步组件并设置默认角色 ID
            NetworkHeroAppearance appearance = player.GetComponent<NetworkHeroAppearance>();
            if (appearance == null)
            {
                Debug.LogError("[CharacterNetworkManager] Player 根预制体缺少 NetworkHeroAppearance。");
                Destroy(player);
                player = null;
                return;
            }

            appearance.SetRoleIdOnServer("hero_01");
            player.name = $"{player.name} [connId={connection.connectionId}]";

            // 将玩家交给网络管理，绑定失败时销毁临时对象
            if (NetworkServer.AddPlayerForConnection(connection, player))
                player = null;
            else
            {
                Destroy(player);
                player = null;
            }
        }
        catch (System.Exception exception)
        {
            if (player != null)
                Destroy(player);
            Debug.LogError($"[CharacterNetworkManager] 添加玩家失败，connId={connection?.connectionId}\n{exception}");
        }
        // 清除该连接的生成标记
        finally
        {
            if (connection != null)
                pendingPlayers.Remove(connection.connectionId);
        }
    }

    // 清理断开连接的玩家生成标记
    public override void OnServerDisconnect(NetworkConnectionToClient connection)
    {
        if (connection != null)
            pendingPlayers.Remove(connection.connectionId);
        base.OnServerDisconnect(connection);
    }

    // 服务端停止时清除生成记录并释放角色资源
    public override void OnStopServer()
    {
        pendingPlayers.Clear();
        base.OnStopServer();
        CharacterFactory.Instance?.ReleaseSessionAssets();
    }

    // 客户端启动时预加载武器特效
    public override void OnStartClient() {
        base.OnStartClient();
        ShotEffects.StartSession();
    }

    // 客户端停止时释放武器特效和角色资源
    public override void OnStopClient()
    {
        ShotEffects.StopSession();
        base.OnStopClient();
        CharacterFactory.Instance?.ReleaseSessionAssets();
    }
}
