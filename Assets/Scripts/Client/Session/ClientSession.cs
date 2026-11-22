// 管理本地客户端会话和射击特效资源。
internal sealed class ClientSession {
    internal static ClientSession Current { get; private set; }
    internal WeaponShotEffects ShotEffects { get; } = new WeaponShotEffects();
    // 纯客户端拥有共用角色资源的释放责任。
    private bool ownsCharacterAssets;

    // 登记客户端会话并开始预加载射击特效。
    internal void Start(bool ownsCharacterAssets) {
        if (Current == this)
            return;

        this.ownsCharacterAssets = ownsCharacterAssets;
        Current = this;
        ShotEffects.StartSession();
    }

    // 结束预加载会话并报告是否需要释放共用角色资源。
    internal bool Stop() {
        bool releaseCharacterAssets = false;
        if (Current == this) {
            Current = null;
            releaseCharacterAssets = ownsCharacterAssets;
            ownsCharacterAssets = false;
        }

        ShotEffects.StopSession();
        return releaseCharacterAssets;
    }
}
