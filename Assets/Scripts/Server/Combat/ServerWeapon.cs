// 在服务端校验开火请求、查询射击路径并结算伤害。
using UnityEngine;

internal sealed class ServerWeapon {
    // 每个玩家独立保存武器参数和碰撞查询。
    private readonly WeaponStats stats = new WeaponStats();
    private readonly ShotCollision shotCollision = new ShotCollision();
    // 暂存本帧接受的点击偏航和下次允许开火的时间。
    private float? pendingShotYaw;
    private float nextFireTime;

    // 记录生成网络子弹和播放射击表现所需的结算结果。
    internal readonly struct ShotResult {
        internal Pose Muzzle { get; }
        internal Vector3 Direction { get; }
        internal Vector3 Endpoint { get; }
        internal bool HitTarget { get; }
        internal bool HitVirtualEnemy { get; }

        // 保存同一次射击的枪口位姿、路径终点与命中分类。
        internal ShotResult(Pose muzzle, Vector3 direction, Vector3 endpoint, bool hitTarget, bool hitVirtualEnemy) {
            Muzzle = muzzle;
            Direction = direction;
            Endpoint = endpoint;
            HitTarget = hitTarget;
            HitVirtualEnemy = hitVirtualEnemy;
        }
    }

    // 校验当前开火条件并暂存点击时的偏航。
    internal void QueueShot(float shotYaw, Transform muzzle) {
        // 同一帧只接受一个待结算请求，开火间隔以服务端时间为准。
        if (Time.time < nextFireTime || pendingShotYaw.HasValue || muzzle == null || !float.IsFinite(shotYaw))
            return;

        nextFireTime = Time.time + stats.fireInterval;
        pendingShotYaw = shotYaw;
    }

    // 消费待结算请求并返回服务端确认的射击结果。
    internal bool TryResolveShot(Transform shooter, Transform muzzle, uint shooterNetId, out ShotResult result) {
        result = default;
        if (pendingShotYaw is not float shotYaw) {
            return false;
        }

        // 先消费请求，枪口已失效时也不会在之后重放该发。
        pendingShotYaw = null;
        if (muzzle == null)
            return false;

        // 保留服务端持枪姿势，按点击时的偏航重建枪口位置和朝向。
        Quaternion shotRotation = Quaternion.Euler(0f, shotYaw % 360f, 0f);
        Quaternion rotationDelta = shotRotation * Quaternion.Inverse(shooter.rotation);
        Vector3 rootPosition = shooter.position;
        Vector3 muzzlePosition = rootPosition + rotationDelta * (muzzle.position - rootPosition);
        Quaternion muzzleRotation = rotationDelta * muzzle.rotation;
        Vector3 direction = muzzleRotation * Vector3.forward;
        Ray ray = new Ray(muzzlePosition, direction);

        // 同步碰撞体位置，优先查枪口重叠，再查询射程内最近的碰撞。
        Physics.SyncTransforms();
        bool stopped = shotCollision.TryGetMuzzleHit(muzzlePosition, shooter, out ShotCollision.ColliderInfo hitInfo) ||
            shotCollision.TryFindClosestTarget(ray, stats.range, shooter, out hitInfo);

        // 玩家命中扣血，虚拟敌人命中只记录表现分类。
        PlayerHealth target = stopped ? hitInfo.Target : null;
        Vector3 endpoint = stopped ? hitInfo.Point : ray.GetPoint(stats.range);
        if (target != null)
            target.ServerTakeDemage(stats.demage, shooterNetId);

        result = new ShotResult(new Pose(muzzlePosition, muzzleRotation), direction, endpoint,
            stopped && hitInfo.IsTarget, stopped && hitInfo.IsVirtualEnemy);
        return true;
    }

    // 清除组件停用前尚未结算的请求。
    internal void ClearPendingShot() {
        pendingShotYaw = null;
    }
}
