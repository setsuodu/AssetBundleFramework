# AssetBundle Framework

Traditional **AssetBundle** loading with **UniTask**: reference counting, load deduplication, `CancellationToken`, and hot-update via `StreamingAssets/ab_config.json`.

## Requirements

- Unity **2022.3** or newer (see `package.json` → `unity`)
- [UniTask](https://github.com/Cysharp/UniTask) (`com.cysharp.unitask`)

### Install UniTask first

**OpenUPM**

```bash
openupm add com.cysharp.unitask
```

Or add scoped registry `https://package.openupm.com` with scope `com.cysharp`, then install in Package Manager.

**Git URL**

```
https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
```

## Install this package

**Git URL**

```
https://github.com/setsuodu/AssetBundleFramework.git?path=Packages/com.setsuodu.assetbundleframework
```

Optional tag:

```
https://github.com/setsuodu/AssetBundleFramework.git?path=Packages/com.setsuodu.assetbundleframework#1.0.0
```

**OpenUPM** (after published)

```bash
openupm add com.setsuodu.assetbundleframework
```

## Quick start

1. Create `Assets/StreamingAssets/ab_config.json`:

```json
{
  "remoteRoot": "http://127.0.0.1/AssetBundles",
  "configRemoteUrl": "",
  "enableHotUpdate": true,
  "timeoutSeconds": 30
}
```

2. Boot:

```csharp
var token = this.GetCancellationTokenOnDestroy();
await ABConfig.LoadAsync(token);
await updater.CheckAndUpdateAsync(progress: null, token);
await ABManager.Instance.InitializeAsync(token);
```

3. Editor: menu **Tools / AssetBundle** to label and build bundles.  
   Default build output: `<Project>/Build/AssetBundles/<Platform>/`.

## Samples

Package Manager → this package → **Samples** → import **Basic Example**.

Note: the sample script focuses on AB boot flow. Project-specific UI helpers (e.g. `UIManager`) are not part of this package.

## License

MIT — see [LICENSE.md](LICENSE.md).

## Third-party

See [Third Party Notices.md](Third%20Party%20Notices.md) (UniTask).
