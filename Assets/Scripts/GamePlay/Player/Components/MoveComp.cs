using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>由本地拥有者处理鼠标转向和前后移动，根节点变换通过 NetworkTransformHybrid 同步。</summary>
[DefaultExecutionOrder(-2)]
public class MoveComp : NetworkBehaviour {
    private const float MinDirectionSqrMagnitude = 0.0001f;
    private const float NearAimRadius = 0.24f;

    [Header("速度")]
    [FormerlySerializedAs("moveSpeed")]
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float runSpeed = 5f;
    [SerializeField] private float rotateSpeed = 540f;

    [Header("重力（只有挂了 CharacterController 才生效）")]
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float groundStick = -2f;

    /// <summary>按 W 或 S 时的世界空间目标移动方向，无有效方向时为零。</summary>
    public Vector3 InputDirection { get; private set; }
    public bool IsMoving => CanMove && horizontalSpeed != 0f;
    public bool IsRunning => CanMove && runRequested;
    public float AnimationSpeed => IsMoving ? Mathf.Sign(horizontalSpeed) * (IsRunning ? 1f : 0.5f) : 0f;

    /// <summary>禁动时停止转向和水平位移。</summary>
    public bool CanMove { get; set; } = true;

    private CharacterController controller;
    private Transform muzzle;
    private Vector3 lastTargetDirection;
    private float horizontalSpeed;
    private float verticalSpeed;
    private bool runRequested;

    private void Awake() {
        controller = GetComponent<CharacterController>();
    }

    public void BindMuzzle(Transform firePoint) {
        muzzle = firePoint;
        lastTargetDirection = Vector3.zero;
    }

    public override void OnStopAuthority() {
        ClearMovement();
        lastTargetDirection = Vector3.zero;
    }

    private void OnDisable() {
        ClearMovement();
        lastTargetDirection = Vector3.zero;
    }

    private void Update() {
        if (!isOwned) return;

        if (!CanMove) {
            ClearMovement();
            ApplyMotion(Vector3.zero);
            return;
        }

        Keyboard keyboard = Keyboard.current;
        bool forwardRequested = keyboard != null && keyboard.wKey.isPressed;
        bool backwardRequested = keyboard != null && keyboard.sKey.isPressed;
        int moveDirection = (forwardRequested ? 1 : 0) - (backwardRequested ? 1 : 0);
        runRequested = moveDirection != 0 && keyboard.leftShiftKey.isPressed;

        Vector3 targetDirection = ReadTargetDirection();
        Quaternion targetRotation = Quaternion.LookRotation(targetDirection, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotateSpeed * Time.deltaTime);

        InputDirection = targetDirection * moveDirection;
        horizontalSpeed = (runRequested ? runSpeed : walkSpeed) * moveDirection;
        ApplyMotion(FlatForward() * horizontalSpeed);
    }

    private Vector3 ReadTargetDirection() {
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
                if (muzzle != null) {
                    if (TryGetMuzzleTargetDirection(toTarget, out Vector3 direction))
                        lastTargetDirection = direction;
                } else if (toTarget.sqrMagnitude >= MinDirectionSqrMagnitude) {
                    lastTargetDirection = toTarget.normalized;
                }
            }
        }

        return lastTargetDirection == Vector3.zero ? FlatForward() : lastTargetDirection;
    }

    private bool TryGetMuzzleTargetDirection(Vector3 toTarget, out Vector3 direction) {
        direction = default;
        float targetDistanceSquared = toTarget.sqrMagnitude;
        Vector3 bore = Vector3.ProjectOnPlane(muzzle.forward, Vector3.up);
        if (!float.IsFinite(targetDistanceSquared) || targetDistanceSquared < NearAimRadius * NearAimRadius ||
            bore.sqrMagnitude < MinDirectionSqrMagnitude) return false;

        bore.Normalize();
        Vector3 muzzleOffset = Vector3.ProjectOnPlane(muzzle.position - transform.position, Vector3.up);
        float forwardOffset = Vector3.Dot(muzzleOffset, bore);
        Vector3 sideOffset = muzzleOffset - bore * forwardOffset;
        float axialDistanceSquared = targetDistanceSquared - sideOffset.sqrMagnitude;
        if (axialDistanceSquared <= 0f) return false;

        // Find a point on the current bore at the same distance from the pivot as the target.
        // The yaw between those points aligns the whole character without changing the grip.
        float axialDistance = Mathf.Sqrt(axialDistanceSquared);
        if (axialDistance <= forwardOffset) return false;

        Vector3 pointOnBore = sideOffset + bore * axialDistance;
        float yaw = Vector3.SignedAngle(pointOnBore, toTarget, Vector3.up);
        direction = Quaternion.AngleAxis(yaw, Vector3.up) * FlatForward();
        return true;
    }

    private Vector3 FlatForward() {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude < MinDirectionSqrMagnitude ? Vector3.forward : forward.normalized;
    }

    private void ClearMovement() {
        InputDirection = Vector3.zero;
        horizontalSpeed = 0f;
        runRequested = false;
    }

    private void ApplyMotion(Vector3 horizontalVelocity) {
        float deltaTime = Time.deltaTime;
        if (controller != null) {
            if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = groundStick;
            verticalSpeed += gravity * deltaTime;
            controller.Move((horizontalVelocity + Vector3.up * verticalSpeed) * deltaTime);
        } else {
            transform.position += horizontalVelocity * deltaTime;
        }
    }
}
