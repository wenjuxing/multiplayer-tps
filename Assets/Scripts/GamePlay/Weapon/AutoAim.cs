using Mirror;
using UnityEngine;

/// <summary>
/// 服务端自动瞄准：在所有已生成实体里选"最近"的合法目标，不限距离、不限角度。
/// 静态纯函数，不持有状态，将来换成评分算法只改这里。
/// </summary>
public static class AutoAim
{
    public const float ChestOffset = 0.6f;

    //获取射击点
    public static Vector3 GetAimPoint(Transform target) =>
        target.position + Vector3.up * ChestOffset;

    //获取攻击目标的血量组建
    public static PlayerHealth SelectTarget(NetworkIdentity shooter, Vector3 origin)
    {
        PlayerHealth best = null;
        float bestSqrDistance = float.MaxValue;

        foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
        {
            // 排除自己；只有带 PlayerHealth 的才是可命中的玩家
            if (identity == null || identity.netId == shooter.netId) continue;
            if (!identity.TryGetComponent(out PlayerHealth target)) continue;

            // 用平方距离比较，省掉开方
            float sqrDistance = (GetAimPoint(identity.transform) - origin).sqrMagnitude;
            if (sqrDistance >= bestSqrDistance) continue;

            bestSqrDistance = sqrDistance;
            best = target;
        }

        return best;
    }
}
