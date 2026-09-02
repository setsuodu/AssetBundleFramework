using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 所有 UI 面板的基类。
/// 脚本直接挂在 Prefab 上，组件用 [SerializeField] 在 Inspector 里拖引用。
/// UIManager 实例化后只做 GetComponent + __Init，不再反射 AddComponent。
/// </summary>
public abstract class UIBase : MonoBehaviour
{
    /// <summary>面板唯一名字（与 Prefab 名一致，如 "UI_Home"）</summary>
    public string PanelName { get; private set; }

    /// <summary>是否已关闭（防止重复 Close）</summary>
    public bool IsClosed { get; private set; }

    /// <summary>UIManager 调用：初始化名字并触发 OnOpen</summary>
    internal void __Init(string panelName)
    {
        PanelName = panelName;
        IsClosed = false;
        OnOpen();
    }

    /// <summary>打开时调用（可重写）——在这里绑定按钮、初始化数据</summary>
    protected virtual void OnOpen() { }

    /// <summary>关闭时调用（可重写）——清理监听、临时数据等</summary>
    protected virtual void OnClose() { }

    /// <summary>关闭自己</summary>
    public void Close()
    {
        if (IsClosed) return;
        IsClosed = true;
        OnClose();
        if (UIManager.Instance != null)
            UIManager.Instance.Close(PanelName);
    }

    /// <summary>打开其他面板的便捷方法</summary>
    protected UniTask<GameObject> OpenPanelAsync(string name, CancellationToken ct = default)
        => UIManager.Instance.OpenAsync(name, ct);

    /// <summary>给已有 Button 绑定点击（推荐配合 [SerializeField] 使用）</summary>
    protected void BindButton(UnityEngine.UI.Button btn, UnityEngine.Events.UnityAction action)
    {
        if (btn != null)
            btn.onClick.AddListener(action);
        else
            Debug.LogWarning($"[{PanelName}] Button is null, cannot bind");
    }

    /// <summary>查找子节点上的组件（深度优先）。备用，优先用 SerializeField</summary>
    protected T Find<T>(string path = null) where T : Component
    {
        if (string.IsNullOrEmpty(path))
            return GetComponentInChildren<T>(true);

        var t = transform.Find(path);
        return t != null ? t.GetComponent<T>() : null;
    }

    /// <summary>按路径绑定按钮。备用，优先用 SerializeField + BindButton(btn, action)</summary>
    protected void BindButton(string path, UnityEngine.Events.UnityAction action)
    {
        var btn = Find<UnityEngine.UI.Button>(path);
        if (btn != null)
            btn.onClick.AddListener(action);
        else
            Debug.LogWarning($"[{PanelName}] Button not found: {path}");
    }
}
