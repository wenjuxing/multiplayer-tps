// 处理角色 1001 进入待机状态时的动画
public class IdleState_1001 : IState
{
    readonly PlayerContext ctx;

    // 保存角色组件引用
    public IdleState_1001(PlayerContext ctx) => this.ctx = ctx;

    // 进入待机时清除奔跑动画标记
    public void OnEnter()
    {
        if (ctx.Anim != null) ctx.Anim.SetRunning(false);
    }

    // 保留待机状态的每帧更新入口
    public void OnUpdate() { }

    // 保留退出待机状态的处理入口
    public void OnExit() { }
}
