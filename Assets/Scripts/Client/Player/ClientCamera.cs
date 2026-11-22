// 跟随本地角色并在渲染时播放命中震动。
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class ClientCamera {
    // 命中震动的持续时间和最大幅度
    private const float HitShakeDuration = 0.12f;
    private const float HitShakeAmplitude = 0.035f;

    private Camera cam;                  // 场景里的 Main Camera
    private Transform camTransform;
    private Vector3 followVelocity;      // Vector3.SmoothDamp 的状态变量
    private bool hitShakeActive;
    private double hitShakeStartedAt;
    private Transform shakenCamera;
    // 渲染前保存的相机位置，用于渲染后恢复
    private Vector3 unshakenPosition;

    // 为本地玩家启动或重置命中震动
    internal void PlayHitShake(Vector3 offset) {
        if (!TryGetCamera(offset)) return;

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
    internal void StopHitShake() {
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

    // 每帧按配置直接或平滑跟随本地角色。
    internal void Follow(Vector3 playerPosition, Vector3 offset, float followSmoothTime) {
        if (!TryGetCamera(offset)) return;

        Vector3 desired = playerPosition + offset;
        camTransform.position = followSmoothTime > 0f
            ? Vector3.SmoothDamp(camTransform.position, desired, ref followVelocity, followSmoothTime)
            : desired;
    }

    // 获取并缓存场景主相机
    private bool TryGetCamera(Vector3 offset)
    {
        // 相机已销毁或换场景时重新查找
        if (cam != null) return true;

        cam = Camera.main;
        if (cam == null) return false;

        camTransform = cam.transform;
        RefreshRotation(offset);
        return true;
    }

    // 立即将相机摆到角色的跟随位置
    internal void SnapToTarget(Vector3 playerPosition, Vector3 offset) {
        if (!TryGetCamera(offset)) return;

        followVelocity = Vector3.zero;
        camTransform.position = playerPosition + offset;
        RefreshRotation(offset);
    }

    // 按固定偏移设置相机俯视角
    internal void RefreshRotation(Vector3 offset) {
        if (camTransform == null) return;

        Vector3 lookDir = -offset;
        if (lookDir.sqrMagnitude < 1e-6f) return;

        Vector3 flat = new Vector3(lookDir.x, 0f, lookDir.z);

        // 垂直俯视时用世界前方作为相机的上方向
        camTransform.rotation = flat.sqrMagnitude < 1e-4f
            ? Quaternion.LookRotation(lookDir.normalized, Vector3.forward)
            : Quaternion.LookRotation(lookDir.normalized, Vector3.up);
    }
}
