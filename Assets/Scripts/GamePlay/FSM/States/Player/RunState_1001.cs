// 处理角色 1001 进入奔跑状态时的动画
public class RunState_1001 : IState
{
    readonly PlayerContext ctx;

    // 保存角色组件引用
    public RunState_1001(PlayerContext ctx) => this.ctx = ctx;

    // 进入奔跑时开启奔跑动画标记
    public void OnEnter()
    {
        if (ctx.Anim != null) ctx.Anim.SetRunning(true);
    }

    // 保留奔跑状态的每帧更新入口
    public void OnUpdate() { }

    // 保留退出奔跑状态的处理入口
    public void OnExit() { }
}
