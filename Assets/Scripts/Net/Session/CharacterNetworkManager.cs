// 管理联网玩家生成和会话资源的生命周期
using Mirror;
using UnityEngine;

public sealed class CharacterNetworkManager : NetworkManager
{
    // 玩家根预制体的资源路径
    private const string PlayerPrefabResourcePath = "Prefabs/Player";

    // 正在创建玩家的连接，用于拦截重复请求
    private readonly ServerPlayerSpawner playerSpawner = new ServerPlayerSpawner();

    // 当前本地客户端拥有的会话资源。
    private readonly ClientSession clientSession = new ClientSession();

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

        playerSpawner.AddPlayer(connection.connectionId, playerPrefab, NetworkCharacterAssembly.Instance,
            () => CanRegisterPlayer(connection), GetStartPosition,
            (player, roleId) => RegisterPlayer(connection, player, roleId));
    }

    // 确认等待装配的连接仍属于当前服务端且尚未登记玩家。
    private bool CanRegisterPlayer(NetworkConnectionToClient connection) {
        return NetworkServer.active &&
            NetworkServer.connections.TryGetValue(connection.connectionId, out NetworkConnectionToClient activeConnection) &&
            activeConnection == connection && connection.identity == null;
    }

    // 在网络生成前写入角色 ID，再把玩家交给 Mirror 分配所有权。
    private bool RegisterPlayer(NetworkConnectionToClient connection, GameObject player, string roleId) {
        if (!player.TryGetComponent(out NetworkHeroAppearance appearance)) {
            Debug.LogError("[CharacterNetworkManager] Player 根预制体缺少 NetworkHeroAppearance。", player);
            return false;
        }

        appearance.SetRoleIdOnServer(roleId);
        player.name = $"{player.name} [connId={connection.connectionId}]";
        return NetworkServer.AddPlayerForConnection(connection, player);
    }

    // 清理断开连接的玩家生成标记
    public override void OnServerDisconnect(NetworkConnectionToClient connection)
    {
        if (connection != null)
            playerSpawner.Cancel(connection.connectionId);
        base.OnServerDisconnect(connection);
    }

    // 服务端停止时清除生成记录并释放角色资源
    public override void OnStopServer()
    {
        playerSpawner.CancelAll();
        base.OnStopServer();
        if (CharacterFactory.Instance != null)
            CharacterFactory.Instance.ReleaseSessionAssets();
    }

    // 客户端启动时预加载武器特效
    public override void OnStartClient() {
        base.OnStartClient();
        clientSession.Start(mode == NetworkManagerMode.ClientOnly);
    }

    // 客户端停止时释放武器特效和角色资源
    public override void OnStopClient()
    {
        bool releaseCharacterAssets = clientSession.Stop();
        base.OnStopClient();

        // 纯客户端释放角色资源，Host 的角色资源归服务端停止路径。
        if (releaseCharacterAssets && CharacterFactory.Instance != null)
            CharacterFactory.Instance.ReleaseSessionAssets();
    }
}
