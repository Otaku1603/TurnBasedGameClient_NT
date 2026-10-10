using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Network;
using Game.UI;
using GameClient.Proto;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.App
{
    /// <summary>
    /// 大厅控制器：顶栏资料、匹配流程、各功能面板（商店/背包/排行/好友/留言板/资料/战绩）。
    /// 所有列表渲染查 REST 数据（模板子节点与旧版一致）；发送节流走 RateLimiter。
    /// </summary>
    public sealed class MainSceneController : MonoBehaviour
    {
        private MainSceneView _view;
        private bool _matching;
        private int _lastElo;
        private long _lastGold;
        private IDisposable _errorSub;
        private IDisposable _chatSub;

        private async void Start()
        {
            _view = GetComponent<MainSceneView>();
            if (!_view) _view = FindObjectOfType<MainSceneView>();
            _view.AutoBindUI();

            // 主菜单
            if (_view.MatchButton) _view.MatchButton.onClick.AddListener(OnStartMatch);
            if (_view.ShopButton) _view.ShopButton.onClick.AddListener(OnOpenShop);
            if (_view.BagButton) _view.BagButton.onClick.AddListener(OnOpenBag);
            if (_view.RankButton) _view.RankButton.onClick.AddListener(OnOpenRank);
            if (_view.FriendButton) _view.FriendButton.onClick.AddListener(OnOpenFriends);
            if (_view.MessageButton) _view.MessageButton.onClick.AddListener(OpenWorldChat);

            // 顶栏 -> 个人资料
            if (_view.TopBarButton) _view.TopBarButton.onClick.AddListener(OnOpenProfile);

            // 关闭按钮统一回主菜单
            if (_view.CloseShopButton) _view.CloseShopButton.onClick.AddListener(CloseToMenu);
            if (_view.CloseBagButton) _view.CloseBagButton.onClick.AddListener(CloseToMenu);
            if (_view.CloseRankButton) _view.CloseRankButton.onClick.AddListener(CloseToMenu);
            if (_view.CloseProfileButton) _view.CloseProfileButton.onClick.AddListener(CloseToMenu);
            if (_view.CloseFriendButton) _view.CloseFriendButton.onClick.AddListener(CloseToMenu);
            if (_view.CloseMessageButton) _view.CloseMessageButton.onClick.AddListener(CloseToMenu);
            if (_view.CloseHistoryButton) _view.CloseHistoryButton.onClick.AddListener(() => _view.ShowPanel("Profile"));

            // 好友添加 / 留言发送
            if (_view.AddFriendButton) _view.AddFriendButton.onClick.AddListener(OnAddFriend);
            if (_view.SendMsgButton) _view.SendMsgButton.onClick.AddListener(OnSendWorldMessage);

            var battles = GameEntry.Instance.Battles;
            battles.MatchSuccess += OnMatchSuccess;
            battles.BattleStart += OnBattleStart;
            battles.BattleEnd += OnBattleEndInHall;

            // 服务端 ERROR 帧恢复匹配 UI（fire-and-forget 请求的失败只经 ERROR 回达）：
            // 匹配被拒（战斗中/封禁/限流）、匹配超时移除等——否则卡"Matching..."直到重启
            _errorSub = GameEntry.Instance.PushBus.Subscribe(GameClient.Proto.MessageType.Error, OnServerError);

            // 大厅世界聊天实时刷新（CHAT_PUSH 广播回流）
            _chatSub = GameEntry.Instance.PushBus.Subscribe(GameClient.Proto.MessageType.ChatPush, OnHallChatPush);

            await RefreshTopBarAsync();
        }

        private void OnDestroy()
        {
            if (GameEntry.Instance == null)
            {
                return;
            }

            var battles = GameEntry.Instance.Battles;
            battles.MatchSuccess -= OnMatchSuccess;
            battles.BattleStart -= OnBattleStart;
            battles.BattleEnd -= OnBattleEndInHall;
            _errorSub?.Dispose();
            _errorSub = null;
            _chatSub?.Dispose();
            _chatSub = null;
        }

        /// <summary>战斗中对手放弃/超时等，玩家被送回大厅：关闭滞留的匹配确认面板。</summary>
        private void OnBattleEndInHall(BattleEndResponse end)
        {
            _matching = false;
            if (_view.MatchReadyPanel)
            {
                _view.MatchReadyPanel.SetActive(false);
            }

            if (_view.MatchButton)
            {
                _view.MatchButton.interactable = true;
            }

            _ = RefreshTopBarAsync();
        }

        /// <summary>
        /// 服务端错误 → 恢复匹配状态。匹配类请求为 fire-and-forget，
        /// 失败（正在战斗中/被封禁/限流/超时移除）只经 ERROR 帧回达。
        /// </summary>
        private void OnServerError(GameClient.Proto.GameMessage msg)
        {
            var e = msg.ErrorResponse;
            if (e == null)
            {
                return;
            }

            // 匹配超时是服务端主动推送（requestId=0）；其余 ERROR 只要有可能源于匹配请求就复位
            _matching = false;
            if (_view.MatchReadyPanel)
            {
                _view.MatchReadyPanel.SetActive(false);
            }

            if (_view.MatchButton)
            {
                _view.MatchButton.interactable = true;
            }

            Debug.LogWarning($"[Main] match state reset by server error: code={e.Code} {e.Message}");
        }

        /// <summary>世界聊天推送 → Message 面板可见时实时追加。</summary>
        private void OnHallChatPush(GameClient.Proto.GameMessage msg)
        {
            var push = msg.ChatPush;
            if (push == null)
            {
                return;
            }

            var panel = _view.MessagePanel;
            bool panelVisible = panel != null && panel.activeSelf;
            if (panelVisible && _chatTargetId == 0)
            {
                AppendChatLine(push.FromName, push.FromId, push.Content);
            }
        }

        private void CloseToMenu()
        {
            _view.ClearList();
            _view.ForceShowMenu();
            _ = RefreshTopBarAsync();
        }

        // ---------- 顶栏 / 个人资料 ----------

        private async Task<ProfileDto> FetchMyProfileAsync()
        {
            var result = await GameEntry.Instance.Rest.GetAsync<ProfileDto>("/social/profile").ConfigureAwait(true);
            if (!result.IsOk)
            {
                Debug.LogWarning($"[Main] profile fetch failed: {result.Code} {result.Message}");
                return null;
            }

            return result.Data;
        }

        private async Task RefreshTopBarAsync()
        {
            var auth = GameEntry.Instance.Auth;
            // 先用最近一次已知值铺底：资料请求失败时不能把金币/Elo 显示成 0
            _view.UpdateTopBar(auth.Nickname, (int)_lastGold, _lastElo);

            var profile = await FetchMyProfileAsync().ConfigureAwait(true);
            if (profile != null)
            {
                _lastElo = profile.EloRating;
                _lastGold = profile.Gold ?? 0;
                _view.UpdateTopBar(profile.Nickname, (int)(profile.Gold ?? 0), profile.EloRating);
            }
        }

        private async void OnOpenProfile()
        {
            _view.ShowPanel("Profile");
            var p = await FetchMyProfileAsync().ConfigureAwait(true);
            if (p == null)
            {
                return;
            }

            Debug.Log($"[Main] profile: elo={p.EloRating} gold={p.Gold} battles={p.TotalBattles} wins={p.WinCount}");
            if (_view.ProfileNameText) _view.ProfileNameText.text = p.Nickname;
            if (_view.ProfileIDText) _view.ProfileIDText.text = $"ID: {p.UserId}";
            if (_view.ProfileStatsText)
            {
                _view.ProfileStatsText.text =
                    $"ELO: {p.EloRating}\n" +
                    $"Gold: {p.Gold}\n" +
                    $"Battles: {p.TotalBattles}\n" +
                    $"Wins: {p.WinCount}\n" +
                    $"Win Rate: {p.WinRate}";
            }

            // v2 无独立角色实体（注册默认战士），角色数值在战斗盘面体现
            if (_view.ProfileCharStatsText)
            {
                _view.ProfileCharStatsText.text = "Char: Warrior (default)";
            }

            // 头像（Resources/avatars/warrior.jpg）
            if (_view.ProfileAvatarIcon)
            {
                var avatar = Game.Presentation.AssetService.GetAvatar("warrior");
                _view.ProfileAvatarIcon.sprite = avatar;
                _view.ProfileAvatarIcon.color = avatar ? Color.white : Color.clear;
            }

            // 已装备头像框（/economy/inventory 中 equipped 的 AVATAR_FRAME）
            _ = RenderEquippedFrameAsync();

            if (_view.ProfileHistoryButton)
            {
                _view.ProfileHistoryButton.onClick.RemoveAllListeners();
                _view.ProfileHistoryButton.onClick.AddListener(OnOpenHistory);
            }
        }

        /// <summary>资料页渲染已装备头像框（背包中 equipped 的 AVATAR_FRAME）。</summary>
        private async Task RenderEquippedFrameAsync()
        {
            var frameImg = _view.ProfilePanel
                ? _view.ProfilePanel.transform.Find("ProfileFrame")?.GetComponent<Image>()
                : null;
            if (!frameImg)
            {
                return;
            }

            var bag = await GameEntry.Instance.Rest
                .GetAsync<List<InventoryDto>>("/economy/inventory").ConfigureAwait(true);
            Sprite frameSprite = null;
            if (bag.IsOk && bag.Data != null)
            {
                foreach (var inv in bag.Data)
                {
                    if (inv.Type == "AVATAR_FRAME" && inv.Equipped)
                    {
                        frameSprite = Game.Presentation.AssetService.GetFrame(inv.Name);
                        break;
                    }
                }
            }

            frameImg.sprite = frameSprite;
            frameImg.color = frameSprite ? Color.white : Color.clear;
        }

        // ---------- 商店 ----------

        private List<InventoryDto> _bagCache;

        private async void OnOpenShop()
        {
            _view.ShowPanel("Shop");
            _view.ClearList();
            if (_view.ShopContainer == null || _view.ItemTemplate == null)
            {
                return;
            }

            var items = await GameEntry.Instance.Rest.GetAsync<List<ShopItemDto>>("/shop/items").ConfigureAwait(true);
            var bag = await GameEntry.Instance.Rest.GetAsync<List<InventoryDto>>("/economy/inventory").ConfigureAwait(true);
            _bagCache = bag.IsOk ? bag.Data : new List<InventoryDto>();
            if (!items.IsOk || items.Data == null)
            {
                Debug.LogWarning($"[Main] shop items failed: {items.Code} {items.Message}");
                return;
            }

            Debug.Log($"[Main] shop: {items.Data.Count} items, bag: {_bagCache.Count} stacks");
            foreach (var item in items.Data)
            {
                var go = _view.SpawnItem(_view.ShopContainer, _view.ItemTemplate);
                if (!go)
                {
                    continue;
                }

                var nameText = go.transform.Find("NameText")?.GetComponent<Text>();
                var descText = go.transform.Find("DescText")?.GetComponent<Text>();
                var icon = go.transform.Find("Icon")?.GetComponent<Image>();
                var btn = go.transform.Find("ActionButton")?.GetComponent<Button>();

                if (nameText)
                {
                    nameText.text = item.Name;
                    nameText.color = TypeColor(item.Type); // ColorConfig 语义：类型→颜色
                }
                if (descText) descText.text = $"Type: {item.Type}  Effect: {item.EffectValue}\nPrice: {item.Price}";
                if (icon)
                {
                    icon.sprite = Game.Presentation.AssetService.GetItemIcon(item.Name);
                    icon.color = icon.sprite ? Color.white : Color.clear;
                }

                if (btn)
                {
                    var btnText = btn.GetComponentInChildren<Text>();
                    bool isOwned = _bagCache.Exists(b => b.ItemId == item.Id);
                    bool isUnique = item.Type == "AVATAR_FRAME";

                    if (isUnique && isOwned)
                    {
                        if (btnText) btnText.text = "Owned";
                        btn.interactable = false;
                    }
                    else
                    {
                        if (btnText) btnText.text = "Buy";
                        btn.interactable = true;
                        int itemId = item.Id;
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(() => _ = BuyAsync(itemId));
                    }
                }
            }
        }

        /// <summary>物品类型 → 行染色（POTION=绿 / AVATAR_FRAME=金 / 其他=白）。</summary>
        private static Color TypeColor(string type)
        {
            switch (type)
            {
                case "POTION": return new Color(0.55f, 1f, 0.55f);
                case "AVATAR_FRAME": return new Color(1f, 0.85f, 0.35f);
                default: return Color.white;
            }
        }

        private async Task BuyAsync(int itemId)
        {
            var orderNo = Guid.NewGuid().ToString("N");
            var result = await GameEntry.Instance.Rest
                .PostAsync<BuyResultDto>($"/shop/buy?itemId={itemId}&count=1&orderNo={orderNo}", null)
                .ConfigureAwait(true);
            if (result.IsOk)
            {
                Debug.Log($"[Main] bought {result.Data.ItemName} x{result.Data.Count}, gold left {result.Data.GoldLeft}");
                _lastGold = result.Data.GoldLeft;
                // 只更新金币，避免把顶栏 Elo 清零
                _view.UpdateTopBar(GameEntry.Instance.Auth.Nickname, (int)result.Data.GoldLeft, _lastElo);
                OnOpenShop();
            }
            else
            {
                Debug.LogWarning($"[Main] buy failed: {result.Code} {result.Message}");
            }
        }

        // ---------- 背包 ----------

        private void OnOpenBag()
        {
            _view.ShowPanel("Bag");
            _view.ClearList();
            if (_view.BagContainer == null || _view.ItemTemplate == null)
            {
                return;
            }

            _ = RenderBagAsync();
        }

        private async Task RenderBagAsync()
        {
            var bag = await GameEntry.Instance.Rest.GetAsync<List<InventoryDto>>("/economy/inventory").ConfigureAwait(true);
            if (!bag.IsOk || bag.Data == null)
            {
                Debug.LogWarning($"[Main] inventory failed: {bag.Code} {bag.Message}");
                return;
            }

            Debug.Log($"[Main] bag: {bag.Data.Count} stacks");
            foreach (var inv in bag.Data)
            {
                var go = _view.SpawnItem(_view.BagContainer, _view.ItemTemplate);
                if (!go)
                {
                    continue;
                }

                var nameText = go.transform.Find("NameText")?.GetComponent<Text>();
                var descText = go.transform.Find("DescText")?.GetComponent<Text>();
                var icon = go.transform.Find("Icon")?.GetComponent<Image>();
                var btn = go.transform.Find("ActionButton")?.GetComponent<Button>();

                if (nameText) nameText.text = $"{inv.Name} <color=yellow>x{inv.Count}</color>";
                if (descText) descText.text = inv.Type;
                if (icon)
                {
                    icon.sprite = Game.Presentation.AssetService.GetItemIcon(inv.Name);
                    icon.color = icon.sprite ? Color.white : Color.clear;
                }

                // POTION 无操作按钮；AVATAR_FRAME 显示 装备/卸下（/economy/equip）
                if (btn)
                {
                    if (inv.Type == "AVATAR_FRAME")
                    {
                        btn.gameObject.SetActive(true);
                        var btnText = btn.GetComponentInChildren<Text>();
                        if (btnText) btnText.text = inv.Equipped ? "Unequip" : "Equip";
                        btn.interactable = true;
                        int itemId = inv.ItemId;
                        bool target = !inv.Equipped;
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(() => _ = EquipAsync(itemId, target));
                    }
                    else
                    {
                        btn.gameObject.SetActive(false);
                    }
                }
            }
        }

        private async Task EquipAsync(int itemId, bool equip)
        {
            var result = await GameEntry.Instance.Rest
                .PostAsync<Dictionary<string, bool>>($"/economy/equip?itemId={itemId}&equip={equip.ToString().ToLower()}", null)
                .ConfigureAwait(true);
            Debug.Log($"[Main] equip {itemId} -> {equip}: ok={result.IsOk} {result.Message}");
            if (result.IsOk)
            {
                _ = RenderBagAsync();
            }
        }

        // ---------- 排行榜 ----------

        private void OnOpenRank()
        {
            _view.ShowPanel("Rank");
            _view.ClearList();
            if (_view.RankContainer == null || _view.RankTemplate == null)
            {
                return;
            }

            _ = RenderRankAsync();
        }

        private async Task RenderRankAsync()
        {
            var board = await GameEntry.Instance.Rest.GetAsync<RankBoardDto>("/rank/elo?limit=20").ConfigureAwait(true);
            if (!board.IsOk || board.Data?.Entries == null)
            {
                Debug.LogWarning($"[Main] rank failed: {board.Code} {board.Message}");
                return;
            }

            Debug.Log($"[Main] rank: {board.Data.Entries.Count} entries");
            foreach (var e in board.Data.Entries)
            {
                var go = _view.SpawnItem(_view.RankContainer, _view.RankTemplate);
                if (!go)
                {
                    continue;
                }

                var rt = go.transform.Find("RankText")?.GetComponent<Text>();
                var nt = go.transform.Find("NameText")?.GetComponent<Text>();
                var et = go.transform.Find("EloText")?.GetComponent<Text>();

                if (rt) rt.text = e.Rank.ToString();
                if (nt) nt.text = e.Nickname;
                if (et) et.text = e.EloRating?.ToString() ?? "-";
            }
        }

        // ---------- 好友 ----------

        private void OnOpenFriends()
        {
            _view.ShowPanel("Friend");
            _view.ClearList();
            if (_view.FriendContainer == null || _view.FriendTemplate == null)
            {
                return;
            }

            _ = RenderFriendsAsync();
        }

        private async Task RenderFriendsAsync()
        {
            var friends = await GameEntry.Instance.Rest.GetAsync<List<FriendDto>>("/social/friends").ConfigureAwait(true);
            if (!friends.IsOk || friends.Data == null)
            {
                Debug.LogWarning($"[Main] friends failed: {friends.Code} {friends.Message}");
                return;
            }

            Debug.Log($"[Main] friends: {friends.Data.Count}");
            foreach (var f in friends.Data)
            {
                var go = _view.SpawnItem(_view.FriendContainer, _view.FriendTemplate);
                if (!go)
                {
                    continue;
                }

                var nameText = go.transform.Find("NameText")?.GetComponent<Text>();
                var statusText = go.transform.Find("StatusText")?.GetComponent<Text>();
                var msgBtn = go.transform.Find("MsgButton")?.GetComponent<Button>();

                if (nameText) nameText.text = $"{f.Nickname} (ID:{f.UserId})";
                if (statusText)
                {
                    statusText.text = f.Online ? "<color=green>Online</color>" : "<color=grey>Offline</color>";
                }

                if (msgBtn)
                {
                    msgBtn.onClick.RemoveAllListeners();
                    msgBtn.onClick.AddListener(() => OpenPrivateChat(f.UserId, f.Nickname));
                }
            }
        }

        private async void OnAddFriend()
        {
            if (!_view.FriendIdInput || !long.TryParse(_view.FriendIdInput.text?.Trim(), out long toId))
            {
                Debug.LogWarning("[Main] invalid friend id");
                return;
            }

            var result = await GameEntry.Instance.Rest
                .PostAsync<Dictionary<string, bool>>($"/social/friends/request?toId={toId}", null)
                .ConfigureAwait(true);
            Debug.Log($"[Main] friend request to {toId}: ok={result.IsOk} {result.Message}");
            if (result.IsOk)
            {
                _view.FriendIdInput.text = "";
            }
        }

        // ---------- 留言板（世界聊天 / 私聊双模式） ----------

        private long _chatTargetId;
        private string _chatTargetName;

        private void OnOpenMessages()
        {
            // 从好友行进入时已切私聊；主菜单进入默认世界频道
            if (_chatTargetId == 0)
            {
                OpenWorldChat();
                return;
            }

            _ = RenderChatAsync();
        }

        private void OpenWorldChat()
        {
            _chatTargetId = 0;
            _chatTargetName = null;
            _view.ShowPanel("Message");
            _view.ClearList();
            _ = RenderChatAsync();
        }

        /// <summary>好友行 MsgButton → 私聊模式（Message 面板复用）。</summary>
        private void OpenPrivateChat(long targetId, string nickname)
        {
            _chatTargetId = targetId;
            _chatTargetName = nickname;
            _view.ShowPanel("Message");
            _view.ClearList();
            Debug.Log($"[Main] private chat with {nickname} (ID:{targetId})");
            _ = RenderChatAsync();
        }

        private async Task RenderMessagesAsync()
        {
            _chatTargetId = 0;
            _chatTargetName = null;
            await RenderChatAsync().ConfigureAwait(true);
        }

        private async Task RenderChatAsync()
        {
            string path = _chatTargetId == 0
                ? "/social/chat/world/recent?limit=30"
                : $"/social/chat/private/{_chatTargetId}/recent?limit=30";
            var messages = await GameEntry.Instance.Rest.GetAsync<List<ChatDto>>(path).ConfigureAwait(true);
            if (!messages.IsOk || messages.Data == null)
            {
                Debug.LogWarning($"[Main] messages failed: {messages.Code} {messages.Message}");
                return;
            }

            Debug.Log($"[Main] chat {(_chatTargetId == 0 ? "world" : $"private({_chatTargetName})")}: {messages.Data.Count}");
            foreach (var m in messages.Data)
            {
                AppendChatLine(m.FromName, m.FromId, m.Content);
            }
        }

        private void AppendChatLine(string fromName, long fromId, string content)
        {
            if (_view.MessageContainer == null || _view.MessageTemplate == null)
            {
                return;
            }

            var go = _view.SpawnItem(_view.MessageContainer, _view.MessageTemplate);
            if (!go)
            {
                return;
            }

            var senderText = go.transform.Find("SenderText")?.GetComponent<Text>();
            var contentText = go.transform.Find("ContentText")?.GetComponent<Text>();

            if (senderText) senderText.text = $"{fromName} (ID:{fromId})";
            if (contentText) contentText.text = content;
        }

        private async void OnSendWorldMessage()
        {
            var content = _view.MsgContentInput ? _view.MsgContentInput.text?.Trim() : null;
            if (string.IsNullOrEmpty(content))
            {
                return;
            }

            if (!RateLimiter.Shared.TryAcquire(RateLimiter.ChatMessage))
            {
                Debug.LogWarning("[Main] chat rate limited (8 msgs / 10s)");
                return;
            }

            RestResult<ChatSendResultDto> result;
            if (_chatTargetId == 0)
            {
                result = await GameEntry.Instance.Rest
                    .PostAsync<ChatSendResultDto>("/social/chat/world", new { content })
                    .ConfigureAwait(true);
            }
            else
            {
                result = await GameEntry.Instance.Rest
                    .PostAsync<ChatSendResultDto>("/social/chat/private", new { toId = _chatTargetId, content })
                    .ConfigureAwait(true);
            }

            Debug.Log($"[Main] chat sent ({(_chatTargetId == 0 ? "world" : $"private:{_chatTargetName}")}): ok={result.IsOk} {result.Message}");
            if (result.IsOk)
            {
                _view.MsgContentInput.text = "";
                // 世界频道消息经 CHAT_PUSH 广播回流显示（本类已订阅）；私聊服务端只推收件人，发送方本地追加
                if (_chatTargetId != 0)
                {
                    AppendChatLine(GameEntry.Instance.Auth.Nickname, GameEntry.Instance.Auth.UserId, content);
                }
            }
        }

        // ---------- 战绩 ----------

        private void OnOpenHistory()
        {
            _view.ShowPanel("History");
            _view.ClearList();
            if (_view.HistoryContainer == null || _view.HistoryTemplate == null)
            {
                return;
            }

            _ = RenderHistoryAsync();
        }

        private async Task RenderHistoryAsync()
        {
            var history = await GameEntry.Instance.Rest
                .GetAsync<List<HistoryDto>>("/battle/history?limit=10").ConfigureAwait(true);
            if (!history.IsOk || history.Data == null)
            {
                Debug.LogWarning($"[Main] history failed: {history.Code} {history.Message}");
                return;
            }

            Debug.Log($"[Main] history: {history.Data.Count} rows");
            foreach (var h in history.Data)
            {
                var go = _view.SpawnItem(_view.HistoryContainer, _view.HistoryTemplate);
                if (!go)
                {
                    continue;
                }

                var infoText = go.transform.Find("InfoText")?.GetComponent<Text>();
                var resultText = go.transform.Find("ResultText")?.GetComponent<Text>();

                if (infoText)
                {
                    infoText.text = h.TotalRounds.HasValue
                        ? $"Vs {h.OpponentNickname} ({h.TotalRounds} Rnds)"
                        : $"Vs {h.OpponentNickname}";
                }

                if (resultText)
                {
                    int change = h.EloAfter.HasValue && h.EloBefore.HasValue ? h.EloAfter.Value - h.EloBefore.Value : 0;
                    string sign = change >= 0 ? "+" : "";
                    resultText.text = $"{(h.Win ? "WIN" : "LOSE")} ({sign}{change})";
                    resultText.color = h.Win ? Color.green : Color.red;
                }
            }
        }

        // ---------- 匹配流程 ----------

        private void OnStartMatch()
        {
            if (_matching)
            {
                return;
            }

            _matching = true;
            if (_view.MatchButton) _view.MatchButton.interactable = false;
            // 只把昵称改为状态提示，保留金币/Elo 显示（撮合定时任务 2s 一轮；
            // Elo 区间 100 起每 10s +50，等待是正常行为不是掉线）
            _view.UpdateTopBar("Matching...", (int)_lastGold, _lastElo);
            if (!GameEntry.Instance.Battles.RequestMatch())
            {
                // 发送失败（未连接/限流拦截）：立即恢复，不卡匹配态
                ResetMatchUi();
            }
        }

        /// <summary>复位匹配 UI 状态（按钮恢复 + 顶栏昵称还原）。</summary>
        private void ResetMatchUi()
        {
            _matching = false;
            if (_view.MatchButton) _view.MatchButton.interactable = true;
            _ = RefreshTopBarAsync();
        }

        private void OnMatchSuccess(MatchSuccessResponse resp)
        {
            _matching = false;
            if (_view.MatchReadyPanel)
            {
                _view.MatchReadyPanel.SetActive(true);

                var acceptBtn = _view.MatchReadyPanel.transform.Find("AcceptButton")?.GetComponent<Button>();
                var declineBtn = _view.MatchReadyPanel.transform.Find("DeclineButton")?.GetComponent<Button>();
                var infoText = _view.MatchReadyPanel.transform.Find("InfoText")?.GetComponent<Text>();
                if (infoText && resp.Opponent != null)
                {
                    infoText.text = $"VS {resp.Opponent.Nickname} (ELO {resp.Opponent.EloRating})";
                }

                if (acceptBtn)
                {
                    acceptBtn.onClick.RemoveAllListeners();
                    acceptBtn.onClick.AddListener(() =>
                    {
                        _view.MatchReadyPanel.SetActive(false);
                        // 只把昵称改为状态提示，保留金币/Elo 显示
                        _view.UpdateTopBar("Waiting for start...", (int)_lastGold, _lastElo);
                        GameEntry.Instance.Battles.Ready(resp.BattleId);
                    });
                }

                if (declineBtn)
                {
                    declineBtn.onClick.RemoveAllListeners();
                    declineBtn.onClick.AddListener(() =>
                    {
                        _view.MatchReadyPanel.SetActive(false);
                        if (_view.MatchButton) _view.MatchButton.interactable = true;
                        GameEntry.Instance.Battles.CancelMatch();
                    });
                }
            }
            else
            {
                // 无确认面板时自动接受
                GameEntry.Instance.Battles.Ready(resp.BattleId);
            }
        }

        private void OnBattleStart(BattleStartResponse start)
        {
            // BattleService.CurrentBattle 已保存盘面；切场景
            SceneManager.LoadScene("BattleScene");
        }
    }
}
