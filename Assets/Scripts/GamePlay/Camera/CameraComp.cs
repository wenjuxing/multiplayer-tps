using Mirror;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 第三人称上帝视角跟随相机 —— 挂在角色（Player 预制体）上，由本地玩家自己驱动场景里的 Main Camera。
///
/// 位置：在 MoveComp 更新角色位移后跟随，相机位姿供本帧瞄准和 HUD 使用。
/// 朝向：只由 offset 决定，是个常量 —— 所以相机朝向不随角色朝向改变，转的只有角色自己。
///
/// 因为它是 NetworkBehaviour，只有「本机拥有的那个角色」（isOwned）才会去动相机：
/// 专用服务器和每个客户端上看到的远端角色都不会碰相机。
/// 预制体不能引用场景物体，所以相机是在运行时按 tag 找 Camera.main。
/// </summary>
[DefaultExecutionOrder(-1)]
public class CameraComp : NetworkBehaviour
{
    private const float HitShakeDuration = 0.12f;
    private const float HitShakeAmplitude = 0.035f;

    [Header("相对角色的固定偏移（世界空间）")]
    [Tooltip("相机 = 角色位置 + 这个偏移。y 越大越俯视，z 越小越靠后")]
    [SerializeField] Vector3 offset = new Vector3(0f, 8f, -6f);

    [Header("平滑")]
    [Tooltip("0 = 硬跟随，每帧贴死角色；大于 0 = 平滑跟随的阻尼时间（秒）")]
    [SerializeField] float followSmoothTime = 0f;

    Camera cam;                  // 场景里的 Main Camera
    Transform camTransform;
    Vector3 followVelocity;      // Vector3.SmoothDamp 的状态变量
    private bool hitShakeActive;
    private double hitShakeStartedAt;
    private Transform shakenCamera;
    private Vector3 unshakenPosition;

    // 本地玩家一生成就把相机摆到位，避免开局第一帧相机还停在场景里的原始位置
    public override void OnStartLocalPlayer()
    {
        if (!TryGetCamera()) return;
        SnapToTarget();
    }

    public override void OnStopAuthority() {
        StopHitShake();
    }

    private void OnDisable() {
        StopHitShake();
    }

    internal void PlayHitShake() {
        if (!isActiveAndEnabled || !isOwned || !TryGetCamera()) return;

        hitShakeStartedAt = Time.unscaledTimeAsDouble;
        if (hitShakeActive) return;

        hitShakeActive = true;
        RenderPipelineManager.beginCameraRendering += ApplyHitShake;
        RenderPipelineManager.endCameraRendering += EndCameraRendering;
    }

    private void ApplyHitShake(ScriptableRenderContext context, Camera camera) {
        if (camera != cam || shakenCamera != null) return;

        float progress = (float)(Time.unscaledTimeAsDouble - hitShakeStartedAt) / HitShakeDuration;
        if (progress >= 1f) {
            StopHitShake();
            return;
        }

        // Keep the shake outside gameplay updates so mouse aiming and follow use the base pose.
        float phase = progress * 4f * Mathf.PI;
        float strength = HitShakeAmplitude * (1f - progress);
        Vector3 displacement = new Vector3(Mathf.Cos(phase), 0.5f * Mathf.Sin(phase), 0f) * strength;
        shakenCamera = camTransform;
        unshakenPosition = shakenCamera.position;
        shakenCamera.position += shakenCamera.TransformDirection(displacement);
    }

    private void EndCameraRendering(ScriptableRenderContext context, Camera camera) {
        if (camera == cam)
            RestoreCameraPosition();
    }

    private void StopHitShake() {
        if (!hitShakeActive) return;

        hitShakeActive = false;
        RenderPipelineManager.beginCameraRendering -= ApplyHitShake;
        RenderPipelineManager.endCameraRendering -= EndCameraRendering;
        RestoreCameraPosition();
    }

    private void RestoreCameraPosition() {
        if (shakenCamera != null)
            shakenCamera.position = unshakenPosition;
        shakenCamera = null;
    }

    private void Update()
    {
        // 只有本机拥有的角色驱动相机；远端角色、专用服务器上的角色都跳过
        if (!isOwned) return;
        if (!TryGetCamera()) return;

        Vector3 desired = transform.position + offset;
        camTransform.position = followSmoothTime > 0f
            ? Vector3.SmoothDamp(camTransform.position, desired, ref followVelocity, followSmoothTime)
            : desired;
    }

    // 运行时在 Inspector 里改 offset 立刻刷新俯视角。
    // NetworkBehaviour 自己也声明了 OnValidate（校验 NetworkIdentity），必须 override 并调用 base。
    protected override void OnValidate()
    {
        base.OnValidate();
        if (camTransform != null) RefreshRotation();
    }

    bool TryGetCamera()
    {
        if (cam != null) return true;      // Unity 的 == null 对已销毁对象也成立，所以换场景后会重新找

        cam = Camera.main;                 // 无头专用服务器上没有相机，这里会一直返回 false
        if (cam == null) return false;

        camTransform = cam.transform;
        RefreshRotation();                 // 刚找到就把朝向摆成固定的俯视角
        return true;
    }

    void SnapToTarget()
    {
        followVelocity = Vector3.zero;
        camTransform.position = transform.position + offset;
        RefreshRotation();
    }

    /// <summary>
    /// 朝向是 offset 的函数、和角色无关，所以不用每帧算：找到相机 / 改 offset / 重新跟随时刷新一次就够。
    /// </summary>
    void RefreshRotation()
    {
        Vector3 lookDir = -offset;         // 相机看向角色，视线方向就是偏移的反方向
        if (lookDir.sqrMagnitude < 1e-6f) return;

        Vector3 flat = new Vector3(lookDir.x, 0f, lookDir.z);

        // 正上方垂直俯视时视线与 world up 平行，LookRotation 会失效，改用世界前方当 up
        camTransform.rotation = flat.sqrMagnitude < 1e-4f
            ? Quaternion.LookRotation(lookDir.normalized, Vector3.forward)
            : Quaternion.LookRotation(lookDir.normalized, Vector3.up);
    }
}
