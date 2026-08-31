# Basic Example

1. Install **UniTask** and this package.
2. Add `Assets/StreamingAssets/ab_config.json` (see package README).
3. Scene setup:
   - Empty GameObject + `ABManager`
   - Empty GameObject + `ABUpdater` (optional for hot-update)
   - Optional: attach `ABExample` for ContextMenu tests
4. Enter Play Mode and use Context Menu on `ABExample`, or call:

```csharp
await ABConfig.LoadAsync(token);
await ABManager.Instance.InitializeAsync(token);
```

`UIManager` is **not** shipped in this package. If `ABExample` references it, either remove those lines or provide your own UI manager in the project.
