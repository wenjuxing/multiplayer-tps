// 根据本地输入控制角色移动和枪口瞄准
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

// 本地玩家移动后由网络组件同步角色根节点
[DefaultExecutionOrder(-2)]
public class MoveComp : NetworkBehaviour {
    // 判断水平朝向是否有效的最小长度平方
    private const float MinDirectionSqrMagnitude = 0.0001f;
    // 太靠近角色时保留原瞄准方向的距离
    private const float NearAimRadius = 0.24f;

    // 行走、奔跑和转向的速度配置
    [Header("速度")]
    [FormerlySerializedAs("moveSpeed")]
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float runSpeed = 5f;
    [SerializeField] private float rotateSpeed = 540f;

    // 角色控制器使用的重力和贴地速度
    [Header("重力（只有挂了 CharacterController 才生效）")]
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float groundStick = -2f;

    // 按 W 或 S 时的世界坐标移动方向
    public Vector3 InputDirection { get; private set; }
    public bool IsMoving => CanMove && horizontalSpeed != 0f;
    public bool IsRunning => CanMove && runRequested;
    public float AnimationSpeed => IsMoving ? Mathf.Sign(horizontalSpeed) * (IsRunning ? 1f : 0.5f) : 0f;

    // 禁动时停用转向和水平移动
    public bool CanMove { get; set; } = true;

    private CharacterController controller;
    // 计算持枪瞄准方向时使用的枪口
    private Transform muzzle;
    // 保存最近一次有效的瞄准方向
    private Vector3 lastTargetDirection;
    private float horizontalSpeed;
    private float verticalSpeed;
    private bool runRequested;

    // 获取用于移动和贴地的角色控制器
    private void Awake() {
        controller = GetComponent<CharacterController>();
    }

    // 绑定枪口并清空上一次瞄准方向
    public void BindMuzzle(Transform firePoint) {
        muzzle = firePoint;
        lastTargetDirection = Vector3.zero;
    }

    // 失去角色控制权时清除移动和瞄准状态
    public override void OnStopAuthority() {
        ClearMovement();
        lastTargetDirection = Vector3.zero;
    }

    // 组件禁用时清除移动和瞄准状态
    private void OnDisable() {
        ClearMovement();
        lastTargetDirection = Vector3.zero;
    }

    // 读取本地输入并更新角色朝向和位移
    private void Update() {
        // 仅本地拥有的角色读取输入，禁动时继续处理竖直位移
        if (!isOwned) return;

        if (!CanMove) {
            ClearMovement();
            ApplyMotion(Vector3.zero);
            return;
        }

        // 读取前进、后退和奔跑输入
        Keyboard keyboard = Keyboard.current;
        bool forwardRequested = keyboard != null && keyboard.wKey.isPressed;
        bool backwardRequested = keyboard != null && keyboard.sKey.isPressed;
        int moveDirection = (forwardRequested ? 1 : 0) - (backwardRequested ? 1 : 0);
        runRequested = moveDirection != 0 && keyboard.leftShiftKey.isPressed;

        // 按转向速度旋转到瞄准方向
        Vector3 targetDirection = ReadTargetDirection();
        Quaternion targetRotation = Quaternion.LookRotation(targetDirection, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotateSpeed * Time.deltaTime);

        // 更新移动状态并沿角色当前朝向移动
        InputDirection = targetDirection * moveDirection;
        horizontalSpeed = (runRequested ? runSpeed : walkSpeed) * moveDirection;
        ApplyMotion(FlatForward() * horizontalSpeed);
    }

    // 根据鼠标位置计算水平瞄准方向
    private Vector3 ReadTargetDirection() {
        // 将相机画面内的鼠标投影到枪口高度的水平面
        Camera camera = Camera.main;
        Mouse mouse = Mouse.current;
        if (Application.isFocused && camera != null && mouse != null && mouse.enabled) {
            Vector2 screenPosition = mouse.position.ReadValue();
            Vector3 position = transform.position;
            Vector3 planePoint = muzzle != null ? muzzle.position : position;
            Plane aimPlane = new Plane(Vector3.up, planePoint);
            Ray ray = camera.ScreenPointToRay(screenPosition);
            if (camera.pixelRect.Contains(screenPosition) && aimPlane.Raycast(ray, out float distance)) {
                Vector3 toTarget = ray.GetPoint(distance) - position;
                toTarget.y = 0f;
                // 有枪口时校正枪管方向，否则用角色到瞄准点的方向
                if (muzzle != null) {
                    if (TryGetMuzzleTargetDirection(toTarget, out Vector3 direction))
                        lastTargetDirection = direction;
                } else if (toTarget.sqrMagnitude >= MinDirectionSqrMagnitude) {
                    lastTargetDirection = toTarget.normalized;
                }
            }
        }

        // 当前输入无效时保留最近有效方向，首次使用角色正前方
        return lastTargetDirection == Vector3.zero ? FlatForward() : lastTargetDirection;
    }

    // 计算让枪管指向目标所需的角色朝向
    private bool TryGetMuzzleTargetDirection(Vector3 toTarget, out Vector3 direction) {
        direction = default;
        // 跳过过近目标和无法确定水平朝向的枪管
        float targetDistanceSquared = toTarget.sqrMagnitude;
        Vector3 bore = Vector3.ProjectOnPlane(muzzle.forward, Vector3.up);
        if (!float.IsFinite(targetDistanceSquared) || targetDistanceSquared < NearAimRadius * NearAimRadius ||
            bore.sqrMagnitude < MinDirectionSqrMagnitude) return false;

        // 分解枪口相对角色的前后和侧向偏移
        bore.Normalize();
        Vector3 muzzleOffset = Vector3.ProjectOnPlane(muzzle.position - transform.position, Vector3.up);
        float forwardOffset = Vector3.Dot(muzzleOffset, bore);
        Vector3 sideOffset = muzzleOffset - bore * forwardOffset;
        float axialDistanceSquared = targetDistanceSquared - sideOffset.sqrMagnitude;
        if (axialDistanceSquared <= 0f) return false;

        // 在枪管轴线上找到与目标距角色中心相同的点
        float axialDistance = Mathf.Sqrt(axialDistanceSquared);
        if (axialDistance <= forwardOffset) return false;

        // 按两点的水平夹角旋转角色，保留持枪姿势
        Vector3 pointOnBore = sideOffset + bore * axialDistance;
        float yaw = Vector3.SignedAngle(pointOnBore, toTarget, Vector3.up);
        direction = Quaternion.AngleAxis(yaw, Vector3.up) * FlatForward();
        return true;
    }

    // 取得角色的水平正前方
    private Vector3 FlatForward() {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude < MinDirectionSqrMagnitude ? Vector3.forward : forward.normalized;
    }

    // 清除水平移动和奔跑状态
    private void ClearMovement() {
        InputDirection = Vector3.zero;
        horizontalSpeed = 0f;
        runRequested = false;
    }

    // 应用水平移动和角色控制器的竖直位移
    private void ApplyMotion(Vector3 horizontalVelocity) {
        float deltaTime = Time.deltaTime;
        // 使用角色控制器时处理贴地和重力
        if (controller != null) {
            if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = groundStick;
            verticalSpeed += gravity * deltaTime;
            controller.Move((horizontalVelocity + Vector3.up * verticalSpeed) * deltaTime);
        } else {
            transform.position += horizontalVelocity * deltaTime;
        }
    }
}
