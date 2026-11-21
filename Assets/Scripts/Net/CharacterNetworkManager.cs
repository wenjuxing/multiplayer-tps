using System.Collections.Generic;
using System.Threading.Tasks;
using Mirror;
using UnityEngine;

namespace FPSGame.Net
{
    public sealed class CharacterNetworkManager : NetworkManager
    {
        private const string PlayerPrefabResourcePath = "Prefabs/Player";

        readonly HashSet<int> pendingPlayers = new HashSet<int>();

        public override void Awake()
        {
            base.Awake();
            if (singleton != this || playerPrefab != null)
                return;

            playerPrefab = Resources.Load<GameObject>(PlayerPrefabResourcePath);
            if (playerPrefab == null)
                Debug.LogError($"[CharacterNetworkManager] 无法加载玩家根预制体 Resources/{PlayerPrefabResourcePath}。", this);
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient connection)
        {
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

        async Task CreateAndAddPlayerAsync(NetworkConnectionToClient connection)
        {
            GameObject player = null;
            try
            {
                CharacterFactory factory = CharacterFactory.Instance;
                if (factory == null)
                    throw new System.InvalidOperationException("场景中没有启用的 CharacterFactory。");

                player = await factory.CreateAsync("hero_01");
                if (player == null)
                    return;

                if (!NetworkServer.active || connection == null ||
                    !NetworkServer.connections.TryGetValue(connection.connectionId, out NetworkConnectionToClient activeConnection) ||
                    activeConnection != connection || connection.identity != null)
                {
                    Destroy(player);
                    player = null;
                    return;
                }

                Transform startPosition = GetStartPosition();
                if (startPosition != null)
                {
                    player.transform.SetPositionAndRotation(startPosition.position, startPosition.rotation);
                }

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
            finally
            {
                if (connection != null)
                    pendingPlayers.Remove(connection.connectionId);
            }
        }

        public override void OnServerDisconnect(NetworkConnectionToClient connection)
        {
            if (connection != null)
                pendingPlayers.Remove(connection.connectionId);
            base.OnServerDisconnect(connection);
        }

        public override void OnStopServer()
        {
            pendingPlayers.Clear();
            base.OnStopServer();
            CharacterFactory.Instance?.ReleaseSessionAssets();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            CharacterFactory.Instance?.ReleaseSessionAssets();
        }
    }
}
