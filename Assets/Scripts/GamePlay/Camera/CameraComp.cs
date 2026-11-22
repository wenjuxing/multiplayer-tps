// 让本地玩家的相机俯视跟随角色并播放命中震动
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-1)]
public class CameraComp : NetworkBehaviour
{
    // 命中震动的持续时间和最大幅度
    private const float HitShakeDuration = 0.12f;
    private const float HitShakeAmplitude = 0.035f;

    // 相机相对角色的世界坐标偏移
    [Header("相对角色的固定偏移（世界空间）")]
    [Tooltip("相机 = 角色位置 + 这个偏移。y 越大越俯视，z 越小越靠后")]
    [SerializeField] Vector3 offset = new Vector3(0f, 8f, -6f);

    // 跟随平滑时间，设为零时直接跟随
    [Header("平滑")]
    [Tooltip("0 = 硬跟随，每帧贴死角色；大于 0 = 平滑跟随的阻尼时间（秒）")]
    [SerializeField] float followSmoothTime = 0f;

    Camera cam;                  // 场景里的 Main Camera
    Transform camTransform;
    Vector3 followVelocity;      // Vector3.SmoothDamp 的状态变量
    private bool hitShakeActive;
    private double hitShakeStartedAt;
    private Transform shakenCamera;
    // 渲染前保存的相机位置，用于渲染后恢复
    private Vector3 unshakenPosition;

    // 本地玩家生成时立即摆好相机
    public override void OnStartLocalPlayer()
    {
        if (!TryGetCamera()) return;
        SnapToTarget();
    }

    // 失去角色控制权时停止相机震动
    public override void OnStopAuthority() {
        StopHitShake();
    }

    // 相机组件禁用时清理震动
    private void OnDisable() {
        StopHitShake();
    }

    // 为本地玩家启动或重置命中震动
    internal void PlayHitShake() {
        if (!isActiveAndEnabled || !isOwned || !TryGetCamera()) return;

        // 连续命中时刷新开始时间，沿用已注册的渲染回调
        hitShakeStartedAt = Time.unscaledTimeAsDouble;
        if (hitShakeActive) return;

        hitShakeActive = true;
        RenderPipelineManager.beginCameraRendering += ApplyHitShake;
        RenderPipelineManager.endCameraRendering += EndCameraRendering;
    }

    // 渲染前临时应用逐渐减弱的震动
    private void ApplyHitShake(ScriptableRenderContext context, Camera camera) {
        if (camera != cam || shakenCamera != null) return;

        // 计算震动进度，到期时停止
        float progress = (float)(Time.unscaledTimeAsDouble - hitShakeStartedAt) / HitShakeDuration;
        if (progress >= 1f) {
            StopHitShake();
            return;
        }

        // 仅在渲染时偏移相机，让瞄准和跟随继续使用原位姿
        float phase = progress * 4f * Mathf.PI;
        float strength = HitShakeAmplitude * (1f - progress);
        Vector3 displacement = new Vector3(Mathf.Cos(phase), 0.5f * Mathf.Sin(phase), 0f) * strength;
        shakenCamera = camTransform;
        unshakenPosition = shakenCamera.position;
        shakenCamera.position += shakenCamera.TransformDirection(displacement);
    }

    // 主相机渲染结束后恢复原位置
    private void EndCameraRendering(ScriptableRenderContext context, Camera camera) {
        if (camera == cam)
            RestoreCameraPosition();
    }

    // 移除震动回调并恢复相机位置
    private void StopHitShake() {
        if (!hitShakeActive) return;

        hitShakeActive = false;
        RenderPipelineManager.beginCameraRendering -= ApplyHitShake;
        RenderPipelineManager.endCameraRendering -= EndCameraRendering;
        RestoreCameraPosition();
    }

    // 还原渲染前保存的相机位置
    private void RestoreCameraPosition() {
        if (shakenCamera != null)
            shakenCamera.position = unshakenPosition;
        shakenCamera = null;
    }

    // 每帧更新本地相机的跟随位置
    private void Update()
    {
        // 仅本地拥有的玩家更新相机
        if (!isOwned) return;
        if (!TryGetCamera()) return;

        // 按配置直接或平滑跟随角色
        Vector3 desired = transform.position + offset;
        camTransform.position = followSmoothTime > 0f
            ? Vector3.SmoothDamp(camTransform.position, desired, ref followVelocity, followSmoothTime)
            : desired;
    }

    // 调整检查器参数时刷新相机朝向
    protected override void OnValidate()
    {
        base.OnValidate();
        if (camTransform != null) RefreshRotation();
    }

    // 获取并缓存场景主相机
    bool TryGetCamera()
    {
        // 相机已销毁或换场景时重新查找
        if (cam != null) return true;

        cam = Camera.main;
        if (cam == null) return false;

        camTransform = cam.transform;
        RefreshRotation();
        return true;
    }

    // 立即将相机摆到角色的跟随位置
    void SnapToTarget()
    {
        followVelocity = Vector3.zero;
        camTransform.position = transform.position + offset;
        RefreshRotation();
    }

    // 按固定偏移设置相机俯视角
    void RefreshRotation()
    {
        Vector3 lookDir = -offset;
        if (lookDir.sqrMagnitude < 1e-6f) return;

        Vector3 flat = new Vector3(lookDir.x, 0f, lookDir.z);

        // 垂直俯视时用世界前方作为相机的上方向
        camTransform.rotation = flat.sqrMagnitude < 1e-4f
            ? Quaternion.LookRotation(lookDir.normalized, Vector3.forward)
            : Quaternion.LookRotation(lookDir.normalized, Vector3.up);
    }
}
