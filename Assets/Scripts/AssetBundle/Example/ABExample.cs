using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

/// <summary>
/// 右键 Inspector 测试各种资源加载
/// </summary>
public class ABExample : MonoBehaviour
{
    public ABUpdater updater;

    // ===================== 右键菜单 =====================

    [ContextMenu("0. 完整启动流程")]
    void Menu_Boot() => BootAsync().Forget();

    [ContextMenu("1. 仅初始化 ABManager")]
    void Menu_Init() => InitOnlyAsync().Forget();

    [ContextMenu("2. 打开 UI_Home")]
    void Menu_OpenHome() => OpenUIAsync("ui/ui_home", "UI_Home").Forget();

    [ContextMenu("3. 打开 UI_Login")]
    void Menu_OpenLogin() => OpenUIAsync("ui/ui_login", "UI_Login").Forget();

    [ContextMenu("4. 测试 BGM (bgm07)")]
    void Menu_TestBGM() => TestAudioAsync("bgm/bgm07", "bgm07").Forget();

    [ContextMenu("5. 测试 SFX")]
    void Menu_TestSFX() => TestAudioAsync("sfx/amg_cast_appear_06", "amg_cast_appear_06").Forget();

    [ContextMenu("6. 测试头像 Texture")]
    void Menu_TestHead() => TestTextureAsync("sprites/headimage/head1", "head1").Forget();

    [ContextMenu("7. 测试 Atlas")]
    void Menu_TestAtlas() => TestAtlasAsync("sprites/atlas_ui_common", "Atlas_UI_Common").Forget();

    [ContextMenu("8. 测试角色 Prefab")]
    void Menu_TestCharacter() => TestPrefabAsync("characters/chibigirls_1", "ChibiGirls_1").Forget();

    [ContextMenu("9. 测试道具 Prefab")]
    void Menu_TestProp() => TestPrefabAsync("props/smoothie_01", "Smoothie_01").Forget();

    [ContextMenu("10. 测试字体")]
    void Menu_TestFont() => TestFontAsync("fonts/dosis-bold", "Dosis-Bold").Forget();

    [ContextMenu("11. 测试特效")]
    void Menu_TestVFX() => TestPrefabAsync("vfx/nuclear explosion", "Nuclear Explosion").Forget();

    [ContextMenu("12. 引用计数演示")]
    void Menu_RefCount() => RefCountDemoAsync().Forget();

    [ContextMenu("13. 打印 AB 内资源名 (ui/ui_home)")]
    void Menu_DumpHome() => DumpBundleAssets("ui/ui_home").Forget();

    [ContextMenu("14. 打印 AB 内资源名 (ui/ui_login)")]
    void Menu_DumpLogin() => DumpBundleAssets("ui/ui_login").Forget();

    [ContextMenu("99. 关闭所有 UI")]
    void Menu_CloseAllUI()
    {
        Debug.Log("<color=yellow>===== 关闭所有 UI =====</color>");
        if (UIManager.Instance != null)
            UIManager.Instance.CloseAll();
        else
            Debug.LogWarning("UIManager.Instance 为空");
    }

    // ===================== 实现 =====================

    async UniTaskVoid BootAsync()
    {
        Debug.Log("<color=green>===== START 完整启动流程 =====</color>");
        var token = this.GetCancellationTokenOnDestroy();
        try
        {
            if (updater != null)
            {
                bool updated = await updater.CheckAndUpdateAsync(
                    Progress.Create<(float, string)>(p => Debug.Log($"[Update] {p.Item2} {p.Item1:P0}")),
                    token);
                Debug.Log($"热更: {(updated ? "有更新" : "无更新")}");
            }

            await ABManager.Instance.InitializeAsync(token);
            Debug.Log($"[AB] 版本: {ABManager.Instance.GetVersion()}");

            await UIManager.Instance.OpenAsync("ui/ui_home", "UI_Home", token);
            Debug.Log("<color=green>===== END 完整启动流程 =====</color>");
        }
        catch (OperationCanceledException)
        {
            Debug.Log("<color=orange>[ABExample] 被取消</color>");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    async UniTaskVoid InitOnlyAsync()
    {
        Debug.Log("<color=green>===== START 仅初始化 ABManager =====</color>");
        var token = this.GetCancellationTokenOnDestroy();
        try
        {
            await ABManager.Instance.InitializeAsync(token);
            Debug.Log($"[AB] 初始化完成 version={ABManager.Instance.GetVersion()}");
            Debug.Log("<color=green>===== END 仅初始化 =====</color>");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    async UniTaskVoid OpenUIAsync(string bundle, string asset)
    {
        Debug.Log($"<color=green>===== START 打开 UI: {bundle} / {asset} =====</color>");
        var token = this.GetCancellationTokenOnDestroy();
        try
        {
            await ABManager.Instance.InitializeAsync(token);

            if (UIManager.Instance == null)
            {
                Debug.LogError("UIManager.Instance 为空！请先在场景里挂 UIManager");
                return;
            }

            var go = await UIManager.Instance.OpenAsync(bundle, asset, token);
            if (go != null)
            {
                Debug.Log($"<color=green>打开成功: {go.name}</color>");
            }
            else
            {
                Debug.LogWarning($"打开失败，开始打印 AB 内资源名...");
                await DumpBundleAssets(bundle);
            }
            Debug.Log($"<color=green>===== END 打开 UI: {bundle} =====</color>");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    async UniTaskVoid TestAudioAsync(string bundle, string asset)
    {
        Debug.Log($"<color=green>===== START 测试音频: {bundle}/{asset} =====</color>");
        var token = this.GetCancellationTokenOnDestroy();
        try
        {
            await ABManager.Instance.InitializeAsync(token);
            var clip = await ABManager.Instance.LoadAssetAsync<AudioClip>(bundle, asset, token);
            if (clip != null)
            {
                var src = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
                src.clip = clip;
                src.Play();
                Debug.Log($"<color=green>[Audio] 播放成功: {clip.name}  length={clip.length:F1}s</color>");
            }
            else
            {
                Debug.LogWarning($"[Audio] 加载失败: {bundle}/{asset}");
                await DumpBundleAssets(bundle);
            }
            Debug.Log($"<color=green>===== END 测试音频 =====</color>");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    async UniTaskVoid TestTextureAsync(string bundle, string asset)
    {
        Debug.Log($"<color=green>===== START 测试 Texture: {bundle}/{asset} =====</color>");
        var token = this.GetCancellationTokenOnDestroy();
        try
        {
            await ABManager.Instance.InitializeAsync(token);
            var tex = await ABManager.Instance.LoadAssetAsync<Texture2D>(bundle, asset, token);
            if (tex != null)
                Debug.Log($"<color=green>[Texture] OK: {tex.name}  {tex.width}x{tex.height}</color>");
            else
            {
                Debug.LogWarning($"[Texture] 失败: {bundle}/{asset}");
                await DumpBundleAssets(bundle);
            }
            Debug.Log($"<color=green>===== END 测试 Texture =====</color>");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    async UniTaskVoid TestAtlasAsync(string bundle, string asset)
    {
        Debug.Log($"<color=green>===== START 测试 Atlas: {bundle}/{asset} =====</color>");
        var token = this.GetCancellationTokenOnDestroy();
        try
        {
            await ABManager.Instance.InitializeAsync(token);
            var atlas = await ABManager.Instance.LoadAssetAsync<SpriteAtlas>(bundle, asset, token);
            if (atlas != null)
                Debug.Log($"<color=green>[Atlas] OK: {atlas.name}</color>");
            else
            {
                Debug.LogWarning($"[Atlas] 失败: {bundle}/{asset}");
                await DumpBundleAssets(bundle);
            }
            Debug.Log($"<color=green>===== END 测试 Atlas =====</color>");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    async UniTaskVoid TestPrefabAsync(string bundle, string asset)
    {
        Debug.Log($"<color=green>===== START 测试 Prefab: {bundle}/{asset} =====</color>");
        var token = this.GetCancellationTokenOnDestroy();
        try
        {
            await ABManager.Instance.InitializeAsync(token);
            var prefab = await ABManager.Instance.LoadAssetAsync<GameObject>(bundle, asset, token);
            if (prefab != null)
            {
                var go = Instantiate(prefab);
                go.name = $"[Test] {asset}";
                Debug.Log($"<color=green>[Prefab] 实例化成功: {go.name}</color>");
            }
            else
            {
                Debug.LogWarning($"[Prefab] 失败: {bundle}/{asset}");
                await DumpBundleAssets(bundle);
            }
            Debug.Log($"<color=green>===== END 测试 Prefab =====</color>");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    async UniTaskVoid TestFontAsync(string bundle, string asset)
    {
        Debug.Log($"<color=green>===== START 测试字体: {bundle}/{asset} =====</color>");
        var token = this.GetCancellationTokenOnDestroy();
        try
        {
            await ABManager.Instance.InitializeAsync(token);
            var font = await ABManager.Instance.LoadAssetAsync<Font>(bundle, asset, token);
            if (font != null)
            {
                Debug.Log($"<color=green>[Font] OK: {font.name}</color>");
            }
            else
            {
                Debug.LogWarning($"[Font] 按 Font 类型失败，打印 AB 内容");
                await DumpBundleAssets(bundle);
            }
            Debug.Log($"<color=green>===== END 测试字体 =====</color>");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    async UniTaskVoid RefCountDemoAsync()
    {
        Debug.Log("<color=green>===== START 引用计数演示 =====</color>");
        var token = this.GetCancellationTokenOnDestroy();
        try
        {
            await ABManager.Instance.InitializeAsync(token);

            string b = "bgm/bgm07";
            await ABManager.Instance.LoadBundleAsync(b, token);
            await ABManager.Instance.LoadBundleAsync(b, token);
            Debug.Log($"{b} ref = {ABManager.Instance.GetRefCount(b)}");

            ABManager.Instance.UnloadBundle(b);
            Debug.Log($"unload once → {ABManager.Instance.GetRefCount(b)}");

            ABManager.Instance.UnloadBundle(b);
            Debug.Log($"unload twice → {ABManager.Instance.GetRefCount(b)}");

            Debug.Log("<color=green>===== END 引用计数演示 =====</color>");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    async UniTask DumpBundleAssets(string bundleName)
    {
        Debug.Log($"<color=cyan>===== DUMP {bundleName} =====</color>");
        var token = this.GetCancellationTokenOnDestroy();
        try
        {
            await ABManager.Instance.InitializeAsync(token);
            var ab = await ABManager.Instance.LoadBundleAsync(bundleName, token);
            if (ab == null)
            {
                Debug.LogError($"[Dump] 无法加载 Bundle: {bundleName}");
                return;
            }

            var names = ab.GetAllAssetNames();
            Debug.Log($"[Dump] {bundleName} 共 {names.Length} 个资源:");
            foreach (var n in names)
                Debug.Log("  → " + n);

            ABManager.Instance.UnloadBundle(bundleName);
            Debug.Log($"<color=cyan>===== END DUMP =====</color>");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}