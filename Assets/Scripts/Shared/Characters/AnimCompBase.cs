// 管理角色移动、奔跑和开火的动画参数
using System;
using UnityEngine;

[DisallowMultipleComponent]
public abstract class AnimCompBase : MonoBehaviour
{
    // 动画控制器中使用的参数名
    public const string SpeedParameterName = "Speed";
    public const string RunningParameterName = "IsRunning";
    public const string ShootParameterName = "Shoot";

    [SerializeField] Animator animator;
    // 移动速度参数的过渡时间
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
    // 用于同步开火动画触发器
    private Func<int, bool> trySendShootTrigger;

    // 查找动画组件并初始化参数
    protected virtual void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);

        InitializeAnimator(animator);
    }

    // 绑定动画控制器并准备参数
    public void InitializeAnimator(Animator value)
    {
        animator = value;

        // 动画控制器缺失时清空参数有效标记
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            speedParamValid = false;
            runningParamValid = false;
            shootParamValid = false;
            return;
        }

        // 缓存参数编号并检查参数类型
        speedHash = Animator.StringToHash(SpeedParam);
        runningHash = Animator.StringToHash(RunningParam);
        shootHash = Animator.StringToHash(ShootParam);

        AnimatorControllerParameter[] parameters = animator.parameters;
        speedParamValid = ValidateParameter(parameters, SpeedParam, speedHash, AnimatorControllerParameterType.Float);
        runningParamValid = ValidateParameter(parameters, RunningParam, runningHash, AnimatorControllerParameterType.Bool);
        shootParamValid = ValidateParameter(parameters, ShootParam, shootHash, AnimatorControllerParameterType.Trigger);

        // 将移动和奔跑动画重置为待机状态
        IsRunning = false;
        AnimationSpeed = 0f;
        ApplySpeed(0f);
        ApplyRunning(false);
    }

    // 组件启用时应用当前移动和奔跑参数
    protected virtual void OnEnable()
    {
        Refresh();
    }

    // 限制并应用动画速度参数
    public void SetSpeed(float speed)
    {
        AnimationSpeed = Mathf.Clamp(speed, -1f, 1f);
        ApplySpeed(AnimationSpeed);
    }

    // 设置动画的奔跑状态
    public void SetRunning(bool running)
    {
        IsRunning = running;
        ApplyRunning(running);
    }

    // 按水平速度判断是否播放奔跑动画
    public void SetRunningBySpeed(float planarSpeed) => SetRunning(planarSpeed > 0.5f);

    // 满足播放条件时触发开火动画
    public bool PlayShoot()
    {
        // 奔跑或动画不可用时跳过开火动画
        if (IsRunning || !shootParamValid || animator == null || !animator.isActiveAndEnabled)
            return false;

        // 优先通过网络动画组件同步开火触发器
        if (trySendShootTrigger == null || !trySendShootTrigger(shootHash))
            animator.SetTrigger(shootHash);

        return true;
    }

    // 接入网络开火触发器，发送不可用时继续使用本地 Animator。
    internal void BindShootTrigger(Func<int, bool> sender) {
        trySendShootTrigger = sender;
    }

    // 将当前移动和奔跑状态重新应用到动画控制器
    public void Refresh()
    {
        ApplySpeed(AnimationSpeed);
        ApplyRunning(IsRunning);
    }

    // 更新动画速度，停止时立即归零
    void ApplySpeed(float speed)
    {
        if (!speedParamValid || animator == null || !animator.isActiveAndEnabled) return;
        if (speed == 0f)
            animator.SetFloat(speedHash, speed);
        else
            animator.SetFloat(speedHash, speed, speedDampTime, Time.deltaTime);
    }

    // 在奔跑参数可用时更新其值
    void ApplyRunning(bool running)
    {
        if (!runningParamValid || animator == null || !animator.isActiveAndEnabled) return;
        animator.SetBool(runningHash, running);
    }

    // 检查动画参数是否存在且类型匹配
    private bool ValidateParameter(AnimatorControllerParameter[] parameters, string parameterName, int hash,
        AnimatorControllerParameterType expectedType) {
        // 逐项匹配动画参数并检查类型
        foreach (AnimatorControllerParameter parameter in parameters)
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
