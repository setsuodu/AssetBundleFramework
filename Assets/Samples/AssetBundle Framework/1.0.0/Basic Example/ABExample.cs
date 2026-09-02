using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 右键 Inspector 快速测试。业务示例也参考这里。
/// </summary>
public class ABExample : MonoBehaviour
{
    public ABUpdater updater;

    void Start()
    {
        Menu_Boot();
    }

    [ContextMenu("0. Boot（配置→热更→Init→开 Home）")]
    void Menu_Boot() => BootAsync().Forget();

    [ContextMenu("1. 仅 Initialize")]
    void Menu_Init() => InitAsync().Forget();

    [ContextMenu("2. 打开 UI_Home")]
    void Menu_Home() => OpenUI("UI_Home").Forget();

    [ContextMenu("3. 打开 UI_Login")]
    void Menu_Login() => OpenUI("UI_Login").Forget();

    [ContextMenu("4. 测 BGM")]
    void Menu_BGM() => TestAudio("bgm/bgm07", "bgm07").Forget();

    [ContextMenu("5. 测角色")]
    void Menu_Char() => TestPrefab("characters/chibigirls_1", "ChibiGirls_1").Forget();

    [ContextMenu("6. 引用计数演示")]
    void Menu_Ref() => RefDemo().Forget();

    [ContextMenu("99. 关闭所有 UI + ReleaseAll")]
    void Menu_Cleanup()
    {
        UIManager.Instance?.CloseAll();
        ResManager.ReleaseAll();
    }

    // ---------- 实现 ----------

    async UniTaskVoid BootAsync()
    {
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            await ABConfig.LoadAsync(ct);
            if (updater != null)
                await updater.CheckAndUpdateAsync(null, ct);

            await ResManager.InitializeAsync(ct);
            await UIManager.Instance.OpenAsync("UI_Home", ct);
            Debug.Log("<color=green>[Example] Boot OK</color>");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Debug.LogException(e); }
    }

    async UniTaskVoid InitAsync()
    {
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            await ResManager.InitializeAsync(ct);
            Debug.Log($"[Example] version={ABManager.Instance.GetVersion()}");
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    async UniTaskVoid OpenUI(string name)
    {
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            if (!ResManager.IsReady) await ResManager.InitializeAsync(ct);
            var go = await UIManager.Instance.OpenAsync(name, ct);
            Debug.Log(go != null ? $"[Example] Open {name} OK" : $"[Example] Open {name} FAIL");
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    async UniTaskVoid TestAudio(string bundle, string asset)
    {
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            if (!ResManager.IsReady) await ResManager.InitializeAsync(ct);
            var clip = await ResManager.LoadAudioAsync(bundle, asset, ct);
            if (clip == null) { Debug.LogWarning($"[Example] Audio fail {bundle}/{asset}"); return; }

            var src = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.Play();
            Debug.Log($"[Example] Audio play {clip.name}");
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    async UniTaskVoid TestPrefab(string bundle, string asset)
    {
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            if (!ResManager.IsReady) await ResManager.InitializeAsync(ct);
            var prefab = await ResManager.LoadAsync<GameObject>(bundle, asset, ct);
            if (prefab == null) { Debug.LogWarning($"[Example] Prefab fail {bundle}/{asset}"); return; }

            var go = Instantiate(prefab);
            go.name = $"[Test] {asset}";
            Debug.Log($"[Example] Prefab {go.name}");
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    async UniTaskVoid RefDemo()
    {
        var ct = this.GetCancellationTokenOnDestroy();
        try
        {
            if (!ResManager.IsReady) await ResManager.InitializeAsync(ct);
            const string b = "bgm/bgm07";
            await ABManager.Instance.LoadBundleAsync(b, ct);
            await ABManager.Instance.LoadBundleAsync(b, ct);
            Debug.Log($"{b} ref={ResManager.GetRefCount(b)}");
            ResManager.Unload(b);
            Debug.Log($"unload once → {ResManager.GetRefCount(b)}");
            ResManager.Unload(b);
            Debug.Log($"unload twice → {ResManager.GetRefCount(b)}");
        }
        catch (Exception e) { Debug.LogException(e); }
    }
}
