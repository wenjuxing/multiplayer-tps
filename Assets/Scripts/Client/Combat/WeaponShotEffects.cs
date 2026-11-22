// 管理客户端射击特效和音效的加载、播放与释放
using System;
using System.Threading.Tasks;
using UnityEngine;

internal sealed class WeaponShotEffects {
    // 射击特效资源包及其中的音效目录
    private const string BundleName = "weapon_vfx";
    private const string AudioDirectory = "assets/gameassets/vfx/weapons/ak47/audio/";
    // 枪声和虚拟敌人命中音效的音量
    private const float ShotVolume = 0.8f;
    private const float HitVolume = 0.65f;

    // 保存一次客户端连接使用的特效资源
    private sealed class Session {
        public AssetBundleManager BundleManager;
        // 标记会话是否持有资源包引用
        public bool OwnsBundle;
        public GameObject Muzzle;
        public GameObject Trail;
        public GameObject Hit;
        public AudioClip ShotAudio;
        public AudioClip HitAudio;
    }

    private Session session;
    // 记录预加载任务，让重连按顺序接管资源
    private Task preloadTask = Task.CompletedTask;

    internal static WeaponShotEffects Client => ClientSession.Current?.ShotEffects;

    internal GameObject Muzzle => session?.Muzzle;
    internal GameObject Trail => session?.Trail;
    internal GameObject Hit => session?.Hit;

    // 播放空间枪声和虚拟敌人命中音效
    internal void PlayAudio(Vector3 muzzlePosition, bool hitVirtualEnemy, Vector3 hitPosition) {
        PlayClipAtPoint(session?.ShotAudio, muzzlePosition, ShotVolume);
        if (hitVirtualEnemy)
            PlayClipAtPoint(session?.HitAudio, hitPosition, HitVolume);
    }

    // 开始当前连接的武器特效预加载
    internal void StartSession() {
        if (session != null)
            return;

        session = new Session();
        // 重连时等待上一次会话完成资源加载和释放
        preloadTask = PreloadAsync(session, preloadTask);
    }

    // 结束当前会话并安排释放资源包
    internal void StopSession() {
        // 先清空当前会话，仍在加载的资源由加载任务释放
        Session endingSession = session;
        session = null;
        if (endingSession != null && preloadTask.IsCompleted)
            ReleaseBundle(endingSession);
    }

    // 预加载当前会话的特效和音效
    private async Task PreloadAsync(Session loading, Task previousPreload) {
        // 等待上一次预加载结束，再确认当前会话仍有效
        await previousPreload;
        if (session != loading)
            return;

        try {
            // 加载特效包并记录引用归属
            loading.BundleManager = AssetBundleManager.GetInstance();
            AssetBundle bundle = await loading.BundleManager.LoadBundleAsync(BundleName);
            loading.OwnsBundle = true;
            if (session != loading)
                return;

            // 并行加载枪口、弹道、命中特效和音效
            Task<GameObject> muzzle = LoadAssetAsync<GameObject>(loading.BundleManager, "AK47_Muzzle");
            Task<GameObject> trail = LoadAssetAsync<GameObject>(loading.BundleManager, "AK47_Trail");
            Task<GameObject> hit = LoadAssetAsync<GameObject>(loading.BundleManager, "AK47_Hit");
            Task<AudioClip> shotAudio = LoadOptionalAudioAsync(loading.BundleManager, bundle, "automatic.wav");
            Task<AudioClip> hitAudio = LoadOptionalAudioAsync(loading.BundleManager, bundle, "fire_hit.wav");
            await Task.WhenAll(muzzle, trail, hit, shotAudio, hitAudio);
            if (session != loading)
                return;

            // 会话仍有效时保存加载结果
            loading.Muzzle = await muzzle;
            loading.Trail = await trail;
            loading.Hit = await hit;
            loading.ShotAudio = await shotAudio;
            loading.HitAudio = await hitAudio;
        }
        catch (Exception exception) {
            if (session == loading)
                Debug.LogWarning($"[Weapon VFX] 特效包加载失败，将跳过武器特效和音效。{exception.Message}");
        }
        // 释放已失效会话持有的资源包
        finally {
            if (session != loading)
                ReleaseBundle(loading);
        }
    }

    // 在资源包中存在对应文件时加载音效
    private static Task<AudioClip> LoadOptionalAudioAsync(AssetBundleManager manager, AssetBundle bundle, string fileName) {
        string assetName = AudioDirectory + fileName;
        return bundle.Contains(assetName) ? LoadAssetAsync<AudioClip>(manager, assetName) : Task.FromResult<AudioClip>(null);
    }

    // 加载单项特效资源并记录失败原因
    private static async Task<T> LoadAssetAsync<T>(AssetBundleManager manager, string assetName) where T : UnityEngine.Object {
        try {
            return await manager.LoadAssetAsync<T>(BundleName, assetName);
        }
        catch (Exception exception) {
            Debug.LogWarning($"[Weapon VFX] 无法加载 {assetName}。{exception.Message}");
            return null;
        }
    }

    // 在指定位置播放一次空间音效
    private static void PlayClipAtPoint(AudioClip clip, Vector3 position, float volume) {
        if (clip == null) return;

        // 创建临时音源并设置距离衰减
        var sound = new GameObject(clip.name);
        sound.transform.position = position;
        var source = sound.AddComponent<AudioSource>();
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 6f;
        source.maxDistance = 30f;
        // 播放音效并在结束后销毁音源
        source.PlayOneShot(clip, volume);
        UnityEngine.Object.Destroy(sound, clip.length);
    }

    // 归还会话持有的资源包引用
    private static void ReleaseBundle(Session endingSession) {
        if (endingSession.OwnsBundle && endingSession.BundleManager != null)
            endingSession.BundleManager.Release(BundleName);
    }
}
