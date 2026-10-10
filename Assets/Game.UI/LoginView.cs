using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 登录视图：登录/注册/重连确认三个面板 + 状态栏。
    /// 字段名与旧客户端一致，场景替换后 AutoBindUI 自动恢复引用。
    /// </summary>
    public class LoginView : BaseView
    {
        [Header("Original Panels")]
        public GameObject LoginPanel;
        public GameObject RegisterPanel;

        public InputField LoginUsernameInput;
        public InputField LoginPasswordInput;
        public Button LoginConfirmButton;
        public Button GoToRegisterButton;

        public InputField RegUsernameInput;
        public InputField RegPasswordInput;
        public InputField RegNicknameInput;
        public Dropdown ClassDropdown;
        public Button RegConfirmButton;
        public Button BackToLoginButton;

        public Text StatusText;

        [Header("Reconnect Panel")]
        public GameObject ReconnectPanel;
        public Text ReconnectMsgText;
        public Button ReconnectYesButton;
        public Button ReconnectNoButton;

        public void ShowLogin()
        {
            if (LoginPanel) LoginPanel.SetActive(true);
            if (RegisterPanel) RegisterPanel.SetActive(false);
            if (ReconnectPanel) ReconnectPanel.SetActive(false);
            SetStatus("");
        }

        public void ShowRegister()
        {
            if (LoginPanel) LoginPanel.SetActive(false);
            if (RegisterPanel) RegisterPanel.SetActive(true);
            if (ReconnectPanel) ReconnectPanel.SetActive(false);
            SetStatus("");
        }

        public void ShowReconnectDialog(string msg)
        {
            if (LoginPanel) LoginPanel.SetActive(false);
            if (ReconnectPanel) ReconnectPanel.SetActive(true);
            if (ReconnectMsgText) ReconnectMsgText.text = msg;
        }

        public void SetStatus(string msg, bool isError = false)
        {
            if (StatusText)
            {
                StatusText.text = msg;
                StatusText.color = isError ? Color.red : Color.green;
            }
        }
    }
}
