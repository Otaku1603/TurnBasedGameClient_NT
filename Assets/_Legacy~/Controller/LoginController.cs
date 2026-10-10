using GameClient.Proto;
using System.Collections;
using System.Collections.Generic;
using TurnBasedGame.Network;
using TurnBasedGame.Services;
using TurnBasedGame.View;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TurnBasedGame.Controller
{
    /// <summary>
    /// 登录控制器，管理用户认证流程
    /// 处理配置加载、登录、注册和战斗重连逻辑
    /// </summary>
    public class LoginController : MonoBehaviour
    {
        private LoginView _view;
        private List<Model.TemplateConfig> _cachedTemplates;
        private BattleRejoinResponse _pendingRejoinData;

        void Start()
        {
            _view = GetComponent<LoginView>();
            if (!_view) _view = FindObjectOfType<LoginView>();
            _view.AutoBindUI();

            // 1. 初始状态：全部隐藏，只留 StatusText
            if (_view.LoginPanel) _view.LoginPanel.SetActive(false);
            if (_view.RegisterPanel) _view.RegisterPanel.SetActive(false);
            if (_view.ReconnectPanel) _view.ReconnectPanel.SetActive(false);

            // 绑定事件
            BindEvents();

            NetworkManager.Instance.OnMessageReceived += OnNetworkMessage;

            // 2. 进入强制加载流程
            StartCoroutine(BlockingConfigLoad());
        }

        private void BindEvents()
        {
            if (_view.LoginConfirmButton) _view.LoginConfirmButton.onClick.AddListener(OnLoginClicked);
            if (_view.GoToRegisterButton) _view.GoToRegisterButton.onClick.AddListener(OnGoToRegisterClicked);
            if (_view.RegConfirmButton) _view.RegConfirmButton.onClick.AddListener(OnRegisterClicked);
            if (_view.BackToLoginButton) _view.BackToLoginButton.onClick.AddListener(OnBackToLoginClicked);
            if (_view.ReconnectYesButton) _view.ReconnectYesButton.onClick.AddListener(OnReconnectYes);
            if (_view.ReconnectNoButton) _view.ReconnectNoButton.onClick.AddListener(OnReconnectNo);
        }

        void OnDestroy()
        {
            NetworkManager.Instance.OnMessageReceived -= OnNetworkMessage;
        }

        /// <summary>
        /// 阻塞式配置加载，直到成功为止
        /// 登录界面必须等待所有配置加载完成才能交互
        /// </summary>
        private IEnumerator BlockingConfigLoad()
        {
            _view.SetStatus("Connecting...", false); // 只显示一次

            while (!ConfigManager.Instance.IsLoaded)
            {
                bool loadFinished = false;
                bool loadSuccess = false;

                StartCoroutine(ConfigManager.Instance.LoadAllConfigs((success) =>
                {
                    loadSuccess = success;
                    loadFinished = true;
                }));

                yield return new WaitUntil(() => loadFinished);

                if (loadSuccess)
                {
                    _view.SetStatus("Connected!", false);
                    yield return new WaitForSeconds(0.5f);
                    break;
                }
                else
                {
                    // 失败时什么都不做，不弹窗，不改字，只是默默等待重试
                    yield return new WaitForSeconds(3.0f);
                }
            }

            _view.ShowLogin();
        }

        private void OnLoginClicked()
        {
            string u = _view.LoginUsernameInput.text;
            string p = _view.LoginPasswordInput.text;
            _view.SetStatus("Logging in...", false);
            _view.LoginConfirmButton.interactable = false;

            StartCoroutine(AuthService.Instance.Login(u, p, (success, msg) =>
            {
                if (!success)
                {
                    _view.SetStatus($"Error: {msg}", true);
                    _view.LoginConfirmButton.interactable = true;
                }
                else
                {
                    _view.SetStatus("Auth OK. Handshaking...", false);
                }
            }));
        }

        private void OnRegisterClicked()
        {
            string u = _view.RegUsernameInput.text;
            string p = _view.RegPasswordInput.text;
            string n = _view.RegNicknameInput.text;

            // 从缓存的模板列表中获取 charType
            if (_cachedTemplates == null || _cachedTemplates.Count == 0) return;

            int index = _view.ClassDropdown.value;
            if (index < 0 || index >= _cachedTemplates.Count) return;

            string charType = _cachedTemplates[index].charType; // 获取真实的 charType (warrior/mage...)

            if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p) || string.IsNullOrEmpty(n))
            {
                _view.SetStatus("All fields are required", true);
                return;
            }

            _view.SetStatus("Registering...", false);
            _view.RegConfirmButton.interactable = false;

            StartCoroutine(AuthService.Instance.Register(u, p, n, charType, (success, msg) =>
            {
                if (success) { _view.ShowLogin(); _view.SetStatus("Reg Success", false); }
                else _view.SetStatus(msg, true);
            }));
        }

        private void OnGoToRegisterClicked()
        {
            PopulateClassDropdown();
            _view.ShowRegister();
        }

        private void OnBackToLoginClicked() => _view.ShowLogin();

        /// <summary>
        /// 处理网络消息：登录成功 → 检查未完成战斗 → 决定进入大厅还是重连战斗
        /// 关键时序：HTTP登录 → TCP连接 → Proto登录 → 战斗状态检查
        /// </summary>
        private void OnNetworkMessage(GameMessage msg)
        {
            // 1. TCP 登录成功
            if (msg.Type == MessageType.Login)
            {
                // 登录成功后，不能直接进大厅，先检查是否有未完成的战斗
                if (msg.LoginResponse.Success)
                {
                    _view.SetStatus("Checking battle status...", false);

                    // 发送重连请求，检测是否有战斗
                    BattleService.Instance.SendBattleRejoin();
                }
                else
                {
                    _view.SetStatus($"Login Refused: {msg.LoginResponse.Message}", true);
                    _view.LoginConfirmButton.interactable = true;
                }
            }
            // 2. 收到重连响应
            else if (msg.Type == MessageType.BattleRejoinResponse)
            {
                // 根据后端返回判断是否弹窗
                var resp = msg.BattleRejoinResponse;
                if (resp.Success)
                {
                    // [有战斗] -> 弹出选择框
                    _pendingRejoinData = resp;
                    _view.ShowReconnectDialog($"Found active battle (Round {resp.CurrentRound}).\nReconnect?");
                }
                else
                {
                    // [无战斗] -> 直接进大厅
                    SceneManager.LoadScene("MainScene");
                }
            }
        }

        /// <summary>
        /// 用户选择重新加入战斗，转换数据格式并跳转战斗场景
        /// 注意：需要将重连数据转换为标准的BattleStartResponse格式
        /// </summary>
        private void OnReconnectYes()
        {
            if (_pendingRejoinData == null) return;

            // 转换数据格式
            var startData = new BattleStartResponse
            {
                BattleId = _pendingRejoinData.BattleId,
                Player1 = _pendingRejoinData.Player1,
                Player2 = _pendingRejoinData.Player2,
                CurrentActorUserId = _pendingRejoinData.CurrentActorUserId,
                CurrentRound = _pendingRejoinData.CurrentRound
            };

            BattleController.BattleContext.StartData = startData;
            SceneManager.LoadScene("BattleScene");
        }

        /// <summary>
        /// 用户放弃重连，直接进入大厅
        /// 后端会在超时后自动判负，避免战斗卡住
        /// </summary>
        private void OnReconnectNo()
        {
            SceneManager.LoadScene("MainScene");
        }

        private void PopulateClassDropdown()
        {
            _cachedTemplates = ConfigManager.Instance.TemplateList;
            if (_cachedTemplates == null) return;
            _view.ClassDropdown.ClearOptions();
            List<string> options = new List<string>();
            foreach (var tmpl in _cachedTemplates)
                options.Add($"{tmpl.namePrefix} (HP:{tmpl.baseMaxHp})");
            _view.ClassDropdown.AddOptions(options);
        }
    }
}