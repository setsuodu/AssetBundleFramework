using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 主界面。Prefab 名必须是 UI_Home，脚本直接挂在 Prefab 根节点。
/// 在 Inspector 里把 Button-Login 拖到 loginButton 字段。
/// </summary>
public class UI_Home : UIBase
{
    [Header("UI 引用（Inspector 拖拽）")]
    [SerializeField] Button loginButton;

    protected override void OnOpen()
    {
        BindButton(loginButton, OnClickLogin);
    }

    void OnClickLogin()
    {
        OpenPanelAsync("UI_Login").Forget();
    }

    protected override void OnClose()
    {
        // 清理
    }
}
