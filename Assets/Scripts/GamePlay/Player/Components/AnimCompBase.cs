using Mirror;
using UnityEngine;

[DisallowMultipleComponent]
public abstract class AnimCompBase : MonoBehaviour
{
    public const string SpeedParameterName = "Speed";
    public const string RunningParameterName = "IsRunning";
    public const string ShootParameterName = "Shoot";

    [SerializeField] Animator animator;
    [SerializeField, Min(0f)] float speedDampTime = 0.12f;

    protected virtual string SpeedParam => SpeedParameterName;
    protected virtual string RunningParam => RunningParameterName;
    protected virtual string ShootParam => ShootParameterName;

    public bool IsRunning { get; private set; }
    public float AnimationSpeed { get; private set; }
    public bool HasAnimator => animator != null;
    public bool AnimatorIsRunning =>
        animator != null && runningParamValid && animator.GetBool(runningHash);

    int speedHash;
    int runningHash;
    int shootHash;
    bool speedParamValid;
    bool runningParamValid;
    bool shootParamValid;
    NetworkAnimator networkAnimator;

    protected virtual void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        networkAnimator = GetComponent<NetworkAnimator>();

        InitializeAnimator(animator);
    }

    public void InitializeAnimator(Animator value)
    {
        animator = value;
        if (networkAnimator == null) networkAnimator = GetComponent<NetworkAnimator>();

        if (animator == null || animator.runtimeAnimatorController == null)
        {
            speedParamValid = false;
            runningParamValid = false;
            shootParamValid = false;
            return;
        }

        speedHash = Animator.StringToHash(SpeedParam);
        runningHash = Animator.StringToHash(RunningParam);
        shootHash = Animator.StringToHash(ShootParam);

        speedParamValid = ValidateParameter(SpeedParam, AnimatorControllerParameterType.Float);
        runningParamValid = ValidateParameter(RunningParam, AnimatorControllerParameterType.Bool);
        shootParamValid = ValidateParameter(ShootParam, AnimatorControllerParameterType.Trigger);

        IsRunning = false;
        AnimationSpeed = 0f;
        ApplySpeed(0f);
        ApplyRunning(false);
    }

    protected virtual void OnEnable()
    {
        ApplySpeed(AnimationSpeed);
        ApplyRunning(IsRunning);
    }

    public void SetSpeed(float speed)
    {
        AnimationSpeed = Mathf.Clamp(speed, -1f, 1f);
        ApplySpeed(AnimationSpeed);
    }

    public void SetRunning(bool running)
    {
        IsRunning = running;
        ApplyRunning(running);
    }

    public void SetRunningBySpeed(float planarSpeed) => SetRunning(planarSpeed > 0.5f);

    public bool PlayShoot()
    {
        if (IsRunning || !shootParamValid || animator == null || !animator.isActiveAndEnabled)
            return false;

        if (networkAnimator != null)
            networkAnimator.SetTrigger(shootHash);
        else
            animator.SetTrigger(shootHash);

        return true;
    }

    public void Refresh()
    {
        ApplySpeed(AnimationSpeed);
        ApplyRunning(IsRunning);
    }

    void ApplySpeed(float speed)
    {
        if (!speedParamValid || animator == null || !animator.isActiveAndEnabled) return;
        if (speed == 0f)
            animator.SetFloat(speedHash, speed);
        else
            animator.SetFloat(speedHash, speed, speedDampTime, Time.deltaTime);
    }

    void ApplyRunning(bool running)
    {
        if (!runningParamValid || animator == null || !animator.isActiveAndEnabled) return;
        animator.SetBool(runningHash, running);
    }

    bool ValidateParameter(string parameterName, AnimatorControllerParameterType expectedType)
    {
        if (animator == null)
        {
            Debug.LogWarning($"[AnimComp] {name}: 找不到 Animator。", this);
            return false;
        }

        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning($"[AnimComp] {name}: Animator 上没有挂 AnimatorController。", this);
            return false;
        }

        int hash = Animator.StringToHash(parameterName);
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash != hash) continue;
            if (parameter.type == expectedType) return true;

            Debug.LogError($"[AnimComp] {name}: 参数 {parameterName} 类型错误，期望 {expectedType}。", this);
            return false;
        }

        Debug.LogError($"[AnimComp] {name}: 找不到 {expectedType} 参数 {parameterName}。", this);
        return false;
    }
}
