# AB包管理器（FPSGame-Y1）

运行时管理器及其单例基类位于共用资源模块。 [AssetBundleManager](../Assets/Scripts/Shared/Assets/AssetBundleManager.cs) · [SingletonAutoMono](../Assets/Scripts/Shared/Assets/SingletonAutoMono.cs)

> **给 AI 的调用约定**：涉及 AB 包加载/卸载/缓存的改动时，先读本文件再动 `AssetBundleManager.cs`。
> 本文件描述的是**当前代码的实际行为**，不是设想；改了代码要回来同步本文件。

---

## 1. 异步加载

### 1.1 为什么用 async/await 而不是协程

协程有返回值传递麻烦、异常无法被外层 `try/catch` 捕获、依赖链嵌套难读三个硬伤，而 AB 加载这三样全占。所以本管理器**只保留异步路径**，旧的同步 `LoadAB`/`LoadResource` 和协程版 `LoadResourceAsync` 已全部删除。

### 1.2 两层异步，不是一回事

| 层 | 载体 | 恢复时机 | 代码位置 |
|---|---|---|---|
| 底层 I/O | `AsyncOperation`（`AssetBundleCreateRequest`、`AssetBundleRequest`） | **同一帧**内完成即恢复（`Awaitable`，池化零分配） | `OwnerLoadAsync()` 里 `await req;` |
| 业务等待点 | `Task`（`TaskCompletionSource`） | 投递回 `UnitySynchronizationContext`，**下一帧 Update** 才恢复 | `BundleHandle.Tcs` |

**结论**：每个包加载完成后，调用方比实际操作晚约 1 帧被唤醒；依赖链上每 `await` 一次 `Task` 约 +1 帧。进图有 Loading 兜底，无感。

### 1.3 句柄里为什么存 `Task` 而不是 `Awaitable`

```csharp
public readonly TaskCompletionSource<AssetBundle> Tcs=new();
public Task<AssetBundle>Task =>Tcs.Task;
```

`Awaitable` 被 Unity 池化，**只能 `await` 一次**，重复 `await` 是未定义行为（异常或死锁）。而同一个包会被多个调用方同时等待，所以共享等待点必须是 `Task`（可被任意多个消费者 `await`）。`TaskCompletionSource` 还额外提供了"只完成一次"的幂等语义（`TrySetResult` / `TrySetException`）。

### 1.4 主包/依赖配置为什么是同步的

`EnsureManifest()` 用同步 `AssetBundle.LoadFromFile` 加载主包。主包只有 manifest 数据、几 KB，同步加载是亚毫秒级；更重要的是**同步 = 天然无竞态**，省掉一整套异步去重代码。异步只用在真正的资源包上。

---

## 2. AB 缓存

### 2.1 唯一账本

```csharp
//保存正在加载的AB句柄,防止重复加载
private Dictionary<string,BundleHandle>_handles=new();
```

**`_handles` 是唯一的加载状态账本**（历史上曾有一个 `ABDict` 与它并存，已删除）。它同时承担三件事：包名 → 已完成/在途包、引用计数、依赖列表。

包体本身的引用在 `BundleHandle.bundle`。同一个包在内存里**只有一份** `AssetBundle` 对象。

### 2.2 缓存命中的效果

- **包级**：再次请求同一个包 → 命中句柄 → 直接返回同一个 `Task`，不读文件。
- **资源级**：包已加载后，`bundle.LoadAsset<T>(name)` 同一个资源多次调用不会重复反序列化（Unity 在包内部缓存 Asset）。

### 2.3 卸载：`Unload(false)` vs `Unload(true)`

`DoUnload()` 里用的是 `Unload(false)`：

| | `Unload(false)`（当前使用） | `Unload(true)` |
|---|---|---|
| 包的序列化数据 | 释放 | 释放 |
| 已加载出来的 Asset | **保留在内存** | **立刻销毁** |
| 场景里正在用的对象 | 不受影响 | 材质变粉、贴图丢失、引用变 null |
| Asset 何时真正释放 | 只能等 `Resources.UnloadUnusedAssets()` | 立即 |

**用 `false` 的原因**：引用计数只能证明"没人再请求这个包"，**证明不了"场景里没人引用包里的资源"**（可能已经 `Instantiate` 出去了）。用 `true` 会把正在用的东西销毁。

`false` 的副作用：包卸了但内存不一定降——留下的 Asset 变成"孤儿"，只能靠 `UnloadUnusedAssets` 回收（[懒汉回收，不是即时]）。

### 2.4 内存真正回落的三步

```csharp
await AssetBundleManager.Instance.UnLoadAllAsync();   // 1. 等空闲 + 卸包（Unload(false)）
// 2. 保证场景里已经没有对象引用这些资源
Resources.UnloadUnusedAssets();                       // 3. 才真正回收
```

顺序不能反：场景对象还在的时候就 `UnloadUnusedAssets()`，有引用的资源回收不掉。

---

## 3. 引用计数

### 3.1 契约

| 动作 | 计数变化 |
|---|---|
| `LoadBundleAsync(name)` 首次 | 创建句柄，`RefCount = 1` |
| `LoadBundleAsync(name)` 再次 | `RefCount++` |
| `LoadAssetAsync` / `InstantiateAsync` | **不加**（走 `EnsureBundleAsync`；仅首次创建时记 1） |
| 加载依赖包 | 每个依赖 `RefCount++`（由本包持有） |
| `Release(name)` | `RefCount--`，归零才卸 |

`EnsureBundleAsync()` 只有 4 行，是"取资源不刷爆计数"的关键：

```csharp
private Task<AssetBundle> EnsureBundleAsync(string abName)
{
    if (_handles.TryGetValue(abName,out var exist))return exist.Task;
    return LoadBundleAsync(abName);
}
```

> ⚠️ 副作用：**首次取资源会把包常驻**（记 1 次引用），必须由 `Release` 或 `UnLoadAll` 释放。这是刻意保留旧版"加载即缓存"的语义，让迁移不改变行为。

### 3.2 归零后的三种分支（`Release()`）

| 情况 | 处理 |
|---|---|
| 归零 + 已加载成功 | `DoUnload()` → 卸包 + 级联释放依赖 |
| 归零 + **还在加载中** | 只标记 `PendingUnLoad = true`，**不能立刻卸**；等 `OwnerLoadAsync` 的 `finally` 发现标记后再卸 |
| 归零 + 加载失败过 | 从 `_handles` 移除（兜底；失败时 `catch` 已经移除过） |

`RefCount` 有负数保护：`if(h.RefCount>0)h.RefCount--;`

### 3.3 级联释放

`DoUnload()` 卸完自己后，对它记录的每个依赖调 `Release(deps[i])`。**菱形依赖靠引用计数兜底**：C 被 A 和 B 同时依赖时 `RefCount = 2`，只有 A、B 都卸了才会真卸 C。

### 3.4 引用计数**管不住**什么

计数只反映"包被声明持有的次数"，**不反映场景里有多少实例在引用包里的资源**。所以：

- 不能因为 `RefCount == 0` 就认为可以 `Unload(true)`；
- 已经 `Instantiate` 到场景的物体，其资源在 `Unload(false)` 后依然有效（这正是我们用 `false` 的意义）。

### 3.5 推荐粒度

**进图预载（+1）→ 整局随意取资源 → 出图 `Release` 或 `UnLoadAllAsync`。**

不要按"取一次资源 Release 一次"来管理——那会让包在资源还没加载完子对象时就被卸掉。

---

## 4. 依赖管理

### 4.1 `GetAllDependencies` 已经是递归全集

```csharp
var mf=EnsureManifest();
var deps=mf.GetAllDependencies(handle.Name);   // 含"依赖的依赖"，不需要自己递归
handle.Deps=deps;
```

`AssetBundleManifest.GetAllDependencies(name)` 返回**递归展开的全部依赖**（区别于 `GetDirectDependencies` 只返回直接依赖）。所以这里**不要再自己递归**，否则会重复请求（虽然会被去重挡住，但白跑一圈）。

### 4.2 依赖并发加载

```csharp
//记录每个依赖包加载任务
Task<AssetBundle>[]depTask=new Task<AssetBundle>[deps.Length];
for (int i=0;i<deps.Length;i++)
{
    acquiredDeps.Add(deps[i]);
    depTask[i]=LoadBundleAsync(deps[i]);
}
//挂起当前方法等待所有任务执行完毕
await Task.WhenAll(depTask);
```

- 依赖之间互不依赖，可以并发；
- `LoadBundleAsync` 是**同步登记**（登记路径上没有 `await`），所以这个 `for` 循环不会漏包、也不会重复包；
- `acquiredDeps` 一边登记一边记录，供失败回滚用。

### 4.3 依赖的引用计数怎么算

举例：A 依赖 B，B 依赖 C（`GetAllDependencies("A")` 返回 `[B, C]`）。

| 时刻 | B | C |
|---|---|---|
| A 加载时（A 循环里 +1；B 的 owner 里又 +1） | 1 | 2（A 一次 + B 一次） |
| A 卸载 → `Release(B)` → B 归零 → `DoUnload(B)` → `Release(C)`；同时 A 也直接 `Release(C)` | 0 → 卸 | 2 → 1 → 0 → 卸 |

结论：不泄漏，也不会提前卸。**依赖必须在依赖它的包之前加载完毕**，所以 `OwnerLoadAsync` 把依赖的 `await Task.WhenAll` 放在加载自己之前。

### 4.4 依赖失败的处理

`await Task.WhenAll(depTask)` 中任一依赖失败 → 抛异常 → 进 `catch`：

1. 摘除本包句柄；
2. 对 `acquiredDeps` 里**每一个**已加过计数的依赖调 `Release`（失败的依赖句柄早已被它自己的 `catch` 摘除，`Release` 是空操作）；
3. 标记本包 `TrySetException`。

### 4.5 主包/manifest 的特殊地位

- 主包由 `EnsureManifest()` **内部加载**，不进入 `_handles`，不参与引用计数；
- 手动调 `LoadBundleAsync(MainABName)` 会**同步抛** `InvalidOperationException`；
- 主包在 `UnLoadAll()` 里单独 `Unload(false)` 并清空 `mainAB`/`manifest` 字段。

---

## 5. 如何避免重复加载

### 5.1 三道闸门

```
LoadBundleAsync(abName)
  ├─ 闸门① _handles 命中 ────────→ RefCount++，返回同一个 Task（不再读文件）
  ├─ 闸门② abName == MainABName → 同步抛异常（主包走内部通道）
  └─ 闸门③ 空包名 ──────────────→ 同步抛 ArgumentException
        ↓ 都通过
      创建句柄 → _handles.Add → 启动 OwnerLoadAsync
```

### 5.2 关键不变量：登记临界区不能有 `await`

```csharp
BundleHandle handle=new BundleHandle { Name=abName, RefCount=1 };
//保存正在加载handle
_handles.Add(abName,handle);   // ★ 先登记
//正在加载资源计数
_inFlight++;
//“_”表示不需要这个返回值
_=OwnerLoadAsync(handle);      // ★ 后启动
```

`_handles.Add` 到 `_=OwnerLoadAsync(...)` 之间**绝不允许出现 `await`/`yield`**。Unity 是单线程主循环，只要这段是同步的，就不可能出现"两个并发请求都 miss"——一旦中间让出，两个请求会各自 `LoadFromFile` 同一个文件，Unity 直接报：

```
The AssetBundle 'xxx' can't be loaded because another AssetBundle with the same files is already loaded.
```

### 5.3 为什么不能写成 `handle.Task = OwnerLoadAsync(handle)`

`OwnerLoadAsync(handle)` 被调用时**会同步执行到第一个 `await` 才返回**。如果先启动再 `_handles.Add`，那么在"已经启动、还没登记"的窗口里，任何回头查字典的动作都会 miss → 重复加载。

`TaskCompletionSource` 的价值就在这里：**句柄在还没开始加载时就已经有一个合法的、可 `await` 的 `Task`**（状态是"未完成"），所以"先登记、后启动"的顺序是安全的。

### 5.4 主包不会重复加载

`EnsureManifest()` 用同步的 `if (manifest==null)` 判断。两个 `OwnerLoadAsync` 在同一帧里先后同步跑到这里，第一个加载完设好字段，第二个直接命中——**因为全程没有 `await`，不存在竞态**。

### 5.5 失败后可以重试

`OwnerLoadAsync` 的 `catch` 第一件事就是 `_handles.Remove(handle.Name)`。所以一次失败**不会永久污染**这个包名，下一次请求会重新建句柄、重新加载。

---

## 6. 加载失败如何处理

### 6.1 统一入口：`OwnerLoadAsync` 的 try/catch/finally

```csharp
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
```

四件事缺一不可：

| 步骤 | 为什么 |
|---|---|
| `_handles.Remove` | 否则该包名**永久不可用**（后续请求永远命中一个 faulted 的句柄） |
| 回滚依赖引用计数 | 依赖可能已经加载成功，不回滚就泄漏 |
| `Debug.LogError` | 日志统一前缀 `[AB]` |
| `TrySetException` | **所有等待者必须被唤醒**，漏掉就永久挂起 |

`finally` 里只对"加载成功"的句柄执行延迟卸载：`PendingUnLoad && Status == RanToCompletion`。

### 6.2 异常如何传播到调用方

`TrySetException(e)` 把异常装进 `handle.Task`。所有 `await LoadBundleAsync(...)` / `await LoadAssetAsync(...)` 的调用方**各自**在 `await` 那一行收到同一个异常，可以各自 `try/catch`。

```csharp
try
{
    var go = await AssetBundleManager.Instance.InstantiateAsync("map_01","MapRoot");
}
catch (System.Exception e)
{
    Debug.LogException(e);   // 加载失败的兜底 UI
}
```

### 6.3 哪些情况会抛异常

| 抛出点 | 类型 | 是否同步抛出 |
|---|---|---|
| `abName` 为空 | `ArgumentException` | ✅ 同步（`LoadBundleAsync` 不是 `async` 方法） |
| `abName == MainABName` | `InvalidOperationException` | ✅ 同步 |
| 主包文件缺失 | `IOException` | ❌ 在 Task 里 |
| 主包中没有 `AssetBundleManifest` | `IOException` | ❌ 在 Task 里 |
| 包体加载后 `assetBundle == null` | `IOException` | ❌ 在 Task 里 |
| 依赖包加载失败 | 依赖的异常原样上抛 | ❌ 在 Task 里 |
| **资源不存在 / 类型不匹配** | **不抛异常**，`LogError` + 返回 `null` | — |

最后一行是刻意的：**包级失败抛异常，资源级失败返回 null**。因为同一个包里"某个资源名写错"是单个调用方的问题，不该让整包失败。

### 6.4 同步路径的失败处理

`LoadAsset<T>()` / `InstantiatePrefab()` 走 `GetResidentBundle()`，包未常驻时：

```csharp
Debug.LogError($"[AB] 包未常驻，同步取资源不可用：{abName}，请先await LoadAssetAsync预载");
return null;
```

**返回 null + 报错，不抛异常**，调用方必须判空。

### 6.5 调用方三条禁令

1. **禁止** `LoadBundleAsync(...).Result` / `.Wait()` / `.GetAwaiter().GetResult()` —— 主线程死锁（续体要主线程才能跑，主线程又在等它）。
2. **禁止** `async void` 裸调（`Start` 除外）—— 异常会丢失。用 `async Task` 或包 `try/catch`。
3. **禁止** `await ... .ConfigureAwait(false)` —— 续体会落到线程池，之后调 Unity API 报 `can only be called from the main thread`。
