// 在本地角色拥有控制权时接入移动和瞄准逻辑。
using Mirror;
using UnityEngine;
using UnityEngine.Serialization;

[DefaultExecutionOrder(-2)]
public class MoveComp : NetworkBehaviour {
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

    private readonly ClientMovement movement = new ClientMovement();

    public Vector3 InputDirection => movement.InputDirection;
    public bool IsMoving => CanMove && movement.IsMoving;
    public bool IsRunning => CanMove && movement.IsRunning;
    public float AnimationSpeed => CanMove ? movement.AnimationSpeed : 0f;

    // 禁动时停用转向和水平移动。
    public bool CanMove { get; set; } = true;

    // 初始化本地移动逻辑使用的角色对象。
    private void Awake() {
        movement.Initialize(transform);
    }

    // 将装配提供的枪口交给瞄准逻辑。
    public void BindMuzzle(Transform firePoint) {
        movement.BindMuzzle(firePoint);
    }

    // 失去控制权时清除移动和瞄准状态。
    public override void OnStopAuthority() {
        movement.ResetInput();
    }

    // 组件停用时清除移动和瞄准状态。
    private void OnDisable() {
        movement.ResetInput();
    }

    // 把本地移动状态交给客户端动画驱动。
    internal void ApplyLocalAnimation(AnimCompBase animation) {
        movement.ApplyAnimation(animation, CanMove);
    }

    // 在相机跟随和开火采集以前更新本地角色。
    private void Update() {
        if (isOwned)
            movement.Tick(CanMove, walkSpeed, runSpeed, rotateSpeed, gravity, groundStick);
    }
}
