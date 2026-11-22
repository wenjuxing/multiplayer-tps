// 在本地玩家生命周期中接入相机跟随与命中反馈。
using Mirror;
using UnityEngine;

[DefaultExecutionOrder(-1)]
public class CameraComp : NetworkBehaviour {
    // 相机相对角色的世界坐标偏移
    [Header("相对角色的固定偏移（世界空间）")]
    [Tooltip("相机 = 角色位置 + 这个偏移。y 越大越俯视，z 越小越靠后")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 8f, -6f);

    // 跟随平滑时间，设为零时直接跟随
    [Header("平滑")]
    [Tooltip("0 = 硬跟随，每帧贴死角色；大于 0 = 平滑跟随的阻尼时间（秒）")]
    [SerializeField] private float followSmoothTime = 0f;

    private readonly ClientCamera localCamera = new ClientCamera();

    // 生成本地玩家时将主相机摆到初始位置。
    public override void OnStartLocalPlayer() {
        localCamera.SnapToTarget(transform.position, offset);
    }

    // 失去控制权时结束镜头震动。
    public override void OnStopAuthority() {
        localCamera.StopHitShake();
    }

    // 停用组件时移除镜头震动的渲染回调。
    private void OnDisable() {
        localCamera.StopHitShake();
    }

    // 只为启用中且由本端拥有的玩家启动震动。
    internal void PlayHitShake() {
        if (isActiveAndEnabled && isOwned)
            localCamera.PlayHitShake(offset);
    }

    // 由本地玩家在开火采集前更新相机。
    private void Update() {
        if (isOwned)
            localCamera.Follow(transform.position, offset, followSmoothTime);
    }

    // 检查器修改偏移时刷新已经绑定的相机朝向。
    protected override void OnValidate() {
        base.OnValidate();
        localCamera?.RefreshRotation(offset);
    }
}
