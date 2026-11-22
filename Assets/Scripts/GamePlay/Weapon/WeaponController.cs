using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(10)]
[RequireComponent(typeof(PlayerHealth))]
public class WeaponController : NetworkBehaviour {
    private const string BulletResourcePath = "Prefabs/Bullet";
    private const float MuzzleEffectLifetime = 0.15f;
    private const float HitEffectLifetime = 1f;
    private Transform muzzleVfxPoint;
    private readonly WeaponStats stats = new WeaponStats();
    private AnimComp_1001 animComp;
    private CameraComp cameraComp;
    private ShotCollision shotCollision;
    private float? pendingShotYaw;
    private float nextLocalFireTime;
    private float nextServerFireTime;

    private void Awake() {
        animComp = GetComponent<AnimComp_1001>();
        cameraComp = GetComponent<CameraComp>();
        shotCollision = new ShotCollision();
    }

    public void InitializeWeapon(Transform muzzle) {
        muzzleVfxPoint = muzzle;
    }

    private void Update() {
        if (!isOwned || !Application.isFocused || muzzleVfxPoint == null ||
            Time.time < nextLocalFireTime || (animComp != null && animComp.IsRunning)) return;
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.enabled || !mouse.leftButton.wasPressedThisFrame) return;

        Camera camera = Camera.main;
        if (camera == null || !camera.pixelRect.Contains(mouse.position.ReadValue())) return;

        float shotYaw = transform.eulerAngles.y;
        nextLocalFireTime = Time.time + stats.fireInterval;
        animComp?.PlayShoot();
        CmdFire(shotYaw);
    }

    private void LateUpdate() {
        if (!isServer || !pendingShotYaw.HasValue) return;
        float shotYaw = pendingShotYaw.Value;
        pendingShotYaw = null;
        ResolveServerShot(shotYaw);
    }

    private void OnDisable() {
        pendingShotYaw = null;
    }

    [Command]
    private void CmdFire(float shotYaw) {
        if (!isActiveAndEnabled || Time.time < nextServerFireTime || pendingShotYaw.HasValue ||
            muzzleVfxPoint == null || !float.IsFinite(shotYaw)) return;

        nextServerFireTime = Time.time + stats.fireInterval;
        pendingShotYaw = shotYaw;
    }

    private void ResolveServerShot(float shotYaw) {
        if (muzzleVfxPoint == null) return;

        // Keep the server's evaluated grip and position while applying the click-frame yaw.
        Quaternion shotRotation = Quaternion.Euler(0f, shotYaw % 360f, 0f);
        Quaternion rotationDelta = shotRotation * Quaternion.Inverse(transform.rotation);
        Vector3 rootPosition = transform.position;
        Vector3 muzzlePosition = rootPosition + rotationDelta * (muzzleVfxPoint.position - rootPosition);
        Quaternion muzzleRotation = rotationDelta * muzzleVfxPoint.rotation;
        Vector3 direction = muzzleRotation * Vector3.forward;
        var ray = new Ray(muzzlePosition, direction);
        Physics.SyncTransforms();
        bool stopped = shotCollision.TryGetMuzzleContact(muzzlePosition, transform, out ShotCollision.Contact contact) ||
            shotCollision.TryGetClosest(ray, stats.range, transform, out contact);

        PlayerHealth target = stopped ? contact.Target : null;
        Vector3 hitPosition = stopped ? contact.Point : ray.GetPoint(stats.range);
        if (target != null)
            target.ServerTakeDemage(stats.demage, netId);

        SpawnBullet(muzzlePosition, direction, hitPosition);
        RpcPlayShotEffects(muzzlePosition, muzzleRotation, stopped && contact.IsTarget,
            stopped && contact.IsVirtualEnemy, hitPosition, direction);
    }

    [ClientRpc]
    private void RpcPlayShotEffects(
        Vector3 muzzlePosition, Quaternion muzzleRotation, bool hitTarget, bool hitVirtualEnemy,
        Vector3 hitPosition, Vector3 direction) {
        if (hitVirtualEnemy && isOwned)
            cameraComp?.PlayHitShake();

        WeaponShotEffects effects = WeaponShotEffects.Client;
        if (effects == null)
            return;

        effects.PlayAudio(muzzlePosition, hitVirtualEnemy, hitPosition);

        GameObject muzzle = effects.Muzzle;
        if (muzzle != null) {
            GameObject instance = muzzleVfxPoint != null
                ? Instantiate(muzzle, muzzleVfxPoint, false)
                : Instantiate(muzzle, muzzlePosition, muzzleRotation);
            Destroy(instance, MuzzleEffectLifetime);
        }

        GameObject hit = effects.Hit;
        if (hitTarget && hit != null) {
            GameObject instance = Instantiate(hit, hitPosition, Quaternion.LookRotation(-direction));
            Destroy(instance, HitEffectLifetime);
        }
    }

    private void SpawnBullet(Vector3 origin, Vector3 direction, Vector3 endpoint) {
        GameObject prefab = Resources.Load<GameObject>(BulletResourcePath);
        if (prefab == null) {
            Debug.LogError($"[Combat] 找不到子弹预制体：{BulletResourcePath}");
            return;
        }

        GameObject go = Instantiate(prefab, origin, Quaternion.LookRotation(direction));
        go.GetComponent<Bullet>().InitializePath(origin, endpoint);
        NetworkServer.Spawn(go);
    }
}
