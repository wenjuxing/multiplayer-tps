using UnityEngine;
using UnityEngine.Animations.Rigging;

[DisallowMultipleComponent]
public sealed class HeroVisualBinding : MonoBehaviour
{
    [SerializeField] Rig characterRig;
    [SerializeField] TwoBoneIKConstraint leftHandConstraint;
    [SerializeField] Transform weaponMount;

    public Rig CharacterRig => characterRig;
    public TwoBoneIKConstraint LeftHandConstraint => leftHandConstraint;
    public Transform WeaponMount => weaponMount;

    public bool IsValid()
    {
        return characterRig != null && leftHandConstraint != null && weaponMount != null;
    }
}
