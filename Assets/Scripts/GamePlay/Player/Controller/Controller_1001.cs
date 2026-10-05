using Mirror;
using UnityEngine;

public class Controller_1001 : NetworkBehaviour
{
    PlayerContext ctx;
    StateMacheController stateMache;
    IState idleState;
    IState runState;

    void Awake()
    {
        ctx = new PlayerContext
        {
            Move = GetComponent<MoveComp>(),
            Anim = GetComponent<AnimComp_1001>(),
        };

        idleState = new IdleState_1001(ctx);
        runState = new RunState_1001(ctx);

        stateMache = new StateMacheController();
    }

    void Start()
    {
        stateMache.ChangeState(idleState);
    }

    void Update()
    {
        stateMache.OnUpdate();

        bool moving = ctx.Move != null && ctx.Move.CanMove && ctx.Move.InputDirection != Vector3.zero;
        IState next = moving ? runState : idleState;
        if (next != stateMache.CurrentState) stateMache.ChangeState(next);
    }
}
