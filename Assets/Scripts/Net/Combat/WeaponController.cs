// 衔接客户端开火请求、服务端结算和 Mirror 射击同步。
using Mirror;
using UnityEngine;

[DefaultExecutionOrder(10)]
[RequireComponent(typeof(PlayerHealth))]
public class WeaponController : NetworkBehaviour {
    // 表现子弹的预制体路径
    private const string BulletResourcePath = "Prefabs/Bullet";
    // 射击和枪口特效使用的位置与朝向
    private Transform muzzleVfxPoint;
    // 两侧独立维护当前玩家的开火状态。
    private ClientWeapon clientWeapon;
    private ServerWeapon serverWeapon;

    // 初始化当前玩家的客户端和服务端开火逻辑。
    private void Awake() {
        clientWeapon = new ClientWeapon(transform);
        serverWeapon = new ServerWeapon();
    }

    // 绑定武器装配提供的枪口
    public void InitializeWeapon(Transform muzzle) {
        muzzleVfxPoint = muzzle;
    }

    // 让本地拥有者在角色当帧转向完成后提交开火请求。
    private void Update() {
        if (isOwned && clientWeapon.TryFire(muzzleVfxPoint, out float shotYaw))
            CmdFire(shotYaw);
    }

    // 在服务端帧末结算开火并分发确认结果
    private void LateUpdate() {
        if (!isServer || !serverWeapon.TryResolveShot(transform, muzzleVfxPoint, netId, out ServerWeapon.ShotResult shot))
            return;

        // 使用同一次结算结果生成网络子弹和客户端反馈。
        SpawnBullet(shot.Muzzle.position, shot.Direction, shot.Endpoint);
        RpcPlayShotEffects(shot.Muzzle.position, shot.Muzzle.rotation, shot.HitTarget,
            shot.HitVirtualEnemy, shot.Endpoint, shot.Direction);
    }

    // 清除尚未结算的开火请求
    private void OnDisable() {
        serverWeapon?.ClearPendingShot();
    }

    // 在服务端校验开火请求并暂存朝向
    [Command]
    private void CmdFire(float shotYaw) {
        if (isActiveAndEnabled)
            serverWeapon.QueueShot(shotYaw, muzzleVfxPoint);
    }

    // 将服务端确认结果转交给当前客户端的射击表现。
    [ClientRpc]
    private void RpcPlayShotEffects(
        Vector3 muzzlePosition, Quaternion muzzleRotation, bool hitTarget, bool hitVirtualEnemy,
        Vector3 hitPosition, Vector3 direction) {
        clientWeapon.PlayShotEffects(muzzleVfxPoint, isOwned, new Pose(muzzlePosition, muzzleRotation),
            hitTarget, hitVirtualEnemy, hitPosition, direction);
    }

    // 生成并同步服务端确认弹道的表现子弹
    private void SpawnBullet(Vector3 origin, Vector3 direction, Vector3 endpoint) {
        // 加载子弹预制体
        GameObject prefab = Resources.Load<GameObject>(BulletResourcePath);
        if (prefab == null) {
            Debug.LogError($"[Combat] 找不到子弹预制体：{BulletResourcePath}");
            return;
        }

        // 设置弹道后将子弹同步给客户端
        GameObject go = Instantiate(prefab, origin, Quaternion.LookRotation(direction));
        go.GetComponent<Bullet>().InitializePath(origin, endpoint);
        NetworkServer.Spawn(go);
    }
}
