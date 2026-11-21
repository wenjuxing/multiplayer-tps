using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mirror;
using Res;
using UnityEngine;
using UnityEngine.Animations.Rigging;

[DisallowMultipleComponent]
public sealed class CharacterFactory : MonoBehaviour
{
    const string HeroBundle = "heros";
    const string WeaponBundle = "weapons";
    const string HeroCatalogAsset = "HeroCatalog";
    const string WeaponPrefabAsset = "weapon_01_prefab";

    public static CharacterFactory Instance { get; private set; }

    readonly HashSet<string> requestedBundles = new HashSet<string>(StringComparer.Ordinal);
    AssetBundleManager bundleManager;
    Task<LoadedAssets> loadedAssetsTask;
    int generation;
    bool sessionActive;

    sealed class LoadedAssets
    {
        public HeroCatalog Catalog;
        public GameObject WeaponPrefab;
    }

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

    void OnDestroy()
    {
        if (Instance == this)
        {
            ReleaseSessionAssets();
            Instance = null;
        }
    }

    /// <summary>Creates and assembles a player object. The caller remains responsible for network spawning.</summary>
    public async Task<GameObject> CreateAsync(string roleId)
    {
        int createGeneration = BeginSession();
        GameObject player = null;

        try
        {
            NetworkManager networkManager = NetworkManager.singleton;
            if (networkManager == null || networkManager.playerPrefab == null)
                throw new InvalidOperationException("NetworkManager.playerPrefab 尚未配置。");

            await GetLoadedAssetsAsync(createGeneration);
            EnsureGeneration(createGeneration);

            player = Instantiate(networkManager.playerPrefab);
            if (!await AssembleAppearanceAsync(player, roleId, createGeneration))
                throw new InvalidOperationException($"角色組装失败：{roleId}");

            EnsureGeneration(createGeneration);
            return player;
        }
        catch (Exception exception)
        {
            if (player != null)
                Destroy(player);

            if (loadedAssetsTask != null && loadedAssetsTask.IsFaulted)
                ReleaseSessionAssets();

            Debug.LogError($"[CharacterFactory] 创建角色失败，roleId={roleId}\n{exception}", this);
            return null;
        }
    }

    internal async Task<bool> AssembleAppearanceAsync(GameObject player, string roleId)
    {
        return await AssembleAppearanceAsync(player, roleId, BeginSession());
    }

    async Task<bool> AssembleAppearanceAsync(GameObject player, string roleId, int assemblyGeneration)
    {
        if (player == null)
            throw new ArgumentNullException(nameof(player));

        LoadedAssets assets = await GetLoadedAssetsAsync(assemblyGeneration);
        EnsureGeneration(assemblyGeneration);

        if (!assets.Catalog.TryGetHero(roleId, out HeroDefinition definition))
            throw new KeyNotFoundException($"HeroCatalog 中没有角色 ID：{roleId}");
        if (definition.VisualPrefab == null || definition.Avatar == null)
            throw new InvalidOperationException($"角色 {roleId} 缺少外观预制体或 Avatar。");

        NetworkHeroAppearance networkAppearance = player.GetComponent<NetworkHeroAppearance>();
        if (networkAppearance != null && networkAppearance.IsAssembledFor(roleId))
            return true;

        if (player.GetComponentInChildren<HeroVisualBinding>(true) != null)
            throw new InvalidOperationException($"玩家对象 {player.name} 已有角色外观，但绑定状态不匹配。");

        Animator animator = player.GetComponent<Animator>();
        RigBuilder rigBuilder = player.GetComponent<RigBuilder>();
        AnimComp_1001 animComp = player.GetComponent<AnimComp_1001>();
        Controller_1001 controller = player.GetComponent<Controller_1001>();
        FPSGame.Gameplay.Weapon.WeaponController weaponController =
            player.GetComponent<FPSGame.Gameplay.Weapon.WeaponController>();

        if (animator == null || rigBuilder == null || animComp == null || controller == null || weaponController == null)
            throw new InvalidOperationException("Player 根预制体缺少 Animator、RigBuilder 或角色控制组件。");

        if (animator.runtimeAnimatorController == null)
            throw new InvalidOperationException("Player 根预制体的 Animator 缺少 AnimatorController。");

        animator.avatar = definition.Avatar;
        RefreshNetworkAnimator(player, animator);
        animComp.InitializeAnimator(animator);

        GameObject visual = null;
        GameObject weapon = null;
        try
        {
            visual = Instantiate(definition.VisualPrefab, player.transform, false);
            visual.name = definition.VisualPrefab.name;

            HeroVisualBinding heroBinding = visual.GetComponent<HeroVisualBinding>();
            if (heroBinding == null || !heroBinding.IsValid())
                throw new InvalidOperationException($"角色 {roleId} 外观预制体缺少有效的 HeroVisualBinding。");

            weapon = Instantiate(assets.WeaponPrefab, heroBinding.WeaponMount, false);
            WeaponVisualBinding weaponBinding = weapon.GetComponent<WeaponVisualBinding>();
            if (weaponBinding == null || !weaponBinding.IsValid())
                throw new InvalidOperationException("weapon_01_prefab 缺少有效的 WeaponVisualBinding。");

            TwoBoneIKConstraint leftHandConstraint = heroBinding.LeftHandConstraint;
            TwoBoneIKConstraintData ikData = leftHandConstraint.data;
            ikData.target = weaponBinding.LeftHandIKTarget;
            leftHandConstraint.data = ikData;
            if (!leftHandConstraint.IsValid())
                throw new InvalidOperationException($"角色 {roleId} 的左手 TwoBoneIKConstraint 数据无效。");

            rigBuilder.layers = new List<RigLayer> { new RigLayer(heroBinding.CharacterRig, true) };
            controller.InitializeRigReferences(
                heroBinding.CharacterRig,
                weaponBinding.LeftHandIKTarget,
                weaponBinding.LeftHandIdleWalkPose,
                weaponBinding.LeftHandRunPose);
            weaponController.InitializeWeapon(weaponBinding.FirePoint);

            animator.Rebind();

            if (!rigBuilder.Build())
                throw new InvalidOperationException($"角色 {roleId} 的 RigBuilder.Build() 失败。");

            EnsureGeneration(assemblyGeneration);
            if (networkAppearance != null)
                networkAppearance.MarkAssembled(roleId);

            return true;
        }
        catch
        {
            if (weapon != null) Destroy(weapon);
            if (visual != null) Destroy(visual);
            rigBuilder.Clear();
            rigBuilder.layers.Clear();
            throw;
        }
    }

    async Task<LoadedAssets> GetLoadedAssetsAsync(int loadGeneration)
    {
        EnsureGeneration(loadGeneration);
        if (loadedAssetsTask == null)
            loadedAssetsTask = LoadAssetsAsync(loadGeneration);

        return await loadedAssetsTask;
    }

    async Task<LoadedAssets> LoadAssetsAsync(int loadGeneration)
    {
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

        Task<HeroCatalog> catalogTask = bundleManager.LoadAssetAsync<HeroCatalog>(HeroBundle, HeroCatalogAsset);
        Task<GameObject> weaponTask = bundleManager.LoadAssetAsync<GameObject>(WeaponBundle, WeaponPrefabAsset);
        await Task.WhenAll(catalogTask, weaponTask);
        EnsureGeneration(loadGeneration);

        LoadedAssets assets = new LoadedAssets
        {
            Catalog = await catalogTask,
            WeaponPrefab = await weaponTask,
        };

        if (assets.Catalog == null || assets.WeaponPrefab == null)
            throw new InvalidOperationException("角色目录或共用武器未能从 AB 包加载。");

        return assets;
    }

    int BeginSession()
    {
        if (!sessionActive)
        {
            sessionActive = true;
            generation++;
        }

        return generation;
    }

    void EnsureGeneration(int expectedGeneration)
    {
        if (!sessionActive || expectedGeneration != generation)
            throw new OperationCanceledException("角色资源会话已结束。");
    }

    void RefreshNetworkAnimator(GameObject player, Animator animator)
    {
        NetworkAnimator networkAnimator = player.GetComponent<NetworkAnimator>();
        if (networkAnimator == null)
            throw new InvalidOperationException("Player 根预制体缺少 NetworkAnimator。");

        networkAnimator.animator = animator;
        networkAnimator.enabled = false;
        networkAnimator.enabled = true;
    }

    internal void ReleaseSessionAssets()
    {
        if (!sessionActive && requestedBundles.Count == 0)
            return;

        sessionActive = false;
        generation++;

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
