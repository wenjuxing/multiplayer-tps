// 为角色 1001 绑定移动、奔跑和开火动画参数
using UnityEngine;

public class AnimComp_1001 : AnimCompBase
{
    protected override string SpeedParam => SpeedParameterName;
    protected override string RunningParam => RunningParameterName;
    protected override string ShootParam => ShootParameterName;
}
