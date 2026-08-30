# AssetBundleFramework

传统 AssetBundle + UniTask。引用计数、加载去重、CancellationToken，降低进房/退房打断导致的泄漏。

依赖：Package Manager 安装 UniTask  
`https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask`

---

## 目录规则

```
Assets/
├── Bundles/                 # 唯一打包扫描根 + Prefab 工作区
│   ├── UI/                  # UI Prefab
│   ├── Characters/          # 角色 Prefab
│   ├── Props/               # 道具 Prefab
│   ├── Environment/
│   ├── Fonts/               # 在用字体 + TMP Font Asset（.ttf / .asset）
│   └── Sprites/             # Atlas 等
└── Art/                     # 源资源（FBX / 贴图 / 材质 / 项目自有 Shader）
    ├── Characters/ChibiGirls/{FBX,Materials,Textures}
    ├── Props/FastFood/{FBX,Materials,Textures,Shaders}
    └── ...
```

| 规则 | 说明 |
|------|------|
| 单份资源 | 禁止 Copy 出第二份同 GUID 用途；改哪里以 Prefab 引用为准 |
| Prefab | 只放 `Bundles` |
| 模型/贴图/材质/自有 Shader | 可留在 `Art` 原路径，**不搬也能打共享 Label** |
| 在用字体 | 只放 `Bundles/Fonts`，不要 `Art/Fonts` |
| 美术 | 只改资源与 Prefab，不设 Label、不建 Atlas、不打包 |
| 程序 | 只跑菜单/自动化，不手工搬文件 |

**Art 组织：** 角色按系列（`Characters/HeroA/...`）；道具按品类/套装（`Props/FastFood/...`），不要顶层一个道具一个文件夹。

---

## 打包原则

- **多引用（≥2 个 Prefab）→ 显式共享 AB 或 Atlas**
- **单引用 → 可隐式跟随 Prefab**
- 不物理 Move/Copy；共享靠 Label / SpriteAtlas 引用原路径

---

## 工具菜单 `Tools/AssetBundle`

| 段 | 菜单 | 作用 |
|----|------|------|
| **0** | Check Shared Dependencies | 扫描 `Bundles` 内 Prefab 依赖，报告多引用且无 Label 的资源 |
| **50** | AutoFix Shared → Common Atlas | 多引用 Sprite 写入 `Atlas_UI_Common`（不搬文件） |
| **51** | Set Shared Labels (Art 依赖) | 多引用依赖（含 `Art`）按规则打共享 Label |
| **52** | Set Labels (Bundles 目录) | `Bundles` 下按相对路径设 Label，如 `ui/ui_home` |
| **53** | Clean Labels | 清除工程内全部 AB Name |
| **100** | Build（+ 同步 StreamingAssets） | 完整流水线并可选拷首包 |
| **101** | Build Only | 只构建，不同步 StreamingAssets |

### 一键 Build 顺序

```
Clean Labels
→ AutoFix（Sprite → Atlas）
→ Set Labels（Bundles）
→ Set Shared Labels（Art 等依赖）
→ BuildAssetBundles + manifest
→ Clean Labels
→ （可选）同步 StreamingAssets
```

### Bundles Label 规则

`Assets/Bundles/{相对路径/文件名}.ext` → `{相对路径/文件名}` 小写、去扩展名  

例：`Bundles/UI/UI_Home.prefab` → `ui/ui_home`

### 共享 Label 规则（可改脚本 `SharedLabelRules`）

| 路径包含 | Label |
|----------|--------|
| `/Characters/ChibiGirls/` | `characters/chibi_base` |
| `/Props/Fast Food/` 或 `/Props/FastFood/` | `props/fastfood_common` |
| `/UI/Animations/Common/` | `ui/anim_common` |
| `/UI/Sprites/Common/` | `ui/sprites_common` |
| `/Bundles/Fonts/` | `fonts/common` |

无命中时兜底：`shared/shaders`、`shared/materials`、`shared/{Art下模块}` 等。  
已在 Atlas 内的 Sprite、黑名单路径不打 Label。

报告输出：工程根目录 `AB_SharedDependency_Report.txt`

---

## 特殊情况（不要当普通业务资源乱打 Label）

| 资源 | 处理 |
|------|------|
| **TextMesh Pro 官方 Shader**（如 TMP_SDF） | **Project Settings → Graphics → Always Included Shaders**；黑名单跳过 Label；不要拷进 `Bundles` |
| **Packages / PackageCache** | 不打 Label |
| **Assets/Editor** | 不进 AB |
| **项目自有 Shader**（如 Glass） | 留 Art，多引用走 **Set Shared Labels** |
| **URP/HDRP/后处理等官方 Shader** | 优先 Always Included 或跟渲染管线默认，不靠某个 Prefab AB 隐式携带 |
| **全局几乎必用的业务 Shader** | Always Included，或极小的常驻 `shared/shaders` |

字体（自有 ttf + Font Asset）与 TMP **Shader** 分开：字体在 `Bundles/Fonts`；TMP Shader 走 Always Included。

---

## 运行时（业务必须）

```csharp
async UniTaskVoid OpenPanelAsync()
{
    var token = this.GetCancellationTokenOnDestroy();
    try
    {
        using (var handle = await ABManager.Instance.LoadBundleHandleAsync("ui/ui_home", token))
        {
            var prefab = await ABManager.Instance.LoadAssetAsync<GameObject>("ui/ui_home", "UI_Home", token);
            // ...
        }
    }
    catch (OperationCanceledException) { }
}
```

加载带依赖的 Prefab 前，确保其共享包已加载（或依赖清单自动加载，视 `ABManager` 实现）。

---

## 验收（真机）

1. 快速反复进房/退房  
2. Profiler：主界面 → 进房 → 回主界面，SerializedFile / AB 引用无明显只增不减  
3. 再跑 Check：公用无 Label 应为 0（或仅剩黑名单说明项）

---

## 代码位置

```
Assets/Editor/ABBuilder.cs              # Set/Clean Labels、Build
Assets/Editor/ABDependencyChecker.cs    # Check、AutoFix、Shared Labels
Assets/Scripts/AssetBundle/             # 运行时
```
