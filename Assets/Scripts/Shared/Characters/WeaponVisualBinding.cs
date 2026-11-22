// 提供枪口和持枪姿势的绑定点
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponVisualBinding : MonoBehaviour
{
    // 左手跟随的目标点及两套持枪姿势
    [SerializeField] Transform leftHandIKTarget;
    [SerializeField] Transform leftHandIdleWalkPose;
    [SerializeField] Transform leftHandRunPose;
    // 缺少枪口特效挂点时使用的开火点
    [SerializeField] Transform firePoint;
    // 枪口特效位置，也是优先使用的枪口位置
    [SerializeField] Transform muzzleVfxPoint;

    public Transform LeftHandIKTarget => leftHandIKTarget;
    public Transform LeftHandIdleWalkPose => leftHandIdleWalkPose;
    public Transform LeftHandRunPose => leftHandRunPose;
    public Transform FirePoint => firePoint;
    public Transform MuzzleVfxPoint => muzzleVfxPoint;

    // 检查左手目标点和两套持枪姿势是否齐全
    public bool IsValid()
    {
        return leftHandIKTarget != null && leftHandIdleWalkPose != null && leftHandRunPose != null;
    }
}
