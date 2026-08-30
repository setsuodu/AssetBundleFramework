using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

/// <summary>
/// 极简 UI 管理器
/// - 从 AB 加载 Prefab
/// - 自动挂到场景中的 MainCanvas
/// - 支持关闭 / 清理
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("可选：手动指定 Canvas，不填则自动找名字为 MainCanvas 的")]
    public Canvas mainCanvas;

    // 已打开的界面：key = bundleName + "/" + assetName
    private readonly Dictionary<string, GameObject> _opened = new Dictionary<string, GameObject>();
    // 对应的 AB 引用，方便关闭时减引用
    private readonly Dictionary<string, string> _bundleMap = new Dictionary<string, string>();

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (mainCanvas == null)
            mainCanvas = FindMainCanvas();
    }

    Canvas FindMainCanvas()
    {
        // 优先找名字精确匹配的
        var go = GameObject.Find("MainCanvas");
        if (go != null)
        {
            var c = go.GetComponent<Canvas>();
            if (c != null) return c;
        }

        // 退而求其次：场景里第一个 Canvas
        var any = FindObjectOfType<Canvas>();
        if (any != null)
        {
            Debug.LogWarning("[UIManager] 未找到 MainCanvas，使用场景中第一个 Canvas: " + any.name);
            return any;
        }

        Debug.LogError("[UIManager] 场景中没有任何 Canvas！");
        return null;
    }

    /// <summary>
    /// 打开界面
    /// </summary>
    /// <param name="bundleName">AB 逻辑名，例如 "ui/ui_home"</param>
    /// <param name="assetName">AB 内资源名，例如 "UI_Home"</param>
    public async UniTask<GameObject> OpenAsync(string bundleName, string assetName, CancellationToken token = default)
    {
        string key = $"{bundleName}/{assetName}".ToLowerInvariant();

        if (_opened.TryGetValue(key, out var exist) && exist != null)
        {
            exist.SetActive(true);
            return exist;
        }

        if (mainCanvas == null)
        {
            mainCanvas = FindMainCanvas();
            if (mainCanvas == null) return null;
        }

        Debug.Log($"<color=cyan>[UIManager] 开始 LoadAssetAsync: {bundleName} / {assetName}</color>");

        GameObject prefab = null;
        try
        {
            prefab = await ABManager.Instance.LoadAssetAsync<GameObject>(bundleName, assetName, token);
            Debug.Log($"<color=cyan>[UIManager] LoadAssetAsync 返回了, prefab={(prefab != null ? prefab.name : "null")}</color>");
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("[UIManager] LoadAssetAsync 被取消");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError("[UIManager] LoadAssetAsync 抛异常:");
            Debug.LogException(e);
            return null;
        }

        if (prefab == null)
        {
            Debug.LogError($"[UIManager] 加载失败: {bundleName} / {assetName}");
            var ab = ABManager.Instance.GetLoadedBundle(bundleName);
            if (ab != null)
            {
                foreach (var n in ab.GetAllAssetNames())
                    Debug.Log("  AB内资源: " + n);
            }
            else
            {
                Debug.LogError("Bundle 本身都没加载成功");
            }
            return null;
        }

        var go = Instantiate(prefab, mainCanvas.transform, false);
        go.name = assetName;

        _opened[key] = go;
        _bundleMap[key] = bundleName;

        Debug.Log($"<color=green>[UIManager] 打开界面成功: {key}</color>");
        return go;
    }

    /// <summary>
    /// 关闭界面（销毁 + 减 AB 引用）
    /// </summary>
    public void Close(string bundleName, string assetName)
    {
        string key = $"{bundleName}/{assetName}".ToLowerInvariant();

        if (_opened.TryGetValue(key, out var go) && go != null)
        {
            Destroy(go);
        }
        _opened.Remove(key);

        if (_bundleMap.TryGetValue(key, out var bName))
        {
            ABManager.Instance.UnloadBundle(bName);
            _bundleMap.Remove(key);
        }

        Debug.Log($"[UIManager] 关闭界面: {key}");
    }

    /// <summary>
    /// 关闭所有已打开界面
    /// </summary>
    public void CloseAll()
    {
        foreach (var kv in _opened)
        {
            if (kv.Value != null)
                Destroy(kv.Value);
        }
        _opened.Clear();

        foreach (var bName in _bundleMap.Values)
            ABManager.Instance.UnloadBundle(bName);
        _bundleMap.Clear();

        Debug.Log("[UIManager] 已关闭所有界面");
    }
}