public class IdleState_1001 : IState
{
    readonly PlayerContext ctx;

    public IdleState_1001(PlayerContext ctx) => this.ctx = ctx;

    public void OnEnter()
    {
        if (ctx.Anim != null) ctx.Anim.SetRunning(false);
    }

    public void OnUpdate() { }

    public void OnExit() { }
}
