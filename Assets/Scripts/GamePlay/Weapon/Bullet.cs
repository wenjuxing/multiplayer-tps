using Mirror;
using UnityEngine;

/// <summary>
/// 子弹：纯表现 + 生命周期，命中判定不在这里（服务端开火瞬间已算完）。
/// 各端按同一个 direction 本地推进，所以不需要 NetworkTransform，也没有每帧同步开销。
/// </summary>
public class Bullet : NetworkBehaviour
{
    [Tooltip("飞行速度（米/秒）")]
    [SerializeField] float speed = 40f;

    [Tooltip("最大飞行距离（米），到达后由服务端销毁")]
    [SerializeField] float maxDistance = 30f;

    [SyncVar] public Vector3 direction;

    float traveled;

    void Update()
    {
        float step = speed * Time.deltaTime;
        transform.position += direction * step;

        // 生命周期只由服务端管，客户端的对象等 Mirror 的 unspawn 消息回收
        if (!isServer) return;

        traveled += step;
        if (traveled >= maxDistance) NetworkServer.Destroy(gameObject);
    }
}
