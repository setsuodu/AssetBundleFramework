using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 正确示范：
/// - 必须传 GetCancellationTokenOnDestroy()
/// - try/catch OperationCanceledException
/// - 推荐用 ABHandle / using 自动释放
/// </summary>
public class ABExample : MonoBehaviour
{
    public ABUpdater updater;

    void Start()
    {
        BootAsync().Forget();
    }

    async UniTaskVoid BootAsync()
    {
        // 组件销毁时自动取消所有 await
        var token = this.GetCancellationTokenOnDestroy();

        try
        {
            // 1. 热更
            if (updater != null)
            {
                bool updated = await updater.CheckAndUpdateAsync(
                    Progress.Create<(float, string)>(p => Debug.Log($"[Update] {p.Item2} {p.Item1:P0}")),
                    token);
                Debug.Log($"热更: {(updated ? "有更新" : "无更新")}");
            }

            // 2. 初始化
            await ABManager.Instance.InitializeAsync(token);

            // 3. 推荐写法：Handle + BindTo，UI 销毁自动减引用
            using (var handle = await ABManager.Instance.LoadBundleHandleAsync("prefabs", token))
            {
                handle.BindTo(gameObject); // 可选，和 using 二选一即可
                var prefab = await ABManager.Instance.LoadAssetAsync<GameObject>("prefabs", "Player", token);
                if (prefab != null)
                    Instantiate(prefab);
            }

            // 4. 各类资源
            await LoadSamples(token);

            // 5. 引用计数演示
            await ABManager.Instance.LoadBundleAsync("textures", token);
            await ABManager.Instance.LoadBundleAsync("textures", token);
            Debug.Log($"textures ref = {ABManager.Instance.GetRefCount("textures")}");

            ABManager.Instance.UnloadBundle("textures");
            Debug.Log($"unload once → {ABManager.Instance.GetRefCount("textures")}");

            ABManager.Instance.UnloadBundle("textures");
            Debug.Log($"unload twice → {ABManager.Instance.GetRefCount("textures")}");
        }
        catch (OperationCanceledException)
        {
            // 退房 / 销毁时走到这里是正常的
            // ABManager 内部已对「加载出来但未注册」的孤儿 AB 做了 Unload
            Debug.Log("[ABExample] 加载被取消（组件销毁或主动 Cancel）");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    async UniTask LoadSamples(CancellationToken token)
    {
        var tex = await ABManager.Instance.LoadAssetAsync<Texture2D>("textures", "Background", token);
        if (tex != null) Debug.Log($"Texture {tex.width}x{tex.height}");

        var mat = await ABManager.Instance.LoadAssetAsync<Material>("materials", "StandardMat", token);
        if (mat != null) Debug.Log("Material OK");

        var clip = await ABManager.Instance.LoadAssetAsync<AudioClip>("audios", "BGM", token);
        if (clip != null)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.Play();
        }

        var sp = await ABManager.Instance.LoadAssetAsync<Sprite>("ui_atlas", "icon_coin", token);
        if (sp != null)
        {
            var img = FindObjectOfType<Image>();
            if (img) img.sprite = sp;
        }

        var shader = await ABManager.Instance.LoadAssetAsync<Shader>("shaders", "Custom/MyShader", token);
        if (shader != null) Debug.Log($"Shader {shader.name}");

        var config = await ABManager.Instance.LoadAssetAsync<TextAsset>("configs", "level_config", token);
        if (config != null) Debug.Log($"Config len={config.text.Length}");
    }
}
