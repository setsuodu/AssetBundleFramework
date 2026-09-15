using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 登录界面。Prefab 名必须是 UI_Login，脚本直接挂在 Prefab 根节点。
/// 在 Inspector 里把对应 Button 拖到下面字段，不再用 transform.Find 路径。
/// </summary>
public class UI_Login : UIBase
{
    [Header("UI 引用（Inspector 拖拽）")]
    [SerializeField] Button closeButton;
    [SerializeField] Button loginButton;
    [SerializeField] Button cancelButton;
    [SerializeField] Button signUpButton;

    protected override void OnOpen()
    {
        BindButton(closeButton, Close);
        BindButton(loginButton, OnClickConfirm);
        BindButton(cancelButton, Close);
        BindButton(signUpButton, OnClickSignUp);
    }

    void OnClickConfirm()
    {
        // TODO: 实际登录逻辑
        Debug.Log("[UI_Login] 登录成功，关闭自己");
        Close();
    }

    void OnClickSignUp()
    {
        Debug.Log("[UI_Login] 切换到注册界面");
    }

    protected override void OnClose()
    {
        // 清理
    }
}
