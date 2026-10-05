public class RunState_1001 : IState
{
    readonly PlayerContext ctx;

    public RunState_1001(PlayerContext ctx) => this.ctx = ctx;

    public void OnEnter()
    {
        if (ctx.Anim != null) ctx.Anim.SetRunning(true);
    }

    public void OnUpdate() { }

    public void OnExit() { }
}
