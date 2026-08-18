using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// AssetBundle 核心管理器（UniTask 版）
/// - 加载任务去重
/// - CancellationToken 全链路
/// - Cancel 时孤儿 AB 立即 Unload
/// - 依赖级联引用计数
/// - 编辑器默认 AssetDatabase
/// </summary>
public class ABManager : MonoBehaviour
{
    public static ABManager Instance { get; private set; }

    private ABManifest _manifest;
    private readonly Dictionary<string, ABRef> _loaded = new Dictionary<string, ABRef>();
    private readonly Dictionary<string, UniTask<AssetBundle>> _loadingTasks = new Dictionary<string, UniTask<AssetBundle>>();
    private bool _inited;

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

    public async UniTask InitializeAsync(CancellationToken token = default)
    {
        if (_inited) return;

#if UNITY_EDITOR
        if (!ShouldUseAssetBundle())
        {
            _manifest = new ABManifest { version = "editor" };
            _inited = true;
            Debug.Log("[AB] 编辑器模式：AssetDatabase 直读");
            return;
        }
#endif

        string path = ABPath.GetManifestPath(true);
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            _manifest = JsonUtility.FromJson<ABManifest>(json) ?? new ABManifest();
        }
        else
        {
            Debug.LogWarning($"[AB] Manifest 不存在: {path}");
            _manifest = new ABManifest();
        }

        _inited = true;
        Debug.Log($"[AB] 初始化完成 version={_manifest.version} count={_manifest.bundles?.Count ?? 0}");
        await UniTask.CompletedTask;
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

    public AssetBundle GetLoadedBundle(string bundleName)
    {
        bundleName = Normalize(bundleName);
        return _loaded.TryGetValue(bundleName, out var r) ? r.Bundle : null;
    }

    #endregion

    #region 加载 Bundle（去重 + Token + 孤儿清理 + 级联依赖）

    /// <summary>
    /// 异步加载 Bundle。同一 AB 并发请求会共享同一个 UniTask。
    /// </summary>
    public async UniTask<AssetBundle> LoadBundleAsync(string bundleName, CancellationToken token = default)
    {
        bundleName = Normalize(bundleName);

        // 已加载：直接加引用
        if (_loaded.TryGetValue(bundleName, out var abRef))
        {
            abRef.Retain();
            // 依赖也要加引用（级联）
            RetainDependencies(bundleName);
            return abRef.Bundle;
        }

        // 去重：已有加载任务则直接 await 同一个
        if (_loadingTasks.TryGetValue(bundleName, out var existing))
        {
            var ab = await existing.AttachExternalCancellation(token);
            // 任务完成后可能已经被别人 Register，再 Retain
            if (_loaded.TryGetValue(bundleName, out abRef))
            {
                abRef.Retain();
                RetainDependencies(bundleName);
            }
            return ab;
        }

        // 新建加载任务
        var utcs = new UniTaskCompletionSource<AssetBundle>();
        _loadingTasks[bundleName] = utcs.Task;

        AssetBundle result = null;
        try
        {
            result = await LoadBundleInternalAsync(bundleName, token);
            utcs.TrySetResult(result);
            return result;
        }
        catch (OperationCanceledException)
        {
            utcs.TrySetCanceled(token);
            throw;
        }
        catch (Exception ex)
        {
            utcs.TrySetException(ex);
            throw;
        }
        finally
        {
            _loadingTasks.Remove(bundleName);
        }
    }

    async UniTask<AssetBundle> LoadBundleInternalAsync(string bundleName, CancellationToken token)
    {
        // 1. 先加载依赖（级联）
        var info = _manifest?.Get(bundleName);
        if (info?.depends != null)
        {
            foreach (var dep in info.depends)
            {
                if (string.IsNullOrEmpty(dep)) continue;
                await LoadBundleAsync(dep, token); // 依赖也会去重 + 加引用
            }
        }

        token.ThrowIfCancellationRequested();

        // 2. 已加载则直接返回（可能在 await 依赖期间被别人加载完）
        if (_loaded.TryGetValue(bundleName, out var abRef))
        {
            abRef.Retain();
            return abRef.Bundle;
        }

        string filePath = ResolveBundlePath(bundleName, info);
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Debug.LogError($"[AB] 文件不存在: {bundleName} → {filePath}");
            return null;
        }

        AssetBundle ab = null;
        try
        {
            var request = AssetBundle.LoadFromFileAsync(filePath);
            // UniTask 扩展：WithCancellation 在取消时会抛 OperationCanceledException
            ab = await request.ToUniTask(cancellationToken: token);

            token.ThrowIfCancellationRequested();

            // 再次检查是否已被别人注册
            if (_loaded.TryGetValue(bundleName, out abRef))
            {
                // 自己加载出来的变成多余，立刻卸掉
                if (ab != null && ab != abRef.Bundle)
                    ab.Unload(false);
                abRef.Retain();
                return abRef.Bundle;
            }

            abRef = new ABRef(bundleName, ab);
            abRef.Retain(); // 初始 1
            _loaded[bundleName] = abRef;
            return ab;
        }
        catch (OperationCanceledException)
        {
            // 核心防漏：Cancel 时如果 AB 已经创建出来但还没注册成功，必须立刻释放
            if (ab != null)
            {
                ab.Unload(true);
                ab = null;
            }
            throw;
        }
    }

    /// <summary>
    /// 返回可自动释放的 Handle（推荐业务层使用）
    /// </summary>
    public async UniTask<ABHandle> LoadBundleHandleAsync(string bundleName, CancellationToken token = default)
    {
        await LoadBundleAsync(bundleName, token);
        return new ABHandle(this, Normalize(bundleName));
    }

    void RetainDependencies(string bundleName)
    {
        var info = _manifest?.Get(bundleName);
        if (info?.depends == null) return;
        foreach (var dep in info.depends)
        {
            if (string.IsNullOrEmpty(dep)) continue;
            string d = Normalize(dep);
            if (_loaded.TryGetValue(d, out var r))
                r.Retain();
        }
    }

    string ResolveBundlePath(string bundleName, ABInfo info)
    {
        string fileName = (info != null && !string.IsNullOrEmpty(info.hash))
            ? info.hash + ".unity3d"
            : bundleName;

        string hot = Path.Combine(ABPath.PersistentRoot, fileName);
        if (File.Exists(hot))
            return hot;

        return Path.Combine(ABPath.StreamingRoot, fileName);
    }

    static string Normalize(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return name.ToLowerInvariant().Replace('\\', '/');
    }

    #endregion

    #region 加载资源

    public async UniTask<T> LoadAssetAsync<T>(string bundleName, string assetName, CancellationToken token = default)
        where T : UnityEngine.Object
    {
#if UNITY_EDITOR
        if (!ShouldUseAssetBundle())
        {
            string path1 = $"Assets/Bundles/{bundleName}/{assetName}";
            var obj = AssetDatabase.LoadAssetAtPath<T>(path1);
            if (obj == null)
                obj = AssetDatabase.LoadAssetAtPath<T>($"Assets/Bundles/{assetName}");
            return obj;
        }
#endif
        var ab = await LoadBundleAsync(bundleName, token);
        if (ab == null) return null;

        var req = ab.LoadAssetAsync<T>(assetName);
        return await req.ToUniTask(cancellationToken: token) as T;
    }

    #endregion

    #region 卸载

    /// <summary>
    /// 减少引用。归零后真正 Unload，并级联减少依赖引用。
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

        // 级联减依赖
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
        _loadingTasks.Clear();
        Resources.UnloadUnusedAssets();
        Debug.Log("[AB] UnloadAll 完成");
    }

    public int GetRefCount(string bundleName)
    {
        bundleName = Normalize(bundleName);
        return _loaded.TryGetValue(bundleName, out var r) ? r.RefCount : 0;
    }

    public bool IsLoaded(string bundleName) => _loaded.ContainsKey(Normalize(bundleName));

    #endregion
}
