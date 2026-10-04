using System;
using System.IO;
using UnityEngine;
using Mirror;

/// <summary>
/// 启动时覆盖 NetworkManager.networkAddress。
///
/// 目的：同一份包既能连本地服务端、也能连腾讯云生产服，不需要为了换地址重新出包。
///
/// 覆盖优先级（先命中先用，命中后不再往低优先级找）：
///   1. 命令行参数  -serverAddr 127.0.0.1        （推荐用于快捷方式 / bat / 命令行自测）
///   2. 环境变量    FPSGAME_SERVER_ADDR
///   3. 配置文件    &lt;持久化目录&gt;/server_addr.txt   （改了立刻生效，不用重新打包）
///   4. 都不存在    → 用场景里 NetworkManager 上配的地址（什么都不做）
///
/// 本组件挂到场景里 NetworkManager 所在的物体上即可（和 KcpTransport 同一个物体）。
/// 只改 networkAddress，不碰端口 —— 端口仍然由 KcpTransport（7777）决定。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
public class NetworkAddressConfig : MonoBehaviour
{
    [Tooltip("关掉后本组件完全不生效，地址只由场景里 NetworkManager 的值决定。")]
    public bool enableOverride = true;

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

    // NetworkManager 的 Awake() 里会注册 singleton，本组件必须晚于它执行；
    // 但又要早于用户点 HUD 上的 Client 按钮（那是 StartClient），所以 Awake 正合适。
    void Awake()
    {
        // 先兜底：万一 NetworkManager 还没注册（脚本执行顺序被改过），自己找一次。
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

        ApplyOverride(nm);
    }

    /// <summary>
    /// 按优先级解析并应用地址覆盖。可在运行时手动调用（例如做调试按钮）。
    /// 返回 true 表示确实发生了覆盖。
    /// </summary>
    public bool ApplyOverride(NetworkManager nm)
    {
        if (!enableOverride || nm == null)
            return false;

        string source;
        string address = Resolve(out source);

        if (string.IsNullOrEmpty(address))
        {
            Debug.Log(string.Format(
                "[NetworkAddressConfig] 未发现地址覆盖，使用场景配置: {0}", nm.networkAddress));
            return false;
        }

        string previous = nm.networkAddress;
        nm.networkAddress = address;

        Debug.Log(string.Format(
            "[NetworkAddressConfig] 地址已覆盖 ({0}): {1} -> {2}",
            source, previous, address));
        return true;
    }

    /// <summary>按优先级解析地址；没有任何覆盖来源时返回 null，source 为 null。</summary>
    public string Resolve(out string source)
    {
        source = null;

        if (allowCommandLine)
        {
            string fromArgs = ReadCommandLineAddress();
            if (!string.IsNullOrEmpty(fromArgs))
            {
                source = "命令行 -serverAddr";
                return fromArgs;
            }
        }

        if (allowEnvironmentVariable)
        {
            string fromEnv = ReadEnvironmentAddress();
            if (!string.IsNullOrEmpty(fromEnv))
            {
                source = "环境变量 FPSGAME_SERVER_ADDR";
                return fromEnv;
            }
        }

        if (allowConfigFile)
        {
            string fromFile = ReadConfigFileAddress();
            if (!string.IsNullOrEmpty(fromFile))
            {
                source = "配置文件 " + ConfigFilePath;
                return fromFile;
            }
        }

        return null;
    }

    // -serverAddr 127.0.0.1   /   -serverAddr=127.0.0.1   两种写法都认
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

    private static string ReadEnvironmentAddress()
    {
        string v = Environment.GetEnvironmentVariable("FPSGAME_SERVER_ADDR");
        return string.IsNullOrEmpty(v) ? null : Sanitize(v);
    }

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

    // 去掉空白、引号、结尾的换行（bat 里 echo 会带 \r\n）
    private static string Sanitize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        string s = raw.Trim().Trim('"', '\'').Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    /// <summary>把地址写进配置文件（出包后也能改，无需重新打包）。</summary>
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

    /// <summary>删除配置文件，回到场景默认地址。</summary>
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

    // 右键组件 → 打印当前生效来源，排障用
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
