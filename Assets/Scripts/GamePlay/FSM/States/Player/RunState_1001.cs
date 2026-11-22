// 为角色选择奔跑档位的水平移动速度。
public class RunState_1001 : IState {
    private readonly PlayerContext context;

    // 记录奔跑状态所属角色的移动上下文。
    public RunState_1001(PlayerContext context) {
        this.context = context;
    }

    // 进入奔跑后由当帧更新应用配置速度。
    public void OnEnter() { }

    // 读取当帧的奔跑速度配置。
    public void OnUpdate() {
        context.MoveSpeed = context.RunSpeed;
    }

    // 退出奔跑时由下一状态接管速度。
    public void OnExit() { }
}
