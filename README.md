# AssetBundleFramework

传统 AssetBundle + UniTask。  
带引用计数、加载去重、CancellationToken，减少频繁进退房时的资源泄漏。

## 安装

1. 安装本包（OpenUPM）：

```
openupm add com.setsuodu.assetbundleframework
```

2. **必须**再安装 UniTask：

```
https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
```

## 资源怎么放

```
Assets/
├── Bundles/          ← Prefab、字体、Atlas 只放这里（打包扫描根）
│   ├── UI/
│   ├── Characters/
│   ├── Props/
│   └── Fonts/
└── Art/              ← FBX、贴图、材质、自有 Shader 可以留在原处
```

简单规则：
- Prefab 只放 `Bundles`
- 不要复制资源，共享靠 Label / Atlas
- 美术只改资源和 Prefab，不手动设 Label

## 打包

菜单：`Tools/AssetBundle`

推荐直接点 **Build（+ 同步 StreamingAssets）**，它会自动按顺序执行：

```
Clean Labels → AutoFix Atlas → Set Labels → Set Shared Labels → Build → Clean Labels
```

常用检查：
- `Check Shared Dependencies`：看有没有多引用却没打 Label 的资源

## 运行时怎么用

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
    }
    catch (OperationCanceledException) { }
}
```

`using` 结束会自动减引用，取消时也会清理。

更多细节见 [使用说明](docs/usage.md)。

## 注意事项（容易踩坑）

- TMP 官方 Shader（TMP_SDF 等）请放到 **Project Settings → Graphics → Always Included Shaders**，不要打进 AB。
- 字体放 `Bundles/Fonts`，和 TMP Shader 分开。
- 同一字体族尽量打进同一个 AB，避免循环依赖。

完整注意事项见 [注意事项](docs/notes.md)。

## 代码位置

```
Assets/Editor/          # 打包相关
Assets/Scripts/AssetBundle/   # 运行时
```
