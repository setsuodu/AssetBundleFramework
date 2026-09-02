using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 主界面。Prefab 名必须是 UI_Home。
/// 点击 Button-Login 弹出 UI_Login。
/// </summary>
public class UI_Home : UIBase
{
    protected override void OnOpen()
    {
        // 路径按你实际 Prefab 层级改，常见写法：
        // "Button-Login" 或 "Panel/Button-Login"
        BindButton("Button-Login", OnClickLogin);

        // 如果按钮是子物体且名字固定，也可以这样：
        // var btn = transform.Find("Button-Login")?.GetComponent<Button>();
        // btn?.onClick.AddListener(OnClickLogin);
    }

    void OnClickLogin()
    {
        // 弹出登录界面
        OpenPanelAsync("UI_Login").Forget();
    }

    protected override void OnClose()
    {
        // 这里可以做清理
    }
}