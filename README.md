# 传统 AssetBundle 框架（UniTask + 防泄漏版）

针对真机「高频进出房间 / 中途强行打断」场景重写，通过引用计数、加载去重、CancellationToken、孤儿 AB 清理，降低资源泄漏风险。

## 依赖

- **UniTask**（Cysharp）必须安装  
  Package Manager → Add package from git URL：  
  `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask`

## 目录

```
AssetBundleFramework/
├── Editor/
│   ├── ABBuilder.cs
│   └── ABDependencyChecker.cs   # 依赖冗余检测 + Common Atlas 自动压入
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

## 资源与依赖规则（公用显式 / 独享隐式）

### 核心原则

1. **运行时资源工作区：`Assets/Bundles/`**  
   Prefab、碎图、字体等参与打包的资源放这里。只对 `Bundles` 做依赖分析与 Set Labels。
2. **磁盘上每张图只有一份（GUID 唯一）**  
   禁止为「提阶」Copy/剪切出第二张同名图。Prefab 永远引用原路径。
3. **多引用（≥2 个 Prefab）→ 显式**  
   - Sprite：自动/半自动压入 **SpriteAtlas**（不改路径）  
   - Font / 共享 Material / Shader：单独 AB Label（不改路径）
4. **单引用 → 可隐式**  
   跟随引用它的 Prefab 进包即可。
5. **打包流程**  
   `依赖分析/AutoFix → Set Labels → Build AB → Clean Labels`

### 美术 SOP

- 在 `Assets/Bundles/UI/` 提交完整 `UI_xxx.prefab`。
- 碎图按职责放：`Bundles/Sprites/Common/...`、`Bundles/Sprites/Panels/Xxx/...` 或模块目录。
- **不建 Atlas、不设 AB Label**；改图只在原路径覆盖提交。
- 不要求为每个 Panel 区分「是否会被公用」——交给检测脚本。

### 程序 / Atlas 规则

| 层级 | 数量 | 内容 | 加载 |
|------|------|------|------|
| 全局 Common Atlas | 1 张 | 被 ≥2 个 Prefab 引用的碎图 | 启动或进主 UI 常驻 |
| 模块 Atlas | 白名单内 0～N | 按系统（Shop/Battle），**不按每个 Panel** | 进模块时加载 |
| 面板独有大图 | 尽量不建 Atlas | 单图隐式跟随 Prefab | 随界面 |

- 全项目 Atlas 建议控制在约 **5～12 张**，避免 30+ Panel = 30 张 Atlas。
- Background-Tiles 等大图可单独 Atlas 或不合集。

### 模块白名单（代码内配置）

在 `ABDependencyChecker.ModuleAtlasWhitelist` 中配置，例如：

```csharp
{ "Shop",   "Assets/Bundles/Sprites/Atlas_UI_Shop.spriteatlas" },
{ "Battle", "Assets/Bundles/Sprites/Atlas_UI_Battle.spriteatlas" },
```

未在白名单中的多引用碎图 → 进入全局 `Atlas_UI_Common`。

### 菜单

```
Tools/AssetBundle/
  ├── Check Shared Dependencies (仅报告)
  ├── AutoFix Shared → Common Atlas (不搬文件)
  └── Build ...
```

- **仅报告**：列出公用但无 Label 的资源 → `AB_SharedDependency_Report.txt`
- **AutoFix**：把多引用 Sprite **Add** 进 Common/模块 Atlas（API，不 Move/Copy 文件）

### 建议打包顺序

1. `AutoFix Shared → Common Atlas`（或 CI 调用 `ABDependencyChecker.AutoFixShared()`）
2. Set Labels（给 Prefab / Atlas / 共享 Font 等打 Label）
3. Build AssetBundles
4. Clean Labels

### 3D / 动画 / Shader（同一原则）

- Humanoid 公用动画、多角色共用贴图 → 显式 common 包  
- Shader 默认按公用处理（独立 AB 或 Always Included）  
- 检测脚本同样会报告多引用的 `.anim` / `.mat` / `.shader`

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
