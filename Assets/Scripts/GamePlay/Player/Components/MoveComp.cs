using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>由本地拥有者处理鼠标转向和前进，根节点变换通过 NetworkTransformHybrid 同步。</summary>
[DefaultExecutionOrder(-2)]
public class MoveComp : NetworkBehaviour {
    private const float MinDirectionSqrMagnitude = 0.0001f;

    [Header("速度")]
    [FormerlySerializedAs("moveSpeed")]
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float runSpeed = 5f;
    [SerializeField] private float rotateSpeed = 540f;

    [Header("重力（只有挂了 CharacterController 才生效）")]
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float groundStick = -2f;

    /// <summary>按住 W 时的世界空间目标方向，否则为零。</summary>
    public Vector3 InputDirection { get; private set; }
    public bool IsMoving => CanMove && horizontalSpeed != 0f;
    public bool IsRunning => CanMove && runRequested;
    public float AnimationSpeed => !IsMoving ? 0f : IsRunning ? 1f : 0.5f;

    /// <summary>禁动时停止转向和水平位移。</summary>
    public bool CanMove { get; set; } = true;

    private CharacterController controller;
    private Vector3 lastTargetDirection;
    private float horizontalSpeed;
    private float verticalSpeed;
    private bool runRequested;

    private void Awake() {
        controller = GetComponent<CharacterController>();
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
        bool moveRequested = keyboard != null && keyboard.wKey.isPressed;
        runRequested = moveRequested && keyboard.leftShiftKey.isPressed;

        Vector3 targetDirection = ReadTargetDirection();
        Quaternion targetRotation = Quaternion.LookRotation(targetDirection, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotateSpeed * Time.deltaTime);

        InputDirection = moveRequested ? targetDirection : Vector3.zero;
        horizontalSpeed = moveRequested ? (runRequested ? runSpeed : walkSpeed) : 0f;
        ApplyMotion(FlatForward() * horizontalSpeed);
    }

    private Vector3 ReadTargetDirection() {
        Camera camera = Camera.main;
        Mouse mouse = Mouse.current;
        if (camera != null && mouse != null) {
            Ray ray = camera.ScreenPointToRay(mouse.position.ReadValue());
            Vector3 position = transform.position;
            Plane movementPlane = new Plane(Vector3.up, position);
            if (movementPlane.Raycast(ray, out float distance)) {
                Vector3 direction = ray.GetPoint(distance) - position;
                direction.y = 0f;
                if (direction.sqrMagnitude >= MinDirectionSqrMagnitude)
                    lastTargetDirection = direction.normalized;
            }
        }

        return lastTargetDirection == Vector3.zero ? FlatForward() : lastTargetDirection;
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
