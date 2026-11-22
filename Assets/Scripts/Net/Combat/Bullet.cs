// 沿服务端确认的弹道播放子弹飞行表现
using Mirror;
using UnityEngine;

public class Bullet : NetworkBehaviour {
    // 子弹在弹道终点的停留时间（秒）
    private const float EndpointHold = 0.15f;

    [Tooltip("飞行速度（米/秒）")]
    [SerializeField, Min(0.01f)] private float speed = 20f;
    // 由服务端同步的弹道起终点和飞行时间
    [SyncVar] private Vector3 origin;
    [SyncVar] private Vector3 endpoint;
    [SyncVar] private double launchedAt;
    [SyncVar] private float flightTime;

    // 在服务端记录弹道起终点和发射时间
    [Server]
    public void InitializePath(Vector3 start, Vector3 end) {
        origin = start;
        endpoint = end;
        flightTime = Vector3.Distance(start, end) / speed;
        launchedAt = NetworkTime.time;
    }

    // 在客户端定位子弹并挂上弹道特效
    public override void OnStartClient() {
        base.OnStartClient();
        UpdatePosition(NetworkTime.time - launchedAt);

        // 有可用弹道特效时挂到子弹上
        GameObject trail = WeaponShotEffects.Client?.Trail;
        if (trail != null)
            Instantiate(trail, transform, false);
    }

    // 推进飞行表现并在结束后销毁子弹
    private void Update() {
        double elapsed = NetworkTime.time - launchedAt;
        UpdatePosition(elapsed);
        // 服务端到达终点后短暂停留，给客户端留出追上弹道进度的时间
        if (isServer && elapsed >= flightTime + EndpointHold)
            NetworkServer.Destroy(gameObject);
    }

    // 按同步时间计算子弹当前位置
    private void UpdatePosition(double elapsed) {
        float progress = flightTime > 0f ? (float)elapsed / flightTime : 1f;
        transform.position = Vector3.Lerp(origin, endpoint, progress);
    }
}
