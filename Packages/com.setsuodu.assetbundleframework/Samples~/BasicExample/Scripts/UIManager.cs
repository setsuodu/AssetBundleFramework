using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 模块化 UI 管理器。
/// Prefab 上已挂好 UIBase 子类脚本，组件通过 [SerializeField] 在 Inspector 拖好。
/// 实例化后只 GetComponent&lt;UIBase&gt;，不再反射 AddComponent。
/// 约定：Prefab 名 = 脚本类名，例如 UI_Home.prefab → UI_Home : UIBase
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("不填则自动找 MainCanvas 或场景第一个 Canvas")]
    public Canvas mainCanvas;

    // 已打开的面板：key = 小写名字
    readonly Dictionary<string, UIBase> _opened = new();
    readonly Dictionary<string, string> _bundleOf = new(); // key → bundle

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (mainCanvas == null) mainCanvas = FindMainCanvas();
    }

    /// <summary>
    /// 打开界面。
    /// name 同时作为 Prefab 名、脚本类名、bundle 后缀。
    /// 例：OpenAsync("UI_Home") → bundle=ui/ui_home, asset=UI_Home
    /// </summary>
    public async UniTask<GameObject> OpenAsync(string name, CancellationToken ct = default)
    {
        string key = name.ToLowerInvariant();

        // 已打开则直接显示
        if (_opened.TryGetValue(key, out var exist) && exist != null && !exist.IsClosed)
        {
            exist.gameObject.SetActive(true);
            return exist.gameObject;
        }

        if (mainCanvas == null)
        {
            mainCanvas = FindMainCanvas();
            if (mainCanvas == null) return null;
        }

        // 1. 加载 Prefab
        string bundle = $"ui/{key}";
        var prefab = await ResManager.LoadAsync<GameObject>(bundle, name, ct);
        if (prefab == null)
        {
            Debug.LogError($"[UI] 加载失败: {bundle}/{name}");
            return null;
        }

        // 2. 实例化
        var go = Instantiate(prefab, mainCanvas.transform, false);
        go.name = name;

        // 3. Prefab 上已挂好脚本，直接取
        var ui = go.GetComponent<UIBase>();
        if (ui == null)
        {
            Debug.LogError($"[UI] Prefab 上未挂 UIBase 脚本: {name}（请在 Prefab 上挂 public class {name} : UIBase）");
            Destroy(go);
            return null;
        }

        // 4. 记录并初始化
        _opened[key] = ui;
        _bundleOf[key] = bundle;
        ui.__Init(name);

        return go;
    }

    /// <summary>兼容旧签名</summary>
    public UniTask<GameObject> OpenAsync(string bundle, string asset, CancellationToken ct = default)
        => OpenAsync(asset, ct);

    public void Close(string name)
    {
        string key = name.ToLowerInvariant();
        if (_opened.TryGetValue(key, out var ui) && ui != null)
        {
            Destroy(ui.gameObject);
        }
        _opened.Remove(key);

        if (_bundleOf.TryGetValue(key, out var bundle))
        {
            ResManager.Unload(bundle);
            _bundleOf.Remove(key);
        }
    }

    public void Close(string bundle, string asset) => Close(asset);

    public void CloseAll()
    {
        foreach (var ui in _opened.Values)
            if (ui != null) Destroy(ui.gameObject);
        _opened.Clear();

        foreach (var bundle in _bundleOf.Values)
            ResManager.Unload(bundle);
        _bundleOf.Clear();
    }

    Canvas FindMainCanvas()
    {
        var go = GameObject.Find("MainCanvas");
        if (go != null)
        {
            var c = go.GetComponent<Canvas>();
            if (c != null) return c;
        }
        var any = FindObjectOfType<Canvas>();
        if (any != null)
        {
            Debug.LogWarning($"[UI] 未找到 MainCanvas，使用: {any.name}");
            return any;
        }
        Debug.LogError("[UI] 场景中没有 Canvas");
        return null;
    }
}
