using TurnBasedGame.Core;
using TurnBasedGame.Network;
using TurnBasedGame.Services;
using TurnBasedGame.View;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TurnBasedGame.Controller
{
    public class DevSettingsController : MonoBehaviour
    {
        private DevSettingsView _view;

        void Start()
        {
            _view = GetComponent<DevSettingsView>();
            if (!_view) _view = FindObjectOfType<DevSettingsView>();
            _view.AutoBindUI();

            // 绑定事件
            _view.OpenSettingsButton.onClick.AddListener(OnOpen);
            _view.CloseSettingsButton.onClick.AddListener(OnClose);
            _view.SaveButton.onClick.AddListener(OnSave);
            _view.ResetButton.onClick.AddListener(OnReset);

            // 初始状态
            _view.SettingsPanel.SetActive(false);
            UpdateStatusText();
        }

        private void OnOpen()
        {
            _view.SettingsPanel.SetActive(true);

            // 回显当前配置
            _view.CustomModeToggle.isOn = AppConfig.IsCustomMode;
            _view.IpInput.text = AppConfig.SERVER_IP;
            _view.SslToggle.isOn = AppConfig.USE_SSL;

            // 根据 Toggle 状态控制输入框交互
            _view.CustomModeToggle.onValueChanged.RemoveAllListeners();
            _view.CustomModeToggle.onValueChanged.AddListener(OnToggleChanged);
            OnToggleChanged(_view.CustomModeToggle.isOn);
        }

        private void OnToggleChanged(bool isOn)
        {
            _view.IpInput.interactable = isOn;
            _view.SslToggle.interactable = isOn;
        }

        private void OnClose()
        {
            _view.SettingsPanel.SetActive(false);
        }

        private void OnSave()
        {
            if (_view.CustomModeToggle.isOn)
            {
                string ip = _view.IpInput.text.Trim();
                if (string.IsNullOrEmpty(ip)) ip = "localhost";

                // 保存配置
                AppConfig.SaveConfig(
                    _view.CustomModeToggle.isOn,
                    ip,
                    _view.SslToggle.isOn
                );
            }
            else
            {
                AppConfig.SaveConfig(false, AppConfig.SERVER_IP, AppConfig.USE_SSL);
            }

            // 彻底清理单例状态，确保场景刷新后像“第一次启动”一样
            PerformCleanReload();
        }

        private void PerformCleanReload()
        {
            // 1. 断开网络 (防止旧连接残留)
            NetworkManager.Instance.Disconnect();

            // 2. 重置配置管理器状态 (让它下次 Start 时重新拉取)
            ConfigManager.Instance.Reset();

            // 3. 刷新场景
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void OnReset()
        {
            // 1. 清除本地存储
            AppConfig.ResetToDefaults();

            // 2. 刷新 UI 显示 (变回默认值)
            _view.CustomModeToggle.isOn = false;
            _view.IpInput.text = AppConfig.SERVER_IP; // 此时读取的是 const 默认值
            _view.SslToggle.isOn = AppConfig.USE_SSL;

            // 3. 执行保存并刷新流程
            OnSave();
        }

        private void UpdateStatusText()
        {
            if (_view.CurrentConfigText)
            {
                string mode = AppConfig.IsCustomMode ? "<color=yellow>Custom</color>" : "Default";
                _view.CurrentConfigText.text = $"Mode: {mode}\nTarget: {AppConfig.ApiBaseUrl}";
            }
        }
    }
}