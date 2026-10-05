using UnityEngine;

[DisallowMultipleComponent]
public abstract class AnimCompBase : MonoBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField] float runSpeedThreshold = 0.1f;

    protected abstract string RunningParam { get; }
    public bool IsRunning { get; private set; }
    public bool HasAnimator => animator != null;

    int runningHash;
    bool runningParamValid;

    protected virtual void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);

        runningHash = Animator.StringToHash(RunningParam);
        runningParamValid = ValidateRunningParam();

        IsRunning = false;
        ApplyRunning(false);
    }

    protected virtual void OnEnable() => ApplyRunning(IsRunning);

    public void SetRunning(bool running)
    {
        if (running == IsRunning) return;

        IsRunning = running;
        ApplyRunning(running);
    }

    public void SetRunningBySpeed(float planarSpeed) => SetRunning(planarSpeed > runSpeedThreshold);

    public void Refresh() => ApplyRunning(IsRunning);

    void ApplyRunning(bool running)
    {
        if (!runningParamValid || animator == null) return;
        if (!animator.isActiveAndEnabled) return;

        animator.SetBool(runningHash, running);
    }

    bool ValidateRunningParam()
    {
        if (animator == null)
        {
            Debug.LogWarning($"[AnimComp] {name}：找不到 Animator。", this);
            return false;
        }

        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning($"[AnimComp] {name}：Animator 上没有挂 AnimatorController。", this);
            return false;
        }

        foreach (AnimatorControllerParameter par in animator.parameters)
        {
            if (par.nameHash != runningHash) continue;
            if (par.type == AnimatorControllerParameterType.Bool) return true;

            Debug.LogError($"[AnimComp] {name}：参数 {RunningParam} 不是 Bool 类型。", this);
            return false;
        }

        Debug.LogError($"[AnimComp] {name}：找不到 Bool 参数 {RunningParam}。", this);
        return false;
    }
}
