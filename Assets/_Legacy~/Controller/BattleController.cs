using GameClient.Proto;
using System.Collections;
using System.Collections.Generic;
using TurnBasedGame.Core;
using TurnBasedGame.Model;
using TurnBasedGame.Services;
using TurnBasedGame.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TurnBasedGame.Controller
{
    /// <summary>
    /// 战斗控制器，协调战斗逻辑和视图表现
    /// 处理战斗状态机、动画队列和聊天功能
    /// </summary>
    public class BattleController : MonoBehaviour
    {
        private BattleView _view;
        private string _battleId;
        private long _myUserId;
        private BattleStartResponse _startData;

        // 动画队列。网络消息可能瞬间收到多条（比如连续的反击），
        // 必须排队等待上一条动画播完才能处理下一条，防止画面跳变。
        private Queue<BattleUpdateResponse> _updateQueue = new Queue<BattleUpdateResponse>();
        private bool _isProcessingQueue = false;

        // 缓存背包数据
        private List<UserInventory> _battleBagCache;

        // 缓存技能冷却数据 (Key: SkillID, Value: 剩余回合)
        private Dictionary<int, int> _currentCooldowns = new Dictionary<int, int>();

        // 聊天刷新协程
        private Coroutine _chatPollCoroutine;

        /// <summary>
        /// 战斗控制器初始化：绑定事件、加载数据、设置UI
        /// 消息队列确保动画顺序执行，避免并发问题
        /// </summary>
        void Start()
        {
            // 1. 初始化引用
            _view = GetComponent<BattleView>();
            if (!_view) _view = FindObjectOfType<BattleView>();
            _view.AutoBindUI();

            _myUserId = AuthService.Instance.CurrentUserId;

            // 2. 绑定网络事件
            BattleService.Instance.OnBattleUpdate += EnqueueBattleUpdate;
            BattleService.Instance.OnBattleEnd += HandleBattleEnd;

            // 3. 绑定按钮
            if (_view.DefendButton) _view.DefendButton.onClick.AddListener(OnDefendClick);
            if (_view.ItemButton) _view.ItemButton.onClick.AddListener(OnItemButtonClick);
            if (_view.CloseBagButton) _view.CloseBagButton.onClick.AddListener(() => _view.BattleBagPanel.SetActive(false));
            if (_view.SurrenderButton) _view.SurrenderButton.onClick.AddListener(OnSurrender);

            // 4. 加载数据，启动协程
            // 必须先加载战斗初始数据（StartData），否则无法渲染战场
            if (BattleContext.StartData != null)
            {
                InitBattle(BattleContext.StartData);
            }
            else
            {
                // 异常保护：如果没有数据直接进战斗（比如直接按了Play），退回大厅
                Debug.LogError("[Battle] Missing StartData! Returning to Main.");
                SceneManager.LoadScene("MainScene");
            }

            // 绑定聊天事件
            if (_view.ToggleChatButton)
                _view.ToggleChatButton.onClick.AddListener(() =>
                    _view.ChatPanel.SetActive(!_view.ChatPanel.activeSelf));

            if (_view.SendChatButton)
                _view.SendChatButton.onClick.AddListener(OnSendBattleChat);

            // 默认开启聊天框
            if (_view.ChatPanel) _view.ChatPanel.SetActive(true);

            // 开启聊天轮询 (每3秒拉取一次)
            _chatPollCoroutine = StartCoroutine(ChatPollLoop());
        }

        void OnDestroy()
        {
            BattleService.Instance.OnBattleUpdate -= EnqueueBattleUpdate;
            BattleService.Instance.OnBattleEnd -= HandleBattleEnd;
            if (_chatPollCoroutine != null) StopCoroutine(_chatPollCoroutine);
        }

        /// <summary>
        /// 初始化战斗数据：确定玩家位置、设置血条、生成技能按钮
        /// 根据服务器数据判断玩家是P1还是P2
        /// </summary>
        private void InitBattle(BattleStartResponse data)
        {
            _startData = data;
            _battleId = data.BattleId;
            bool amIP1 = data.Player1.UserId == _myUserId;

            // 启动协程去获取双方的职业
            StartCoroutine(LoadProfilesAndInitScene(data, amIP1));

            Debug.Log($"[Battle] <color=yellow>Battle Started!</color> ID: {_battleId}, MyUser: {_myUserId}");
        }

        /// <summary>
        /// 获取职业并初始化场景
        /// </summary>
        private IEnumerator LoadProfilesAndInitScene(BattleStartResponse data, bool amIP1)
        {
            string p1CharType = "warrior";
            string p2CharType = "warrior";
            bool p1Loaded = false;
            bool p2Loaded = false;

            // 并发请求双方的个人资料
            StartCoroutine(SocialService.Instance.GetUserProfile(data.Player1.UserId, (success, profile) => {
                if (success && profile != null && profile.character != null) p1CharType = profile.character.charType;
                p1Loaded = true;
            }));

            StartCoroutine(SocialService.Instance.GetUserProfile(data.Player2.UserId, (success, profile) => {
                if (success && profile != null && profile.character != null) p2CharType = profile.character.charType;
                p2Loaded = true;
            }));

            // 等待两个 HTTP 请求都完成
            yield return new WaitUntil(() => p1Loaded && p2Loaded);

            // 1. 初始化 3D 场景 (传入职业)
            _view.InitScene(p1CharType, p2CharType, amIP1);

            // 2. 初始化 UI
            var myData = amIP1 ? data.Player1 : data.Player2;
            var enemyData = amIP1 ? data.Player2 : data.Player1;

            _view.UpdateHUD(myData.CurrentHp, myData.MaxHp, true);
            _view.UpdateHUD(enemyData.CurrentHp, enemyData.MaxHp, false);

            UpdateCooldowns(myData.Cooldowns);
            GenerateSkillButtons();

            CheckTurn(data.CurrentActorUserId);
        }

        /// <summary>
        /// 更新冷却缓存
        /// </summary>
        private void UpdateCooldowns(Google.Protobuf.Collections.MapField<int, int> serverCooldowns)
        {
            _currentCooldowns.Clear();
            foreach (var kvp in serverCooldowns)
            {
                _currentCooldowns[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// 动态生成技能按钮，根据冷却状态设置交互性
        /// 技能ID列表从服务器冷却数据中获取，确保与后端一致
        /// </summary>
        private void GenerateSkillButtons()
        {
            // 清空旧按钮
            foreach (Transform child in _view.SkillContainer) Destroy(child.gameObject);

            List<int> skillIds = new List<int>(_currentCooldowns.Keys);

            // 排序，保证按钮顺序固定（比如按 ID 从小到大，或者你可以按配置表里的顺序）
            skillIds.Sort();

            // 根据服务器下发的冷却数据（Cooldowns）刷新按钮状态
            // 如果 CD > 0，则禁用按钮并显示剩余回合数
            foreach (int id in skillIds)
            {
                var config = ConfigManager.Instance.GetSkill(id);
                if (config == null)
                {
                    Debug.LogWarning($"[Battle] Skill ID {id} exists in server data but not in ConfigManager!");
                    continue;
                }

                GameObject btnObj = Instantiate(_view.SkillButtonTemplate, _view.SkillContainer);
                btnObj.SetActive(true);

                Text txt = btnObj.GetComponentInChildren<Text>();
                Button btn = btnObj.GetComponent<Button>();

                // 查找详情文本框并赋值
                // 假设模板里加了一个叫 DescText 的文本
                Text descTxt = btnObj.transform.Find("DescText")?.GetComponent<Text>();
                if (descTxt)
                {
                    // 显示类型和描述，例如："[attack] 造成攻击力150%的伤害"
                    descTxt.text = $"[{config.type}]\n{config.description}";
                }

                int cd = _currentCooldowns.ContainsKey(id) ? _currentCooldowns[id] : 0;

                if (cd > 0)
                {
                    btn.interactable = false;
                    if (txt) txt.text = $"{config.name} ({cd})";
                }
                else
                {
                    btn.interactable = true;
                    if (txt) txt.text = config.name;
                }

                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    BattleService.Instance.SendBattleAction(_battleId, 1, id);
                    _view.SetInteractable(false);
                });
            }
        }

        private void OnDefendClick()
        {
            BattleService.Instance.SendBattleAction(_battleId, 2, 0);
            _view.SetInteractable(false);
        }

        private void OnItemButtonClick()
        {
            // 先打开面板，让玩家看到响应
            if (_view.BattleBagPanel) _view.BattleBagPanel.SetActive(true);
            else Debug.LogError("[Battle] BattleBagPanel is NULL!");

            RefreshBattleBag();
        }

        private void RefreshBattleBag()
        {
            if (_view.BattleBagContainer == null) return;

            // 清空
            foreach (Transform child in _view.BattleBagContainer) Destroy(child.gameObject);

            StartCoroutine(ShopService.Instance.GetInventory((success, list) =>
            {
                if (!success || list == null) return;
                _battleBagCache = list;

                foreach (var inv in list)
                {
                    if (inv.item == null) continue;
                    int itemId = inv.item.id;
                    var config = ConfigManager.Instance.GetItem(itemId);

                    // 只显示药水
                    if (config != null && config.type == "POTION" && inv.count > 0)
                    {
                        if (_view.BagItemTemplate == null) return;

                        GameObject go = Instantiate(_view.BagItemTemplate, _view.BattleBagContainer);
                        go.SetActive(true);

                        // 查找 Icon 组件
                        var icon = go.transform.Find("Icon")?.GetComponent<Image>();
                        if (icon)
                        {
                            icon.sprite = ResourceManager.Instance.GetItemIcon(itemId);
                            icon.color = Color.white;
                        }

                        var nameText = go.transform.Find("NameText")?.GetComponent<Text>();
                        if (nameText) nameText.text = $"{config.name} x{inv.count}";
                        else Debug.LogWarning("[Battle] NameText not found in BagItemTemplate");

                        // 精确查找 ActionButton
                        var btnTransform = go.transform.Find("ActionButton");
                        if (btnTransform)
                        {
                            Button btn = btnTransform.GetComponent<Button>();
                            btn.onClick.RemoveAllListeners();
                            btn.onClick.AddListener(() =>
                            {
                                BattleService.Instance.SendBattleAction(_battleId, 3, itemId);
                                if (_view.BattleBagPanel) _view.BattleBagPanel.SetActive(false);
                                _view.SetInteractable(false);
                            });
                        }
                        else
                        {
                            Debug.LogError("[Battle] ActionButton not found in BagItemTemplate! Click won't work.");
                        }
                    }
                }
            }));
        }

        // === 队列处理逻辑 ===
        /// <summary>
        /// 将战斗更新加入队列，保证动画按顺序播放
        /// 避免快速连续操作导致的动画错乱
        /// </summary>
        private void EnqueueBattleUpdate(BattleUpdateResponse resp)
        {
            Debug.Log($"[Battle] Turn Update - Round: {resp.CurrentRound}, Actor: {resp.ActorUserId}, Skill: {resp.SkillName}, Dmg: {resp.Damage}");

            _updateQueue.Enqueue(resp);
            if (!_isProcessingQueue)
            {
                StartCoroutine(ProcessUpdateQueue());
            }
        }

        /// <summary>
        /// 处理战斗更新队列，顺序播放动画并更新状态
        /// 在动画播放完成后才更新技能冷却和切换回合
        /// </summary>
        private IEnumerator ProcessUpdateQueue()
        {
            _isProcessingQueue = true;

            while (_updateQueue.Count > 0)
            {
                BattleUpdateResponse resp = _updateQueue.Dequeue();
                // 锁定输入，防止动画播放期间玩家进行操作
                _view.SetInteractable(false);

                // 等待动画序列完全播完（包含移动、特效、飘字）
                yield return StartCoroutine(PlayAnimationSequence(resp));

                // 动画结束后，再更新血条和冷却状态
                bool amIP1 = _startData.Player1.UserId == _myUserId;
                var myData = amIP1 ? resp.Player1 : resp.Player2;
                UpdateCooldowns(myData.Cooldowns);
                GenerateSkillButtons();

                // 切换回合
                CheckTurn(resp.NextActorUserId);
            }

            _isProcessingQueue = false;
        }

        /// <summary>
        /// 解析战斗相应并播放完整的动画序列：行动动画 → 数值效果 → 血条更新 → 死亡检测
        /// 根据消息类型决定播放哪种动画（攻击/闪避/防御/治疗）
        /// </summary>
        private IEnumerator PlayAnimationSequence(BattleUpdateResponse resp)
        {
            // 过滤非战斗逻辑的系统消息
            if (IsSystemMessage(resp.SkillName)) yield break;

            bool isActorMe = resp.ActorUserId == _myUserId;
            bool isTargetMe = resp.TargetUserId == _myUserId;

            // 1. 解析动画策略
            ActorAnimType actorAnim = DetermineActorAnim(resp);
            TargetReactionType targetReaction = DetermineTargetReaction(resp, actorAnim);

            // 2. 执行行动者动画
            switch (actorAnim)
            {
                case ActorAnimType.Defend:
                    yield return _view.PlayDefendAnim(isActorMe);
                    break;
                case ActorAnimType.Item:
                    yield return _view.PlayItemAnim(isActorMe);
                    break;
                case ActorAnimType.Cast:
                    // 治疗动作复用施法表现
                    yield return _view.PlayHealAnim(isActorMe, resp.Heal);
                    break;
                case ActorAnimType.Attack:
                    yield return _view.PlayAttackAnim(isActorMe);
                    break;
            }

            // 3. 执行目标反馈动画（含粒子特效）
            switch (targetReaction)
            {
                case TargetReactionType.Hit:
                    Transform hitTarget = isTargetMe ? _view.PlayerSpawnPoint : _view.EnemySpawnPoint;
                    _view.PlayParticleEffect(hitTarget.position + Vector3.up, Color.red);
                    yield return _view.PlayHitAnim(isTargetMe, resp.Damage);
                    break;

                case TargetReactionType.Dodge:
                    yield return _view.PlayDodgeAnim(isTargetMe);
                    break;

                case TargetReactionType.Heal:
                    // 若行动者非施法动作（如吸血攻击），需额外播放治疗反馈
                    if (actorAnim != ActorAnimType.Cast)
                    {
                        Transform healTarget = isActorMe ? _view.PlayerSpawnPoint : _view.EnemySpawnPoint;
                        _view.PlayParticleEffect(healTarget.position + Vector3.up, Color.green);
                        yield return _view.PlayHealAnim(isActorMe, resp.Heal);
                    }
                    break;
            }

            // 4. 更新数值状态与死亡结算
            UpdateBattleState(resp);
            yield return CheckDeath(resp);
        }

        /// <summary>
        /// 判断是否为系统控制消息
        /// </summary>
        private bool IsSystemMessage(string skillName)
        {
            return skillName == "准备就绪" || skillName == "Battle Start" || skillName == "Wait";
        }

        /// <summary>
        /// 根据响应数据推断行动者的动作类型
        /// </summary>
        private ActorAnimType DetermineActorAnim(BattleUpdateResponse resp)
        {
            // 优先匹配特殊指令
            if (resp.SkillName == "防御") return ActorAnimType.Defend;

            // 匹配道具关键字
            if (resp.SkillName.Contains("药水") || resp.SkillName.Contains("Potion")) return ActorAnimType.Item;

            // 尝试从配置中获取技能类型
            var skillConfig = ConfigManager.Instance.GetSkill(resp.SkillId);
            if (skillConfig != null)
            {
                if (skillConfig.type == "heal" || skillConfig.type == "buff") return ActorAnimType.Cast;
            }

            // 根据数值结果兜底推断
            if (resp.Damage > 0) return ActorAnimType.Attack;
            if (resp.Heal > 0) return ActorAnimType.Cast;

            // 默认行为
            return ActorAnimType.Attack;
        }

        /// <summary>
        /// 根据行动类型和数值推断目标的反应类型
        /// </summary>
        private TargetReactionType DetermineTargetReaction(BattleUpdateResponse resp, ActorAnimType actorAction)
        {
            if (resp.Damage > 0) return TargetReactionType.Hit;

            // 攻击动作但无伤害，判定为闪避
            if (actorAction == ActorAnimType.Attack && resp.Damage == 0) return TargetReactionType.Dodge;

            if (resp.Heal > 0) return TargetReactionType.Heal;

            return TargetReactionType.None;
        }

        /// <summary>
        /// 更新双方血条UI
        /// </summary>
        private void UpdateBattleState(BattleUpdateResponse resp)
        {
            bool amIP1 = _startData.Player1.UserId == _myUserId;
            var p1 = resp.Player1;
            var p2 = resp.Player2;

            if (amIP1)
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

        /// <summary>
        /// 检查并播放死亡动画
        /// </summary>
        private IEnumerator CheckDeath(BattleUpdateResponse resp)
        {
            bool amIP1 = _startData.Player1.UserId == _myUserId;
            if (!resp.Player1.IsAlive) yield return _view.PlayDeathAnim(amIP1);
            if (!resp.Player2.IsAlive) yield return _view.PlayDeathAnim(!amIP1);
        }

        /// <summary>
        /// 初始回合设置
        /// </summary>
        private void CheckTurn(long nextActorId)
        {
            bool isMyTurn = nextActorId == _myUserId;
            _view.SetInteractable(isMyTurn);
        }

        /// <summary>
        /// 战斗结束处理
        /// </summary>
        private void HandleBattleEnd(BattleEndResponse resp)
        {
            // 1. 停止接收新消息，但不要杀掉正在跑的协程
            _isProcessingQueue = false;
            _updateQueue.Clear();

            // 2. 启动结算流程
            StartCoroutine(ShowEndSequence(resp));
        }

        /// <summary>
        /// 结算流程协程
        /// </summary>
        private IEnumerator ShowEndSequence(BattleEndResponse resp)
        {
            bool iWon = resp.WinnerId == _myUserId;

            // 1. 播放胜利者动画
            // 注意：如果此时还在播攻击动画，可能会重叠，但通常 BattleEnd 是最后一条消息
            yield return _view.PlayVictoryAnim(iWon);

            // 2. 显示结算 UI (带原因)
            _view.ShowResultPanel(iWon, resp.EndReason);

            // 3. 等待 3 秒
            yield return new WaitForSeconds(3.0f);

            // 4. 返回大厅
            SceneManager.LoadScene("MainScene");
        }

        /// <summary>
        /// 发送战斗聊天
        /// </summary>
        private void OnSendBattleChat()
        {
            if (!_view.ChatInput) return;
            string content = _view.ChatInput.text;
            if (string.IsNullOrEmpty(content)) return;

            // 调用新的 Redis 接口
            StartCoroutine(SendRedisChat(content));
        }

        /// <summary>
        /// 轮询聊天信息
        /// </summary>
        private IEnumerator ChatPollLoop()
        {
            while (true)
            {
                //if (_view.ChatPanel && _view.ChatPanel.activeSelf)
                RefreshChatList();
                yield return new WaitForSeconds(3.0f);
            }
        }

        private void RefreshChatList()
        {
            StartCoroutine(GetRedisChat());
        }

        private void OnSurrender()
        {
            BattleService.Instance.SendBattleSurrender(_battleId);
        }

        private IEnumerator SendRedisChat(string content)
        {
            var body = new { battleId = _battleId, content = content };
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(body);

            // 手动构造请求
            string url = $"{AppConfig.ApiBaseUrl}/battle/chat/send";
            using (UnityEngine.Networking.UnityWebRequest req = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
                req.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                req.certificateHandler = AppConfig.GetCertificateHandler();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + AuthService.Instance.CurrentToken);

                yield return req.SendWebRequest();

                if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    _view.ChatInput.text = "";
                    RefreshChatList(); // 立即刷新
                }
            }
        }

        private IEnumerator GetRedisChat()
        {
            string url = $"{AppConfig.ApiBaseUrl}/battle/chat/list?battleId={_battleId}";
            using (UnityEngine.Networking.UnityWebRequest req = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                req.certificateHandler = AppConfig.GetCertificateHandler();
                // 项目的Redis 接口不需要 Token，但还是带上
                yield return req.SendWebRequest();

                if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    var res = Newtonsoft.Json.JsonConvert.DeserializeObject<ApiResponse<List<string>>>(req.downloadHandler.text);
                    if (res != null && res.success && res.data != null)
                    {
                        UpdateChatUI(res.data);
                    }
                }
            }
        }

        private void UpdateChatUI(List<string> messages)
        {
            // 清空旧消息
            foreach (Transform child in _view.ChatContainer) Destroy(child.gameObject);

            // Redis 返回的是字符串列表 ["Player1: Hello", "Player2: Hi"]
            // 显示最近 6 条
            int start = Mathf.Max(0, messages.Count - 6);
            for (int i = start; i < messages.Count; i++)
            {
                GameObject go = Instantiate(_view.ChatMsgTemplate, _view.ChatContainer);
                go.SetActive(true);
                Text txt = go.GetComponent<Text>();
                if (!txt) txt = go.GetComponentInChildren<Text>();

                txt.text = messages[i]; // 直接显示
            }
        }

        public static class BattleContext
        {
            public static BattleStartResponse StartData;
        }
    }
}