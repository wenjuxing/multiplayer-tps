// 提供客户端和服务端出包、地址切换与构建自检
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class FPSGameBuildWindow : EditorWindow
{
    // 构建输出路径和启动场景，与出包文档保持一致

    public const string ProjectRoot = FPSGameBuild.ProjectRoot;
    public const string ServerOutputDir = FPSGameBuild.ServerOutputDir;
    public const string ServerExeName = FPSGameBuild.ServerExeName;
    public const string ClientOutputDir = FPSGameBuild.ClientOutputDir;
    public const string ClientExeName = FPSGameBuild.ClientExeName;
    public const string BootScenePath = FPSGameBuild.BootScenePath;

    // 保存出包面板选项和环境选择的编辑器配置键

    private const string PrefSwitchBeforeBuild = "FPSGame_Y1.SwitchBeforeBuild";
    private const string PrefDevClient = "FPSGame_Y1.DevClient";
    private const string PrefOpenFolder = "FPSGame_Y1.OpenFolder";
    private const string PrefConfirmPlatformSwitch = "FPSGame_Y1.ConfirmSwitch";


    private static readonly Vector2 WindowMinSize = new Vector2(430f, 640f);


    private Vector2 _scroll;

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
        string dir = FPSGameBuild.ToAbsolute("Build");
        Directory.CreateDirectory(dir);
        EditorUtility.RevealInFinder(dir);
    }

    //  出包参数

    // 保留编辑器菜单和脚本调用使用的配置入口。
    public class BuildConfig : FPSGameBuildConfig {
    }

    // 构建 Windows 客户端并刷新面板摘要。
    public static bool BuildClient(BuildConfig cfg) {
        return RunBuild(FPSGameBuild.BuildClient, cfg);
    }

    // 构建 Linux 专用服务端并刷新面板摘要。
    public static bool BuildServer(BuildConfig cfg) {
        return RunBuild(FPSGameBuild.BuildServer, cfg);
    }

    // 依次构建 Linux 服务端和 Windows 客户端并刷新面板摘要。
    public static bool BuildBoth(BuildConfig cfg) {
        return RunBuild(FPSGameBuild.BuildBoth, cfg);
    }

    // 执行构建入口并更新已打开面板的构建结果。
    private static bool RunBuild(Func<FPSGameBuildConfig, bool> build, BuildConfig cfg) {
        bool success = build(cfg);
        RepaintAllWindows();
        return success;
    }

    // 切换启动场景的地址并刷新打开的面板。
    public static bool SwitchEnvironment(string address, bool verbose) {
        bool switched = FPSGameBuildEnvironment.SwitchEnvironment(address, verbose);
        if (switched)
            RepaintAllWindows();
        return switched;
    }

    // 重新应用上次选择的服务器地址。
    public static void ReapplySavedEnvironment() {
        SwitchEnvironment(FPSGameBuildEnvironment.GetSavedEnvironment().Address, true);
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
            Mirror.NetworkManager nm = FPSGameBuildEnvironment.FindAnyNetworkManager();
            string current = nm != null ? nm.networkAddress : "(场景里没有 NetworkManager)";

            EditorGUILayout.LabelField("场景里的 networkAddress", current);

            (string savedName, string savedAddress) = FPSGameBuildEnvironment.GetSavedEnvironment();
            EditorGUILayout.LabelField("上次选择", savedName + "（" + savedAddress + "）");

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
        CustomAddressWindow.Show(current, delegate (string addr)
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
                EditorGUILayout.LabelField("场景列表", string.Join(", ", FPSGameBuild.GetEnabledScenes()));
                if (!string.IsNullOrEmpty(FPSGameBuild.LastBuildSummary))
                    EditorGUILayout.LabelField("上次出包", FPSGameBuild.LastBuildSummary);
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
                    Mirror.NetworkManager nm = FPSGameBuildEnvironment.FindAnyNetworkManager();
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
            string[] running = FPSGameBuild.GetRunningGameProcessNames();
            if (running.Length == 0)
                EditorGUILayout.LabelField("残留进程", "无");
            else
            {
                EditorGUILayout.LabelField("残留进程", string.Join(", ", running));
                if (GUILayout.Button("结束这些进程"))
                    FPSGameBuild.KillGameProcesses();
            }

            if (GUILayout.Button("打开输出目录"))
                MenuOpenBuildDir();

            // 输出场景地址和运行时覆盖来源
            if (GUILayout.Button("打印当前生效地址（选中 NetworkManager 后看 Console）"))
            {
                Mirror.NetworkManager nm = FPSGameBuildEnvironment.FindAnyNetworkManager();
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
                _onConfirm?.Invoke(_address);
                Close();
            }
            if (GUILayout.Button("取消"))
                Close();
        }
    }
}
