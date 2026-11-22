// 处理玩家开火输入、服务端命中结算和客户端射击表现
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(10)]
[RequireComponent(typeof(PlayerHealth))]
public class WeaponController : NetworkBehaviour {
    // 表现子弹的预制体路径
    private const string BulletResourcePath = "Prefabs/Bullet";
    // 枪口和命中特效的显示时长（秒）
    private const float MuzzleEffectLifetime = 0.15f;
    private const float HitEffectLifetime = 1f;
    // 射击和枪口特效使用的位置与朝向
    private Transform muzzleVfxPoint;
    // 武器的伤害、开火间隔和射程
    private readonly WeaponStats stats = new WeaponStats();
    private AnimComp_1001 animComp;
    private CameraComp cameraComp;
    private ShotCollision shotCollision;
    // 等待服务端结算的点击朝向
    private float? pendingShotYaw;
    // 本地输入与服务端请求各自的开火冷却
    private float nextLocalFireTime;
    private float nextServerFireTime;

    // 缓存射击表现组件并创建碰撞查询
    private void Awake() {
        animComp = GetComponent<AnimComp_1001>();
        cameraComp = GetComponent<CameraComp>();
        shotCollision = new ShotCollision();
    }

    // 绑定武器装配提供的枪口
    public void InitializeWeapon(Transform muzzle) {
        muzzleVfxPoint = muzzle;
    }

    // 检查本地开火输入并向服务端提交请求
    private void Update() {
        // 检查开火条件和左键按下
        if (!isOwned || !Application.isFocused || muzzleVfxPoint == null ||
            Time.time < nextLocalFireTime || (animComp != null && animComp.IsRunning)) return;
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.enabled || !mouse.leftButton.wasPressedThisFrame) return;

        // 只接受相机画面内的点击
        Camera camera = Camera.main;
        if (camera == null || !camera.pixelRect.Contains(mouse.position.ReadValue())) return;

        // 记录点击时的朝向，播放动画并提交请求
        float shotYaw = transform.eulerAngles.y;
        nextLocalFireTime = Time.time + stats.fireInterval;
        animComp?.PlayShoot();
        CmdFire(shotYaw);
    }

    // 在服务端帧末结算待处理的开火
    private void LateUpdate() {
        if (!isServer || !pendingShotYaw.HasValue) return;
        float shotYaw = pendingShotYaw.Value;
        pendingShotYaw = null;
        ResolveServerShot(shotYaw);
    }

    // 清除尚未结算的开火请求
    private void OnDisable() {
        pendingShotYaw = null;
    }

    // 在服务端校验开火请求并暂存朝向
    [Command]
    private void CmdFire(float shotYaw) {
        // 检查冷却、待处理请求和枪口状态
        if (!isActiveAndEnabled || Time.time < nextServerFireTime || pendingShotYaw.HasValue ||
            muzzleVfxPoint == null || !float.IsFinite(shotYaw)) return;

        // 更新服务端冷却并保存本次开火
        nextServerFireTime = Time.time + stats.fireInterval;
        pendingShotYaw = shotYaw;
    }

    // 重建射击路径、结算伤害并广播表现
    private void ResolveServerShot(float shotYaw) {
        if (muzzleVfxPoint == null) return;

        // 保留服务端的持枪姿势和角色位置，按点击时的朝向重建枪口
        Quaternion shotRotation = Quaternion.Euler(0f, shotYaw % 360f, 0f);
        Quaternion rotationDelta = shotRotation * Quaternion.Inverse(transform.rotation);
        Vector3 rootPosition = transform.position;
        Vector3 muzzlePosition = rootPosition + rotationDelta * (muzzleVfxPoint.position - rootPosition);
        Quaternion muzzleRotation = rotationDelta * muzzleVfxPoint.rotation;
        Vector3 direction = muzzleRotation * Vector3.forward;
        var ray = new Ray(muzzlePosition, direction);
        // 同步碰撞体位置，先查枪口，再查射击路径
        Physics.SyncTransforms();
        bool stopped = shotCollision.TryGetMuzzleHit(muzzlePosition, transform, out ShotCollision.ColliderInfo hitInfo) ||
            shotCollision.TryFindClosestTarget(ray, stats.range, transform, out hitInfo);

        // 确定子弹终点，命中玩家时扣除血量
        PlayerHealth target = stopped ? hitInfo.Target : null;
        Vector3 hitPosition = stopped ? hitInfo.Point : ray.GetPoint(stats.range);
        if (target != null)
            target.ServerTakeDemage(stats.demage, netId);

        // 生成表现子弹并通知客户端播放特效
        SpawnBullet(muzzlePosition, direction, hitPosition);
        RpcPlayShotEffects(muzzlePosition, muzzleRotation, stopped && hitInfo.IsTarget,
            stopped && hitInfo.IsVirtualEnemy, hitPosition, direction);
    }

    // 根据服务端回传的射击结果播放客户端反馈
    [ClientRpc]
    private void RpcPlayShotEffects(
        Vector3 muzzlePosition, Quaternion muzzleRotation, bool hitTarget, bool hitVirtualEnemy,
        Vector3 hitPosition, Vector3 direction) {
        // 命中虚拟敌人时只震动射手的镜头
        if (hitVirtualEnemy && isOwned)
            cameraComp?.PlayHitShake();

        // 获取当前会话的特效资源并播放音效
        WeaponShotEffects effects = WeaponShotEffects.Client;
        if (effects == null)
            return;

        effects.PlayAudio(muzzlePosition, hitVirtualEnemy, hitPosition);

        // 优先在本端枪口挂点播放枪口特效
        GameObject muzzle = effects.Muzzle;
        if (muzzle != null) {
            GameObject instance = muzzleVfxPoint != null
                ? Instantiate(muzzle, muzzleVfxPoint, false)
                : Instantiate(muzzle, muzzlePosition, muzzleRotation);
            Destroy(instance, MuzzleEffectLifetime);
        }

        // 命中玩家或虚拟敌人时播放命中特效
        GameObject hit = effects.Hit;
        if (hitTarget && hit != null) {
            GameObject instance = Instantiate(hit, hitPosition, Quaternion.LookRotation(-direction));
            Destroy(instance, HitEffectLifetime);
        }
    }

    // 生成并同步服务端确认弹道的表现子弹
    private void SpawnBullet(Vector3 origin, Vector3 direction, Vector3 endpoint) {
        // 加载子弹预制体
        GameObject prefab = Resources.Load<GameObject>(BulletResourcePath);
        if (prefab == null) {
            Debug.LogError($"[Combat] 找不到子弹预制体：{BulletResourcePath}");
            return;
        }

        // 设置弹道后将子弹同步给客户端
        GameObject go = Instantiate(prefab, origin, Quaternion.LookRotation(direction));
        go.GetComponent<Bullet>().InitializePath(origin, endpoint);
        NetworkServer.Spawn(go);
    }
}
