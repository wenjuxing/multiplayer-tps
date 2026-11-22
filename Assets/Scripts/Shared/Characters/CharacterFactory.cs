// 加载角色资源并装配玩家外观、武器和动画
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Animations.Rigging;

[DisallowMultipleComponent]
public sealed class CharacterFactory : MonoBehaviour
{
    // 角色和武器所在的资源包及资源名
    const string HeroBundle = "heros";
    const string WeaponBundle = "weapons";
    const string HeroCatalogAsset = "HeroCatalog";
    const string WeaponPrefabAsset = "weapon_01_prefab";

    public static CharacterFactory Instance { get; private set; }

    // 记录本次会话持有的资源包引用
    readonly HashSet<string> requestedBundles = new HashSet<string>(StringComparer.Ordinal);
    AssetBundleManager bundleManager;
    // 复用当前会话的资源加载任务
    Task<LoadedAssets> loadedAssetsTask;
    // 区分会话，拦截旧会话尚未完成的装配
    int generation;
    bool sessionActive;

    // 保存已加载的角色目录和共用武器
    sealed class LoadedAssets
    {
        public HeroCatalog Catalog;
        public GameObject WeaponPrefab;
    }

    // 注册当前场景的角色工厂
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("[CharacterFactory] 场景中存在多个 CharacterFactory。", this);
            enabled = false;
            return;
        }

        Instance = this;
    }

    // 当前工厂销毁时释放会话资源
    void OnDestroy()
    {
        if (Instance == this)
        {
            ReleaseSessionAssets();
            Instance = null;
        }
    }

    // 创建并装配玩家对象，网络生成由调用方负责
    internal async Task<GameObject> CreateAsync(GameObject playerPrefab, string roleId, ICharacterAssemblyBindings bindings)
    {
        int createGeneration = BeginSession();
        GameObject player = null;

        try
        {
            // 检查调用方提供的玩家根预制体，再准备本次会话的资源。
            if (playerPrefab == null)
                throw new ArgumentNullException(nameof(playerPrefab));

            await GetLoadedAssetsAsync(createGeneration);
            EnsureGeneration(createGeneration);

            // 创建玩家根对象并装配指定角色
            player = Instantiate(playerPrefab);
            await AssembleAppearanceAsync(player, roleId, bindings, createGeneration, null);

            EnsureGeneration(createGeneration);
            return player;
        }
        // 创建失败时清理临时玩家和已失败的加载会话
        catch (Exception exception)
        {
            if (player != null)
                Destroy(player);

            // 旧会话结束后，迟到的失败不得释放新会话的资源。
            if (generation != createGeneration)
                return null;

            if (loadedAssetsTask != null && loadedAssetsTask.IsFaulted)
                ReleaseSessionAssets();

            Debug.LogError($"[CharacterFactory] 创建角色失败，roleId={roleId}\n{exception}", this);
            return null;
        }
    }

    // 在当前会话为已有玩家对象装配外观
    internal Task AssembleAppearanceAsync(GameObject player, string roleId, ICharacterAssemblyBindings bindings,
        Func<bool> isCurrent = null) {
        return AssembleAppearanceAsync(player, roleId, bindings, BeginSession(), isCurrent);
    }

    // 按角色配置安装模型、武器和动画约束
    private async Task AssembleAppearanceAsync(GameObject player, string roleId, ICharacterAssemblyBindings bindings,
        int assemblyGeneration, Func<bool> isCurrent)
    {
        if (player == null)
            throw new ArgumentNullException(nameof(player));

        // 读取并检查指定角色的外观配置
        LoadedAssets assets = await GetLoadedAssetsAsync(assemblyGeneration);
        EnsureGeneration(assemblyGeneration);
        if (isCurrent != null && !isCurrent())
            return;

        if (!assets.Catalog.TryGetHero(roleId, out HeroDefinition definition))
            throw new KeyNotFoundException($"HeroCatalog 中没有角色 ID：{roleId}");
        if (definition.VisualPrefab == null || definition.Avatar == null)
            throw new InvalidOperationException($"角色 {roleId} 缺少外观预制体或 Avatar。");

        // 已存在其他角色外观时中止装配。
        if (player.GetComponentInChildren<HeroVisualBinding>(true) != null)
            throw new InvalidOperationException($"玩家对象 {player.name} 已有角色外观，但绑定状态不匹配。");

        // 获取装配和动画所需的角色组件
        if (!player.TryGetComponent(out Animator animator) ||
            !player.TryGetComponent(out RigBuilder rigBuilder) ||
            !player.TryGetComponent(out AnimComp_1001 animComp)) {
            throw new InvalidOperationException("Player 根预制体缺少 Animator、RigBuilder 或动画组件。");
        }

        if (animator.runtimeAnimatorController == null)
            throw new InvalidOperationException("Player 根预制体的 Animator 缺少 AnimatorController。");

        // 绑定角色骨骼并刷新本地和网络动画
        animator.avatar = definition.Avatar;
        bindings.PrepareAnimator(player, animator);
        animComp.InitializeAnimator(animator);

        GameObject visual = null;
        GameObject weapon = null;
        try
        {
            // 实例化角色外观并检查武器挂点
            visual = Instantiate(definition.VisualPrefab, player.transform, false);
            visual.name = definition.VisualPrefab.name;

            if (!visual.TryGetComponent(out HeroVisualBinding heroBinding) || !heroBinding.IsValid()) {
                throw new InvalidOperationException($"角色 {roleId} 外观预制体缺少有效的 HeroVisualBinding。");
            }

            // 安装共用武器并检查持枪绑定
            weapon = Instantiate(assets.WeaponPrefab, heroBinding.WeaponMount, false);
            if (!weapon.TryGetComponent(out WeaponVisualBinding weaponBinding) || !weaponBinding.IsValid()) {
                throw new InvalidOperationException("weapon_01_prefab 缺少有效的 WeaponVisualBinding。");
            }

            // 将角色左手约束绑定到武器目标点
            TwoBoneIKConstraint leftHandConstraint = heroBinding.LeftHandConstraint;
            TwoBoneIKConstraintData ikData = leftHandConstraint.data;
            ikData.target = weaponBinding.LeftHandIKTarget;
            leftHandConstraint.data = ikData;
            if (!leftHandConstraint.IsValid())
                throw new InvalidOperationException($"角色 {roleId} 的左手 TwoBoneIKConstraint 数据无效。");

            // 选择枪口位置并重建持枪动画约束
            Transform muzzle = weaponBinding.MuzzleVfxPoint != null ? weaponBinding.MuzzleVfxPoint : weaponBinding.FirePoint;
            rigBuilder.layers = new List<RigLayer> { new RigLayer(heroBinding.CharacterRig, true) };
            bindings.BindRig(player, leftHandConstraint, weaponBinding);
            animator.Rebind();

            if (!rigBuilder.Build())
                throw new InvalidOperationException($"角色 {roleId} 的 RigBuilder.Build() 失败。");

            EnsureGeneration(assemblyGeneration);
            // 会话仍有效时绑定枪口并记录装配完成
            bindings.Complete(player, roleId, muzzle);
        }
        // 装配失败时销毁临时外观和武器并清理动画约束
        catch
        {
            if (weapon != null) Destroy(weapon);
            if (visual != null) Destroy(visual);
            rigBuilder.Clear();
            rigBuilder.layers.Clear();
            throw;
        }
    }

    // 获取或启动当前会话的资源加载任务
    async Task<LoadedAssets> GetLoadedAssetsAsync(int loadGeneration)
    {
        EnsureGeneration(loadGeneration);
        if (loadedAssetsTask == null)
            loadedAssetsTask = LoadAssetsAsync(loadGeneration);

        return await loadedAssetsTask;
    }

    // 加载角色目录和共用武器
    async Task<LoadedAssets> LoadAssetsAsync(int loadGeneration)
    {
        // 并行加载角色包和武器包并记录引用
        bundleManager = AssetBundleManager.GetInstance();
        string[] bundleNames = { HeroBundle, WeaponBundle };
        Task<AssetBundle>[] bundleTasks = new Task<AssetBundle>[bundleNames.Length];

        for (int i = 0; i < bundleNames.Length; i++)
        {
            requestedBundles.Add(bundleNames[i]);
            bundleTasks[i] = bundleManager.LoadBundleAsync(bundleNames[i]);
        }

        await Task.WhenAll(bundleTasks);
        EnsureGeneration(loadGeneration);

        // 加载包内的角色目录和武器预制体
        Task<HeroCatalog> catalogTask = bundleManager.LoadAssetAsync<HeroCatalog>(HeroBundle, HeroCatalogAsset);
        Task<GameObject> weaponTask = bundleManager.LoadAssetAsync<GameObject>(WeaponBundle, WeaponPrefabAsset);
        await Task.WhenAll(catalogTask, weaponTask);
        EnsureGeneration(loadGeneration);

        // 验证并返回会话所需的资源
        LoadedAssets assets = new LoadedAssets
        {
            Catalog = await catalogTask,
            WeaponPrefab = await weaponTask,
        };

        if (assets.Catalog == null || assets.WeaponPrefab == null)
            throw new InvalidOperationException("角色目录或共用武器未能从 AB 包加载。");

        return assets;
    }

    // 按需开启资源会话并返回会话编号
    int BeginSession()
    {
        if (!sessionActive)
        {
            sessionActive = true;
            generation++;
        }

        return generation;
    }

    // 会话已结束或已切换时取消旧操作
    void EnsureGeneration(int expectedGeneration)
    {
        if (!sessionActive || expectedGeneration != generation)
            throw new OperationCanceledException("角色资源会话已结束。");
    }

    // 结束会话并归还角色资源包引用
    internal void ReleaseSessionAssets()
    {
        if (!sessionActive && requestedBundles.Count == 0)
            return;

        // 更新会话编号，让仍在进行的旧操作失效
        sessionActive = false;
        generation++;

        // 归还资源包引用并清空会话缓存
        if (bundleManager != null)
        {
            foreach (string bundleName in requestedBundles)
                bundleManager.Release(bundleName);
        }

        requestedBundles.Clear();
        loadedAssetsTask = null;
        bundleManager = null;
    }
}

// 允许调用方在共用角色装配的固定阶段接入自己的组件。
internal interface ICharacterAssemblyBindings {
    // 在本地动画参数初始化前准备角色动画接入。
    void PrepareAnimator(GameObject player, Animator animator);

    // 在 Animator 重绑前为持枪姿势提供 IK 引用。
    void BindRig(GameObject player, TwoBoneIKConstraint constraint, WeaponVisualBinding weapon);

    // 在装配成功后绑定枪口并提交当前角色状态。
    void Complete(GameObject player, string roleId, Transform muzzle);
}
