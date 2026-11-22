// 更新本地移动状态、重力和枪口瞄准。
using UnityEngine;
using UnityEngine.InputSystem;

internal sealed class ClientMovement {
    // 判断水平朝向是否有效的最小长度平方
    private const float MinDirectionSqrMagnitude = 0.0001f;
    // 太靠近角色时保留原瞄准方向的距离
    private const float NearAimRadius = 0.24f;

    // 按 W 或 S 时的世界坐标移动方向
    internal Vector3 InputDirection { get; private set; }
    internal bool IsMoving => horizontalSpeed != 0f;
    internal bool IsRunning => movementStates.CurrentState == runState;
    internal float AnimationSpeed => IsMoving ? Mathf.Sign(horizontalSpeed) * (IsRunning ? 1f : 0.5f) : 0f;

    // 每个角色持有自己的状态机、速度上下文和可复用状态。
    private readonly StateMacheController movementStates = new StateMacheController();
    private readonly PlayerContext movementContext = new PlayerContext();
    private readonly IdleState_1001 idleState;
    private readonly WalkState_1001 walkState;
    private readonly RunState_1001 runState;

    private Transform player;
    private CharacterController controller;
    // 计算持枪瞄准方向时使用的枪口
    private Transform muzzle;
    // 保存最近一次有效的瞄准方向
    private Vector3 lastTargetDirection;
    private float horizontalSpeed;
    private float verticalSpeed;

    // 创建当前角色的移动状态并从待机开始。
    internal ClientMovement() {
        idleState = new IdleState_1001(movementContext);
        walkState = new WalkState_1001(movementContext);
        runState = new RunState_1001(movementContext);
        movementStates.ChangeState(idleState);
    }

    // 获取用于移动和贴地的角色控制器
    internal void Initialize(Transform player) {
        this.player = player;
        player.TryGetComponent(out controller);
    }

    // 绑定枪口并清空上一次瞄准方向
    internal void BindMuzzle(Transform firePoint) {
        muzzle = firePoint;
        lastTargetDirection = Vector3.zero;
    }

    // 在失权或停用时清除水平移动和瞄准状态。
    internal void ResetInput() {
        ClearMovement();
        lastTargetDirection = Vector3.zero;
    }

    // 根据本帧的本地输入更新角色朝向和位移。
    internal void Tick(bool canMove, float walkSpeed, float runSpeed, float rotateSpeed, float gravity, float groundStick) {
        // 禁动时仍处理竖直位移。
        if (!canMove) {
            ClearMovement();
            ApplyMotion(Vector3.zero, gravity, groundStick);
            return;
        }

        // W 和 S 同按时抵消，只有方向有效时才按左 Shift 选择奔跑。
        Keyboard keyboard = Keyboard.current;
        int moveDirection = 0;
        IState nextState = idleState;
        if (keyboard != null) {
            bool forwardRequested = keyboard.wKey.isPressed;
            bool backwardRequested = keyboard.sKey.isPressed;
            moveDirection = (forwardRequested ? 1 : 0) - (backwardRequested ? 1 : 0);
            if (moveDirection != 0) {
                nextState = keyboard.leftShiftKey.isPressed ? runState : walkState;
            }
        }

        // 用当帧速度配置更新选中的移动档位。
        movementContext.WalkSpeed = walkSpeed;
        movementContext.RunSpeed = runSpeed;
        movementStates.ChangeState(nextState);
        movementStates.OnUpdate();

        // 按转向速度旋转到瞄准方向
        Vector3 targetDirection = ReadTargetDirection();
        Quaternion targetRotation = Quaternion.LookRotation(targetDirection, Vector3.up);
        player.rotation = Quaternion.RotateTowards(player.rotation, targetRotation, rotateSpeed * Time.deltaTime);

        // 沿转向完成后的角色朝向应用带方向的水平速度。
        InputDirection = targetDirection * moveDirection;
        horizontalSpeed = movementContext.MoveSpeed * moveDirection;
        ApplyMotion(FlatForward() * horizontalSpeed, gravity, groundStick);
    }

    // 根据本地控制状态驱动移动和奔跑动画。
    internal void ApplyAnimation(AnimCompBase animation, bool canMove) {
        animation.SetSpeed(canMove ? AnimationSpeed : 0f);
        animation.SetRunning(canMove && IsRunning);
    }

    // 根据鼠标位置计算水平瞄准方向
    private Vector3 ReadTargetDirection() {
        // 将相机画面内的鼠标投影到枪口高度的水平面
        Camera camera = Camera.main;
        Mouse mouse = Mouse.current;
        if (Application.isFocused && camera != null && mouse != null && mouse.enabled) {
            Vector2 screenPosition = mouse.position.ReadValue();
            Vector3 position = player.position;
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
        Vector3 muzzleOffset = Vector3.ProjectOnPlane(muzzle.position - player.position, Vector3.up);
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
        Vector3 forward = player.forward;
        forward.y = 0f;
        return forward.sqrMagnitude < MinDirectionSqrMagnitude ? Vector3.forward : forward.normalized;
    }

    // 回到待机并清除水平移动输出。
    private void ClearMovement() {
        movementStates.ChangeState(idleState);
        InputDirection = Vector3.zero;
        horizontalSpeed = 0f;
    }

    // 应用水平移动和角色控制器的竖直位移
    private void ApplyMotion(Vector3 horizontalVelocity, float gravity, float groundStick) {
        float deltaTime = Time.deltaTime;
        // 使用角色控制器时处理贴地和重力
        if (controller != null) {
            if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = groundStick;
            verticalSpeed += gravity * deltaTime;
            controller.Move((horizontalVelocity + Vector3.up * verticalSpeed) * deltaTime);
        } else {
            player.position += horizontalVelocity * deltaTime;
        }
    }
}
