using UnityEngine;
using UnityEngine.UI; // 回归原生 UI
// using TMPro; // 移除 TMP

namespace TurnBasedGame.View
{
    /// <summary>
    /// 登录视图，处理登录和注册界面
    /// 支持重连提示和状态信息显示
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

        // ==========================================

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

        /// <summary>
        /// 显示重连确认对话框，询问是否重新加入战斗
        /// 在登录后检测到有未完成战斗时调用
        /// </summary>
        public void ShowReconnectDialog(string msg)
        {
            if (LoginPanel) LoginPanel.SetActive(false);
            if (ReconnectPanel) ReconnectPanel.SetActive(true);
            if (ReconnectMsgText) ReconnectMsgText.text = msg;
        }

        /// <summary>
        /// 显示在线状态
        /// </summary>
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