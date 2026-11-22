// 管理构建场景中的服务器地址和编辑器环境选择。
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class FPSGameBuildEnvironment {
    // 沿用已有的环境偏好键和取值。
    private const string PrefEnvironment = "FPSGame_Y1.BuildEnv";
    private const string EnvLocal = "last";
    private const string EnvProduction = "production";

    // 将当前构建场景的网络管理器改为指定服务器地址
    internal static bool SwitchEnvironment(string address, bool verbose)
    {
        Scene scene = SceneManager.GetActiveScene();

        // 必要时询问是否保存改动，再打开启动场景
        if (scene.path != FPSGameBuild.BootScenePath && File.Exists(FPSGameBuild.ToAbsolute(FPSGameBuild.BootScenePath)) && !EditorApplication.isPlaying)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            scene = EditorSceneManager.OpenScene(FPSGameBuild.BootScenePath, OpenSceneMode.Single);
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

        return true;
    }

    // 读取上次选择供面板显示或重新应用。
    internal static (string Name, string Address) GetSavedEnvironment() {
        string key = EditorPrefs.GetString(PrefEnvironment, EnvProduction);
        string address = key == EnvLocal ? NetworkAddressConfig.LocalAddress : NetworkAddressConfig.ProductionAddress;
        return (EnvironmentDisplayName(key), address);
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
    internal static Mirror.NetworkManager FindAnyNetworkManager()
    {
        Mirror.NetworkManager[] inScene = FindNetworkManagers(SceneManager.GetActiveScene());
        if (inScene.Length > 0)
            return inScene[0];

        // 当前场景找不到时扩大查找范围，包括未启用对象
        return UnityEngine.Object.FindAnyObjectByType<Mirror.NetworkManager>(FindObjectsInactive.Include);
    }
}
