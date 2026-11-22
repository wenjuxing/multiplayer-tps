// 管理状态切换和当前状态的更新
using UnityEngine;

public class StateMacheController
{
    // 记录正在运行的状态
    private IState _currentState;
    public IState CurrentState => _currentState;

    // 切换到指定状态
    public void ChangeState(IState newState)
    {
        // 跳过无效或重复的切换请求
        if (newState == null || newState == _currentState) return;

        // 先退出旧状态，再进入新状态
        _currentState?.OnExit();
        _currentState = newState;
        _currentState.OnEnter();
    }
    // 更新当前状态
    public void OnUpdate()
    {
        _currentState?.OnUpdate();
    }
}
