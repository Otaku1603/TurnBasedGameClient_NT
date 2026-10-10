using Game.Network;
using Game.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.App
{
    /// <summary>
    /// 开发者服务器配置（IP/SSL，存 PlayerPrefs）。逻辑沿用旧版，配置源换成 NetworkConfig。
    /// 保存后重载场景让网络栈按新配置重建。
    /// </summary>
    public sealed class DevSettingsController : MonoBehaviour
    {
        private DevSettingsView _view;

        private void Start()
        {
            _view = GetComponent<DevSettingsView>();
            if (!_view)
            {
                _view = FindObjectOfType<DevSettingsView>();
            }

            _view.AutoBindUI();

            _view.OpenSettingsButton.onClick.AddListener(OnOpen);
            _view.CloseSettingsButton.onClick.AddListener(OnClose);
            _view.SaveButton.onClick.AddListener(OnSave);
            _view.ResetButton.onClick.AddListener(OnReset);

            _view.SettingsPanel.SetActive(false);
            UpdateStatusText();
        }

        private void OnOpen()
        {
            _view.SettingsPanel.SetActive(true);

            var config = NetworkConfig.Load();
            _view.CustomModeToggle.isOn = config.Host != DefaultHost;
            _view.IpInput.text = config.Host;
            _view.SslToggle.isOn = config.UseSsl;

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
            var config = new NetworkConfig();
            if (_view.CustomModeToggle.isOn)
            {
                string ip = _view.IpInput.text.Trim();
                config.Host = string.IsNullOrEmpty(ip) ? DefaultHost : ip;
                config.UseSsl = _view.SslToggle.isOn;
            }
            else
            {
                config.Host = DefaultHost;
                config.UseSsl = false;
            }

            config.Save();
            Debug.Log($"[DevSettings] saved host={config.Host} ssl={config.UseSsl}");
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void OnReset()
        {
            _view.CustomModeToggle.isOn = false;
            _view.IpInput.text = DefaultHost;
            _view.SslToggle.isOn = false;
            OnSave();
        }

        private void UpdateStatusText()
        {
            if (_view.CurrentConfigText)
            {
                var config = NetworkConfig.Load();
                string mode = config.Host != DefaultHost ? "<color=yellow>Custom</color>" : "Default";
                _view.CurrentConfigText.text = $"Mode: {mode}\nTarget: {config.ApiBase}";
            }
        }

        private const string DefaultHost = "10.244.216.57";
    }
}
