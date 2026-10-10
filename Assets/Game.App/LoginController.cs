using Game.Network;
using Game.UI;
using GameClient.Proto;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.App
{
    /// <summary>
    /// 登录控制器（协议 v2 改造）：
    /// 阻塞式配置加载 → 登录/注册（REST）→ WS 认证 → BATTLE_REJOIN 探测未完成战斗
    /// → 有战斗弹重连确认 / 无战斗进大厅。时序与旧版一致，传输层换 WS+Protobuf v2。
    /// </summary>
    public sealed class LoginController : MonoBehaviour
    {
        private LoginView _view;
        private BattleRejoinResponse _pendingRejoinData;

        private void Start()
        {
            _view = GetComponent<LoginView>();
            if (!_view)
            {
                _view = FindObjectOfType<LoginView>();
            }

            _view.AutoBindUI();

            if (_view.LoginPanel) _view.LoginPanel.SetActive(false);
            if (_view.RegisterPanel) _view.RegisterPanel.SetActive(false);
            if (_view.ReconnectPanel) _view.ReconnectPanel.SetActive(false);

            BindEvents();

            var entry = GameEntry.Instance;
            entry.Auth.Authenticated += OnWsAuthenticated;
            entry.ConnState.OnAuthFailed += OnAuthFailed;
            entry.Battles.BattleRejoinResponse += OnRejoinResponse;
            entry.Battles.BattleStart += OnRejoinBattleStart;

            // 协议 v2：/api/gamedata/* 需要登录鉴权，登录前拉不到配置。
            // 因此登录面板直接展示；配置在 WS 认证成功后加载（见 OnWsAuthenticated）。
            _view.ShowLogin();
        }

        private void OnDestroy()
        {
            var entry = GameEntry.Instance;
            if (entry != null)
            {
                entry.Auth.Authenticated -= OnWsAuthenticated;
                entry.ConnState.OnAuthFailed -= OnAuthFailed;
                entry.Battles.BattleRejoinResponse -= OnRejoinResponse;
                entry.Battles.BattleStart -= OnRejoinBattleStart;
            }
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

        private async void OnLoginClicked()
        {
            string u = _view.LoginUsernameInput.text;
            string p = _view.LoginPasswordInput.text;
            _view.SetStatus("Logging in...", false);
            _view.LoginConfirmButton.interactable = false;

            var result = await GameEntry.Instance.Auth.LoginAsync(u, p);
            if (!result.IsOk)
            {
                _view.SetStatus($"Error: {result.Code} {result.Message}", true);
                _view.LoginConfirmButton.interactable = true;
            }
            else
            {
                _view.SetStatus("Auth OK. Handshaking...", false);
                // WS LOGIN 由 AuthService 触发；Authenticated 事件回调继续
            }
        }

        private async void OnRegisterClicked()
        {
            string u = _view.RegUsernameInput.text;
            string p = _view.RegPasswordInput.text;
            string n = _view.RegNicknameInput.text;

            // 协议 v2：注册只传用户名/密码/昵称（服务端默认战士），不依赖 gamedata（注册时尚未登录）
            if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p) || string.IsNullOrEmpty(n))
            {
                _view.SetStatus("All fields are required", true);
                return;
            }

            // 协议 v2：注册只传用户名/密码/昵称，职业由服务端默认战士（注册即送，见 HANDOVER §2）
            _view.SetStatus("Registering...", false);
            _view.RegConfirmButton.interactable = false;

            var result = await GameEntry.Instance.Auth.RegisterAsync(u, p, n);
            if (result.IsOk)
            {
                _view.ShowLogin();
                _view.SetStatus("Reg Success", false);
            }
            else
            {
                _view.SetStatus($"{result.Code} {result.Message}", true);
                _view.RegConfirmButton.interactable = true;
            }
        }

        private void OnGoToRegisterClicked()
        {
            PopulateClassDropdown();
            _view.ShowRegister();
        }

        private void OnBackToLoginClicked() => _view.ShowLogin();

        /// <summary>
        /// WS 认证成功 → 拉取配置（gamedata 需 token）→ 探测未完成战斗。
        /// 配置加载失败短暂重试（3 次）后仍进大厅（战斗场景才依赖技能表）。
        /// </summary>
        private async void OnWsAuthenticated()
        {
            _view.SetStatus("Loading configs...", false);

            var gameData = GameEntry.Instance.GameData;
            for (int i = 0; i < 3 && !gameData.IsLoaded; i++)
            {
                await gameData.LoadAllAsync();
                if (!gameData.IsLoaded)
                {
                    await System.Threading.Tasks.Task.Delay(1500);
                }
            }

            Debug.Log($"[Login] configs loaded={gameData.IsLoaded} skills={gameData.Skills.Count} templates={gameData.Templates.Count}");
            _view.SetStatus("Checking battle status...", false);
            GameEntry.Instance.Battles.Rejoin();
        }

        private void OnAuthFailed(string reason)
        {
            _view.SetStatus($"Login Refused: {reason}", true);
            _view.LoginConfirmButton.interactable = true;
        }

        private void OnRejoinResponse(BattleRejoinResponse resp)
        {
            // 盘面完整性守卫：success 但不带双方数据的回包不得当成真盘面（与 ReconnectRecovery 一致）
            if (resp.Success && resp.Player1 != null && resp.Player2 != null)
            {
                _pendingRejoinData = resp;
                _view.ShowReconnectDialog($"Found active battle (Round {resp.CurrentRound}).\nReconnect?");
            }
            else
            {
                if (resp.Success)
                {
                    Debug.LogWarning("[Login] rejoin success 但缺盘面，按无战斗处理");
                }

                SceneManager.LoadScene("MainScene");
            }
        }

        private void OnReconnectYes()
        {
            if (_pendingRejoinData == null)
            {
                return;
            }

            // 转换为 BATTLE_START 格式作为战斗场景上下文
            var startData = new BattleStartResponse
            {
                BattleId = _pendingRejoinData.BattleId,
                Player1 = _pendingRejoinData.Player1,
                Player2 = _pendingRejoinData.Player2,
                CurrentActorUserId = _pendingRejoinData.CurrentActorUserId,
                CurrentRound = _pendingRejoinData.CurrentRound,
            };
            GameEntry.Instance.Battles.CurrentBattle = startData;
            SceneManager.LoadScene("BattleScene");
        }

        private void OnReconnectNo()
        {
            SceneManager.LoadScene("MainScene");
        }

        /// <summary>重连战斗的盘面经 BATTLE_START 事件（M3 由 BattleScene 快照系统消费）。</summary>
        private void OnRejoinBattleStart(BattleStartResponse start)
        {
            // M3：喂给 DOTS BattleSnapshotSystem；M2 阶段无需处理
        }

        /// <summary>
        /// 填充注册页职业下拉。协议 v2 注册不传职业（服务端默认战士），此处仅展示。
        /// 配置在登录后才拉得到（gamedata 需 token），未登录时显示占位。
        /// </summary>
        private void PopulateClassDropdown()
        {
            if (_view.ClassDropdown == null)
            {
                return;
            }

            var templates = GameEntry.Instance.GameData.Templates;
            _view.ClassDropdown.ClearOptions();
            var options = new System.Collections.Generic.List<string>();
            if (templates == null || templates.Count == 0)
            {
                options.Add("Warrior (default)");
            }
            else
            {
                foreach (var tmpl in templates)
                {
                    options.Add($"{tmpl.DisplayName} (HP:{tmpl.BaseHp})");
                }
            }

            _view.ClassDropdown.AddOptions(options);
        }
    }
}
