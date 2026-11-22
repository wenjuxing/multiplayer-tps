// 定义状态进入、更新和退出时的行为
public interface IState
{
    // 执行进入状态时的处理
    public void OnEnter();
    // 执行当前状态的每帧处理
    public void OnUpdate();
    // 执行退出状态时的处理
    public void OnExit();
}
