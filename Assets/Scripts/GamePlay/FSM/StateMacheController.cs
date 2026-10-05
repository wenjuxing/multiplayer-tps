using UnityEngine;

public class StateMacheController
{
    private IState _currentState;
    public IState CurrentState => _currentState;

    public void ChangeState(IState newState)
    {
        if (newState == null || newState == _currentState) return;

        _currentState?.OnExit();
        _currentState = newState;
        _currentState.OnEnter();
    }
    public void OnUpdate()
    {
        _currentState?.OnUpdate();
    }
}
