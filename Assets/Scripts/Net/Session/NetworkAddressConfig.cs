// 在启动时按配置优先级覆盖服务器地址
using System;
using System.IO;
using UnityEngine;
using Mirror;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
public class NetworkAddressConfig : MonoBehaviour
{
    // 服务器地址覆盖总开关
    [Tooltip("关掉后本组件完全不生效，地址只由场景里 NetworkManager 的值决定。")]
    public bool enableOverride = true;

    // 启用可用的地址来源，按命令行、环境变量、配置文件的顺序尝试
    [Tooltip("允许命令行参数 -serverAddr <ip> 覆盖地址。")]
    public bool allowCommandLine = true;

    [Tooltip("允许环境变量 FPSGAME_SERVER_ADDR 覆盖地址。")]
    public bool allowEnvironmentVariable = true;

    [Tooltip("允许读取 <持久化目录>/server_addr.txt 覆盖地址。")]
    public bool allowConfigFile = true;

    /// <summary>本地测试常用地址。</summary>
    public const string LocalAddress = "127.0.0.1";

    /// <summary>腾讯云生产服地址。</summary>
    public const string ProductionAddress = "43.142.103.57";

    /// <summary>配置文件名（放在 Application.persistentDataPath 下）。</summary>
    public const string ConfigFileName = "server_addr.txt";

    /// <summary>配置文件的完整路径，出包后可以直接告诉玩家/自己改这个文件。</summary>
    public static string ConfigFilePath
    {
        get { return Path.Combine(Application.persistentDataPath, ConfigFileName); }
    }

    // 在组件初始化时查找网络管理器并应用地址
    void Awake()
    {
        // 网络管理器尚未注册时从当前对象或场景查找
        NetworkManager nm = NetworkManager.singleton;
        if (nm == null)
            nm = GetComponent<NetworkManager>();
        if (nm == null)
            nm = FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
        if (nm == null)
        {
            Debug.LogWarning("[NetworkAddressConfig] 场景里找不到 NetworkManager，地址覆盖跳过。");
            return;
        }

        // 在连接前应用启动时读取到的地址
        ApplyOverride(nm);
    }

    // 将解析到的服务器地址应用到网络管理器
    public bool ApplyOverride(NetworkManager nm)
    {
        if (!enableOverride || nm == null)
            return false;

        // 按优先级查找有效的覆盖地址
        string source;
        string address = Resolve(out source);

        if (address == null)
        {
            Debug.Log(string.Format(
                "[NetworkAddressConfig] 未发现地址覆盖，使用场景配置: {0}", nm.networkAddress));
            return false;
        }

        // 更新网络管理器的地址并记录覆盖来源
        string previous = nm.networkAddress;
        nm.networkAddress = address;

        Debug.Log(string.Format(
            "[NetworkAddressConfig] 地址已覆盖 ({0}): {1} -> {2}",
            source, previous, address));
        return true;
    }

    // 查找第一个有效的地址覆盖及其来源
    public string Resolve(out string source) {
        source = null;

        // 优先检查命令行参数
        string address = allowCommandLine ? ReadCommandLineAddress() : null;
        if (address != null) {
            source = "命令行 -serverAddr";
            return address;
        }

        // 命令行未给出有效地址时检查环境变量
        address = allowEnvironmentVariable
            ? Sanitize(Environment.GetEnvironmentVariable("FPSGAME_SERVER_ADDR"))
            : null;
        if (address != null) {
            source = "环境变量 FPSGAME_SERVER_ADDR";
            return address;
        }

        // 最后读取持久化目录中的配置文件
        address = allowConfigFile ? ReadConfigFileAddress() : null;
        if (address != null) {
            source = "配置文件 " + ConfigFilePath;
        }

        return address;
    }

    // 读取空格或等号形式的服务器地址参数
    private static string ReadCommandLineAddress()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];

            if (string.Equals(a, "-serverAddr", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                    return Sanitize(args[i + 1]);
            }
            else if (a.StartsWith("-serverAddr=", StringComparison.OrdinalIgnoreCase))
            {
                return Sanitize(a.Substring("-serverAddr=".Length));
            }
        }
        return null;
    }

    // 从本地配置文件读取服务器地址
    private static string ReadConfigFileAddress()
    {
        try
        {
            string path = ConfigFilePath;
            if (!File.Exists(path))
                return null;

            return Sanitize(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[NetworkAddressConfig] 读取配置文件失败: " + e.Message);
            return null;
        }
    }

    // 清理地址两端的空白和引号，统一用 null 表示缺少地址
    private static string Sanitize(string raw) {
        if (string.IsNullOrEmpty(raw)) {
            return null;
        }

        string address = raw.Trim().Trim('"', '\'').Trim();
        return address.Length == 0 ? null : address;
    }

    // 将处理后的地址写入本地配置文件
    public static bool WriteConfigFile(string address)
    {
        try
        {
            File.WriteAllText(ConfigFilePath, Sanitize(address) ?? string.Empty);
            Debug.Log("[NetworkAddressConfig] 已写入配置: " + ConfigFilePath);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[NetworkAddressConfig] 写配置失败: " + e.Message);
            return false;
        }
    }

    // 删除本地地址配置文件
    public static bool ClearConfigFile()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                File.Delete(ConfigFilePath);
                Debug.Log("[NetworkAddressConfig] 已删除配置: " + ConfigFilePath);
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[NetworkAddressConfig] 删除配置失败: " + e.Message);
            return false;
        }
    }

    // 在组件菜单中输出覆盖地址、来源和场景地址
    [ContextMenu("打印当前生效的服务器地址")]
    void LogEffectiveAddress()
    {
        string source = null;
        string resolved = Resolve(out source);
        NetworkManager nm = NetworkManager.singleton != null ? NetworkManager.singleton : GetComponent<NetworkManager>();

        Debug.Log(string.Format(
            "[NetworkAddressConfig] 覆盖值={0} 来源={1} | 场景值={2}",
            resolved ?? "(无)", source ?? "(无)", nm != null ? nm.networkAddress : "(找不到 NetworkManager)"));
    }
}
