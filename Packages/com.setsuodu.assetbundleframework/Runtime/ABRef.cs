using System;
using UnityEngine;

/// <summary>
/// AssetBundle 引用计数包装 + 可选自动释放句柄
/// </summary>
public class ABRef
{
    public string Name { get; private set; }
    public AssetBundle Bundle { get; private set; }
    public int RefCount { get; private set; }

    public ABRef(string name, AssetBundle bundle)
    {
        Name = name;
        Bundle = bundle;
        RefCount = 0;
    }

    public void Retain() => RefCount++;

    public void Release()
    {
        RefCount--;
        if (RefCount < 0)
        {
            Debug.LogError($"[AB] RefCount 异常: {Name} → {RefCount}");
            RefCount = 0;
        }
    }

    public bool CanUnload => RefCount <= 0;
}

/// <summary>
/// 安全句柄：Dispose / using 时自动 Release
/// 也可 BindTo 某个 GameObject，销毁时自动释放
/// </summary>
public sealed class ABHandle : IDisposable
{
    private ABManager _manager;
    private string _bundleName;
    private bool _disposed;

    internal ABHandle(ABManager manager, string bundleName)
    {
        _manager = manager;
        _bundleName = bundleName;
    }

    public AssetBundle Bundle =>
        _manager != null ? _manager.GetLoadedBundle(_bundleName) : null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _manager?.UnloadBundle(_bundleName, false);
        _manager = null;
        _bundleName = null;
    }

    /// <summary>
    /// 绑定到 GameObject 生命周期，OnDestroy 时自动 Dispose
    /// </summary>
    public ABHandle BindTo(GameObject go)
    {
        if (go == null) return this;
        var binder = go.GetComponent<ABHandleBinder>();
        if (binder == null)
            binder = go.AddComponent<ABHandleBinder>();
        binder.Add(this);
        return this;
    }
}

/// <summary>
/// 内部组件：GameObject 销毁时释放所有绑定的 Handle
/// </summary>
internal class ABHandleBinder : MonoBehaviour
{
    private readonly System.Collections.Generic.List<ABHandle> _handles =
        new System.Collections.Generic.List<ABHandle>();

    public void Add(ABHandle h)
    {
        if (h != null) _handles.Add(h);
    }

    void OnDestroy()
    {
        for (int i = 0; i < _handles.Count; i++)
            _handles[i]?.Dispose();
        _handles.Clear();
    }
}
