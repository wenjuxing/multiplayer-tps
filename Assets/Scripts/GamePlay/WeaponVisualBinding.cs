using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponVisualBinding : MonoBehaviour
{
    [SerializeField] Transform leftHandIKTarget;
    [SerializeField] Transform leftHandIdleWalkPose;
    [SerializeField] Transform leftHandRunPose;
    [SerializeField] Transform firePoint;
    [SerializeField] Transform muzzleVfxPoint;

    public Transform LeftHandIKTarget => leftHandIKTarget;
    public Transform LeftHandIdleWalkPose => leftHandIdleWalkPose;
    public Transform LeftHandRunPose => leftHandRunPose;
    public Transform FirePoint => firePoint;
    public Transform MuzzleVfxPoint => muzzleVfxPoint;

    public bool IsValid()
    {
        return leftHandIKTarget != null && leftHandIdleWalkPose != null && leftHandRunPose != null;
    }
}
