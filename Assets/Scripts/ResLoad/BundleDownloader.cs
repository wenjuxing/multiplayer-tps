// 下载预制体资源包并实例化包中的第一个资源
using System.Collections;
using System.Net;
using System.Threading.Tasks;
using Mirror.BouncyCastle.Ocsp;
using UnityEngine;
using UnityEngine.Networking;

public class BundleDownloader : MonoBehaviour
{
    const string ServerIP = "43.142.103.57";
    const string Version  = "1.0.0";
    // 下载的资源包名
    const string Bundle   = "prefab";
    // 编辑器使用本地地址，出包后使用发布地址
#if UNITY_EDITOR
    const string BaseUrl = "http://127.0.0.1:8000/";
#else
    const string BaseUrl = "http://43.142.103.57/1.0.0/";
#endif
    // 启动资源包下载流程
    void Start()
    {
        StartCoroutine(Run());
    }
    // 检查缓存并下载、解析资源包
    IEnumerator Run()
    {
        // 读取资源清单中的版本哈希
        Hash128 hash=default;

        using (var req=UnityWebRequest.Get(BaseUrl+Bundle+".manifest"))
        {
            yield return req.SendWebRequest();
            if (req.result==UnityWebRequest.Result.Success)
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    req.downloadHandler.text,
                    @"AssetFileHash:[\s\S]*?Hash:\s*([0-9a-fA-F]{32})");
                if (m.Success) { hash = Hash128.Parse(m.Groups[1].Value); }
                Debug.Log($"[AB] 服务端 hash = {hash}");
            }
            else
            {
                Debug.LogWarning($"[AB] manifest 拉取失败：{req.error}（将无 hash 下载）");
            }
        }

        // 按哈希检查本地版本，下载请求会复用匹配的缓存
        bool cached=hash.isValid&&Caching.IsVersionCached(BaseUrl+Bundle,hash);
        Debug.Log(cached ? "[AB] 本地已是最新，读缓存" : "[AB] 需要下载…");

        // 请求指定版本的资源包，哈希不可用时直接下载
        using (var req=UnityWebRequestAssetBundle.GetAssetBundle(BaseUrl+Bundle,hash))
        {
            req.timeout=60;
            yield return req.SendWebRequest();
            
            // 请求失败时记录原因并结束流程
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[AB] 下载失败：{req.error}"); 
                yield break; 
            }

            // 从下载结果中读取资源包
            var bundle = DownloadHandlerAssetBundle.GetContent(req);
            if (bundle==null)
            {
                Debug.LogError("[AB] bundle 解析失败");
                yield break;
            }

            // 记录包内资源清单并加载第一个资源
            string[] names=bundle.GetAllAssetNames();
            Debug.Log($"[AB] 下载成功，资源数 = {names.Length}");
            foreach (var n in names) Debug.Log($"  资源路径: {n}");

            LoadABAsync(names[0],bundle);
            Debug.Log("异步加载中...");
        }
    }
    // 异步读取包中的预制体并实例化
    async Task LoadABAsync(string path,AssetBundle bundle)
    {
        var ab=bundle.LoadAssetAsync(path);
        await ab;
        GameObject obj=(GameObject)ab.asset;
        Instantiate(obj);
        Debug.Log("资源加载完毕");
    }
}
