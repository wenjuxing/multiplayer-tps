// 提供角色装配需要的动画绑定和武器挂点
using UnityEngine;
using UnityEngine.Animations.Rigging;

[DisallowMultipleComponent]
public sealed class HeroVisualBinding : MonoBehaviour
{
    // 角色的动画约束层
    [SerializeField] Rig characterRig;
    // 让左手贴合持枪点的约束
    [SerializeField] TwoBoneIKConstraint leftHandConstraint;
    // 武器在角色骨骼上的挂点
    [SerializeField] Transform weaponMount;

    public Rig CharacterRig => characterRig;
    public TwoBoneIKConstraint LeftHandConstraint => leftHandConstraint;
    public Transform WeaponMount => weaponMount;

    // 检查角色装配需要的引用是否齐全
    public bool IsValid()
    {
        return characterRig != null && leftHandConstraint != null && weaponMount != null;
    }
}
