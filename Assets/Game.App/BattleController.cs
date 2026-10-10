using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.App;
using Game.Network;
using Game.UI;
using GameClient.Proto;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.App
{
    /// <summary>
    /// 战斗控制器（M2 版）：事件队列顺序播放动画、血条/冷却/技能按钮、投降、结算。
    /// 相对旧版的协议 v2 改造：
    /// - 动画分派改用 BattleUpdateResponse.ActionType 强类型枚举（删除"防御"/"药水"中文字符串判断坏味道）；
    /// - 技能列表来自 ownedSkillIds − cooldowns.keys（旧版错误地用 cooldowns.keys 当技能清单）；
    /// - 聊天改 WS CHAT_PUSH（M5 接入）；战斗背包接 /economy/inventory（M4 接入）。
    /// M3：本类中的状态重演逻辑迁移至 DOTS Sim 层。
    /// </summary>
    public sealed class BattleController : MonoBehaviour
    {
        private BattleView _view;
        private string _battleId;
        private long _myUserId;
        private BattleStartResponse _startData;
        private bool _amIP1;
        private bool _ended;

        // 动画队列：网络消息可能瞬间到达多条，排队逐条播放防止画面跳变
        private readonly Queue<BattleUpdateResponse> _updateQueue = new Queue<BattleUpdateResponse>();
        private bool _isProcessingQueue;
        private IDisposable _chatSub;
        // 服务端拒绝动作时的提示（短暂覆盖回合提示行，否则玩家点击"没反应"）
        private IDisposable _errorSub;
        private string _rejectMsg;
        private float _rejectMsgUntil;

        // Sim 世界（ECS）：HUD/回合门控的唯一数据源
        private Game.Sim.BattleSimWorld _sim;
        private int _lastSelfHp = -1;
        private int _lastEnemyHp = -1;
        private float _nextTurnLabelTime;

        private void Start()
        {
            _view = GetComponent<BattleView>();
            if (!_view) _view = FindObjectOfType<BattleView>();
            _view.AutoBindUI();

            _myUserId = GameEntry.Instance.Auth.UserId;
            _amIP1 = GameEntry.Instance.Battles.CurrentBattle?.Player1.UserId == _myUserId;

            var battles = GameEntry.Instance.Battles;
            battles.BattleUpdate += EnqueueBattleUpdate;
            battles.BattleEnd += HandleBattleEnd;
            GameEntry.Instance.Recovery.BattleRecovered += OnBattleRecovered;

            // 战斗聊天（v2：世界频道 REST 发送 + CHAT_PUSH 实时推送）
            if (_view.ToggleChatButton)
            {
                _view.ToggleChatButton.onClick.AddListener(() =>
                    _view.ChatPanel.SetActive(!_view.ChatPanel.activeSelf));
            }

            if (_view.SendChatButton)
            {
                _view.SendChatButton.onClick.AddListener(OnSendBattleChat);
            }

            if (_view.ChatPanel)
            {
                _view.ChatPanel.SetActive(true);
            }

            _chatSub = GameEntry.Instance.PushBus.Subscribe(GameClient.Proto.MessageType.ChatPush, OnChatPush);
            // 服务端拒绝动作（未拥有技能/冷却中/道具不足/未轮到你）走 ERROR 推送，必须让玩家看见
            _errorSub = GameEntry.Instance.PushBus.Subscribe(GameClient.Proto.MessageType.Error, OnServerError);

            if (_view.DefendButton) _view.DefendButton.onClick.AddListener(OnDefendClick);
            if (_view.ItemButton) _view.ItemButton.onClick.AddListener(OnItemButtonClick);
            if (_view.CloseBagButton) _view.CloseBagButton.onClick.AddListener(() => _view.BattleBagPanel.SetActive(false));
            if (_view.SurrenderButton) _view.SurrenderButton.onClick.AddListener(OnSurrender);

            if (GameEntry.Instance.Battles.CurrentBattle != null)
            {
                InitBattle(GameEntry.Instance.Battles.CurrentBattle);
            }
            else
            {
                // 直接进战斗场景（如编辑器 Play）异常保护
                Debug.LogError("[Battle] Missing CurrentBattle! Returning to Main.");
                SceneManager.LoadScene("MainScene");
            }
        }

        /// <summary>断线重连且盘面恢复：重挂 Sim 快照、清动画队列（本场景战斗时）。</summary>
        private void OnBattleRecovered(BattleStartResponse start)
        {
            if (start.BattleId != _battleId)
            {
                return;
            }

            Debug.Log("[Battle] battle recovered after reconnect");
            _updateQueue.Clear();
            _sim?.ApplyStart(start, _myUserId);
            _lastSelfHp = -1;
            _lastEnemyHp = -1;
            _ended = false;
            _startData = start;
            GenerateSkillButtons(_sim.SelfSnapshot);
        }

        private void OnDestroy()
        {
            if (GameEntry.Instance == null)
            {
                return;
            }

            var battles = GameEntry.Instance.Battles;
            battles.BattleUpdate -= EnqueueBattleUpdate;
            battles.BattleEnd -= HandleBattleEnd;
            GameEntry.Instance.Recovery.BattleRecovered -= OnBattleRecovered;
            _chatSub?.Dispose();
            _chatSub = null;
            _errorSub?.Dispose();
            _errorSub = null;
            _sim?.Dispose();
            _sim = null;
        }

        private void InitBattle(BattleStartResponse data)
        {
            _startData = data;
            _battleId = data.BattleId;

            // Sim 世界初始化：快照进 ECS，此后 HUD 只读 UiState
            _sim = new Game.Sim.BattleSimWorld();
            _sim.ApplyStart(data, _myUserId);

            InitScene(data, _amIP1);
            Debug.Log($"[Battle] Battle Started! ID: {_battleId}, MyUser: {_myUserId}");
        }

        private void Update()
        {
            if (_sim == null)
            {
                return;
            }

            _sim.Tick();
            var ui = _sim.ReadUiState();

            if (ui.SelfHp != _lastSelfHp)
            {
                _lastSelfHp = ui.SelfHp;
                _view.UpdateHUD(ui.SelfHp, ui.SelfMaxHp, true);
            }

            if (ui.EnemyHp != _lastEnemyHp)
            {
                _lastEnemyHp = ui.EnemyHp;
                _view.UpdateHUD(ui.EnemyHp, ui.EnemyMaxHp, false);
            }

            // 回合提示（低频刷新，含倒计时）
            if (Time.unscaledTime >= _nextTurnLabelTime && !ui.Ended)
            {
                _nextTurnLabelTime = Time.unscaledTime + 0.5f;
                if (_view.LogText)
                {
                    if (!string.IsNullOrEmpty(_rejectMsg) && Time.unscaledTime < _rejectMsgUntil)
                    {
                        _view.LogText.text = $"⚠ {_rejectMsg}";
                    }
                    else
                    {
                        _view.LogText.text = ui.IsMyTurn
                            ? $"Your Turn ({ui.TurnRemaining:0}s)"
                            : "Opponent Turn";
                    }
                }

                if (ActionPanelVisible() != ui.IsMyTurn && !_isProcessingQueue)
                {
                    _view.SetInteractable(ui.IsMyTurn);
                }
            }
        }

        private bool ActionPanelVisible()
        {
            return _view.ActionPanel != null && _view.ActionPanel.activeSelf;
        }

        private void InitScene(BattleStartResponse data, bool amIP1)
        {
            // 协议 v2 无角色实体（注册即默认战士），职业显示直接取配置字典首模板；
            // 服务端补多职业后应改为从盘面/资料下发 charType
            string p1CharType = DefaultCharType();
            string p2CharType = DefaultCharType();

            _view.InitScene(p1CharType, p2CharType, amIP1);

            var myData = amIP1 ? data.Player1 : data.Player2;
            var enemyData = amIP1 ? data.Player2 : data.Player1;

            _view.UpdateHUD(myData.CurrentHp, myData.MaxHp, true);
            _view.UpdateHUD(enemyData.CurrentHp, enemyData.MaxHp, false);

            GenerateSkillButtons(myData);
            CheckTurn(data.CurrentActorUserId);
        }

        /// <summary>配置字典中的默认职业（当前服务端注册即送首职业；查不到兜底 warrior）。</summary>
        private static string DefaultCharType()
        {
            var templates = GameEntry.Instance.GameData.Templates;
            return templates != null && templates.Count > 0 ? templates[0].CharType : "warrior";
        }

        /// <summary>可用技能 = Sim 世界的 CD 实体（Remain==0 即可用）；按 gamedata 渲染名称。</summary>
        private void GenerateSkillButtons(BattlePlayerData myData)
        {
            if (_view.SkillContainer == null || _view.SkillButtonTemplate == null || _sim == null)
            {
                return;
            }

            foreach (Transform child in _view.SkillContainer) Destroy(child.gameObject);

            var cooldowns = _sim.GetCooldowns();
            var cdMap = new Dictionary<int, int>();
            foreach (var (skillId, remain) in cooldowns)
            {
                cdMap[skillId] = remain;
            }

            foreach (int id in myData.OwnedSkillIds)
            {
                var config = GameEntry.Instance.GameData.GetSkill(id);
                if (config == null)
                {
                    // 配置缺失（未加载/表里没有）不能直接 continue：否则战斗里一个技能按钮都没有，
                    // 玩家只能挨到回合超时判负。降级为可点击的兜底按钮。
                    Debug.LogWarning($"[Battle] Skill ID {id} not in gamedata, rendering fallback button");
                }

                GameObject btnObj = Instantiate(_view.SkillButtonTemplate, _view.SkillContainer);
                btnObj.SetActive(true);

                Text txt = btnObj.GetComponentInChildren<Text>();
                Button btn = btnObj.GetComponent<Button>();
                Text descTxt = btnObj.transform.Find("DescText")?.GetComponent<Text>();
                if (descTxt)
                {
                    descTxt.text = config != null
                        ? $"[{config.SkillType}]\npower {config.Power} cd {config.Cooldown}"
                        : "(config missing)";
                }

                string skillName = config != null ? config.SkillName : $"Skill {id}";
                bool usable = !cdMap.TryGetValue(id, out var cd) || cd <= 0;

                if (!usable)
                {
                    btn.interactable = false;
                    if (txt) txt.text = $"{skillName} ({cd})";
                }
                else
                {
                    btn.interactable = true;
                    if (txt) txt.text = skillName;
                }

                int skillId = id;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => SendAction(ActionType.UseSkill, skillId));
            }
        }

        private void SendAction(ActionType actionType, int paramId)
        {
            _view.SetInteractable(false);
            GameEntry.Instance.Battles.Action(_battleId, actionType, paramId);
        }

        private void OnDefendClick() => SendAction(ActionType.Defend, 0);

        private void OnItemButtonClick()
        {
            if (_view.BattleBagPanel) _view.BattleBagPanel.SetActive(true);
            RefreshBattleBag();
        }

        /// <summary>战斗背包：/economy/inventory 过滤 POTION，点击 → BATTLE_ACTION(USE_ITEM, itemId)。</summary>
        private async void RefreshBattleBag()
        {
            if (_view.BattleBagContainer == null || _view.BagItemTemplate == null)
            {
                return;
            }

            foreach (Transform child in _view.BattleBagContainer) Destroy(child.gameObject);

            var bag = await GameEntry.Instance.Rest
                .GetAsync<System.Collections.Generic.List<PotionDto>>("/economy/inventory").ConfigureAwait(true);
            if (!bag.IsOk || bag.Data == null)
            {
                Debug.LogWarning($"[Battle] inventory failed: {bag.Code} {bag.Message}");
                return;
            }

            foreach (var inv in bag.Data)
            {
                if (inv.Type != "POTION" || inv.Count <= 0)
                {
                    continue;
                }

                GameObject go = Instantiate(_view.BagItemTemplate, _view.BattleBagContainer);
                go.SetActive(true);

                var nameText = go.transform.Find("NameText")?.GetComponent<Text>();
                if (nameText) nameText.text = $"{inv.Name} x{inv.Count}";

                var btnTransform = go.transform.Find("ActionButton");
                if (btnTransform)
                {
                    Button btn = btnTransform.GetComponent<Button>();
                    int itemId = inv.ItemId;
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() =>
                    {
                        if (_view.BattleBagPanel) _view.BattleBagPanel.SetActive(false);
                        SendAction(ActionType.UseItem, itemId);
                    });
                }
            }

            var potionCount = bag.Data.FindAll(p => p.Type == "POTION" && p.Count > 0).Count;
            Debug.Log($"[Battle] bag potions rendered: {potionCount}");
        }

        private sealed class PotionDto
        {
            [Newtonsoft.Json.JsonProperty("itemId")] public int ItemId;
            [Newtonsoft.Json.JsonProperty("name")] public string Name;
            [Newtonsoft.Json.JsonProperty("type")] public string Type;
            [Newtonsoft.Json.JsonProperty("count")] public int Count;
        }

        private void OnSurrender()
        {
            GameEntry.Instance.Battles.Surrender(_battleId);
        }

        // ---------- 战斗聊天（v2：世界频道 + CHAT_PUSH 实时推送） ----------

        private void OnChatPush(GameClient.Proto.GameMessage msg)
        {
            var push = msg.ChatPush;
            if (push == null || _view.ChatContainer == null || _view.ChatMsgTemplate == null)
            {
                return;
            }

            GameObject go = Instantiate(_view.ChatMsgTemplate, _view.ChatContainer);
            go.SetActive(true);
            Text txt = go.GetComponent<Text>();
            if (!txt) txt = go.GetComponentInChildren<Text>();
            if (txt) txt.text = $"{push.FromName}: {push.Content}";
        }

        private async void OnSendBattleChat()
        {
            if (!_view.ChatInput)
            {
                return;
            }

            var content = _view.ChatInput.text?.Trim();
            if (string.IsNullOrEmpty(content))
            {
                return;
            }

            if (!RateLimiter.Shared.TryAcquire(RateLimiter.ChatMessage))
            {
                Debug.LogWarning("[Battle] chat rate limited (8 msgs / 10s)");
                return;
            }

            var result = await GameEntry.Instance.Rest
                .PostAsync<ChatSendResultDto>("/social/chat/world", new { content })
                .ConfigureAwait(true);
            Debug.Log($"[Battle] chat sent: ok={result.IsOk} {result.Message}");
            if (result.IsOk)
            {
                _view.ChatInput.text = "";
                // 自己的消息经 CHAT_PUSH 广播回流显示，无需本地追加
            }
        }

        // === 队列处理 ===

        private void EnqueueBattleUpdate(BattleUpdateResponse resp)
        {
            Debug.Log($"[Battle] Round {resp.CurrentRound} actor={resp.ActorUserId} type={resp.ActionType} dmg={resp.Damage} heal={resp.Heal}");

            // 快照进 Sim 世界（服务端权威覆盖 HP/CD/回合指针）
            _sim?.ApplyUpdate(resp);

            _updateQueue.Enqueue(resp);
            if (!_isProcessingQueue)
            {
                StartCoroutine(ProcessUpdateQueue());
            }
        }

        private IEnumerator ProcessUpdateQueue()
        {
            _isProcessingQueue = true;

            while (_updateQueue.Count > 0)
            {
                BattleUpdateResponse resp = _updateQueue.Dequeue();
                _view.SetInteractable(false);

                yield return StartCoroutine(PlayAnimationSequence(resp));

                var myData = _amIP1 ? resp.Player1 : resp.Player2;
                GenerateSkillButtons(myData);
                CheckTurn(resp.NextActorUserId);
            }

            _isProcessingQueue = false;
        }

        /// <summary>
        /// 动画序列：行动者动作 → 目标反馈 → 血条 → 死亡。
        /// 分派完全由 ActionType 强类型枚举驱动（协议 v2），不再解析服务端文本。
        /// </summary>
        private IEnumerator PlayAnimationSequence(BattleUpdateResponse resp)
        {
            bool isActorMe = resp.ActorUserId == _myUserId;
            bool isTargetMe = resp.TargetUserId == _myUserId;

            // 1. 行动者动作（ActionType 分派；heal 类技能查 gamedata 定性为施法）
            switch (resp.ActionType)
            {
                case ActionType.Defend:
                    yield return _view.PlayDefendAnim(isActorMe);
                    break;
                case ActionType.UseItem:
                    yield return _view.PlayItemAnim(isActorMe);
                    break;
                case ActionType.UseSkill:
                    var skill = GameEntry.Instance.GameData.GetSkill(resp.SkillId);
                    bool isHealSkill = skill != null && (skill.SkillType == "heal" || skill.SkillType == "buff");
                    if (isHealSkill)
                    {
                        yield return _view.PlayHealAnim(isActorMe, resp.Heal);
                    }
                    else
                    {
                        yield return _view.PlayAttackAnim(isActorMe);
                    }

                    break;
                default:
                    // 未指定动作（可能为系统驱动回合）——按数值兜底
                    if (resp.Damage > 0)
                    {
                        yield return _view.PlayAttackAnim(isActorMe);
                    }

                    break;
            }

            // 2. 目标反馈
            if (resp.Damage > 0)
            {
                Transform hitTarget = isTargetMe ? _view.PlayerSpawnPoint : _view.EnemySpawnPoint;
                _view.PlayParticleEffect(hitTarget.position + Vector3.up, Color.red);
                yield return _view.PlayHitAnim(isTargetMe, resp.Damage);
            }
            else if (resp.ActionType == ActionType.UseSkill && resp.Heal > 0)
            {
                // 攻击型技能无伤害 → 目标闪避（服务端 dodge 时 damage=0）
                var skill2 = GameEntry.Instance.GameData.GetSkill(resp.SkillId);
                if (skill2 == null || (skill2.SkillType != "heal" && skill2.SkillType != "buff"))
                {
                    yield return _view.PlayDodgeAnim(isTargetMe);
                }
            }

            // 3. 血条
            UpdateBattleState(resp);

            // 4. 死亡表现
            if (!resp.Player1.IsAlive) yield return _view.PlayDeathAnim(_amIP1);
            if (!resp.Player2.IsAlive) yield return _view.PlayDeathAnim(!_amIP1);
        }

        private void UpdateBattleState(BattleUpdateResponse resp)
        {
            var p1 = resp.Player1;
            var p2 = resp.Player2;

            if (_amIP1)
            {
                _view.UpdateHUD(p1.CurrentHp, p1.MaxHp, true);
                _view.UpdateHUD(p2.CurrentHp, p2.MaxHp, false);
            }
            else
            {
                _view.UpdateHUD(p2.CurrentHp, p2.MaxHp, true);
                _view.UpdateHUD(p1.CurrentHp, p1.MaxHp, false);
            }
        }

        private void CheckTurn(long nextActorId)
        {
            bool isMyTurn = nextActorId == _myUserId && !_ended;
            _view.SetInteractable(isMyTurn);
        }

        /// <summary>服务端拒绝动作（未拥有技能/冷却中/道具不足/未轮到）→ 提示 + 恢复操作面板。</summary>
        private void OnServerError(GameClient.Proto.GameMessage msg)
        {
            var e = msg.ErrorResponse;
            if (e == null)
            {
                return;
            }

            Debug.LogWarning($"[Battle] action rejected: code={e.Code} {e.Message}");
            _rejectMsg = string.IsNullOrEmpty(e.Message) ? e.Code.ToString() : e.Message;
            _rejectMsgUntil = Time.unscaledTime + 2.5f;

            if (_sim != null)
            {
                _view.SetInteractable(_sim.ReadUiState().IsMyTurn);
            }
        }

        private void HandleBattleEnd(BattleEndResponse resp)
        {
            _ended = true;
            _isProcessingQueue = false;
            _updateQueue.Clear();
            _sim?.MarkEnded();
            StartCoroutine(ShowEndSequence(resp));
        }

        private IEnumerator ShowEndSequence(BattleEndResponse resp)
        {
            bool iWon = resp.WinnerId == _myUserId;
            yield return _view.PlayVictoryAnim(iWon);

            // EndReason 强类型枚举 → 可读文案（查表，不用服务端字符串）
            _view.ShowResultPanel(iWon, ReasonText(resp.EndReason));

            yield return new WaitForSeconds(3.0f);
            SceneManager.LoadScene("MainScene");
        }

        private static string ReasonText(EndReason reason)
        {
            switch (reason)
            {
                case EndReason.Defeat: return "Opponent Defeated";
                case EndReason.Surrender: return "Opponent Surrendered";
                case EndReason.Timeout: return "Round Timeout";
                case EndReason.Disconnect: return "Opponent Disconnected";
                case EndReason.OpponentLeft: return "Opponent Left";
                default: return "Battle Over";
            }
        }
    }
}
