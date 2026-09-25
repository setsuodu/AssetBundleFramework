# 使用说明

## 初始化

游戏启动时调用一次：

```csharp
await ABManager.Instance.InitializeAsync();
```

编辑器默认走 AssetDatabase，真机走 AssetBundle。

## 推荐加载写法

```csharp
async UniTaskVoid OpenPanelAsync()
{
    var token = this.GetCancellationTokenOnDestroy();
    try
    {
        using (var handle = await ABManager.Instance.LoadBundleHandleAsync("ui/ui_home", token))
        {
            var prefab = await ABManager.Instance.LoadAssetAsync<GameObject>(
                "ui/ui_home", "UI_Home", token);
            // 使用 prefab
        }
        // using 结束自动释放
    }
    catch (OperationCanceledException)
    {
        // 切场景 / 销毁时被取消是正常的
    }
}
```

## 其他常用接口

```csharp
// 手动管理引用
await ABManager.Instance.LoadBundleAsync("ui/ui_home", token);
ABManager.Instance.UnloadBundle("ui/ui_home");

// 查引用计数
int count = ABManager.Instance.GetRefCount("ui/ui_home");
```

## 热更新（可选）

```csharp
bool ok = await ABUpdater.CheckAndUpdateAsync(
    progress: p => Debug.Log($"{p.progress:P0} {p.tip}"),
    token: destroyCancellationToken);
```
