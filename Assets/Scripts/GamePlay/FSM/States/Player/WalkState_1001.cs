// 为角色选择行走档位的水平移动速度。
public class WalkState_1001 : IState {
    // 保存当前角色的速度配置与档位输出。
    private readonly PlayerContext context;

    // 记录行走状态所属角色的移动上下文。
    public WalkState_1001(PlayerContext context) {
        this.context = context;
    }

    // 进入行走后由当帧更新应用配置速度。
    public void OnEnter() { }

    // 读取当帧的行走速度配置。
    public void OnUpdate() {
        context.MoveSpeed = context.WalkSpeed;
    }

    // 退出行走时由下一状态接管速度。
    public void OnExit() { }
}
