# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-08-31

### Added

- Runtime AssetBundle manager with reference counting, load deduplication, and `CancellationToken` support (UniTask).
- Hot-update flow via `ABUpdater` driven by `ABConfig` / `StreamingAssets/ab_config.json` (optional remote JSON override).
- Editor tools under `Tools/AssetBundle` (shared dependency check, labels, build pipeline).
- Sample: `Samples~/BasicExample` boot flow (`ABConfig.LoadAsync` → update → load).

### Notes

- Requires UniTask (`com.cysharp.unitask`). Install via OpenUPM or Git URL.
- Place `ab_config.json` under the consuming project's `Assets/StreamingAssets/`.

## [1.0.1] - 2026-08-31

### Fixed

- Fix assetdatabase default loading path from editor to be false.

## [1.0.2] - 2026-08-31

### Added

- Add a Sample with UIManager, UIBase, UI_Login and UI_Home.

## [1.0.3] - 2026-08-31

### Fixed

- Fix Sample Prefab with C# Component.
