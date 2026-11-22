// 从编辑器菜单构建 Windows 资源包
using UnityEngine;
using UnityEditor;
using System.IO;

public class BuildAssetBundles
{
    // 按项目约定构建资源包并打开输出目录
    [MenuItem("Tools/Build AssetBundles")]
    public static void Build()
    {
        // 创建输出目录，目录名 PC 同时是主包名
        string outDir=Path.Combine(Application.dataPath,"StreamingAssets","PC");
        Directory.CreateDirectory(outDir);
       
        // 使用分块压缩并移除资源包中的 Unity 版本信息
        var options=BuildAssetBundleOptions.ChunkBasedCompression|
        BuildAssetBundleOptions.AssetBundleStripUnityVersion;

        // 为 Windows 64 位构建资源包
        BuildPipeline.BuildAssetBundles(outDir,options,
        BuildTarget.StandaloneWindows64);

        // 刷新编辑器资源并显示输出目录
        AssetDatabase.Refresh();
        Debug.Log($"构建完成:{outDir}");
        EditorUtility.RevealInFinder(outDir);
    }
}
