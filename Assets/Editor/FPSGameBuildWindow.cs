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

/// <summary>
/// FPSGame-Y1 出包工具（Editor 菜单 Tools/FPSGame-Y1，快捷键 Ctrl+Shift+B 打开面板）。
///
/// 提供四件事：
///   1. 客户端打包      Build/WindowsClient/FPSGame-Y1.exe          (StandaloneWindows64 / Player)
///   2. 服务端打包      Build/LinuxServer/FPSGame-Y1.x86_64         (StandaloneLinux64 / Server, Mono)
///   3. 双端同时出包    Windows 客户端 + Linux 服务端，一次点完，自动切平台
///   4. 切换服务端地址  本地 127.0.0.1 / 真实 43.142.103.57，直接改场景里的 NetworkManager
///
/// 设计要点：
///   - 地址写在场景里，构建时自动被带进包里，所以「出包前先切环境」是必须的顺序，
///     面板里的「先切环境再出包」勾选框就是为这一步准备的。
///   - 出包前自动检测残留进程（否则 Unity 报「文件正在被使用」），并给出一键结束。
/// </summary>
public class FPSGameBuildWindow : EditorWindow
{
    // ---------------- 路径 / 平台常量（与项目文档「出包与发布流程.md」保持一致） ----------------

    public const string ProjectRoot = ".";
    public const string ServerOutputDir = "Build/LinuxServer";
    public const string ServerExeName = "FPSGame-Y1.x86_64";
    public const string ClientOutputDir = "Build/WindowsClient";
    public const string ClientExeName = "FPSGame-Y1.exe";
    public const string BootScenePath = "Assets/Scenes/TestScene.unity";

    // ---------------- EditorPrefs ----------------

    private const string PrefEnvironment = "FPSGame_Y1.BuildEnv";          // last / production
    private const string PrefSwitchBeforeBuild = "FPSGame_Y1.SwitchBeforeBuild";
    private const string PrefDevClient = "FPSGame_Y1.DevClient";
    private const string PrefOpenFolder = "FPSGame_Y1.OpenFolder";
    private const string PrefConfirmPlatformSwitch = "FPSGame_Y1.ConfirmSwitch";

    private const string EnvLocal = "last";
    private const string EnvProduction = "production";

    private static readonly Vector2 WindowMinSize = new Vector2(430f, 640f);

    // ---------------- 运行期状态 ----------------

    private Vector2 _scroll;

    // 静态：静态的 FinishShared 要写、面板重绘后还要能看到上一次结果
    private static double _lastBuildSeconds;
    private static string _lastBuildSummary = "";

    // =====================================================================================
    //  菜单项
    // =====================================================================================

    [MenuItem("Tools/FPSGame-Y1/出包面板 %#b", false, 1)]
    public static void OpenWindow()
    {
        FPSGameBuildWindow w = GetWindow<FPSGameBuildWindow>(false, "出包 / 环境", true);
        w.minSize = WindowMinSize;
        w.Show();
    }

    [MenuItem("Tools/FPSGame-Y1/打包客户端 (Windows) %#c", false, 20)]
    public static void MenuBuildClient()
    {
        BuildClient(new BuildConfig());
    }

    [MenuItem("Tools/FPSGame-Y1/打包服务端 (Linux Server) %#s", false, 21)]
    public static void MenuBuildServer()
    {
        BuildServer(new BuildConfig());
    }

    [MenuItem("Tools/FPSGame-Y1/双端同时出包 (客户端+服务端) %#d", false, 22)]
    public static void MenuBuildBoth()
    {
        BuildBoth(new BuildConfig());
    }

    [MenuItem("Tools/FPSGame-Y1/切换到本地服务地址 (127.0.0.1)", false, 40)]
    public static void MenuSwitchLocal()
    {
        SwitchEnvironment(NetworkAddressConfig.LocalAddress, true);
    }

    [MenuItem("Tools/FPSGame-Y1/切换到真实服务地址 (43.142.103.57)", false, 41)]
    public static void MenuSwitchProduction()
    {
        SwitchEnvironment(NetworkAddressConfig.ProductionAddress, true);
    }

    [MenuItem("Tools/FPSGame-Y1/打开出包输出目录", false, 60)]
    public static void MenuOpenBuildDir()
    {
        string dir = ToAbsolute("Build");
        Directory.CreateDirectory(dir);
        EditorUtility.RevealInFinder(dir);
    }

    // =====================================================================================
    //  出包参数
    // =====================================================================================

    /// <summary>一次出包要用的参数集合。</summary>
    public class BuildConfig
    {
        /// <summary>勾选 Development Build（排障期用；正式包不要勾）。</summary>
        public bool Development;

        /// <summary>是否在出包前先把环境切到某个地址；null 表示不动。</summary>
        public string SwitchAddressBeforeBuild;

        /// <summary>出包完成后打开输出目录。</summary>
        public bool RevealAfterBuild;

        /// <summary>允许自动切换构建平台（会触发一次资源重新导入）。</summary>
        public bool AllowSwitchPlatform = true;
    }

    // =====================================================================================
    //  出包主流程
    // =====================================================================================

    /// <summary>只出 Windows 客户端。</summary>
    public static bool BuildClient(BuildConfig cfg)
    {
        return RunSingle("Windows 客户端", cfg,
            BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player,
            ClientOutputDir, ClientExeName);
    }

    /// <summary>只出 Linux 服务端。</summary>
    public static bool BuildServer(BuildConfig cfg)
    {
        return RunSingle("Linux 服务端", cfg,
            BuildTarget.StandaloneLinux64, StandaloneBuildSubtarget.Server,
            ServerOutputDir, ServerExeName);
    }

    /// <summary>先服务端、后客户端，一次点完。</summary>
    public static bool BuildBoth(BuildConfig cfg)
    {
        if (!PrepareShared(cfg, "双端同时出包"))
            return false;

        double t0 = EditorApplication.timeSinceStartup;

        // 服务端在前：Linux Server 是发布必需项，客户端可以稍后补。
        if (!RunSingleCore("Linux 服务端", cfg, BuildTarget.StandaloneLinux64,
                StandaloneBuildSubtarget.Server, ServerOutputDir, ServerExeName))
        {
            FinishShared(t0, "双端出包失败：服务端这一步没过，客户端已跳过。", false, cfg);
            return false;
        }

        if (!RunSingleCore("Windows 客户端", cfg, BuildTarget.StandaloneWindows64,
                StandaloneBuildSubtarget.Player, ClientOutputDir, ClientExeName))
        {
            FinishShared(t0, "双端出包部分成功：服务端 OK，客户端失败。", false, cfg);
            return false;
        }

        FinishShared(t0, "双端出包完成。", true, cfg);
        return true;
    }

    // ---- 单个目标的包装：负责公共前置检查与结果汇总 ----

    private static bool RunSingle(string label, BuildConfig cfg,
        BuildTarget target, StandaloneBuildSubtarget subtarget, string outputDir, string exeName)
    {
        if (!PrepareShared(cfg, label))
            return false;

        double t0 = EditorApplication.timeSinceStartup;
        bool ok = RunSingleCore(label, cfg, target, subtarget, outputDir, exeName);
        FinishShared(t0, ok ? label + " 出包完成。" : label + " 出包失败。", ok, cfg);
        return ok;
    }

    private static bool PrepareShared(BuildConfig cfg, string label)
    {
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

        // 场景列表检查：项目只有 TestScene，序号 0
        if (GetEnabledScenes().Length == 0)
        {
            EditorUtility.DisplayDialog("无法出包",
                "Build Settings 里一个启用的场景都没有。\n请确认 Assets/Scenes/TestScene.unity 已勾选且排在第 0 位。", "好");
            return false;
        }

        // 残留进程检查：文件被占用是最高频的出包失败原因
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

        // 出包前切换环境
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

    private static void FinishShared(double t0, string message, bool ok, BuildConfig cfg)
    {
        _lastBuildSeconds = EditorApplication.timeSinceStartup - t0; // 静态字段，窗口在时也看得到
        _lastBuildSummary = string.Format("{0} 用时 {1:F1}s", message, _lastBuildSeconds);

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

        FPSGameBuildWindow[] windows = Resources.FindObjectsOfTypeAll<FPSGameBuildWindow>();
        for (int i = 0; i < windows.Length; i++)
            windows[i].Repaint();
    }

    // ---- 真正调用 BuildPipeline 的地方 ----

    private static bool RunSingleCore(string label, BuildConfig cfg,
        BuildTarget target, StandaloneBuildSubtarget subtarget,
        string outputDir, string exeName)
    {
        if (!EnsureBuildTarget(target, subtarget, cfg.AllowSwitchPlatform))
            return false;

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

    /// <summary>必要时切换构建平台；Standalone 之间还要设置 Server/Player 子目标。</summary>
    private static bool EnsureBuildTarget(BuildTarget target, StandaloneBuildSubtarget subtarget, bool allowSwitch)
    {
        if (!allowSwitch && EditorUserBuildSettings.activeBuildTarget != target)
        {
            Debug.LogError(string.Format(
                "[出包] 当前激活平台是 {0}，目标是 {1}，但没有允许自动切换平台。",
                EditorUserBuildSettings.activeBuildTarget, target));
            return false;
        }

        // StandaloneLinux64 / StandaloneWindows64 之间也需要切子目标，否则会出成普通客户端
        if (IsStandalone(target))
            EditorUserBuildSettings.standaloneBuildSubtarget = subtarget;

        if (EditorUserBuildSettings.activeBuildTarget == target)
            return true;

        if (!allowSwitch)
            return false;

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

        // 切完平台要再设一次子目标（切换过程会把它重置）
        if (IsStandalone(target))
            EditorUserBuildSettings.standaloneBuildSubtarget = subtarget;

        return true;
    }

    private static bool IsStandalone(BuildTarget t)
    {
        return t == BuildTarget.StandaloneWindows64
            || t == BuildTarget.StandaloneLinux64
            || t == BuildTarget.StandaloneWindows
            || t == BuildTarget.StandaloneOSX;
    }

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

    // =====================================================================================
    //  服务端地址切换（本地 / 真实）
    // =====================================================================================

    /// <summary>
    /// 把场景里 NetworkManager.networkAddress 改成指定地址。
    /// 返回 false 表示场景里找不到 NetworkManager（或场景没打开）。
    /// </summary>
    public static bool SwitchEnvironment(string address, bool verbose)
    {
        Scene scene = SceneManager.GetActiveScene();

        // 需要时自动打开 TestScene（其它场景里通常没有 NetworkManager）
        if (scene.path != BootScenePath && File.Exists(ToAbsolute(BootScenePath)) && !EditorApplication.isPlaying)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);
        }

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

        // 注意：这里刻意不自动保存场景，避免把用户其它未保存的改动一起写盘。
        // 保存由用户在 Unity 里 Ctrl+S 完成；批处理模式下可用 SaveScene(s) 显式保存。
        // 只有用户主动切环境（verbose=true）才弹提示；出包前自动切换时不打扰。
        if (verbose && !Application.isBatchMode && scene.isDirty)
            EditorUtility.DisplayDialog("地址已切换",
                "已切到 " + address + "，但场景还没保存。\n\n" +
                "请在 Unity 里按 Ctrl+S 保存场景（或菜单 File → Save），否则出包时带的还是旧地址。",
                "知道了");

        // 记住选择，方便「恢复上次环境」
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

    /// <summary>把上次选择的环境再应用一次（编辑器重启 / 拉取别人改动后用）。</summary>
    public static void ReapplySavedEnvironment()
    {
        string addr = GetSavedEnvironmentAddress();
        SwitchEnvironment(addr, true);
    }

    private static string GetSavedEnvironmentAddress()
    {
        string envKey = EditorPrefs.GetString(PrefEnvironment, EnvProduction);
        return envKey == EnvLocal ? NetworkAddressConfig.LocalAddress : NetworkAddressConfig.ProductionAddress;
    }

    private static string EnvironmentDisplayName(string envKey)
    {
        return envKey == EnvLocal ? "本地" : "真实";
    }

    private static Mirror.NetworkManager[] FindNetworkManagers(Scene scene)
    {
        List<Mirror.NetworkManager> list = new List<Mirror.NetworkManager>();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            list.AddRange(roots[i].GetComponentsInChildren<Mirror.NetworkManager>(true));
        return list.ToArray();
    }

    private static Mirror.NetworkManager FindAnyNetworkManager()
    {
        Mirror.NetworkManager[] inScene = FindNetworkManagers(SceneManager.GetActiveScene());
        if (inScene.Length > 0)
            return inScene[0];

        // 场景没打开也要能显示当前值（Include 保证和旧的 FindObjectOfType 一样能看到未激活对象）
        return UnityEngine.Object.FindAnyObjectByType<Mirror.NetworkManager>(FindObjectsInactive.Include);
    }

    // =====================================================================================
    //  进程占用检测
    // =====================================================================================

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
                // 忽略：拿不到进程列表不影响出包主流程
            }
        }

        return names.ToArray();
    }

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

    // =====================================================================================
    //  面板 UI
    // =====================================================================================

    private void OnEnable()
    {
        minSize = WindowMinSize;
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawEnvironmentSection();
        EditorGUILayout.Space(8);

        DrawBuildSection();
        EditorGUILayout.Space(8);

        DrawRuntimeSection();
        EditorGUILayout.Space(8);

        DrawDiagnosticsSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawEnvironmentSection()
    {
        EditorGUILayout.LabelField("① 服务端地址", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            Mirror.NetworkManager nm = FindAnyNetworkManager();
            string current = nm != null ? nm.networkAddress : "(场景里没有 NetworkManager)";

            EditorGUILayout.LabelField("场景里的 networkAddress", current);

            string envKey = EditorPrefs.GetString(PrefEnvironment, EnvProduction);
            EditorGUILayout.LabelField("上次选择", EnvironmentDisplayName(envKey)
                + "（" + GetSavedEnvironmentAddress() + "）");

            EditorGUILayout.Space(4);

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

    private static void ShowCustomAddressDialog(string current)
    {
        string input = current;
        CustomAddressWindow.Show(input, delegate (string addr)
        {
            if (!string.IsNullOrEmpty(addr))
                SwitchEnvironment(addr.Trim(), true);
        });
    }

    private void DrawBuildSection()
    {
        EditorGUILayout.LabelField("② 出包", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
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

            BuildConfig cfg = new BuildConfig
            {
                Development = devClientNew,
                RevealAfterBuild = revealNew,
                AllowSwitchPlatform = true,
                SwitchAddressBeforeBuild = switchBeforeNew
                    ? (switchToLocal ? NetworkAddressConfig.LocalAddress : NetworkAddressConfig.ProductionAddress)
                    : null,
            };

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

    private void DrawRuntimeSection()
    {
        EditorGUILayout.LabelField("③ 运行时地址覆盖（不想重新出包时用）", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
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
                if (GUILayout.Button("把当前场景地址写入配置文件"))
                {
                    Mirror.NetworkManager nm = FindAnyNetworkManager();
                    if (nm != null && NetworkAddressConfig.WriteConfigFile(nm.networkAddress))
                        Debug.Log("[环境] 配置文件已写入: " + nm.networkAddress);
                }

                if (GUILayout.Button("删除配置文件（回到场景地址）"))
                    NetworkAddressConfig.ClearConfigFile();
            }

            if (GUILayout.Button("复制一条本地测试命令行"))
            {
                EditorGUIUtility.systemCopyBuffer =
                    "FPSGame-Y1.exe -serverAddr " + NetworkAddressConfig.LocalAddress + " -logFile -";
                Debug.Log("[环境] 命令行已复制到剪贴板。");
            }
        }
    }

    private void DrawDiagnosticsSection()
    {
        EditorGUILayout.LabelField("④ 自检", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
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

    // =====================================================================================
    //  小工具
    // =====================================================================================

    private static void RepaintAllWindows()
    {
        FPSGameBuildWindow[] windows = Resources.FindObjectsOfTypeAll<FPSGameBuildWindow>();
        for (int i = 0; i < windows.Length; i++)
            windows[i].Repaint();
    }

    private static string ToAbsolute(string relative)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.GetFullPath(Path.Combine(projectRoot, relative));
    }

    private static string FormatBytes(ulong bytes)
    {
        double b = bytes;
        if (b >= 1024.0 * 1024.0 * 1024.0) return (b / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
        if (b >= 1024.0 * 1024.0) return (b / (1024.0 * 1024.0)).ToString("F1") + " MB";
        if (b >= 1024.0) return (b / 1024.0).ToString("F1") + " KB";
        return b.ToString("F0") + " B";
    }
}

/// <summary>自定义地址输入的小弹窗。</summary>
public class CustomAddressWindow : EditorWindow
{
    private string _address = "";
    private Action<string> _onConfirm;

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

    private void OnGUI()
    {
        EditorGUILayout.LabelField("输入 IP 或域名（不含端口，端口由 KcpTransport 决定）：");
        _address = EditorGUILayout.TextField(_address);

        EditorGUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
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
