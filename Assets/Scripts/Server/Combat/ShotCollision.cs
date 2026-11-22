// 检测射击路径上的目标和障碍物
using System;
using UnityEngine;

internal sealed class ShotCollision {
    // 枪口碰撞检测半径
    private const float MuzzleCheckRadius = 0.01f;
    // 虚拟敌人标签
    private const string VirtualEnemyTag = "Enemy";
    // 玩家受击层
    private readonly int playerHitboxLayer = LayerMask.NameToLayer("PlayerHitbox");
    // 缓存射线和枪口的查询结果
    private RaycastHit[] rayHits = new RaycastHit[8];
    private Collider[] muzzleOverlaps = new Collider[4];

    // 保存碰撞位置、距离和目标信息
    public readonly struct ColliderInfo {
        public Vector3 Point { get; }
        public float Distance { get; }
        public PlayerHealth Target { get; }
        public bool IsVirtualEnemy { get; }
        // 判断是否命中玩家或虚拟敌人
        public bool IsTarget => Target != null || IsVirtualEnemy;

        // 初始化碰撞信息
        public ColliderInfo(Vector3 point, float distance, PlayerHealth target, bool isVirtualEnemy = false) {
            Point = point;
            Distance = distance;
            Target = target;
            IsVirtualEnemy = isVirtualEnemy;
        }
    }

    // 找到距离最近的目标
    public bool TryFindClosestTarget(Ray ray, float range, Transform shooter, out ColliderInfo hitInfo) {
        // 查询射程内的碰撞
        int count = Physics.RaycastNonAlloc(ray, rayHits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

        // 结果填满时扩大数组并重查
        while (count == rayHits.Length) {
            Array.Resize(ref rayHits, rayHits.Length * 2);
            count = Physics.RaycastNonAlloc(ray, rayHits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        }

        // 选出最近的有效碰撞，距离相同时优先选障碍物
        hitInfo = default;
        bool found = false;
        for (int i = 0; i < count; i++) {
            RaycastHit hit = rayHits[i];
            if (!TryGetValidColliderInfo(hit.collider, shooter, hit.point, hit.distance, out ColliderInfo candidate)) continue;
            if (found && (candidate.Distance > hitInfo.Distance ||
                          (candidate.Distance == hitInfo.Distance && (candidate.IsTarget || !hitInfo.IsTarget)))) continue;

            hitInfo = candidate;
            found = true;
        }

        return found;
    }

    // 检查枪口是否碰到目标或障碍物
    public bool TryGetMuzzleHit(Vector3 muzzle, Transform shooter, out ColliderInfo hitInfo) {
        // 补查枪口附近，避免射线漏掉包住起点的碰撞体
        int count = Physics.OverlapSphereNonAlloc(muzzle, MuzzleCheckRadius, muzzleOverlaps,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

        // 结果填满时扩大数组并重查
        while (count == muzzleOverlaps.Length) {
            Array.Resize(ref muzzleOverlaps, muzzleOverlaps.Length * 2);
            count = Physics.OverlapSphereNonAlloc(muzzle, MuzzleCheckRadius, muzzleOverlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        }

        // 筛选有效碰撞，枪口命中距离记为零
        hitInfo = default;
        for (int i = 0; i < count; i++) {
            Collider collider = muzzleOverlaps[i];
            if (!TryGetValidColliderInfo(collider, shooter, muzzle, 0f, out ColliderInfo candidate)) continue;

            // 障碍物进入检测范围就会挡住射击
            if (!candidate.IsTarget) {
                hitInfo = candidate;
                return true;
            }

            // 枪口位于目标碰撞体内部或表面时才算命中
            if (collider.ClosestPoint(muzzle) == muzzle)
                hitInfo = candidate;
        }

        return hitInfo.IsTarget;
    }

    // 判断碰撞体是否有效
    private bool TryGetValidColliderInfo(Collider collider, Transform shooter, Vector3 point, float distance, out ColliderInfo hitInfo) {
        // 跳过空引用和射手自身的碰撞体
        hitInfo = default;
        if (collider == null || collider.transform.IsChildOf(shooter)) return false;

        // 通过受击区域查找玩家生命组件
        if (collider.gameObject.layer == playerHitboxLayer) {
            PlayerHealth target = collider.GetComponentInParent<PlayerHealth>();
            if (target == null) return false;
            hitInfo = new ColliderInfo(point, distance, target);
            return true;
        }

        // 跳过其他触发器，按标签区分虚拟敌人和障碍物
        if (collider.isTrigger) return false;
        hitInfo = new ColliderInfo(point, distance, null, collider.CompareTag(VirtualEnemyTag));
        return true;
    }
}
