using System;
using System.Threading.Tasks;
using Mirror;
using UnityEngine;

internal sealed class WeaponShotEffects {
    private const string BundleName = "weapon_vfx";
    private const string AudioDirectory = "assets/gameassets/vfx/weapons/ak47/audio/";
    private const float ShotVolume = 0.8f;
    private const float HitVolume = 0.65f;

    private sealed class Session {
        public AssetBundleManager BundleManager;
        public bool OwnsBundle;
        public GameObject Muzzle;
        public GameObject Trail;
        public GameObject Hit;
        public AudioClip ShotAudio;
        public AudioClip HitAudio;
    }

    private Session session;
    private Task preloadTask = Task.CompletedTask;

    internal static WeaponShotEffects Client =>
        NetworkManager.singleton is CharacterNetworkManager manager ? manager.ShotEffects : null;

    internal GameObject Muzzle => session?.Muzzle;
    internal GameObject Trail => session?.Trail;
    internal GameObject Hit => session?.Hit;

    internal void PlayAudio(Vector3 muzzlePosition, bool hitVirtualEnemy, Vector3 hitPosition) {
        PlayClipAtPoint(session?.ShotAudio, muzzlePosition, ShotVolume);
        if (hitVirtualEnemy)
            PlayClipAtPoint(session?.HitAudio, hitPosition, HitVolume);
    }

    internal void StartSession() {
        if (session != null)
            return;

        session = new Session();
        // A reconnect must wait for the previous session to finish using and releasing its bundle.
        preloadTask = PreloadAsync(session, preloadTask);
    }

    internal void StopSession() {
        Session endingSession = session;
        session = null;
        if (endingSession != null && preloadTask.IsCompleted)
            ReleaseBundle(endingSession);
    }

    private async Task PreloadAsync(Session loading, Task previousPreload) {
        await previousPreload;
        if (session != loading)
            return;

        try {
            loading.BundleManager = AssetBundleManager.GetInstance();
            AssetBundle bundle = await loading.BundleManager.LoadBundleAsync(BundleName);
            loading.OwnsBundle = true;
            if (session != loading)
                return;

            Task<GameObject> muzzle = LoadAssetAsync<GameObject>(loading.BundleManager, "AK47_Muzzle");
            Task<GameObject> trail = LoadAssetAsync<GameObject>(loading.BundleManager, "AK47_Trail");
            Task<GameObject> hit = LoadAssetAsync<GameObject>(loading.BundleManager, "AK47_Hit");
            Task<AudioClip> shotAudio = LoadOptionalAudioAsync(loading.BundleManager, bundle, "automatic.wav");
            Task<AudioClip> hitAudio = LoadOptionalAudioAsync(loading.BundleManager, bundle, "fire_hit.wav");
            await Task.WhenAll(muzzle, trail, hit, shotAudio, hitAudio);
            if (session != loading)
                return;

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
        finally {
            if (session != loading)
                ReleaseBundle(loading);
        }
    }

    private static Task<AudioClip> LoadOptionalAudioAsync(AssetBundleManager manager, AssetBundle bundle, string fileName) {
        string assetName = AudioDirectory + fileName;
        return bundle.Contains(assetName) ? LoadAssetAsync<AudioClip>(manager, assetName) : Task.FromResult<AudioClip>(null);
    }

    private static async Task<T> LoadAssetAsync<T>(AssetBundleManager manager, string assetName) where T : UnityEngine.Object {
        try {
            return await manager.LoadAssetAsync<T>(BundleName, assetName);
        }
        catch (Exception exception) {
            Debug.LogWarning($"[Weapon VFX] 无法加载 {assetName}。{exception.Message}");
            return null;
        }
    }

    private static void PlayClipAtPoint(AudioClip clip, Vector3 position, float volume) {
        if (clip == null) return;

        var sound = new GameObject(clip.name);
        sound.transform.position = position;
        var source = sound.AddComponent<AudioSource>();
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 6f;
        source.maxDistance = 30f;
        source.PlayOneShot(clip, volume);
        UnityEngine.Object.Destroy(sound, clip.length);
    }

    private static void ReleaseBundle(Session endingSession) {
        if (endingSession.OwnsBundle && endingSession.BundleManager != null)
            endingSession.BundleManager.Release(BundleName);
    }
}
