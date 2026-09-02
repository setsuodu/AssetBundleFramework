using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 登录界面。Prefab 名必须是 UI_Login。
/// </summary>
public class UI_Login : UIBase
{
    public Button CloseBtn;
    public Button LoginBtn;
    public Button CancelBtn;
    public Button SignUpBtn;

    // 1. 常量集中管理路径，或直接内联使用
    private const string PATH_CLOSE = "Login-Popup/Popup/Button-Close";
    private const string PATH_LOGIN = "Login-Popup/Popup/Buttons-Bottom-Area/Buttons/Button-LogIn";
    private const string PATH_CANCEL = "Login-Popup/Popup/Buttons-Bottom-Area/Buttons/Button-Cancel";
    private const string PATH_SIGNUP = "Login-Popup/Button-Sign-Up";

    void Awake()
    {
        CloseBtn = transform.Find(PATH_CLOSE)?.GetComponent<Button>();
        Debug.Assert(CloseBtn != null, "[UI_Login] CloseBtn is null");
        LoginBtn = transform.Find(PATH_LOGIN)?.GetComponent<Button>();
        Debug.Assert(LoginBtn != null, "[UI_Login] LoginBtn is null");
        CancelBtn = transform.Find(PATH_CANCEL)?.GetComponent<Button>();
        Debug.Assert(CancelBtn != null, "[UI_Login] CancelBtn is null");
        SignUpBtn = transform.Find(PATH_SIGNUP)?.GetComponent<Button>();
        Debug.Assert(SignUpBtn != null, "[UI_Login] SignUpBtn is null");
    }

    protected override void OnOpen()
    {
        // 关闭按钮示例
        BindButton(PATH_CLOSE, Close);

        // 登录按钮示例
        BindButton(PATH_LOGIN, OnClickConfirm);

        // 取消按钮示例
        BindButton(PATH_CANCEL, Close);

        // 注册按钮示例
        BindButton(PATH_SIGNUP, OnClickSignUp);
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