# 传统 AssetBundle 完整框架（重构版）

针对早期常见问题（无引用计数、加载完立刻 Unload、StreamingAssets 脏、更新逻辑粗糙）做的完整重写。

## 目录

```
AssetBundleFramework/
├── Editor/
│   └── ABBuilder.cs                 # 打包（输出工程外干净目录 + 可选同步首包）
├── Scripts/AssetBundle/
│   ├── Data/ABManifest.cs           # Manifest 数据结构
│   ├── ABPath.cs                    # 路径（persistent 优先 → StreamingAssets）
│   ├── ABRef.cs                     # 引用计数
│   ├── ABManager.cs                 # 加载 / 卸载 / 依赖 / 编辑器直读
│   ├── ABUpdater.cs                 # 热更（version 快速判断 + Hash 差量）
│   └── Example/ABExample.cs         # 完整示例
└── README.md
```

## 设计要点

### 1. 打包
- 输出到 `Project/Build/AssetBundles/{平台}/`（工程外，无 .meta）
- 文件最终以 **hash.unity3d** 命名
- 生成 `manifest.json` + `version.txt`
- 可选一键同步完整当前版本到 `StreamingAssets/AssetBundles/{平台}/` 作为首包

### 2. 热更新
- 先比 `version.txt`，相同则直接跳过
- 不同再拉 `manifest.json`，按 hash 差量下载
- 下载写入 `persistentDataPath/AssetBundles/{平台}/`
- 下载完成后覆盖本地 manifest

### 3. 加载路径优先级
```
persistent（热更） → StreamingAssets（首包）
```
只选一个文件加载，不会两个都读。

### 4. 引用计数
- `LoadBundle` / `LoadAsset` 自动 Retain
- `UnloadBundle` 减引用，归零才真正 `Unload`
- 依赖会一起加/减引用

### 5. 编辑器
- 默认 **不走 AB**，直接 `AssetDatabase.LoadAssetAtPath`
- 需要测 AB 时定义宏 `USE_ASSETBUNDLE` 或设置 `ABManager.ForceUseAssetBundleInEditor = true`

## 使用步骤

1. 把 `Editor` 和 `Scripts` 放进工程
2. 资源放到 `Assets/Bundles/...` 并设置 AssetBundle 名（或自己扩展标记工具）
3. 菜单 `Tools/AssetBundle/Build (当前平台)`
4. 场景挂 `ABManager` + `ABUpdater`（填 remoteRoot）
5. 参考 `ABExample` 调用

## 与旧版 MoeFight 问题的对应修复

| 旧问题 | 本框架处理 |
|--------|------------|
| 加载完立刻 Unload(false) | 引用计数，归零才卸 |
| 依赖每次临时加载后立刻卸 | 依赖一起 Retain/Release |
| 只有 persistent，无首包回退 | persistent → StreamingAssets |
| 构建产物直接进 Assets 产生 .meta | 输出到工程外干净目录 |
| 更新无整包 version 快速跳过 | 先比 version，再 Hash 差量 |
| LoadAllAssets()[0] | 使用 LoadAsset&lt;T&gt;(name) |
| 编辑器与真机逻辑分裂 | 编辑器默认 AssetDatabase |

## 注意

- `JsonUtility` 对 `List` 支持良好，对 `Dictionary` 不支持，所以 Manifest 使用 `List&lt;ABInfo&gt;`。
- Android 上 StreamingAssets 不能直接 `File.Exists`，如需更严谨可改成 `UnityWebRequest` 探测或启动时把首包拷到 persistent。
- 生产环境建议给下载加 MD5 校验和失败重试。
