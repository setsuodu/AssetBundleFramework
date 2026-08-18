using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// AssetBundle 核心管理器
/// - 引用计数
/// - 依赖自动加载/卸载
/// - 路径优先级：persistent → StreamingAssets
/// - 编辑器下可直接走 AssetDatabase（不打 AB）
/// </summary>
public class ABManager : MonoBehaviour
{
    public static ABManager Instance { get; private set; }

    private ABManifest _manifest;
    private readonly Dictionary<string, ABRef> _loaded = new Dictionary<string, ABRef>();
    private bool _inited;

    /// <summary>
    /// 编辑器下是否强制使用 AssetBundle（默认 false，直接 AssetDatabase）
    /// 可在启动前设置，或用宏 USE_ASSETBUNDLE
    /// </summary>
    public static bool ForceUseAssetBundleInEditor = false;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    #region 初始化

    public IEnumerator Initialize(Action onComplete = null)
    {
        if (_inited)
        {
            onComplete?.Invoke();
            yield break;
        }

#if UNITY_EDITOR
        if (!ShouldUseAssetBundle())
        {
            _manifest = new ABManifest { version = "editor" };
            _inited = true;
            Debug.Log("[AB] 编辑器模式：直接使用 AssetDatabase，跳过 Manifest");
            onComplete?.Invoke();
            yield break;
        }
#endif

        string path = ABPath.GetManifestPath(true);
        if (!File.Exists(path))
        {
            // Android StreamingAssets 不能直接 File.Exists，需要特殊处理时可扩展
            Debug.LogWarning($"[AB] Manifest 不存在: {path}，请确认首包已放入 StreamingAssets 或已热更");
            _manifest = new ABManifest();
        }
        else
        {
            string json = File.ReadAllText(path);
            _manifest = JsonUtility.FromJson<ABManifest>(json);
            if (_manifest == null)
                _manifest = new ABManifest();
        }

        _inited = true;
        Debug.Log($"[AB] 初始化完成, version={_manifest.version}, bundles={_manifest.bundles?.Count ?? 0}");
        onComplete?.Invoke();
    }

    bool ShouldUseAssetBundle()
    {
#if UNITY_EDITOR
        if (ForceUseAssetBundleInEditor) return true;
#if USE_ASSETBUNDLE
        return true;
#else
        return false;
#endif
#else
        return true;
#endif
    }

    public string GetVersion() => _manifest?.version ?? "0";

    #endregion

    #region 加载 Bundle（带引用计数 + 依赖）

    /// <summary>
    /// 同步加载 Bundle（自动加载依赖并增加引用）
    /// </summary>
    public AssetBundle LoadBundle(string bundleName)
    {
        bundleName = Normalize(bundleName);

        if (_loaded.TryGetValue(bundleName, out var abRef))
        {
            abRef.Retain();
            return abRef.Bundle;
        }

        // 先加载依赖
        var info = _manifest?.Get(bundleName);
        if (info?.depends != null)
        {
            foreach (var dep in info.depends)
            {
                if (!string.IsNullOrEmpty(dep))
                    LoadBundle(dep);
            }
        }

        string filePath = ResolveBundlePath(bundleName, info);
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Debug.LogError($"[AB] 文件不存在: {bundleName} → {filePath}");
            return null;
        }

        AssetBundle ab = AssetBundle.LoadFromFile(filePath);
        if (ab == null)
        {
            Debug.LogError($"[AB] LoadFromFile 失败: {filePath}");
            return null;
        }

        abRef = new ABRef(bundleName, ab);
        abRef.Retain();
        _loaded[bundleName] = abRef;
        return ab;
    }

    public IEnumerator LoadBundleAsync(string bundleName, Action<AssetBundle> onComplete)
    {
        bundleName = Normalize(bundleName);

        if (_loaded.TryGetValue(bundleName, out var abRef))
        {
            abRef.Retain();
            onComplete?.Invoke(abRef.Bundle);
            yield break;
        }

        var info = _manifest?.Get(bundleName);
        if (info?.depends != null)
        {
            foreach (var dep in info.depends)
            {
                if (!string.IsNullOrEmpty(dep))
                    yield return LoadBundleAsync(dep, null);
            }
        }

        string filePath = ResolveBundlePath(bundleName, info);
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Debug.LogError($"[AB] 文件不存在: {bundleName}");
            onComplete?.Invoke(null);
            yield break;
        }

        var req = AssetBundle.LoadFromFileAsync(filePath);
        yield return req;

        if (req.assetBundle == null)
        {
            Debug.LogError($"[AB] 异步加载失败: {filePath}");
            onComplete?.Invoke(null);
            yield break;
        }

        abRef = new ABRef(bundleName, req.assetBundle);
        abRef.Retain();
        _loaded[bundleName] = abRef;
        onComplete?.Invoke(req.assetBundle);
    }

    string ResolveBundlePath(string bundleName, ABInfo info)
    {
        // 优先用 hash 作为实际文件名（热更友好），没有则用逻辑名
        string fileName = (info != null && !string.IsNullOrEmpty(info.hash))
            ? info.hash + ".unity3d"
            : bundleName;

        // 统一走路径优先级
        string hot = Path.Combine(ABPath.PersistentRoot, fileName);
        if (File.Exists(hot))
            return hot;

        string stream = Path.Combine(ABPath.StreamingRoot, fileName);
        return stream;
    }

    static string Normalize(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return name.ToLowerInvariant().Replace('\\', '/');
    }

    #endregion

    #region 加载具体资源

    public T LoadAsset<T>(string bundleName, string assetName) where T : UnityEngine.Object
    {
#if UNITY_EDITOR
        if (!ShouldUseAssetBundle())
        {
            // 编辑器直读，约定路径：Assets/Bundles/xxx
            string editorPath = $"Assets/Bundles/{bundleName}/{assetName}";
            // 尝试常见扩展
            var obj = AssetDatabase.LoadAssetAtPath<T>(editorPath);
            if (obj == null)
            {
                // 再试不带目录的
                obj = AssetDatabase.LoadAssetAtPath<T>($"Assets/Bundles/{assetName}");
            }
            return obj;
        }
#endif
        var ab = LoadBundle(bundleName);
        if (ab == null) return null;
        return ab.LoadAsset<T>(assetName);
    }

    public IEnumerator LoadAssetAsync<T>(string bundleName, string assetName, Action<T> onComplete) where T : UnityEngine.Object
    {
#if UNITY_EDITOR
        if (!ShouldUseAssetBundle())
        {
            var obj = LoadAsset<T>(bundleName, assetName);
            onComplete?.Invoke(obj);
            yield break;
        }
#endif
        AssetBundle ab = null;
        yield return LoadBundleAsync(bundleName, b => ab = b);
        if (ab == null)
        {
            onComplete?.Invoke(null);
            yield break;
        }

        var req = ab.LoadAssetAsync<T>(assetName);
        yield return req;
        onComplete?.Invoke(req.asset as T);
    }

    /// <summary>
    /// 加载 Bundle 内所有资源（少用，优先用带名字的 LoadAsset）
    /// </summary>
    public UnityEngine.Object[] LoadAllAssets(string bundleName)
    {
        var ab = LoadBundle(bundleName);
        if (ab == null) return Array.Empty<UnityEngine.Object>();
        return ab.LoadAllAssets();
    }

    #endregion

    #region 卸载

    /// <summary>
    /// 减少引用。归零后真正 Unload。
    /// unloadAllLoadedObjects=true 会销毁已加载的资产实例（慎用）
    /// </summary>
    public void UnloadBundle(string bundleName, bool unloadAllLoadedObjects = false)
    {
        bundleName = Normalize(bundleName);
        if (!_loaded.TryGetValue(bundleName, out var abRef))
            return;

        abRef.Release();
        if (!abRef.CanUnload)
            return;

        // 先卸自己
        abRef.Bundle.Unload(unloadAllLoadedObjects);
        _loaded.Remove(bundleName);

        // 依赖的引用也要减（简单做法：只减直接依赖）
        var info = _manifest?.Get(bundleName);
        if (info?.depends != null)
        {
            foreach (var dep in info.depends)
            {
                if (!string.IsNullOrEmpty(dep))
                    UnloadBundle(dep, false);
            }
        }
    }

    public void UnloadAll(bool unloadAllLoadedObjects = true)
    {
        foreach (var kv in _loaded)
            kv.Value.Bundle.Unload(unloadAllLoadedObjects);
        _loaded.Clear();
        Resources.UnloadUnusedAssets();
        Debug.Log("[AB] UnloadAll 完成");
    }

    public int GetRefCount(string bundleName)
    {
        bundleName = Normalize(bundleName);
        return _loaded.TryGetValue(bundleName, out var r) ? r.RefCount : 0;
    }

    public bool IsLoaded(string bundleName)
    {
        return _loaded.ContainsKey(Normalize(bundleName));
    }

    #endregion
}
