using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

//AB包数据结构
public class BundleHandle
{
    public string Name;
    public AssetBundle bundle;
    public int RefCount;
    //共享点
    public readonly TaskCompletionSource<AssetBundle> Tcs=new();
    public Task<AssetBundle>Task =>Tcs.Task;
    //存储依赖包名
    public string[] Deps=System.Array.Empty<string>();
    //引用计数为0但还没加载完，完成后马上卸载
    public bool PendingUnLoad;
}

/// <summary>
/// AB包管理器
/// </summary>
public class AssetBundleManager:SingletonAutoMono<AssetBundleManager>
{
    //缓存主包
    private AssetBundle mainAB = null;
    //缓存主包中的依赖文件
    private AssetBundleManifest manifest = null;
    //★新增注释：AB包目录改为 StreamingAssets/PC/，与 BuildAssetBundles 的输出目录保持一致
    //缓存存储路径
    private string PathUrl => Application.streamingAssetsPath+"/"+MainABName+"/";
    //缓存主包名字
    //本项目只出 PC 端，AB 包统一放在 StreamingAssets/PC/（与 BuildAssetBundles 的输出目录一致）
    private const string MainABName = "PC";

    #region 异步加载AB包资源
    //保存正在加载的AB句柄,防止重复加载
    private Dictionary<string,BundleHandle>_handles=new();
    //正在加载数量
    private int _inFlight;
    //是否有正在加载的AB包
    public bool HasInFlight=>_inFlight>0;
    //加载AB包入口

    //等待所有正在加载的资源加载完毕（切换场景/全量卸载前使用）
    public async Task WaitIdleAsync()
    {
        while(_inFlight>0)await Awaitable.NextFrameAsync();
    }
    public Task<AssetBundle> LoadBundleAsync(string abName)
    {
        //防止包名为空或异常
        if (string.IsNullOrEmpty(abName))
        {
            throw new System.ArgumentException("abName 不能为空", nameof(abName));
        }
        if (abName==MainABName)
        {
            throw new System.InvalidOperationException($"主包 {MainABName} 由管理器内部加载，请勿手动加载");
        }

        //防止重复加载
        if (_handles.TryGetValue(abName,out var exist))
        {
            exist.RefCount++;
            return exist.Task;
        }

        BundleHandle handle=new BundleHandle
        {
            Name=abName,
            RefCount=1
        };
        //保存正在加载handle
        _handles.Add(abName,handle);
        //正在加载资源计数
        _inFlight++;
        //“_”表示不需要这个返回值
        _=OwnerLoadAsync(handle);
        return handle.Task;
    }

    //★新增：取资源专用入口。包已就绪时直接复用句柄，不再增加引用计数，
    //避免“取一次资源就加一次引用”；包不存在时才走LoadBundleAsync首次加载（记1次引用）
    private Task<AssetBundle> EnsureBundleAsync(string abName)
    {
        if (_handles.TryGetValue(abName,out var exist))return exist.Task;
        return LoadBundleAsync(abName);
    }

    //实际加载AB方法
    private async Task OwnerLoadAsync(BundleHandle handle)
    {
        //存储依赖包列表
        List<string> acquiredDeps=new();
        try
        {
            //记录ab包的所有依赖关系
            var mf=EnsureManifest();
            var deps=mf.GetAllDependencies(handle.Name);
            handle.Deps=deps;

            //加载依赖包
            if (deps.Length>0)
            {
                //记录每个依赖包加载任务
                Task<AssetBundle>[]depTask=new Task<AssetBundle>[deps.Length];
                for (int i=0;i<deps.Length;i++)
                {
                    acquiredDeps.Add(deps[i]);
                    depTask[i]=LoadBundleAsync(deps[i]);
                }
                //挂起当前方法等待所有任务执行完毕
                await Task.WhenAll(depTask);
            }

            //加载ab包
            var req=AssetBundle.LoadFromFileAsync(PathUrl+handle.Name);
            await req;
            handle.bundle=req.assetBundle;

            //异常处理
            if (handle.bundle==null)
            {
                throw new System.IO.IOException($"AB 加载失败：{handle.Name}");
            }

            //标记任务完成
            handle.Tcs.TrySetResult(handle.bundle);
        }
        catch (System.Exception e)
        {
            //移除加载记录
            _handles.Remove(handle.Name);

            //移除增加的引用计数
            for (int i=0;i<acquiredDeps.Count;i++)
            {
                Release(acquiredDeps[i]);
            }
            Debug.LogError($"[AB] 加载失败：{handle.Name}\n{e}");

            //标记任务失败
            handle.Tcs.TrySetException(e);
        }
        finally
        {
            _inFlight--;
            //引用计数为0，卸载ab包
            if (handle.PendingUnLoad && handle.Tcs.Task.Status == TaskStatus.RanToCompletion)
                DoUnload(handle);
        }
    }
    //加载依赖文件
    private AssetBundleManifest EnsureManifest()
    {
        if (manifest==null)
        {
            mainAB=AssetBundle.LoadFromFile(PathUrl+MainABName);
            //★新增：主包缺失时给出明确报错，避免下一行空引用
            if (mainAB==null)
            {
                throw new System.IO.IOException($"主包加载失败：{PathUrl+MainABName}");
            }
            manifest=mainAB.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
        }
        return manifest;
    }
    //释放资源
    public void Release(string abName)
    {
        //ab包包名为空或不在处理字典中
        if(string.IsNullOrEmpty(abName))return;
        if(!_handles.TryGetValue(abName,out var h))return;

        //减少引用计数，如果减少之后为0则卸载
        if(h.RefCount>0)h.RefCount--;
        if(h.RefCount>0)return;

        //有任务正在加载
        if (!h.Tcs.Task.IsCompleted)
        {
            h.PendingUnLoad=true;
            return;
        }

        //
        if (h.Tcs.Task.IsFaulted||h.Tcs.Task.IsCanceled)
        {
            _handles.Remove(abName);
            return;
        }
        DoUnload(h);
    }
    //卸载资源
    public void DoUnload(BundleHandle handle)
    {
        //从缓存中移除
        _handles.Remove(handle.Name);

        //卸载ab包
        var deps=handle.Deps;
        handle.Deps=System.Array.Empty<string>();
        handle.bundle?.Unload(false);//false：只卸载没有被使用的，保留场景中正常使用的ab包
        handle.bundle=null;

        //释放依赖包的引用计数
        if(deps==null)return;
        for (int i=0;i<deps.Length;i++)
        {
            Release(deps[i]);
        }
    }
    #endregion

    #region 资源取用（新增）

    /// <summary>
    /// 异步加载资源（不实例化；GameObject请用InstantiateAsync）
    /// 注意：首次取资源会把包常驻（引用计数记1），由Release或UnLoadAll释放
    /// </summary>
    public async Task<T> LoadAssetAsync<T>(string abName,string resName)where T:Object
    {
        //★新增：走EnsureBundleAsync，包已就绪就复用，不再加引用计数
        var bundle=await EnsureBundleAsync(abName);
        var req=bundle.LoadAssetAsync<T>(resName);
        await req;
        var asset=req.asset as T;
        if (asset==null)Debug.LogError($"[AB] 资源不存在或类型不匹配：{abName}/{resName}");
        return asset;
    }

    /// <summary>
    /// 异步加载资源 指定类型
    /// </summary>
    public async Task<Object> LoadAssetAsync(string abName,string resName,System.Type type)
    {
        var bundle=await EnsureBundleAsync(abName);
        var req=bundle.LoadAssetAsync(resName,type);
        await req;
        if (req.asset==null)Debug.LogError($"[AB] 资源不存在：{abName}/{resName}");
        return req.asset;
    }

    /// <summary>
    /// 异步加载并实例化GameObject（Prefab）
    /// </summary>
    public async Task<GameObject> InstantiateAsync(string abName,string resName,Transform parent=null)
    {
        var prefab=await LoadAssetAsync<GameObject>(abName,resName);
        return prefab==null?null:Instantiate(prefab,parent);
    }

    /// <summary>
    /// 同步取资源：仅当包已加载完成时可用（战斗内/对象池）
    /// 包未常驻会报错并返回null，请先await LoadAssetAsync预载
    /// </summary>
    public T LoadAsset<T>(string abName,string resName)where T:Object
    {
        var bundle=GetResidentBundle(abName);
        return bundle==null?null:bundle.LoadAsset<T>(resName);
    }

    /// <summary>
    /// 同步取资源 指定类型
    /// </summary>
    public Object LoadAsset(string abName,string resName,System.Type type)
    {
        var bundle=GetResidentBundle(abName);
        return bundle==null?null:bundle.LoadAsset(resName,type);
    }

    /// <summary>
    /// 同步实例化GameObject（战斗内/对象池）
    /// </summary>
    public GameObject InstantiatePrefab(string abName,string resName,Transform parent=null)
    {
        var prefab=LoadAsset<GameObject>(abName,resName);
        return prefab==null?null:Instantiate(prefab,parent);
    }

    /// <summary>
    /// 包是否已加载完成
    /// </summary>
    public bool IsResident(string abName)=>_handles.TryGetValue(abName,out var h)&&h.bundle!=null;

    //取常驻包，未常驻时报错返回null
    private AssetBundle GetResidentBundle(string abName)
    {
        if(!string.IsNullOrEmpty(abName)&&_handles.TryGetValue(abName,out var h)&&h.bundle!=null)
            return h.bundle;
        Debug.LogError($"[AB] 包未常驻，同步取资源不可用：{abName}，请先await LoadAssetAsync预载");
        return null;
    }
    #endregion

    #region AB包资源卸载

    /// <summary>
    /// 卸载单个包
    /// </summary>
    /// <param name="abName"></param>
    public void UnLoad(string abName)
    {
        if(!_handles.TryGetValue(abName,out var h))return;

        //★新增：强制卸载，先把引用计数清零（忽略还有谁在用）
        h.RefCount=0;
        //★新增：包还在加载中，先标记，等OwnerLoadAsync加载完成后自己卸载
        if (!h.Tcs.Task.IsCompleted)
        {
            h.PendingUnLoad=true;
            return;
        }
        DoUnload(h);
    }

    /// <summary>
    /// 卸载所有包
    /// </summary>
    public void UnLoadAll()
    {
        //★新增：有包在加载中时拒绝执行，否则加载完成的包会变成无人管理的野包
        if (_inFlight>0)
        {
            Debug.LogWarning($"[AB] 还有 {_inFlight} 个包在加载中，请改用 await UnLoadAllAsync()");
            return;
        }

        AssetBundle.UnloadAllAssetBundles(false);
        //★修改：原 ABDict.Clear() 改为清空句柄字典
        _handles.Clear();
        mainAB = null;
        manifest = null;
    }

    /// <summary>
    /// 全量卸载：先等在途加载结束（新增）
    /// </summary>
    public async Task UnLoadAllAsync()
    {
        await WaitIdleAsync();
        UnLoadAll();
    }
    #endregion
}
