// 读取本地开火输入并播放客户端射击表现。
using UnityEngine;
using UnityEngine.InputSystem;

internal sealed class ClientWeapon {
    private const float MuzzleEffectLifetime = 0.15f;
    private const float HitEffectLifetime = 1f;

    private readonly Transform player;
    private readonly AnimComp_1001 animComp;
    private readonly CameraComp cameraComp;
    // 本地间隔控制输入节奏，服务端独立校验开火请求。
    private readonly float fireInterval = new WeaponStats().fireInterval;
    private float nextFireTime;

    // 缓存开火动画、镜头反馈和采集偏航所需的角色组件。
    internal ClientWeapon(Transform player) {
        this.player = player;
        player.TryGetComponent(out animComp);
        player.TryGetComponent(out cameraComp);
    }

    // 读取本地开火输入并返回点击时角色实际达到的偏航。
    internal bool TryFire(Transform muzzle, out float shotYaw) {
        shotYaw = default;

        // 枪口装配完成、窗口聚焦且角色允许开火时才读取点击。
        if (!Application.isFocused || muzzle == null || Time.time < nextFireTime ||
            (animComp != null && animComp.IsRunning))
            return false;

        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.enabled || !mouse.leftButton.wasPressedThisFrame)
            return false;

        // 限制点击位置在主相机画面内。
        Camera camera = Camera.main;
        if (camera == null || !camera.pixelRect.Contains(mouse.position.ReadValue()))
            return false;

        // 采集当帧转向后的偏航并立即播放本地动画。
        shotYaw = player.eulerAngles.y;
        nextFireTime = Time.time + fireInterval;
        if (animComp != null)
            animComp.PlayShoot();

        return true;
    }

    // 根据服务端确认结果播放枪口、命中、声音与镜头反馈。
    internal void PlayShotEffects(Transform muzzle, bool isOwned, Pose shotMuzzle, bool hitTarget,
        bool hitVirtualEnemy, Vector3 hitPosition, Vector3 direction) {
        // 命中虚拟敌人时只震动射手的镜头。
        if (hitVirtualEnemy && isOwned && cameraComp != null)
            cameraComp.PlayHitShake();

        // 只有会话特效资源可用时才播放资源反馈。
        WeaponShotEffects effects = WeaponShotEffects.Client;
        if (effects == null)
            return;

        effects.PlayAudio(shotMuzzle.position, hitVirtualEnemy, hitPosition);

        // 优先使用本端枪口，未装配时使用服务端确认的位姿。
        GameObject muzzleEffect = effects.Muzzle;
        if (muzzleEffect != null) {
            GameObject instance = muzzle != null
                ? Object.Instantiate(muzzleEffect, muzzle, false)
                : Object.Instantiate(muzzleEffect, shotMuzzle.position, shotMuzzle.rotation);
            Object.Destroy(instance, MuzzleEffectLifetime);
        }

        // 命中玩家或虚拟敌人时在碰撞点播放命中特效。
        GameObject hit = effects.Hit;
        if (hitTarget && hit != null) {
            GameObject instance = Object.Instantiate(hit, hitPosition, Quaternion.LookRotation(-direction));
            Object.Destroy(instance, HitEffectLifetime);
        }
    }
}
