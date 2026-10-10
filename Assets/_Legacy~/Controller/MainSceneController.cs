using GameClient.Proto;
using System.Collections.Generic;
using TurnBasedGame.Model;
using TurnBasedGame.Services;
using TurnBasedGame.View;
using UnityEngine;
using UnityEngine.UI;

namespace TurnBasedGame.Controller
{
    /// <summary>
    /// 主场景控制器，协调大厅所有功能模块
    /// 处理UI交互、数据刷新和场景切换
    /// </summary>
    public class MainSceneController : MonoBehaviour
    {
        private MainSceneView _view;
        private int _currentGold = 1000;

        void Start()
        {
            try
            {
                _view = GetComponent<MainSceneView>();
                if (!_view) _view = FindObjectOfType<MainSceneView>();

                // 强制绑定
                _view.AutoBindUI();

                // 绑定所有按钮事件
                BindEvents();

                // 初始化界面状态
                CloseAllPanels();
                RefreshTopBar();

                // 强制显示主菜单
                if (_view.TopBar) _view.TopBar.SetActive(true);
                if (_view.MainMenu) _view.MainMenu.SetActive(true);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MainScene] Init Error: {e.Message}");
            }

            // 确保配置加载
            if (!ConfigManager.Instance.IsLoaded)
                StartCoroutine(ConfigManager.Instance.LoadAllConfigs(null));

            // 注册战斗事件
            BattleService.Instance.OnMatchSuccess += OnMatchSuccess;
            BattleService.Instance.OnBattleStart += OnBattleStart;
        }

        void OnDestroy()
        {
            BattleService.Instance.OnMatchSuccess -= OnMatchSuccess;
            BattleService.Instance.OnBattleStart -= OnBattleStart;
        }

        private void BindEvents()
        {
            // 菜单按钮
            if (_view.ShopButton) _view.ShopButton.onClick.AddListener(OnOpenShop);
            if (_view.BagButton) _view.BagButton.onClick.AddListener(OnOpenBag);
            if (_view.RankButton) _view.RankButton.onClick.AddListener(OnOpenRank);
            if (_view.MatchButton) _view.MatchButton.onClick.AddListener(OnStartMatch);
            if (_view.FriendButton) _view.FriendButton.onClick.AddListener(OnOpenFriends);
            if (_view.MessageButton) _view.MessageButton.onClick.AddListener(OnOpenMessages);
            if (_view.TopBarButton) _view.TopBarButton.onClick.AddListener(OnOpenProfile);
            if (_view.ProfileHistoryButton) _view.ProfileHistoryButton.onClick.AddListener(OnOpenHistory);

            // 关闭按钮
            if (_view.CloseShopButton) _view.CloseShopButton.onClick.AddListener(CloseAllPanels);
            if (_view.CloseBagButton) _view.CloseBagButton.onClick.AddListener(CloseAllPanels);
            if (_view.CloseRankButton) _view.CloseRankButton.onClick.AddListener(CloseAllPanels);
            if (_view.CloseProfileButton) _view.CloseProfileButton.onClick.AddListener(CloseAllPanels);
            if (_view.CloseFriendButton) _view.CloseFriendButton.onClick.AddListener(CloseAllPanels);
            if (_view.CloseMessageButton) _view.CloseMessageButton.onClick.AddListener(CloseAllPanels);
            if (_view.CloseHistoryButton) _view.CloseHistoryButton.onClick.AddListener(() => _view.ShowPanel("Profile"));

            // 功能按钮
            if (_view.AddFriendButton) _view.AddFriendButton.onClick.AddListener(OnAddFriend);
            if (_view.SendMsgButton) _view.SendMsgButton.onClick.AddListener(OnSendMessage);
        }

        private void RefreshTopBar()
        {
            if (AuthService.Instance == null) return;
            string nick = AuthService.Instance.CurrentNickname ?? "Player";
            int gold = AuthService.Instance.CurrentGold;
            int elo = AuthService.Instance.CurrentElo;
            _currentGold = gold;
            _view.UpdateTopBar(nick, gold, elo);
        }

        private void CloseAllPanels() => _view.ShowPanel("");

        // ================= 商店逻辑 =================
        /// <summary>
        /// 打开商店：先获取背包数据 → 构建已拥有物品集合 → 渲染商品列表
        /// 关键逻辑：已拥有的唯一物品（如头像框）显示为"Owned"且不可购买
        /// </summary>
        private void OnOpenShop()
        {
            _view.ShowPanel("Shop");
            _view.ClearList();

            if (_view.ShopContainer == null || _view.ItemTemplate == null) return;

            // 1. 先请求背包，获取已拥有的物品 ID 集合
            StartCoroutine(ShopService.Instance.GetInventory((success, inventoryList) =>
            {
                // 使用 HashSet 快速查找已拥有的 ItemID
                HashSet<int> ownedItemIds = new HashSet<int>();
                if (success && inventoryList != null)
                {
                    foreach (var inv in inventoryList)
                    {
                        if (inv.item != null) ownedItemIds.Add(inv.item.id);
                    }
                }

                // 2. 遍历所有商品配置，渲染商店列表
                foreach (var kvp in ConfigManager.Instance.ItemDict)
                {
                    var config = kvp.Value;
                    GameObject go = _view.SpawnItem(_view.ShopContainer, _view.ItemTemplate);
                    if (!go) continue;

                    var nameText = SafeFindText(go, "NameText");
                    var descText = SafeFindText(go, "DescText");
                    var icon = SafeFindImage(go, "Icon");
                    var btn = go.transform.Find("ActionButton")?.GetComponent<Button>();

                    if (nameText) nameText.text = config.name;
                    if (descText) descText.text = $"{config.description}\nPrice: {config.price}";

                    if (icon)
                    {
                        icon.sprite = ResourceManager.Instance.GetItemIcon(config.id);
                        icon.color = Color.white;
                    }

                    if (btn)
                    {
                        var btnText = btn.GetComponentInChildren<Text>();

                        // 检查是否已拥有该物品
                        bool isOwned = ownedItemIds.Contains(config.id);
                        bool isUnique = config.type == "AVATAR_FRAME";

                        if (isUnique && isOwned)
                        {
                            // 如果是头像框且已拥有 -> 按钮变灰，显示 Owned
                            if (btnText) btnText.text = "Owned";
                            btn.interactable = false;
                        }
                        else
                        {
                            // 否则显示 Buy
                            if (btnText) btnText.text = "Buy";
                            btn.interactable = true;

                            btn.onClick.RemoveAllListeners();
                            btn.onClick.AddListener(() =>
                            {
                                if (AuthService.Instance.CurrentGold < config.price) return;

                                StartCoroutine(ShopService.Instance.BuyItem(config.id, (buySuccess, msg) =>
                                {
                                    if (buySuccess)
                                    {
                                        int newGold = AuthService.Instance.CurrentGold - config.price;
                                        AuthService.Instance.UpdateGold(newGold);
                                        RefreshTopBar();

                                        // 购买成功后，刷新商店列表（让刚才买的变成 Owned）
                                        OnOpenShop();
                                    }
                                    else
                                    {
                                        Debug.LogError($"Buy Failed: {msg}");
                                    }
                                }));
                            });
                        }
                    }
                }
            }));
        }

        // ================= 背包逻辑 =================
        private void OnOpenBag()
        {
            _view.ShowPanel("Bag");
            _view.ClearList();
            if (_view.BagContainer == null) return;

            StartCoroutine(ShopService.Instance.GetInventory((success, inventoryList) =>
            {
                if (!success || inventoryList == null) return;

                foreach (var inv in inventoryList)
                {
                    if (inv.item == null) continue;
                    int itemId = inv.item.id;
                    var config = ConfigManager.Instance.GetItem(itemId);
                    if (config == null) continue;

                    // 构建 ViewModel
                    var vm = new InventoryItemViewModel
                    {
                        ItemId = itemId,
                        Count = inv.count,
                        IsEquipped = inv.isEquipped,
                        Name = config.name,
                        Description = config.description,
                        Type = config.type,
                        Icon = ResourceManager.Instance.GetItemIcon(itemId)
                    };

                    // 调用独立方法渲染
                    RenderInventoryItem(vm);
                }
            }));
        }

        // ================= 个人资料与战绩逻辑 =================
        /// <summary>
        /// 点击顶部条 -> 查看自己
        /// </summary>
        private void OnOpenProfile()
        {
            var auth = AuthService.Instance;

            var myProfile = new UserProfile
            {
                userId = auth.CurrentUserId,
                nickname = auth.CurrentNickname,
                eloRating = auth.CurrentElo,
                gold = auth.CurrentGold,
                avatarFrameId = auth.CurrentAvatarFrameId,
                totalBattles = auth.TotalBattles,
                winCount = auth.WinCount,
                winRate = auth.WinRate,
                character = auth.MyCharacter
            };

            ShowProfileUI(myProfile);
        }

        /// <summary>
        /// 请求并显示指定用户的详细资料
        /// </summary>
        private void InspectUser(long targetId)
        {
            // 若目标为当前登录用户，直接使用本地缓存数据
            if (targetId == AuthService.Instance.CurrentUserId)
            {
                OnOpenProfile();
                return;
            }

            // 请求服务端获取最新资料
            StartCoroutine(SocialService.Instance.GetUserProfile(targetId, (success, profile) =>
            {
                if (success && profile != null)
                {
                    ShowProfileUI(profile);
                }
                else
                {
                    Debug.LogError($"[MainScene] Failed to retrieve profile for user: {targetId}");
                }
            }));
        }

        /// <summary>
        /// 通用显示逻辑
        /// </summary>
        private void ShowProfileUI(UserProfile p)
        {
            _view.ShowPanel("Profile");

            if (_view.ProfileNameText) _view.ProfileNameText.text = p.nickname;
            if (_view.ProfileIDText) _view.ProfileIDText.text = $"ID: {p.userId}";

            // 统计数据
            if (_view.ProfileStatsText)
            {
                _view.ProfileStatsText.text =
                    $"ELO: {p.eloRating}\n" +
                    $"Gold: {p.gold}\n" +
                    $"Battles: {p.totalBattles}\n" +
                    $"Wins: {p.winCount}\n" +
                    $"Win Rate: {p.winRate}";
            }

            // 角色数值
            if (_view.ProfileCharStatsText)
            {
                var c = p.character;
                if (c != null)
                {
                    _view.ProfileCharStatsText.text =
                        $"<color=yellow>{c.charName}</color> (Lv.{c.level})\n" +
                        $"HP: {c.maxHp}   SPD: {c.speed}\n" +
                        $"ATK: {c.attack}   DEF: {c.defense}";
                }
                else
                {
                    _view.ProfileCharStatsText.text = "No Character Data";
                }
            }

            // 头像与头像框
            string charType = p.character != null ? p.character.charType : "warrior";

            if (_view.ProfileAvatarIcon)
            {
                _view.ProfileAvatarIcon.sprite = ResourceManager.Instance.GetAvatarSprite(charType);
                _view.ProfileAvatarIcon.color = Color.white;
            }

            var frameImg = SafeFindImage(_view.ProfilePanel, "ProfileFrame");
            if (frameImg)
            {
                var sprite = ResourceManager.Instance.GetFrameSprite(p.avatarFrameId);
                frameImg.sprite = sprite;
                frameImg.color = sprite == null ? Color.clear : Color.white;
            }

            // 绑定 History 按钮，查看该用户的战绩
            if (_view.ProfileHistoryButton)
            {
                _view.ProfileHistoryButton.onClick.RemoveAllListeners();
                _view.ProfileHistoryButton.onClick.AddListener(() =>
                {
                    OpenHistoryForUser(p.userId);
                });
            }
        }

        /// <summary>
        /// 带参数的战绩查询
        /// </summary>
        private void OpenHistoryForUser(long targetUserId)
        {
            _view.ShowPanel("History");
            _view.ClearList();

            StartCoroutine(SocialService.Instance.GetBattleHistory(targetUserId, (success, list) =>
            {
                // ... (复制之前的渲染逻辑，或者提取为 RenderHistoryList 方法) ...
                if (!success || list == null) return;
                foreach (var item in list)
                {
                    GameObject go = _view.SpawnItem(_view.HistoryContainer, _view.HistoryTemplate);
                    if (!go) continue;
                    var timeText = SafeFindText(go, "TimeText");
                    var infoText = SafeFindText(go, "InfoText");
                    var resultText = SafeFindText(go, "ResultText");

                    if (timeText) timeText.text = item.time;
                    if (infoText) infoText.text = $"Vs {item.opponentName} ({item.rounds} Rnds)";
                    if (resultText)
                    {
                        string sign = item.eloChange >= 0 ? "+" : "";
                        resultText.text = $"{item.result} ({sign}{item.eloChange})";
                        resultText.color = item.result == "WIN" ? Color.green : Color.red;
                    }
                }
            }));
        }

        private void OnOpenHistory()
        {
            _view.ShowPanel("History");
            _view.ClearList();

            // 获取当前查看的用户的 ID
            long targetUserId = AuthService.Instance.CurrentUserId;

            StartCoroutine(SocialService.Instance.GetBattleHistory(targetUserId, (success, list) =>
            {
                if (!success || list == null) return;

                foreach (var item in list)
                {
                    GameObject go = _view.SpawnItem(_view.HistoryContainer, _view.HistoryTemplate);
                    if (!go) continue;

                    var timeText = SafeFindText(go, "TimeText");
                    var infoText = SafeFindText(go, "InfoText");
                    var resultText = SafeFindText(go, "ResultText");

                    if (timeText) timeText.text = item.time;
                    if (infoText) infoText.text = $"Vs {item.opponentName} ({item.rounds} Rnds)";

                    if (resultText)
                    {
                        string sign = item.eloChange >= 0 ? "+" : "";
                        resultText.text = $"{item.result} ({sign}{item.eloChange})";
                        // 赢了绿色，输了红色
                        resultText.color = item.result == "WIN" ? Color.green : Color.red;
                    }
                }
            }));
        }

        /// <summary>
        /// 渲染背包物品项，根据物品类型显示不同UI
        /// 药水：显示数量，无操作按钮
        /// 头像框：显示装备状态，可点击装备
        /// </summary>
        private void RenderInventoryItem(InventoryItemViewModel vm)
        {
            GameObject go = _view.SpawnItem(_view.BagContainer, _view.ItemTemplate);
            if (!go) return;

            var nameText = SafeFindText(go, "NameText");
            var icon = SafeFindImage(go, "Icon");
            var descText = SafeFindText(go, "DescText");
            var btn = go.transform.Find("ActionButton")?.GetComponent<Button>();

            // 显示物品名称和数量
            if (nameText) nameText.text = $"{vm.Name} <color=yellow>x{vm.Count}</color>";

            if (icon)
            {
                icon.sprite = vm.Icon;
                icon.color = Color.white; // 强制重置为白色
            }

            // 根据物品类型区分显示逻辑：
            // - 药水：显示数量，不可在背包界面直接使用
            // - 头像框：显示“装备/未装备”按钮
            if (vm.Type == "POTION")
            {
                if (descText) descText.text = vm.Description;
                if (btn) btn.gameObject.SetActive(false);
            }
            else if (vm.Type == "AVATAR_FRAME")
            {
                if (descText) descText.text = vm.Description;
                if (btn)
                {
                    btn.gameObject.SetActive(true);
                    var btnText = btn.GetComponentInChildren<Text>();

                    if (vm.IsEquipped)
                    {
                        if (btnText) btnText.text = "Equipped";
                        btn.interactable = false;
                    }
                    else
                    {
                        if (btnText) btnText.text = "Equip";
                        btn.interactable = true;
                        btn.onClick.RemoveAllListeners(); // 防止重复绑定
                        btn.onClick.AddListener(() =>
                        {
                            StartCoroutine(ShopService.Instance.EquipItem(vm.ItemId, (s, m) =>
                            {
                                if (s)
                                {
                                    // 手动更新本地缓存
                                    AuthService.Instance.UpdateAvatarFrame(vm.ItemId);

                                    // 刷新背包界面
                                    OnOpenBag();
                                }
                            }));
                        });
                    }
                }
            }
        }

        // ================= 好友逻辑 (含在线状态) =================
        /// <summary>
        /// 打开好友列表，显示在线状态和头像
        /// </summary>
        private void OnOpenFriends()
        {
            _view.ShowPanel("Friend");
            _view.ClearList();

            StartCoroutine(SocialService.Instance.GetFriendList((success, list) =>
            {
                if (!success || list == null) return;
                foreach (var f in list)
                {
                    GameObject go = _view.SpawnItem(_view.FriendContainer, _view.FriendTemplate);
                    if (!go) continue;

                    var nameText = SafeFindText(go, "NameText");
                    var statusText = SafeFindText(go, "StatusText");
                    var avatar = SafeFindImage(go, "Avatar");
                    var msgBtn = go.transform.Find("MsgButton")?.GetComponent<Button>();

                    if (nameText) nameText.text = $"{f.nickname} (ID:{f.friendId})";

                    string onlineStr = f.online ? "<color=green>Online</color>" : "<color=grey>Offline</color>";
                    if (statusText) statusText.text = onlineStr;

                    if (avatar) avatar.sprite = ResourceManager.Instance.GetAvatarSprite(f.charType);

                    // 绑定私信按钮事件
                    if (msgBtn)
                    {
                        msgBtn.onClick.RemoveAllListeners();
                        msgBtn.onClick.AddListener(() => OpenMessagePanelWithTarget(f.friendId));
                    }

                    // 绑定列表项点击事件：查看好友详情
                    Button itemBtn = go.GetComponent<Button>();
                    if (itemBtn == null) itemBtn = go.AddComponent<Button>();

                    itemBtn.onClick.RemoveAllListeners();
                    itemBtn.onClick.AddListener(() => InspectUser(f.friendId));
                }
            }));
        }

        private void OnAddFriend()
        {
            if (!_view.FriendIdInput) return;
            if (long.TryParse(_view.FriendIdInput.text, out long fid))
            {
                StartCoroutine(SocialService.Instance.AddFriend(fid, (success, msg) =>
                {
                    if (success) OnOpenFriends();
                }));
            }
        }

        // ================= 排行榜逻辑 (含头像) =================
        /// <summary>
        /// 渲染排行榜列表
        /// </summary>
        private void OnOpenRank()
        {
            _view.ShowPanel("Rank");
            _view.ClearList();
            StartCoroutine(SocialService.Instance.GetLeaderboard((success, users) =>
            {
                if (!success) return;
                int rank = 1;
                foreach (var user in users)
                {
                    GameObject go = _view.SpawnItem(_view.RankContainer, _view.RankTemplate);
                    if (!go) continue;

                    var rt = SafeFindText(go, "RankText");
                    var nt = SafeFindText(go, "NameText");
                    var et = SafeFindText(go, "EloText");
                    var avatar = SafeFindImage(go, "Avatar");

                    if (rt) rt.text = rank++.ToString();
                    if (nt) nt.text = user.nickname;
                    if (et) et.text = user.eloRating.ToString();
                    if (avatar) avatar.sprite = ResourceManager.Instance.GetAvatarSprite(user.charType);

                    // 绑定列表项点击事件：查看玩家详情
                    Button itemBtn = go.GetComponent<Button>();
                    if (itemBtn == null) itemBtn = go.AddComponent<Button>();

                    itemBtn.onClick.RemoveAllListeners();
                    itemBtn.onClick.AddListener(() => InspectUser(user.id));
                }
            }));
        }

        // ================= 留言板逻辑 =================
        private void OnOpenMessages()
        {
            _view.ShowPanel("Message");
            _view.ClearList();

            StartCoroutine(SocialService.Instance.GetMessages((success, list) =>
            {
                if (!success || list == null) return;

                foreach (var m in list)
                {
                    GameObject go = _view.SpawnItem(_view.MessageContainer, _view.MessageTemplate);
                    if (!go) continue;

                    var senderText = SafeFindText(go, "SenderText");
                    var contentText = SafeFindText(go, "ContentText");

                    // 增加空值保护，防止 senderUser 为空导致报错中断
                    string name = "Unknown";
                    long uid = 0;

                    if (m.senderUser != null)
                    {
                        name = m.senderUser.nickname;
                        uid = m.senderUser.id;
                    }
                    else
                    {
                        // 如果后端返回的结构不对，尝试直接读 senderName
                        Debug.LogWarning($"Message {m.id} has null senderUser!");
                    }

                    if (senderText) senderText.text = $"{name} (ID:{uid})";
                    if (contentText) contentText.text = m.content;
                }
            }));
        }

        private void OnSendMessage()
        {
            if (!_view.MsgTargetIdInput || !_view.MsgContentInput) return;

            if (long.TryParse(_view.MsgTargetIdInput.text, out long tid))
            {
                string content = _view.MsgContentInput.text;
                if (string.IsNullOrEmpty(content)) return;

                StartCoroutine(SocialService.Instance.SendMessage(tid, content, (success, msg) =>
                {
                    if (success) _view.MsgContentInput.text = "";
                }));
            }
        }

        // ================= 匹配逻辑 =================
        private void OnStartMatch()
        {
            if (_view.MatchButton) _view.MatchButton.interactable = false;
            _view.UpdateTopBar("Matching...", _currentGold, AuthService.Instance.CurrentElo);
            BattleService.Instance.SendMatchRequest();
        }

        /// <summary>
        /// 匹配成功处理：显示确认面板，让用户选择接受或拒绝
        /// 弹出确认框显示对手信息，防止玩家误触匹配或在匹配期间离开
        /// </summary>
        private void OnMatchSuccess(MatchSuccessResponse resp)
        {
            if (_view.MatchReadyPanel)
            {
                _view.MatchReadyPanel.SetActive(true);

                // 查找按钮 (Accept / Decline)
                var acceptBtn = _view.MatchReadyPanel.transform.Find("AcceptButton")?.GetComponent<Button>();
                var declineBtn = _view.MatchReadyPanel.transform.Find("DeclineButton")?.GetComponent<Button>();
                var infoText = _view.MatchReadyPanel.transform.Find("InfoText")?.GetComponent<Text>();

                if (infoText) infoText.text = $"Opponent Found!\n{resp.Opponent.Nickname} (ELO: {resp.Opponent.EloRating})";

                if (acceptBtn)
                {
                    acceptBtn.onClick.RemoveAllListeners();
                    acceptBtn.onClick.AddListener(() =>
                    {
                        BattleService.Instance.SendBattleReady(resp.BattleId);
                        _view.MatchReadyPanel.SetActive(false);
                        _view.UpdateTopBar("Waiting for start...", _currentGold, AuthService.Instance.CurrentElo);
                    });
                }

                if (declineBtn)
                {
                    declineBtn.onClick.RemoveAllListeners();
                    declineBtn.onClick.AddListener(() =>
                    {
                        // 发送取消匹配 (虽然协议里可能没定义 Decline，这里仅做 UI 关闭)
                        _view.MatchReadyPanel.SetActive(false);
                        _view.MatchButton.interactable = true;
                        _view.UpdateTopBar("Match Cancelled", _currentGold, AuthService.Instance.CurrentElo);
                    });
                }
            }
            else
            {
                // 如果没做面板，还是自动进吧，防止卡死
                BattleService.Instance.SendBattleReady(resp.BattleId);
            }
        }

        private void OnBattleStart(BattleStartResponse resp)
        {
            BattleController.BattleContext.StartData = resp;
            UnityEngine.SceneManagement.SceneManager.LoadScene("BattleScene");
        }

        // ================= 辅助方法 =================
        private Text SafeFindText(GameObject root, string name)
        {
            if (root == null) return null;
            // 尝试在 root 及其所有子后代中查找名为 name 的物体
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) // true 表示包括隐藏物体
            {
                if (t.name == name) return t.GetComponent<Text>();
            }
            return null;
        }

        private Image SafeFindImage(GameObject root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t.GetComponent<Image>();
            }
            return null;
        }

        // 带参数打开留言板
        private void OpenMessagePanelWithTarget(long targetId)
        {
            _view.ShowPanel("Message");
            // 自动填入 ID
            if (_view.MsgTargetIdInput)
                _view.MsgTargetIdInput.text = targetId.ToString();

            // 刷新列表（查看我和他的对话，或者所有留言）
            OnOpenMessages();
        }
    }
}