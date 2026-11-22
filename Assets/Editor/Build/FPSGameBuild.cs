// 执行 Windows 客户端和 Linux 专用服务端的 Unity 构建流程。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

// 保存一次构建需要的选项
public class FPSGameBuildConfig
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

internal static class FPSGameBuild {
    // 两端构建使用的场景和产物路径。
    internal const string ProjectRoot = ".";
    internal const string ServerOutputDir = "Build/LinuxServer";
    internal const string ServerExeName = "FPSGame-Y1.x86_64";
    internal const string ClientOutputDir = "Build/WindowsClient";
    internal const string ClientExeName = "FPSGame-Y1.exe";
    internal const string BootScenePath = "Assets/Scenes/TestScene.unity";

    // 检查和结束残留进程时使用相同的游戏进程名。
    private static readonly string[] GameProcessNames = { "FPSGame-Y1", "FPSGame_Y1" };

    // 最近一次构建的结果，由出包面板显示。
    internal static string LastBuildSummary { get; private set; } = "";

    //  出包主流程

    // 构建 Windows 客户端
    internal static bool BuildClient(FPSGameBuildConfig cfg)
    {
        return RunSingle("Windows 客户端", cfg,
            BuildTarget.StandaloneWindows64, StandaloneBuildSubtarget.Player,
            ClientOutputDir, ClientExeName);
    }

    // 构建 Linux 专用服务端
    internal static bool BuildServer(FPSGameBuildConfig cfg)
    {
        return RunSingle("Linux 服务端", cfg,
            BuildTarget.StandaloneLinux64, StandaloneBuildSubtarget.Server,
            ServerOutputDir, ServerExeName);
    }

    // 一次构建 Linux 服务端和 Windows 客户端
    internal static bool BuildBoth(FPSGameBuildConfig cfg)
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

    private static bool RunSingle(string label, FPSGameBuildConfig cfg,
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
    private static bool PrepareShared(FPSGameBuildConfig cfg, string label)
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
        if (!string.IsNullOrEmpty(cfg.SwitchAddressBeforeBuild) &&
            !FPSGameBuildEnvironment.SwitchEnvironment(cfg.SwitchAddressBeforeBuild, false)) {
            Debug.LogWarning("[出包] 环境切换失败，已中止出包，避免打出连错地址的包。");
            return false;
        }

        Debug.Log("[出包] 开始：" + label + " | Development=" + cfg.Development);
        return true;
    }

    // 记录构建结果并按选项显示输出
    private static void FinishShared(double t0, string message, bool ok, FPSGameBuildConfig cfg)
    {
        double elapsedSeconds = EditorApplication.timeSinceStartup - t0;
        LastBuildSummary = string.Format("{0} 用时 {1:F1}s", message, elapsedSeconds);

        // 构建成功时刷新资源，按配置打开输出目录
        if (ok)
        {
            AssetDatabase.Refresh();
            Debug.Log("[出包] " + LastBuildSummary);

            if (cfg.RevealAfterBuild)
            {
                string dir = ToAbsolute("Build");
                Directory.CreateDirectory(dir);
                EditorUtility.RevealInFinder(dir);
            }
        }
        else
        {
            Debug.LogError("[出包] " + LastBuildSummary);
        }

    }

    // 按指定平台构建单个目标并读取构建报告

    private static bool RunSingleCore(string label, FPSGameBuildConfig cfg,
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

        BuildSummary summary = report.summary;
        if (summary.result != BuildResult.Succeeded) {
            Debug.LogError(string.Format(
                "[出包] {0} 失败：result={1} errors={2} warnings={3}\n{4}",
                label, summary.result, summary.totalErrors,
                summary.totalWarnings, summary.outputPath));
            return false;
        }

        Debug.Log(string.Format(
            "[出包] {0} 成功 → {1}\n        大小 {2} | 错误 {3} | 警告 {4} | 耗时 {5:F1}s",
            label, summary.outputPath, FormatBytes(summary.totalSize),
            summary.totalErrors, summary.totalWarnings,
            summary.totalTime.TotalSeconds));
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
    internal static string[] GetEnabledScenes()
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

    //  进程占用检测

    // 统计运行中的游戏进程名称和数量
    internal static string[] GetRunningGameProcessNames()
    {
        List<string> names = new List<string>();
        foreach (string processName in GameProcessNames) {
            try
            {
                Process[] ps = Process.GetProcessesByName(processName);
                if (ps.Length > 0)
                    names.Add(processName + " x" + ps.Length);
            }
            catch (Exception)
            {
                // 进程查询失败时继续检查其他进程名称
            }
        }

        return names.ToArray();
    }

    // 结束检测到的游戏进程以解除文件占用
    internal static void KillGameProcesses()
    {
        foreach (string processName in GameProcessNames) {
            try
            {
                foreach (Process process in Process.GetProcessesByName(processName)) {
                    process.Kill();
                    Debug.Log("[出包] 已结束进程 " + process.Id + " (" + processName + ")");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[出包] 结束进程 " + processName + " 失败：" + e.Message);
            }
        }
    }

    // 将项目相对路径转为完整路径
    internal static string ToAbsolute(string relative)
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
