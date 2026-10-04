using FPSGame.GamePlay.Player;
using FPSGame.Shared.Config;
using Mirror;
using UnityEngine;

namespace FPSGame.Gameplay.Weapon
{
    [RequireComponent(typeof(PlayerHealth))]
    public class WeaponController : NetworkBehaviour
    {
        private Transform firePoint;
        private GameObject bulletPrefab;
        private WeaponStats stats = new WeaponStats();
        private const string BulletResourcePath = "Prefabs/Bullet";

        float nextLocalFireTime;   // 客户端节流（省流量）
        float nextServerFireTime;  // 服务端限速（唯一权威）

        void Update()
        {
            // 只有拥有者读输入；无头 Dedicated Server 上 isOwned 恒为 false
            if (!isOwned) return;
            if (!Input.GetKeyDown(KeyCode.Mouse0)) return;

            // 第一层：本地节流，避免每帧都发一个 Command
            if (Time.time < nextLocalFireTime) return;
            nextLocalFireTime = Time.time + stats.fireInterval;

            CmdFire();
        }

        Vector3 GetOrigin() => firePoint != null
            ? firePoint.position
            : transform.position + Vector3.up * AutoAim.ChestOffset;
        
        //当前方法是客户端请求服务端运行的方法
        //开火目标选择/命中检测服务端算
        [Command]
        void CmdFire()
        {
            // 第二层：服务端限速，客户端的 fireInterval 不可信
            if (Time.time < nextServerFireTime) return;
            nextServerFireTime = Time.time + stats.fireInterval;

            Vector3 origin = GetOrigin();

            // 服务端判定命中
            PlayerHealth target = AutoAim.SelectTarget(netIdentity, origin);

            Vector3 direction = target != null
                ? AutoAim.GetAimPoint(target.transform) - origin
                : transform.forward;

            // 方向退化（目标和枪口几乎重合）时兜底，避免 LookRotation(0) 报错
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;

            if (target != null)
                target.ServerTakeDemage(stats.demage, netId);

            SpawnBullet(origin, direction);
        }

        void SpawnBullet(Vector3 origin, Vector3 direction)
        {
            GameObject prefab = bulletPrefab != null
                ? bulletPrefab
                : Resources.Load<GameObject>(BulletResourcePath);

            if (prefab == null)
            {
                Debug.LogError($"[Combat] 找不到子弹预制体：{BulletResourcePath}");
                return;
            }

            GameObject go = Instantiate(prefab, origin, Quaternion.LookRotation(direction));

            // direction 是 SyncVar，必须在 Spawn 之前赋值，才会随 spawn 消息下发给客户端
            if (go.TryGetComponent(out Bullet bullet))
                bullet.direction = direction;

            NetworkServer.Spawn(go);   // 只有服务端能 Spawn
        }
    }
}