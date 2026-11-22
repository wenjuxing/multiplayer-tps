// 提供客户端和服务端出包、地址切换与构建自检
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

public class FPSGameBuildWindow : EditorWindow
{
    // 构建输出路径和启动场景，与出包文档保持一致

    public const string ProjectRoot = ".";
    public const string ServerOutputDir = "Build/LinuxServer";
    public const string ServerExeName = "FPSGame-Y1.x86_64";
    public const string ClientOutputDir = "Build/WindowsClient";
    public const string ClientExeName = "FPSGame-Y1.exe";
    public const string BootScenePath = "Assets/Scenes/TestScene.unity";

    // 保存出包面板选项和环境选择的编辑器配置键

    private const string PrefEnvironment = "FPSGame_Y1.BuildEnv";
    private const string PrefSwitchBeforeBuild = "FPSGame_Y1.SwitchBeforeBuild";
    private const string PrefDevClient = "FPSGame_Y1.DevClient";
    private const string PrefOpenFolder = "FPSGame_Y1.OpenFolder";
    private const string PrefConfirmPlatformSwitch = "FPSGame_Y1.ConfirmSwitch";

    private const string EnvLocal = "last";
    private const string EnvProduction = "production";

    private static readonly Vector2 WindowMinSize = new Vector2(430f, 640f);


    private Vector2 _scroll;

    // 保存上次构建用时和结果供面板显示
    private static double _lastBuildSeconds;
    private static string _lastBuildSummary = "";

    //  菜单项

    // 打开出包面板
    [MenuItem("Tools/FPSGame-Y1/出包面板 %#b", false, 1)]
    public static void OpenWindow()
    {
        FPSGameBuildWindow w = GetWindow<FPSGameBuildWindow>(false, "出包 / 环境", true);
        w.minSize = WindowMinSize;
        w.Show();
    }

    // 用默认配置构建 Windows 客户端
    [MenuItem("Tools/FPSGame-Y1/打包客户端 (Windows) %#c", false, 20)]
    public static void MenuBuildClient()
    {
        BuildClient(new BuildConfig());
    }

    // 用默认配置构建 Linux 服务端
    [MenuItem("Tools/FPSGame-Y1/打包服务端 (Linux Server) %#s", false, 21)]
    public static void MenuBuildServer()
    {
        BuildServer(new BuildConfig());
    }

    // 用默认配置依次构建服务端和客户端
    [MenuItem("Tools/FPSGame-Y1/双端同时出包 (客户端+服务端) %#d", false, 22)]
    public static void MenuBuildBoth()
    {
        BuildBoth(new BuildConfig());
    }

    // 将场景服务器地址切到本地
    [MenuItem("Tools/FPSGame-Y1/切换到本地服务地址 (127.0.0.1)", false, 40)]
    public static void MenuSwitchLocal()
    {
        SwitchEnvironment(NetworkAddressConfig.LocalAddress, true);
    }

    // 将场景服务器地址切到生产环境
    [MenuItem("Tools/FPSGame-Y1/切换到真实服务地址 (43.142.103.57)", false, 41)]
    public static void MenuSwitchProduction()
    {
        SwitchEnvironment(NetworkAddressConfig.ProductionAddress, true);
    }

    // 创建并打开出包输出目录
    [MenuItem("Tools/FPSGame-Y1/打开出包输出目录", false, 60)]
    public static void MenuOpenBuildDir()
    {
        string dir = ToAbsolute("Build");
        Directory.CreateDirectory(dir);
        EditorUtility.RevealInFinder(dir);
    }

    //  出包参数

    // 保存一次构建需要的选项
    public class BuildConfig
    {
        // 是否生成调试用的开发构建
        public bool Development;

        // 构建前要切换的服务器地址，空值时沿用当前地址
        public string SwitchAddressBeforeBuild;

        // 构建成功后是否打开输出目录
        public bool RevealAfterBuild;

        // 是否允许自动切换平台并重新导入资源
        public bool AllowSwitchPlatform = true;
    }

    //  出包主流程

    // 构建 Windows 客户端
    public static bool BuildClient(BuildConfig cfg)
    {
        return RunSingle("Windows 客户端", cfg,
            BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player,
            ClientOutputDir, ClientExeName);
    }

    // 构建 Linux 专用服务端
    public static bool BuildServer(BuildConfig cfg)
    {
        return RunSingle("Linux 服务端", cfg,
            BuildTarget.StandaloneLinux64, StandaloneBuildSubtarget.Server,
            ServerOutputDir, ServerExeName);
    }

    // 一次构建 Linux 服务端和 Windows 客户端
    public static bool BuildBoth(BuildConfig cfg)
    {
        if (!PrepareShared(cfg, "双端同时出包"))
            return false;

        double t0 = EditorApplication.timeSinceStartup;

        // 优先构建服务端，失败时跳过客户端
        if (!RunSingleCore("Linux 服务端", cfg, BuildTarget.StandaloneLinux64,
                StandaloneBuildSubtarget.Server, ServerOutputDir, ServerExeName))
        {
            FinishShared(t0, "双端出包失败：服务端这一步没过，客户端已跳过。", false, cfg);
            return false;
        }

        // 服务端通过后构建 Windows 客户端
        if (!RunSingleCore("Windows 客户端", cfg, BuildTarget.StandaloneWindows64,
                StandaloneBuildSubtarget.Player, ClientOutputDir, ClientExeName))
        {
            FinishShared(t0, "双端出包部分成功：服务端 OK，客户端失败。", false, cfg);
            return false;
        }

        FinishShared(t0, "双端出包完成。", true, cfg);
        return true;
    }

    // 执行单个构建目标的检查、出包和汇总

    private static bool RunSingle(string label, BuildConfig cfg,
        BuildTarget target, StandaloneBuildSubtarget subtarget, string outputDir, string exeName)
    {
        // 先执行共同的构建前检查
        if (!PrepareShared(cfg, label))
            return false;

        // 构建当前目标并汇总结果
        double t0 = EditorApplication.timeSinceStartup;
        bool ok = RunSingleCore(label, cfg, target, subtarget, outputDir, exeName);
        FinishShared(t0, ok ? label + " 出包完成。" : label + " 出包失败。", ok, cfg);
        return ok;
    }

    // 检查编辑器、场景和输出文件占用，再按配置切换地址
    private static bool PrepareShared(BuildConfig cfg, string label)
    {
        // 播放、编译或导入期间跳过构建
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("无法出包",
                "正在 Play 模式（或正准备进入 Play 模式），请先停止运行再出包。", "好");
            return false;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            Debug.LogWarning("[出包] Unity 还在编译/导入中，稍等一下再点。");
            return false;
        }

        // 确认至少有一个场景启用了构建
        if (GetEnabledScenes().Length == 0)
        {
            EditorUtility.DisplayDialog("无法出包",
                "Build Settings 里一个启用的场景都没有。\n请确认 Assets/Scenes/TestScene.unity 已勾选且排在第 0 位。", "好");
            return false;
        }

        // 检查残留进程，用户确认后结束以解除占用
        string[] running = GetRunningGameProcessNames();
        if (running.Length > 0)
        {
            bool kill = EditorUtility.DisplayDialog("有旧进程还在跑",
                "检测到本工程的客户端/服务端进程仍在运行：\n\n    " + string.Join(", ", running) + "\n\n" +
                "不结束它们，Unity 会报 “Copying the file failed: 另一个程序正在使用此文件”。\n\n" +
                "要现在结束这些进程吗？（调试中的客户端会直接关掉）",
                "结束进程并继续", "我自己去关");

            if (!kill)
                return false;

            KillGameProcesses();
        }

        // 按构建配置切换服务器地址，失败时中止构建
        if (!string.IsNullOrEmpty(cfg.SwitchAddressBeforeBuild))
        {
            if (!SwitchEnvironment(cfg.SwitchAddressBeforeBuild, false))
            {
                Debug.LogWarning("[出包] 环境切换失败，已中止出包，避免打出连错地址的包。");
                return false;
            }
        }

        Debug.Log("[出包] 开始：" + label + " | Development=" + cfg.Development);
        return true;
    }

    // 记录构建结果并刷新面板
    private static void FinishShared(double t0, string message, bool ok, BuildConfig cfg)
    {
        _lastBuildSeconds = EditorApplication.timeSinceStartup - t0;
        _lastBuildSummary = string.Format("{0} 用时 {1:F1}s", message, _lastBuildSeconds);

        // 构建成功时刷新资源，按配置打开输出目录
        if (ok)
        {
            AssetDatabase.Refresh();
            Debug.Log("[出包] " + _lastBuildSummary);

            if (cfg.RevealAfterBuild)
            {
                string dir = ToAbsolute("Build");
                Directory.CreateDirectory(dir);
                EditorUtility.RevealInFinder(dir);
            }
        }
        else
        {
            Debug.LogError("[出包] " + _lastBuildSummary);
        }

        // 通知已打开的出包面板刷新结果
        FPSGameBuildWindow[] windows = Resources.FindObjectsOfTypeAll<FPSGameBuildWindow>();
        for (int i = 0; i < windows.Length; i++)
            windows[i].Repaint();
    }

    // 按指定平台构建单个目标并读取构建报告

    private static bool RunSingleCore(string label, BuildConfig cfg,
        BuildTarget target, StandaloneBuildSubtarget subtarget,
        string outputDir, string exeName)
    {
        // 确保构建平台和子目标正确
        if (!EnsureBuildTarget(target, subtarget, cfg.AllowSwitchPlatform))
            return false;

        // 准备输出路径、场景列表和构建参数
        string absDir = ToAbsolute(outputDir);
        Directory.CreateDirectory(absDir);
        string location = Path.Combine(absDir, exeName).Replace('\\', '/');

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = GetEnabledScenes(),
            locationPathName = location,
            target = target,
            targetGroup = BuildPipeline.GetBuildTargetGroup(target),
            subtarget = (int)subtarget,
            options = cfg.Development ? BuildOptions.Development : BuildOptions.None,
        };

        // 执行构建并在结束后关闭进度条
        BuildReport report;
        try
        {
            EditorUtility.DisplayProgressBar("出包中", label + " → " + outputDir, 0.5f);
            report = BuildPipeline.BuildPlayer(options);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // 根据构建报告记录错误或成功信息
        if (report == null)
        {
            Debug.LogError("[出包] " + label + "：BuildPipeline 返回 null（目标平台模块可能没装）。");
            return false;
        }

        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError(string.Format(
                "[出包] {0} 失败：result={1} errors={2} warnings={3}\n{4}",
                label, report.summary.result, report.summary.totalErrors,
                report.summary.totalWarnings, report.summary.outputPath));
            return false;
        }

        Debug.Log(string.Format(
            "[出包] {0} 成功 → {1}\n        大小 {2} | 错误 {3} | 警告 {4} | 耗时 {5:F1}s",
            label, report.summary.outputPath, FormatBytes(report.summary.totalSize),
            report.summary.totalErrors, report.summary.totalWarnings,
            report.summary.totalTime.TotalSeconds));
        return true;
    }

    // 按需切换构建平台并设置客户端或服务端子目标
    private static bool EnsureBuildTarget(BuildTarget target, StandaloneBuildSubtarget subtarget, bool allowSwitch)
    {
        // 禁止切平台时检查当前平台是否匹配
        if (!allowSwitch && EditorUserBuildSettings.activeBuildTarget != target)
        {
            Debug.LogError(string.Format(
                "[出包] 当前激活平台是 {0}，目标是 {1}，但没有允许自动切换平台。",
                EditorUserBuildSettings.activeBuildTarget, target));
            return false;
        }

        // 为桌面平台设置客户端或专用服务端子目标
        if (IsStandalone(target))
            EditorUserBuildSettings.standaloneBuildSubtarget = subtarget;

        if (EditorUserBuildSettings.activeBuildTarget == target)
            return true;

        if (!allowSwitch)
            return false;

        // 切换平台并在结束后关闭进度条
        string from = EditorUserBuildSettings.activeBuildTarget.ToString();
        Debug.Log(string.Format(
            "[出包] 切换构建平台 {0} → {1}（首次切换会重新导入资源，可能要几分钟）", from, target));

        bool switched;
        try
        {
            EditorUtility.DisplayProgressBar("切换平台", from + " → " + target, 0.5f);
            switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildPipeline.GetBuildTargetGroup(target), target);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (!switched)
        {
            Debug.LogError(string.Format(
                "[出包] 切换平台失败：{0} → {1}。请确认 Unity Hub 里装了对应模块（Linux Server / Windows Server）。",
                from, target));
            return false;
        }

        // 平台切换成功后重新设置被重置的子目标
        if (IsStandalone(target))
            EditorUserBuildSettings.standaloneBuildSubtarget = subtarget;

        return true;
    }

    // 判断是否为桌面构建平台
    private static bool IsStandalone(BuildTarget t)
    {
        return t == BuildTarget.StandaloneWindows64
            || t == BuildTarget.StandaloneLinux64
            || t == BuildTarget.StandaloneWindows
            || t == BuildTarget.StandaloneOSX;
    }

    // 读取构建设置中已启用的场景路径
    private static string[] GetEnabledScenes()
    {
        List<string> list = new List<string>();
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        for (int i = 0; i < scenes.Length; i++)
        {
            if (scenes[i] != null && scenes[i].enabled)
                list.Add(scenes[i].path);
        }
        return list.ToArray();
    }

    //  服务端地址切换（本地 / 真实）

    // 将当前构建场景的网络管理器改为指定服务器地址
    public static bool SwitchEnvironment(string address, bool verbose)
    {
        Scene scene = SceneManager.GetActiveScene();

        // 必要时询问是否保存改动，再打开启动场景
        if (scene.path != BootScenePath && File.Exists(ToAbsolute(BootScenePath)) && !EditorApplication.isPlaying)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);
        }

        // 查找场景中所有网络管理器
        Mirror.NetworkManager[] managers = FindNetworkManagers(scene);
        if (managers.Length == 0)
        {
            EditorUtility.DisplayDialog("切换失败",
                "当前场景（" + scene.name + "）里找不到 NetworkManager。\n" +
                "请先打开 Assets/Scenes/TestScene.unity 再切换。", "好");
            return false;
        }

        if (managers.Length > 1)
            Debug.LogWarning("[环境] 场景里有 " + managers.Length + " 个 NetworkManager，全部一起改。");

        // 更新地址并记录编辑器撤销操作
        for (int i = 0; i < managers.Length; i++)
        {
            Mirror.NetworkManager nm = managers[i];

            if (nm.networkAddress == address)
            {
                if (verbose)
                    Debug.Log("[环境] 已经是 " + address + "，无需修改。");
                continue;
            }

            Undo.RecordObject(nm, "切换服务端地址");
            string old = nm.networkAddress;
            nm.networkAddress = address;
            EditorUtility.SetDirty(nm);

            if (verbose)
                Debug.Log("[环境] networkAddress: " + old + " → " + address);
        }

        EditorSceneManager.MarkSceneDirty(scene);

        // 标记场景待保存，手动切换时提醒用户保存
        if (verbose && !Application.isBatchMode && scene.isDirty)
            EditorUtility.DisplayDialog("地址已切换",
                "已切到 " + address + "，但场景还没保存。\n\n" +
                "请在 Unity 里按 Ctrl+S 保存场景（或菜单 File → Save），否则出包时带的还是旧地址。",
                "知道了");

        // 保存本次选择供下次恢复使用
        string envKey = (address == NetworkAddressConfig.LocalAddress) ? EnvLocal : EnvProduction;
        EditorPrefs.SetString(PrefEnvironment, envKey);

        if (verbose)
        {
            Debug.Log(string.Format(
                "[环境] 当前地址 = {0}（{1}）\n        场景 {2} 已标记为需要保存 —— 出包前记得 Ctrl+S，" +
                "否则包里带的还是旧地址。",
                address, EnvironmentDisplayName(envKey), scene.path));
        }

        RepaintAllWindows();
        return true;
    }

    // 重新应用上次选择的服务器地址
    public static void ReapplySavedEnvironment()
    {
        string addr = GetSavedEnvironmentAddress();
        SwitchEnvironment(addr, true);
    }

    // 根据上次选择读取服务器地址
    private static string GetSavedEnvironmentAddress()
    {
        string envKey = EditorPrefs.GetString(PrefEnvironment, EnvProduction);
        return envKey == EnvLocal ? NetworkAddressConfig.LocalAddress : NetworkAddressConfig.ProductionAddress;
    }

    // 取得环境在面板上的显示名称
    private static string EnvironmentDisplayName(string envKey)
    {
        return envKey == EnvLocal ? "本地" : "真实";
    }

    // 查找指定场景内的网络管理器，包括未启用对象
    private static Mirror.NetworkManager[] FindNetworkManagers(Scene scene)
    {
        List<Mirror.NetworkManager> list = new List<Mirror.NetworkManager>();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            list.AddRange(roots[i].GetComponentsInChildren<Mirror.NetworkManager>(true));
        return list.ToArray();
    }

    // 优先在当前场景查找网络管理器
    private static Mirror.NetworkManager FindAnyNetworkManager()
    {
        Mirror.NetworkManager[] inScene = FindNetworkManagers(SceneManager.GetActiveScene());
        if (inScene.Length > 0)
            return inScene[0];

        // 当前场景找不到时扩大查找范围，包括未启用对象
        return UnityEngine.Object.FindAnyObjectByType<Mirror.NetworkManager>(FindObjectsInactive.Include);
    }

    //  进程占用检测

    // 统计运行中的游戏进程名称和数量
    private static string[] GetRunningGameProcessNames()
    {
        List<string> names = new List<string>();
        string[] candidates = { "FPSGame-Y1", "FPSGame_Y1" };

        for (int i = 0; i < candidates.Length; i++)
        {
            try
            {
                Process[] ps = Process.GetProcessesByName(candidates[i]);
                if (ps.Length > 0)
                    names.Add(candidates[i] + " x" + ps.Length);
            }
            catch (Exception)
            {
                // 进程查询失败时继续检查其他进程名称
            }
        }

        return names.ToArray();
    }

    // 结束检测到的游戏进程以解除文件占用
    private static void KillGameProcesses()
    {
        string[] candidates = { "FPSGame-Y1", "FPSGame_Y1" };
        for (int i = 0; i < candidates.Length; i++)
        {
            try
            {
                Process[] ps = Process.GetProcessesByName(candidates[i]);
                for (int j = 0; j < ps.Length; j++)
                {
                    ps[j].Kill();
                    Debug.Log("[出包] 已结束进程 " + ps[j].Id + " (" + candidates[i] + ")");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[出包] 结束进程 " + candidates[i] + " 失败：" + e.Message);
            }
        }
    }

    //  面板 UI

    // 设定出包面板的最小尺寸
    private void OnEnable()
    {
        minSize = WindowMinSize;
    }

    // 绘制出包面板的可滚动内容
    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        // 绘制地址、出包、运行时覆盖和自检区
        DrawEnvironmentSection();
        EditorGUILayout.Space(8);

        DrawBuildSection();
        EditorGUILayout.Space(8);

        DrawRuntimeSection();
        EditorGUILayout.Space(8);

        DrawDiagnosticsSection();

        EditorGUILayout.EndScrollView();
    }

    // 显示当前服务器地址并提供切换操作
    private void DrawEnvironmentSection()
    {
        EditorGUILayout.LabelField("① 服务端地址", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            // 读取场景地址和上次选择的环境
            Mirror.NetworkManager nm = FindAnyNetworkManager();
            string current = nm != null ? nm.networkAddress : "(场景里没有 NetworkManager)";

            EditorGUILayout.LabelField("场景里的 networkAddress", current);

            string envKey = EditorPrefs.GetString(PrefEnvironment, EnvProduction);
            EditorGUILayout.LabelField("上次选择", EnvironmentDisplayName(envKey)
                + "（" + GetSavedEnvironmentAddress() + "）");

            EditorGUILayout.Space(4);

            // 提供本地和生产环境的快捷切换
            using (new EditorGUILayout.HorizontalScope())
            {
                Color old = GUI.backgroundColor;

                if (current == NetworkAddressConfig.LocalAddress)
                    GUI.backgroundColor = new Color(0.55f, 0.9f, 0.55f);

                if (GUILayout.Button("切到本地\n127.0.0.1", GUILayout.Height(42)))
                    SwitchEnvironment(NetworkAddressConfig.LocalAddress, true);

                GUI.backgroundColor = old;

                if (current == NetworkAddressConfig.ProductionAddress)
                    GUI.backgroundColor = new Color(1f, 0.75f, 0.55f);

                if (GUILayout.Button("切到真实\n43.142.103.57", GUILayout.Height(42)))
                    SwitchEnvironment(NetworkAddressConfig.ProductionAddress, true);

                GUI.backgroundColor = old;
            }

            // 提供自定义地址和恢复上次环境的入口
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("自定义地址…"))
                    ShowCustomAddressDialog(current);

                if (GUILayout.Button("恢复上次环境"))
                    ReapplySavedEnvironment();
            }

            EditorGUILayout.HelpBox(
                "地址保存在场景文件里，出包时会带进包里 —— 所以换地址后要 Ctrl+S 保存场景，" +
                "再出包才生效。\n" +
                "不想重新出包就换地址：用下面的「运行时地址覆盖」，出包后用命令行或配置文件改。",
                MessageType.Info);
        }
    }

    // 打开自定义地址对话框并应用确认结果
    private static void ShowCustomAddressDialog(string current)
    {
        string input = current;
        CustomAddressWindow.Show(input, delegate (string addr)
        {
            if (!string.IsNullOrEmpty(addr))
                SwitchEnvironment(addr.Trim(), true);
        });
    }

    // 绘制出包配置、构建按钮和结果
    private void DrawBuildSection()
    {
        EditorGUILayout.LabelField("② 出包", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            // 读取和保存出包面板选项
            bool devClient = EditorPrefs.GetBool(PrefDevClient, true);
            bool devClientNew = EditorGUILayout.ToggleLeft(
                "Development Build（排障用，正式包请取消勾选）", devClient);
            if (devClientNew != devClient)
                EditorPrefs.SetBool(PrefDevClient, devClientNew);

            bool switchBefore = EditorPrefs.GetBool(PrefSwitchBeforeBuild, true);
            bool switchBeforeNew = EditorGUILayout.ToggleLeft(
                "出包前先切换地址（勾选后按下面选的环境出包）", switchBefore);
            if (switchBeforeNew != switchBefore)
                EditorPrefs.SetBool(PrefSwitchBeforeBuild, switchBeforeNew);

            bool switchToLocal = EditorPrefs.GetBool("FPSGame_Y1.SwitchBeforeBuildToLocal", true);
            using (new EditorGUI.DisabledScope(!switchBeforeNew))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("先切到", GUILayout.Width(50));
                    if (GUILayout.Toggle(switchToLocal, "本地", EditorStyles.miniButton, GUILayout.Width(60)))
                    {
                        switchToLocal = true;
                        EditorPrefs.SetBool("FPSGame_Y1.SwitchBeforeBuildToLocal", true);
                    }
                    if (GUILayout.Toggle(!switchToLocal, "真实", EditorStyles.miniButton, GUILayout.Width(60)))
                    {
                        switchToLocal = false;
                        EditorPrefs.SetBool("FPSGame_Y1.SwitchBeforeBuildToLocal", false);
                    }
                    GUILayout.FlexibleSpace();
                }
            }

            bool reveal = EditorPrefs.GetBool(PrefOpenFolder, true);
            bool revealNew = EditorGUILayout.ToggleLeft("出包完成后打开 Build 目录", reveal);
            if (revealNew != reveal)
                EditorPrefs.SetBool(PrefOpenFolder, revealNew);

            EditorGUILayout.Space(6);

            // 将当前面板选项整理为一次构建的参数
            BuildConfig cfg = new BuildConfig
            {
                Development = devClientNew,
                RevealAfterBuild = revealNew,
                AllowSwitchPlatform = true,
                SwitchAddressBeforeBuild = switchBeforeNew
                    ? (switchToLocal ? NetworkAddressConfig.LocalAddress : NetworkAddressConfig.ProductionAddress)
                    : null,
            };

            // 显示客户端、服务端和双端出包入口
            string clientPath = ClientOutputDir + "/" + ClientExeName;
            string serverPath = ServerOutputDir + "/" + ServerExeName;

            if (GUILayout.Button(new GUIContent("只出客户端（Windows）\n" + clientPath,
                    "StandaloneWindows64 / Player，输出到 " + clientPath), GUILayout.Height(44)))
                BuildClient(cfg);

            if (GUILayout.Button(new GUIContent("只出服务端（Linux Server）\n" + serverPath,
                    "StandaloneLinux64 / Server，Mono，输出到 " + serverPath + "，之后用 Tools/deploy/deploy.ps1 发布"),
                    GUILayout.Height(44)))
                BuildServer(cfg);

            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.6f, 0.85f, 1f);
            if (GUILayout.Button(new GUIContent("双端同时出包（服务端 → 客户端）",
                    "先 Linux Server 再 Windows 客户端，自动切换构建平台，一次点完"), GUILayout.Height(52)))
                BuildBoth(cfg);
            GUI.backgroundColor = oldBg;

            EditorGUILayout.Space(4);

            // 显示当前平台、场景列表和上次构建结果
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField("当前平台", EditorUserBuildSettings.activeBuildTarget.ToString());
                EditorGUILayout.LabelField("Standalone 子目标",
                    EditorUserBuildSettings.standaloneBuildSubtarget.ToString());
                EditorGUILayout.LabelField("场景列表", string.Join(", ", GetEnabledScenes()));
                if (!string.IsNullOrEmpty(_lastBuildSummary))
                    EditorGUILayout.LabelField("上次出包", _lastBuildSummary);
            }
        }
    }

    // 显示运行时服务器地址覆盖选项
    private void DrawRuntimeSection()
    {
        EditorGUILayout.LabelField("③ 运行时地址覆盖（不想重新出包时用）", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            // 显示配置路径和地址覆盖优先级
            EditorGUILayout.LabelField("本次会话的配置目录", EditorStyles.miniLabel);
            EditorGUILayout.SelectableLabel(NetworkAddressConfig.ConfigFilePath,
                EditorStyles.textField, GUILayout.Height(18));

            EditorGUILayout.HelpBox(
                "场景里挂 NetworkAddressConfig 后，客户端启动时会按下面的优先级覆盖地址：\n" +
                "  1. 命令行   FPSGame-Y1.exe -serverAddr 127.0.0.1\n" +
                "  2. 环境变量 FPSGAME_SERVER_ADDR\n" +
                "  3. 配置文件 server_addr.txt（上面的路径）\n" +
                "  4. 都没有 → 用场景里的地址\n\n" +
                "注意：配置文件路径是 Application.persistentDataPath，只有运行过一次游戏后才有。",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                // 提供写入或删除本地地址配置的操作
                if (GUILayout.Button("把当前场景地址写入配置文件"))
                {
                    Mirror.NetworkManager nm = FindAnyNetworkManager();
                    if (nm != null && NetworkAddressConfig.WriteConfigFile(nm.networkAddress))
                        Debug.Log("[环境] 配置文件已写入: " + nm.networkAddress);
                }

                if (GUILayout.Button("删除配置文件（回到场景地址）"))
                    NetworkAddressConfig.ClearConfigFile();
            }

            // 将本地测试命令复制到剪贴板
            if (GUILayout.Button("复制一条本地测试命令行"))
            {
                EditorGUIUtility.systemCopyBuffer =
                    "FPSGame-Y1.exe -serverAddr " + NetworkAddressConfig.LocalAddress + " -logFile -";
                Debug.Log("[环境] 命令行已复制到剪贴板。");
            }
        }
    }

    // 绘制进程占用、输出目录和地址排查工具
    private void DrawDiagnosticsSection()
    {
        EditorGUILayout.LabelField("④ 自检", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            // 显示残留进程并提供结束操作
            string[] running = GetRunningGameProcessNames();
            if (running.Length == 0)
                EditorGUILayout.LabelField("残留进程", "无");
            else
            {
                EditorGUILayout.LabelField("残留进程", string.Join(", ", running));
                if (GUILayout.Button("结束这些进程"))
                    KillGameProcesses();
            }

            if (GUILayout.Button("打开输出目录"))
                MenuOpenBuildDir();

            // 输出场景地址和运行时覆盖来源
            if (GUILayout.Button("打印当前生效地址（选中 NetworkManager 后看 Console）"))
            {
                Mirror.NetworkManager nm = FindAnyNetworkManager();
                if (nm != null)
                {
                    NetworkAddressConfig cfg = nm.GetComponent<NetworkAddressConfig>();
                    string source = null;
                    string resolved = cfg != null ? cfg.Resolve(out source) : null;
                    Debug.Log(string.Format(
                        "[环境] 场景值={0} | 运行时覆盖={1} 来源={2}",
                        nm.networkAddress, resolved ?? "(无)", source ?? "(无)"));
                }
            }
        }
    }

    //  小工具

    // 刷新全部已打开的出包面板
    private static void RepaintAllWindows()
    {
        FPSGameBuildWindow[] windows = Resources.FindObjectsOfTypeAll<FPSGameBuildWindow>();
        for (int i = 0; i < windows.Length; i++)
            windows[i].Repaint();
    }

    // 将项目相对路径转为完整路径
    private static string ToAbsolute(string relative)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.GetFullPath(Path.Combine(projectRoot, relative));
    }

    // 将构建大小转换为适合显示的单位
    private static string FormatBytes(ulong bytes)
    {
        double b = bytes;
        if (b >= 1024.0 * 1024.0 * 1024.0) return (b / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
        if (b >= 1024.0 * 1024.0) return (b / (1024.0 * 1024.0)).ToString("F1") + " MB";
        if (b >= 1024.0) return (b / 1024.0).ToString("F1") + " KB";
        return b.ToString("F0") + " B";
    }
}

// 自定义服务器地址输入窗口
public class CustomAddressWindow : EditorWindow
{
    private string _address = "";
    // 保存地址确认后的回调
    private Action<string> _onConfirm;

    // 打开自定义服务器地址窗口
    public static void Show(string initial, Action<string> onConfirm)
    {
        CustomAddressWindow w = CreateInstance<CustomAddressWindow>();
        w.titleContent = new GUIContent("自定义服务端地址");
        w._address = initial ?? "";
        w._onConfirm = onConfirm;
        w.minSize = new Vector2(340, 120);
        w.maxSize = new Vector2(600, 160);
        w.ShowUtility();
    }

    // 绘制地址输入框和确认、取消按钮
    private void OnGUI()
    {
        // 读取用户输入的服务器地址
        EditorGUILayout.LabelField("输入 IP 或域名（不含端口，端口由 KcpTransport 决定）：");
        _address = EditorGUILayout.TextField(_address);

        EditorGUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            // 确认时提交当前地址，取消时直接关闭
            if (GUILayout.Button("确定"))
            {
                if (_onConfirm != null)
                    _onConfirm(_address);
                Close();
            }
            if (GUILayout.Button("取消"))
                Close();
        }
    }
}
