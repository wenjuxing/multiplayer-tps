using Mirror;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 角色移动组件 —— 轮盘式：读方向输入，角色先转向该方向，再朝该方向移动。
/// W+A 同时按时会先合成一个方向向量（正好 45°），转向和位移都按这个合成方向走。
///
/// 网络模型（P0，客户端权威）：只有拥有该角色的客户端读输入并推进位置，
/// 位置/旋转靠 Player 预制体上的 NetworkTransformHybrid(syncDirection = ClientToServer) 同步。
/// P1 改服务端权威时，只需把输入换成 [Command] 上报，转向/位移这套逻辑不用动。
/// </summary>
public class MoveComp : NetworkBehaviour
{
    [Header("速度")]
    [FormerlySerializedAs("moveSpeed")]
    [SerializeField] float walkSpeed = 3f;      // 行走速度：米/秒
    [SerializeField] float runSpeed = 5f;       // 奔跑速度：米/秒
    [SerializeField] float rotateSpeed = 540f;  // 转身角速度：度/秒（540 ≈ 0.33 秒转 180°）

    [Header("轮盘方向参考")]
    [Tooltip("勾选：W 表示「相机正前方（屏幕深处）」，方向以相机为参照；不勾选：直接用世界坐标轴")]
    [SerializeField] bool useCameraYaw = true;
    [Tooltip("勾选：位移严格等于轮盘方向（转身没转完也照走直线）；不勾选：位移严格等于当前朝向（转身过程中走一小段弧线）")]
    [SerializeField] bool moveAlongInputDir = false;

    [Header("重力（只有挂了 CharacterController 才生效）")]
    [SerializeField] float gravity = -20f;
    [SerializeField] float groundStick = -2f;   // 贴地时保留一点向下速度，免得走下坡一跳一跳

    /// <summary>本帧的轮盘方向（世界空间单位向量；没有输入时是零向量）。动画/音效可以读它。</summary>
    public Vector3 InputDirection { get; private set; }
    public bool IsMoving => CanMove && InputDirection != Vector3.zero;
    public bool IsRunning => IsMoving && Input.GetKey(KeyCode.LeftShift);
    public float AnimationSpeed => !IsMoving ? 0f : IsRunning ? 1f : 0.5f;

    /// <summary>死亡、眩晕、被控时置 false，转向和位移都会停。</summary>
    public bool CanMove { get; set; } = true;

    CharacterController controller;
    Transform cameraTransform;   // 懒加载缓存
    float verticalSpeed;

    void Awake()
    {
        // 有 CharacterController 就走它（能跟场景碰撞体互推、能贴地）；
        // 没有就直接改 transform.position（能移动，但会穿墙）。
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        // 只有「本机拥有这个角色」的一端读输入。
        // 专用服务器上 isOwned 恒为 false，其他客户端看到的远端角色也是 false，
        // 所以必须用 isOwned 而不是 isServerOnly，否则每个客户端上别人的角色都会跟着你的按键乱动。
        if (!isOwned) return;

        InputDirection = ReadInputDirection();

        // 没有输入/不能动就原地待命：不转身、不位移。
        // 注意这里必须先判输入再算位移方向 —— FlatForward() 是单位向量，
        // 不判输入的话角色会一直沿朝向自己走。
        // （重力/贴地照旧走 ApplyMotion，它和水平输入无关。）
        if (!CanMove || InputDirection == Vector3.zero)
        {
            ApplyMotion(Vector3.zero);
            return;
        }

        // ---- 1. 先转身：只绕 Y 轴把 forward 转到轮盘方向（不会俯仰/翻滚）----
        // Quaternion.Euler(dir) 那种写法是错的：Euler 的参数是绕 x/y/z 轴的「角度」，
        // 把方向向量塞进去会变成俯仰 + 翻滚，y 恒为 0 → 角色永远不左右转。
        Quaternion targetRot = Quaternion.LookRotation(InputDirection, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, targetRot, rotateSpeed * Time.deltaTime);

        // ---- 2. 再位移：默认沿当前朝向走 → 移动方向和朝向是同一个向量 ----
        Vector3 moveDir = moveAlongInputDir ? InputDirection : FlatForward();
        float speed = IsRunning ? runSpeed : walkSpeed;
        ApplyMotion(moveDir * speed);
    }

    /// <summary>把「以相机为参照的轮盘输入」换算成世界空间方向。</summary>
    Vector3 ReadInputDirection()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 raw = new Vector3(h, 0f, v);
        if (raw.sqrMagnitude < 0.01f) return Vector3.zero;   // 死区：松手就保持当前朝向
        raw.Normalize();                                     // W+A 同时按 → 正好 45°

        Transform cam = useCameraYaw ? GetCameraTransform() : null;
        if (cam == null) return raw;                         // 没相机（如无头服务端）就退回世界坐标轴

        // 相机的 forward/right 投影到水平面，于是 W = 往屏幕深处走，D = 往屏幕右边走
        Vector3 f = cam.forward; f.y = 0f;
        Vector3 r = cam.right;   r.y = 0f;
        if (f.sqrMagnitude < 1e-4f || r.sqrMagnitude < 1e-4f) return raw;   // 相机垂直俯视时兜底

        return (f.normalized * raw.z + r.normalized * raw.x).normalized;
    }

    /// <summary>当前朝向在水平面上的投影（保证纯水平移动，不会被俯仰带飞）。</summary>
    Vector3 FlatForward()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        return f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
    }

    void ApplyMotion(Vector3 horizontalVelocity)
    {
        float dt = Time.deltaTime;

        if (controller != null)
        {
            if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = groundStick;
            verticalSpeed += gravity * dt;
            controller.Move((horizontalVelocity + Vector3.up * verticalSpeed) * dt);
        }
        else
        {
            // 没有 CharacterController 时不加重力，否则角色会一直往下掉
            transform.position += horizontalVelocity * dt;
        }
    }

    Transform GetCameraTransform()
    {
        if (cameraTransform == null)
        {
            Camera main = Camera.main;          // 相机是本地表现层的，客户端才有
            if (main != null) cameraTransform = main.transform;
        }
        return cameraTransform;
    }
}
