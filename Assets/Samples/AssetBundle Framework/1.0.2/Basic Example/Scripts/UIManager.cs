using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 模块化 UI 管理器。
/// 实例化 Prefab 后，根据名字反射查找对应 UIBase 子类并 AddComponent。
/// 约定：Prefab 名 = 类名，例如 UI_Home.prefab → UI_Home : UIBase
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("不填则自动找 MainCanvas 或场景第一个 Canvas")]
    public Canvas mainCanvas;

    // 已打开的面板：key = 小写名字
    readonly Dictionary<string, UIBase> _opened = new();
    readonly Dictionary<string, string> _bundleOf = new(); // key → bundle

    // 缓存：panelName → Type（只扫一次）
    static Dictionary<string, Type> _typeCache;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (mainCanvas == null) mainCanvas = FindMainCanvas();
        BuildTypeCache();
    }

    /// <summary>
    /// 打开界面。
    /// name 同时作为 Prefab 名、脚本类名、bundle 后缀。
    /// 例：OpenAsync("UI_Home") → bundle=ui/ui_home, asset=UI_Home, AddComponent&lt;UI_Home&gt;
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

        // 3. 按名字找对应脚本并 AddComponent
        var ui = AttachUIBase(go, name);
        if (ui == null)
        {
            Debug.LogError($"[UI] 找不到对应脚本: {name}（请确认存在 public class {name} : UIBase）");
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
            if (!ui.IsClosed)
            {
                // 防止递归：先标记再调 OnClose
                // （UIBase.Close 已经处理了）
            }
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

    /// <summary>根据名字反射找 Type 并 AddComponent</summary>
    UIBase AttachUIBase(GameObject go, string panelName)
    {
        // 已挂过就直接返回
        var existing = go.GetComponent<UIBase>();
        if (existing != null) return existing;

        if (_typeCache == null) BuildTypeCache();

        if (!_typeCache.TryGetValue(panelName, out var type))
        {
            // 再尝试一次全程序集搜索（防止热更后缓存过期）
            type = FindTypeByName(panelName);
            if (type != null) _typeCache[panelName] = type;
        }

        if (type == null || !typeof(UIBase).IsAssignableFrom(type))
            return null;

        return go.AddComponent(type) as UIBase;
    }

    static void BuildTypeCache()
    {
        _typeCache = new Dictionary<string, Type>(StringComparer.Ordinal);
        var baseType = typeof(UIBase);

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            catch { continue; }

            if (types == null) continue;

            foreach (var t in types)
            {
                if (t == null || t.IsAbstract || !baseType.IsAssignableFrom(t)) continue;
                // 只用类名（不含命名空间）作为 key
                _typeCache[t.Name] = t;
            }
        }
    }

    static Type FindTypeByName(string name)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(name);               // 无命名空间
            if (t != null && typeof(UIBase).IsAssignableFrom(t)) return t;

            // 带命名空间的情况（可选）
            foreach (var type in asm.GetTypes())
            {
                if (type.Name == name && typeof(UIBase).IsAssignableFrom(type))
                    return type;
            }
        }
        return null;
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