using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 完整使用示例：初始化 → 热更 → 加载各类资源 → 引用计数卸载
/// </summary>
public class ABExample : MonoBehaviour
{
    public ABUpdater updater;   // 场景里挂一个 ABUpdater，填好 remoteRoot

    void Start()
    {
        StartCoroutine(Boot());
    }

    IEnumerator Boot()
    {
        // 1. 热更新（可选）
        if (updater != null)
        {
            bool updated = false;
            yield return updater.CheckAndUpdate(
                (p, tip) => Debug.Log($"[Update] {tip}  {p:P0}"),
                hasUpdate => updated = hasUpdate);
            Debug.Log($"热更结果: {(updated ? "有更新" : "无更新")}");
        }

        // 2. 初始化管理器
        yield return ABManager.Instance.Initialize();

        // 3. 各类资源加载示例（bundle 名和 asset 名按你实际打包结果改）

        // Prefab
        yield return ABManager.Instance.LoadAssetAsync<GameObject>(
            "prefabs", "Player",
            go =>
            {
                if (go != null)
                {
                    Instantiate(go);
                    Debug.Log("Prefab OK");
                }
            });

        // Texture
        yield return ABManager.Instance.LoadAssetAsync<Texture2D>(
            "textures", "Background",
            tex => { if (tex != null) Debug.Log($"Texture {tex.width}x{tex.height}"); });

        // Material
        yield return ABManager.Instance.LoadAssetAsync<Material>(
            "materials", "StandardMat",
            mat => { if (mat != null) Debug.Log("Material OK"); });

        // Audio
        yield return ABManager.Instance.LoadAssetAsync<AudioClip>(
            "audios", "BGM",
            clip =>
            {
                if (clip != null)
                {
                    var src = gameObject.AddComponent<AudioSource>();
                    src.clip = clip;
                    src.Play();
                }
            });

        // UI Atlas / Sprite
        yield return ABManager.Instance.LoadAssetAsync<Sprite>(
            "ui_atlas", "icon_coin",
            sp =>
            {
                if (sp != null)
                {
                    var img = FindObjectOfType<Image>();
                    if (img) img.sprite = sp;
                }
            });

        // Shader
        yield return ABManager.Instance.LoadAssetAsync<Shader>(
            "shaders", "Custom/MyShader",
            sh => { if (sh != null) Debug.Log($"Shader {sh.name}"); });

        // Config (TextAsset)
        yield return ABManager.Instance.LoadAssetAsync<TextAsset>(
            "configs", "level_config",
            ta => { if (ta != null) Debug.Log($"Config len={ta.text.Length}"); });

        // 同步加载示例
        var font = ABManager.Instance.LoadAsset<Font>("fonts", "MyFont");
        if (font != null) Debug.Log("Font OK");

        // 4. 引用计数演示
        ABManager.Instance.LoadBundle("textures");
        ABManager.Instance.LoadBundle("textures");
        Debug.Log($"textures ref = {ABManager.Instance.GetRefCount("textures")}");

        ABManager.Instance.UnloadBundle("textures");
        Debug.Log($"unload once → {ABManager.Instance.GetRefCount("textures")}");

        ABManager.Instance.UnloadBundle("textures");
        Debug.Log($"unload twice → {ABManager.Instance.GetRefCount("textures")} (应归零并真正卸载)");
    }
}
