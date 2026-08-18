# 传统 AssetBundle 框架（UniTask + 防泄漏版）

针对真机「高频进出房间 / 中途强行打断」场景重写，通过引用计数、加载去重、CancellationToken、孤儿 AB 清理，降低资源泄漏风险。

## 依赖

- **UniTask**（Cysharp）必须安装  
  Package Manager → Add package from git URL：  
  `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask`

## 目录

```
AssetBundleFramework/
├── Editor/ABBuilder.cs
├── Scripts/AssetBundle/
│   ├── Data/ABManifest.cs
│   ├── ABPath.cs
│   ├── ABRef.cs          # 引用计数 + ABHandle（IDisposable）
│   ├── ABManager.cs      # UniTask 加载 / 去重 / Token / 级联计数
│   ├── ABUpdater.cs      # UniTask 热更
│   └── Example/ABExample.cs
├── link.xml              # IL2CPP 防裁
└── README.md
```

## 已修复的关键点

| 问题 | 处理 |
|------|------|
| 并发加载同一 AB 多次 | `_loadingTasks` 去重，共享同一个 UniTask |
| Cancel 时 AB 已创建未注册 | `catch OperationCanceledException` 里立刻 `Unload` |
| 无引用计数 / 依赖乱卸 | 级联 Retain / Release，归零才 Unload |
| 业务层孤儿异步 | 示例强制 `GetCancellationTokenOnDestroy()` |
| StreamingAssets 脏 / 路径 | 构建输出工程外，运行时 persistent → StreamingAssets |
| 编辑器与真机分裂 | 编辑器默认 AssetDatabase |

## 业务层正确写法（必须遵守）

```csharp
async UniTaskVoid OpenPanelAsync()
{
    var token = this.GetCancellationTokenOnDestroy();
    try
    {
        using (var handle = await ABManager.Instance.LoadBundleHandleAsync("ui_panel", token))
        {
            handle.BindTo(gameObject); // 可选
            var prefab = await ABManager.Instance.LoadAssetAsync<GameObject>("ui_panel", "Panel", token);
            // ...
        }
    }
    catch (OperationCanceledException)
    {
        // 退房 / 销毁，正常情况
    }
}
```

## 验收标准（建议真机跑）

1. 0.1s 内连续进房/退房 10 次  
2. Profiler 对比 Snapshot A（主界面）与 Snapshot C（恢复后）  
3. 通过条件：
   - `SerializedFile` 数量 Delta ≈ 0  
   - UniTaskTracker 无残留的 `LoadBundleAsync`  
   - 无泄漏的 `ABRef` 实例  

## 打包

菜单：`Tools/AssetBundle/Build (当前平台 + 同步 StreamingAssets)`

产物在 `Project/Build/AssetBundles/{平台}/`（干净，无 .meta），并可选同步到 StreamingAssets 作为首包。
