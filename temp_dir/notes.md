# 注意事项

## 必须知道的

1. **UniTask 是硬依赖**，不装会编译不过。
2. Prefab 必须放在 `Assets/Bundles` 下，否则工具扫不到。
3. 不要手动复制资源做“共享”，用 Label 或 SpriteAtlas。

## 容易踩坑

| 情况 | 处理方式 |
|------|----------|
| TMP 官方 Shader（TMP_SDF 等） | 放到 Always Included Shaders，不要打进 AB |
| 项目自有 Shader | 可以留在 Art，多引用时用 Set Shared Labels |
| 字体 | 放 `Bundles/Fonts`，和 TMP Shader 分开 |
| 同一字体不同字重互相引用 | 尽量打进同一个 AB，否则可能循环依赖卡死 |

## 打包后建议检查

1. 跑一次 `Check Shared Dependencies`，公用资源尽量都有 Label。
2. 真机反复进房/退房，用 Profiler 看 AssetBundle 引用有没有只增不减。
3. 加载过程中切场景，确认不会留下泄漏。

## OpenUPM 更新

改完代码后：

1. 更新 `package.json` 的 version
2. 提交并打 tag（如 `1.0.1`）
3. push tags，等 OpenUPM 同步即可
