// 选择待机档位并清除水平移动速度。
public class IdleState_1001 : IState {
    private readonly PlayerContext context;

    // 记录待机状态所属角色的移动上下文。
    public IdleState_1001(PlayerContext context) {
        this.context = context;
    }

    // 进入待机时停止水平移动。
    public void OnEnter() {
        context.MoveSpeed = 0f;
    }

    // 待机期间保持零水平速度。
    public void OnUpdate() { }

    // 退出待机时由下一状态接管速度。
    public void OnExit() { }
}
