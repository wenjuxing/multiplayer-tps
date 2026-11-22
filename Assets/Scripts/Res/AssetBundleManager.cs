// 管理资源包的加载、依赖和引用释放
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// 保存单个资源包的加载任务、引用数和依赖
public class BundleHandle
{
    public string Name;
    public AssetBundle bundle;
    // 记录当前持有资源包的引用数
    public int RefCount;
    // 同名资源包的加载请求共用这个任务
    public readonly TaskCompletionSource<AssetBundle> Tcs=new();
    public Task<AssetBundle>Task =>Tcs.Task;
    // 记录加载该包时持有的依赖包
    public string[] Deps=System.Array.Empty<string>();
    // 标记加载完成后立即卸载
    public bool PendingUnLoad;
}

public class AssetBundleManager:SingletonAutoMono<AssetBundleManager>
{
    // 缓存存放依赖清单的主包
    private AssetBundle mainAB = null;
    // 缓存资源包依赖清单
    private AssetBundleManifest manifest = null;
    // 资源包目录需与编辑器构建输出一致
    private string PathUrl => Application.streamingAssetsPath+"/"+MainABName+"/";
    // 主包名与资源包目录名保持一致
    private const string MainABName = "PC";

    #region 异步加载AB包资源
    // 保存已加载或正在加载的资源包记录
    private Dictionary<string,BundleHandle>_handles=new();
    // 当前正在加载的资源包数
    private int _inFlight;
    public bool HasInFlight=>_inFlight>0;

    // 等待所有资源包结束加载
    public async Task WaitIdleAsync()
    {
        while(_inFlight>0)await Awaitable.NextFrameAsync();
    }
    // 加载资源包并为调用方保留一个引用
    public Task<AssetBundle> LoadBundleAsync(string abName)
    {
        // 检查包名，主包只由管理器内部加载
        if (string.IsNullOrEmpty(abName))
        {
            throw new System.ArgumentException("abName 不能为空", nameof(abName));
        }
        if (abName==MainABName)
        {
            throw new System.InvalidOperationException($"主包 {MainABName} 由管理器内部加载，请勿手动加载");
        }

        // 同名资源包已在记录中时复用加载任务并增加引用
        if (_handles.TryGetValue(abName,out var exist))
        {
            exist.RefCount++;
            return exist.Task;
        }

        // 为首次请求创建共享任务，并启动实际加载
        BundleHandle handle=new BundleHandle
        {
            Name=abName,
            RefCount=1
        };
        _handles.Add(abName,handle);
        _inFlight++;
        _=OwnerLoadAsync(handle);
        return handle.Task;
    }

    // 取用资源时复用现有任务，仅首次加载包时记录引用
    private Task<AssetBundle> EnsureBundleAsync(string abName)
    {
        if (_handles.TryGetValue(abName,out var exist))return exist.Task;
        return LoadBundleAsync(abName);
    }

    // 加载依赖包和目标包，完成后通知等待的调用方
    private async Task OwnerLoadAsync(BundleHandle handle)
    {
        // 记录此次已取得引用的依赖，用于失败时释放
        List<string> acquiredDeps=new();
        try
        {
            // 从主包清单读取目标包的依赖
            var mf=EnsureManifest();
            var deps=mf.GetAllDependencies(handle.Name);
            handle.Deps=deps;

            // 先并行加载所有依赖包，再加载目标包
            if (deps.Length>0)
            {
                Task<AssetBundle>[]depTask=new Task<AssetBundle>[deps.Length];
                for (int i=0;i<deps.Length;i++)
                {
                    acquiredDeps.Add(deps[i]);
                    depTask[i]=LoadBundleAsync(deps[i]);
                }
                await Task.WhenAll(depTask);
            }

            // 从文件异步加载目标包
            var req=AssetBundle.LoadFromFileAsync(PathUrl+handle.Name);
            await req;
            handle.bundle=req.assetBundle;

            // 读不到资源包时报告加载失败
            if (handle.bundle==null)
            {
                throw new System.IO.IOException($"AB 加载失败：{handle.Name}");
            }

            // 将目标包交给所有等待的调用方
            handle.Tcs.TrySetResult(handle.bundle);
        }
        catch (System.Exception e)
        {
            // 清理失败记录并归还已取得的依赖引用
            _handles.Remove(handle.Name);

            for (int i=0;i<acquiredDeps.Count;i++)
            {
                Release(acquiredDeps[i]);
            }
            Debug.LogError($"[AB] 加载失败：{handle.Name}\n{e}");

            // 将加载失败传给等待的调用方
            handle.Tcs.TrySetException(e);
        }
        finally
        {
            _inFlight--;
            // 加载期间已请求卸载时，在加载成功后执行
            if (handle.PendingUnLoad && handle.Tcs.Task.Status == TaskStatus.RanToCompletion)
                DoUnload(handle);
        }
    }
    // 首次使用时从主包读取依赖清单
    private AssetBundleManifest EnsureManifest()
    {
        if (manifest==null)
        {
            mainAB=AssetBundle.LoadFromFile(PathUrl+MainABName);
            // 主包不存在时报告具体路径
            if (mainAB==null)
            {
                throw new System.IO.IOException($"主包加载失败：{PathUrl+MainABName}");
            }
            manifest=mainAB.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
        }
        return manifest;
    }
    // 归还一个资源包引用，最后一个引用归还后安排卸载
    public void Release(string abName)
    {
        // 跳过空包名或未登记的资源包
        if(string.IsNullOrEmpty(abName))return;
        if(!_handles.TryGetValue(abName,out var h))return;

        // 仍有调用方持有引用时继续保留资源包
        if(h.RefCount>0)h.RefCount--;
        if(h.RefCount>0)return;

        // 正在加载时把卸载延后到加载完成
        if (!h.Tcs.Task.IsCompleted)
        {
            h.PendingUnLoad=true;
            return;
        }

        // 加载失败或取消时清除无效记录
        if (h.Tcs.Task.IsFaulted||h.Tcs.Task.IsCanceled)
        {
            _handles.Remove(abName);
            return;
        }
        DoUnload(h);
    }
    // 卸载资源包并归还依赖包引用
    public void DoUnload(BundleHandle handle)
    {
        // 先移除资源包的缓存记录
        _handles.Remove(handle.Name);

        // 卸载包本身，保留已加载的资源对象
        var deps=handle.Deps;
        handle.Deps=System.Array.Empty<string>();
        handle.bundle?.Unload(false);
        handle.bundle=null;

        // 逐个归还依赖包引用
        if(deps==null)return;
        for (int i=0;i<deps.Length;i++)
        {
            Release(deps[i]);
        }
    }
    #endregion

    #region 资源取用（新增）

    // 异步加载指定类型的资源
    public async Task<T> LoadAssetAsync<T>(string abName,string resName)where T:Object
    {
        // 复用现有包，首次取用时保留一个引用
        var bundle=await EnsureBundleAsync(abName);
        var req=bundle.LoadAssetAsync<T>(resName);
        await req;
        var asset=req.asset as T;
        if (asset==null)Debug.LogError($"[AB] 资源不存在或类型不匹配：{abName}/{resName}");
        return asset;
    }

    // 按传入的类型异步加载资源
    public async Task<Object> LoadAssetAsync(string abName,string resName,System.Type type)
    {
        var bundle=await EnsureBundleAsync(abName);
        var req=bundle.LoadAssetAsync(resName,type);
        await req;
        if (req.asset==null)Debug.LogError($"[AB] 资源不存在：{abName}/{resName}");
        return req.asset;
    }

    // 异步加载预制体并实例化到指定父节点
    public async Task<GameObject> InstantiateAsync(string abName,string resName,Transform parent=null)
    {
        var prefab=await LoadAssetAsync<GameObject>(abName,resName);
        return prefab==null?null:Instantiate(prefab,parent);
    }

    // 从已加载完成的资源包同步读取资源
    public T LoadAsset<T>(string abName,string resName)where T:Object
    {
        var bundle=GetResidentBundle(abName);
        return bundle==null?null:bundle.LoadAsset<T>(resName);
    }

    // 从已加载完成的资源包按指定类型读取资源
    public Object LoadAsset(string abName,string resName,System.Type type)
    {
        var bundle=GetResidentBundle(abName);
        return bundle==null?null:bundle.LoadAsset(resName,type);
    }

    // 从已加载完成的资源包同步实例化预制体
    public GameObject InstantiatePrefab(string abName,string resName,Transform parent=null)
    {
        var prefab=LoadAsset<GameObject>(abName,resName);
        return prefab==null?null:Instantiate(prefab,parent);
    }

    // 判断资源包是否已加载并保存在内存中
    public bool IsResident(string abName)=>_handles.TryGetValue(abName,out var h)&&h.bundle!=null;

    // 查找已加载的资源包，缺失时记录预加载提示
    private AssetBundle GetResidentBundle(string abName)
    {
        if(!string.IsNullOrEmpty(abName)&&_handles.TryGetValue(abName,out var h)&&h.bundle!=null)
            return h.bundle;
        Debug.LogError($"[AB] 包未常驻，同步取资源不可用：{abName}，请先await LoadAssetAsync预载");
        return null;
    }
    #endregion

    #region AB包资源卸载

    // 将指定资源包的引用清零并卸载
    public void UnLoad(string abName)
    {
        if(!_handles.TryGetValue(abName,out var h))return;

        // 强制清空引用计数
        h.RefCount=0;
        // 正在加载时标记为完成后卸载
        if (!h.Tcs.Task.IsCompleted)
        {
            h.PendingUnLoad=true;
            return;
        }
        DoUnload(h);
    }

    // 卸载全部资源包并清空管理记录
    public void UnLoadAll()
    {
        // 等待中的加载任务结束后才能卸载全部包
        if (_inFlight>0)
        {
            Debug.LogWarning($"[AB] 还有 {_inFlight} 个包在加载中，请改用 await UnLoadAllAsync()");
            return;
        }

        // 卸载资源包本身并保留已加载的资源对象
        AssetBundle.UnloadAllAssetBundles(false);
        // 清空资源包记录和主包缓存
        _handles.Clear();
        mainAB = null;
        manifest = null;
    }

    // 等待所有加载任务完成后卸载全部资源包
    public async Task UnLoadAllAsync()
    {
        await WaitIdleAsync();
        UnLoadAll();
    }
    #endregion
}
