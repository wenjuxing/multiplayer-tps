using UnityEngine;
using UnityEditor;
using System.IO;

public class BuildAssetBundles
{
    [MenuItem("Tools/Build AssetBundles")]
    public static void Build()
    {
        //拼接打包路径并创建目录
        //必须与 AssetBundleManager.PathUrl 一致：Application.streamingAssetsPath + "/PC/"
        //输出目录名即主包名，所以这里必须叫 PC
        string outDir=Path.Combine(Application.dataPath,"StreamingAssets","PC");
        Directory.CreateDirectory(outDir);
       
        //选择压缩方式
        var options=BuildAssetBundleOptions.ChunkBasedCompression|
        BuildAssetBundleOptions.AssetBundleStripUnityVersion;

        //构建AB包
        BuildPipeline.BuildAssetBundles(outDir,options,
        BuildTarget.StandaloneWindows64);

        AssetDatabase.Refresh();
        Debug.Log($"构建完成:{outDir}");
        EditorUtility.RevealInFinder(outDir);
    }
}
