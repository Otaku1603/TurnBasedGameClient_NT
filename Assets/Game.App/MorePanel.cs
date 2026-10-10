using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Network;
using Newtonsoft.Json;
using UnityEngine;

namespace Game.App
{
    /// <summary>
    /// M5 附加功能面板（程序化 IMGUI，随 GameEntry 生成，零场景接线）：
    /// Mail（已读/详情/领取）、Quest、SignIn、Guild（创建/搜索/申请/审批/成员管理/公告/聊天/离开解散）、
    /// FrReq（好友申请审批）、Report（单局战报）。
    /// 服务端 guild/report 接口返回宽松 Map → 统一 Dictionary dump + 结构化操作按钮。
    /// </summary>
    public sealed class MorePanel : MonoBehaviour
    {
        private bool _open;
        private Rect _windowRect = new Rect(20, 80, 480, 440);
        private int _tab;
        private readonly string[] _tabs = { "Mail", "Quest", "SignIn", "Guild", "FrReq", "Report" };

        private MailListDto _mail;
        private MailDetailDto _mailDetail;
        private List<Dictionary<string, object>> _quests;
        private Dictionary<string, object> _guildMe;
        private List<Dictionary<string, object>> _guildMembers;
        private List<Dictionary<string, object>> _guildApplications;
        private List<Dictionary<string, object>> _guildChat;
        private List<Dictionary<string, object>> _guildList;
        private List<Dictionary<string, object>> _friendRequests;
        private List<Dictionary<string, object>> _reports;
        private Dictionary<string, object> _reportDetail;
        private string _statusLine = "";
        private string _guildSearch = "";
        private string _guildCreateName = "";
        private string _guildNoticeDraft = "";
        private string _guildChatDraft = "";
        private long _myGuildId;
        private bool _guildJoined;
        private Vector2 _scroll;
        private bool _loading;

        public static void Attach(GameObject host)
        {
            host.AddComponent<MorePanel>();
        }

        private RestClient Rest => GameEntry.Instance.Rest;

        private void OnGUI()
        {
            // 右上角（避开场景里左上角的 DevSettings 按钮）
            GUILayout.BeginArea(new Rect(Screen.width - 100, 6, 96, 30));
            if (GUILayout.Button(_open ? "MORE ▲" : "MORE ▼", GUILayout.Width(90), GUILayout.Height(24)))
            {
                _open = !_open;
                if (_open)
                {
                    _ = RefreshCurrentTab();
                }
            }

            GUILayout.EndArea();

            if (_open)
            {
                _windowRect = GUILayout.Window(7301, _windowRect, DrawWindow, "Extras");
            }
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (GUILayout.Toggle(_tab == i, _tabs[i], "Button") && _tab != i)
                {
                    _tab = i;
                    _ = RefreshCurrentTab();
                }
            }

            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_statusLine))
            {
                GUILayout.Label(_statusLine);
            }

            if (_loading)
            {
                GUILayout.Label("Loading...");
                GUI.DragWindow();
                return;
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case 0: DrawMail(); break;
                case 1: DumpListWithActions("quests", _quests, null); break;
                case 2: DrawSignIn(); break;
                case 3: DrawGuild(); break;
                case 4: DumpListWithActions("friend requests", _friendRequests, DrawFriendRequestActions); break;
                case 5: DrawReports(); break;
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private async Task RefreshCurrentTab()
        {
            try
            {
                _loading = true;
                switch (_tab)
                {
                    case 0:
                        var mail = await Rest.GetAsync<MailListDto>("/mail/list?page=0&size=20").ConfigureAwait(true);
                        _mail = mail.IsOk ? mail.Data : null;
                        _statusLine = mail.IsOk
                            ? $"mail total={_mail?.Total} unread={_mail?.Unread}"
                            : $"mail failed: {mail.Code} {mail.Message}";
                        break;
                    case 1:
                        var quests = await Rest.GetAsync<List<Dictionary<string, object>>>("/quest/mine").ConfigureAwait(true);
                        _quests = quests.IsOk ? quests.Data : null;
                        _statusLine = quests.IsOk ? $"quests: {_quests?.Count}" : $"quest failed: {quests.Code} {quests.Message}";
                        break;
                    case 2:
                        var status = await Rest.GetAsync<Dictionary<string, object>>("/activity/sign-in/status").ConfigureAwait(true);
                        _statusLine = status.IsOk ? $"sign-in: {Dump(status.Data)}" : $"sign-in failed: {status.Code} {status.Message}";
                        break;
                    case 3:
                        await RefreshGuildAsync().ConfigureAwait(true);
                        break;
                    case 4:
                        var reqs = await Rest.GetAsync<List<Dictionary<string, object>>>("/social/friends/requests").ConfigureAwait(true);
                        _friendRequests = reqs.IsOk ? reqs.Data : null;
                        _statusLine = reqs.IsOk ? $"requests: {_friendRequests?.Count}" : $"requests failed: {reqs.Code} {reqs.Message}";
                        break;
                    case 5:
                        var history = await Rest.GetAsync<List<Dictionary<string, object>>>("/battle/history?limit=10").ConfigureAwait(true);
                        _reports = history.IsOk ? history.Data : null;
                        _reportDetail = null;
                        _statusLine = history.IsOk ? $"reports: {_reports?.Count} battles" : $"history failed: {history.Code} {history.Message}";
                        break;
                }
            }
            catch (Exception e)
            {
                _statusLine = "error: " + e.Message;
            }
            finally
            {
                _loading = false;
            }
        }

        // ---------- Mail ----------

        private void DrawMail()
        {
            if (_mailDetail != null)
            {
                GUILayout.BeginVertical("box");
                GUILayout.Label($"<b>{_mailDetail.Title}</b>");
                GUILayout.Label($"from {_mailDetail.SenderName}");
                GUILayout.Space(4);
                GUILayout.Label(string.IsNullOrEmpty(_mailDetail.Content) ? "(no content)" : _mailDetail.Content);
                GUILayout.Space(4);
                GUILayout.Label(AttachmentText(_mailDetail));
                if (GUILayout.Button("Close"))
                {
                    _mailDetail = null;
                }

                GUILayout.EndVertical();
            }

            if (_mail?.Items == null)
            {
                GUILayout.Label("(no data)");
                return;
            }

            if (GUILayout.Button("Claim All") && _mail.Items.Exists(m => m.HasUnclaimed))
            {
                _ = PostAndRefresh("/mail/claim-all", "claim-all");
            }

            foreach (var m in _mail.Items)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label($"{m.Title}  [{m.SenderName}] {(m.HasUnclaimed ? "*附件*" : m.Read ? "(read)" : "(new)")}",
                    GUILayout.Width(280));
                // 已读也允许重看详情（read 接口幂等）
                if (GUILayout.Button(m.Read ? "View" : "Read", GUILayout.Width(52)))
                {
                    _ = ReadMail(m.Id);
                }

                if (m.HasUnclaimed && GUILayout.Button("Claim", GUILayout.Width(56)))
                {
                    _ = PostAndRefresh($"/mail/{m.Id}/claim", "claim");
                }

                GUILayout.EndHorizontal();
            }
        }

        /// <summary>读信：响应即 MailDetail（含正文），弹出详情区。</summary>
        private async Task ReadMail(long mailId)
        {
            var result = await Rest.PostAsync<MailDetailDto>($"/mail/{mailId}/read", null).ConfigureAwait(true);
            if (result.IsOk && result.Data != null)
            {
                _mailDetail = result.Data;
                _statusLine = $"mail {mailId} read";
            }
            else
            {
                _statusLine = $"read failed: {result.Code} {result.Message}";
            }

            await RefreshMailListOnly().ConfigureAwait(true);
        }

        private async Task RefreshMailListOnly()
        {
            var mail = await Rest.GetAsync<MailListDto>("/mail/list?page=0&size=20").ConfigureAwait(true);
            if (mail.IsOk)
            {
                _mail = mail.Data;
            }
        }

        // ---------- SignIn ----------

        private void DrawSignIn()
        {
            GUILayout.Label(_statusLine);
            if (GUILayout.Button("Sign In Today"))
            {
                _ = PostAndRefresh("/activity/sign-in", "sign-in");
            }
        }

        // ---------- Guild（全功能） ----------

        private async Task RefreshGuildAsync()
        {
            var me = await Rest.GetAsync<Dictionary<string, object>>("/guild/me").ConfigureAwait(true);
            if (!me.IsOk)
            {
                _statusLine = $"guild failed: {me.Code} {me.Message}";
                return;
            }

            _guildJoined = me.Data != null && me.Data.TryGetValue("joined", out var joined) && joined is bool b && b;
            _guildMembers = null;
            _guildApplications = null;
            _guildChat = null;
            _guildList = null;
            _myGuildId = 0;

            if (_guildJoined && me.Data.TryGetValue("guild", out var g) && g is Dictionary<string, object> info)
            {
                _guildMe = info;
                _myGuildId = GetLong(info, "id", "guildId");
                _guildNoticeDraft = Convert.ToString(info.TryGetValue("notice", out var notice) ? notice : "");
                _statusLine = $"guild: {DumpShort(info)}";
            }
            else
            {
                _guildMe = null;
                _statusLine = "no guild";
            }
        }

        private void DrawGuild()
        {
            if (_guildJoined)
            {
                DrawGuildMember();
            }
            else
            {
                DrawGuildBrowser();
            }
        }

        private void DrawGuildMember()
        {
            GUILayout.Label(_statusLine);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Members"))
            {
                _ = LoadGuildSub($"/guild/{_myGuildId}/members", d => _guildMembers = d);
            }

            if (GUILayout.Button("Applications"))
            {
                _ = LoadGuildSub("/guild/applications", d => _guildApplications = d);
            }

            if (GUILayout.Button("Chat"))
            {
                _ = LoadGuildSub("/guild/chat/recent?limit=30", d => _guildChat = d);
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Leave"))
            {
                _ = PostAndRefresh("/guild/leave", "leave");
            }

            if (GUILayout.Button("Disband"))
            {
                _ = PostAndRefresh("/guild/disband", "disband");
            }

            GUILayout.EndHorizontal();

            // 公告编辑
            GUILayout.BeginHorizontal();
            GUILayout.Label("Notice:", GUILayout.Width(56));
            _guildNoticeDraft = GUILayout.TextField(_guildNoticeDraft);
            if (GUILayout.Button("Set", GUILayout.Width(44)))
            {
                _ = PutAndRefresh("/guild/notice", new { notice = _guildNoticeDraft }, "notice");
            }

            GUILayout.EndHorizontal();

            // 公会聊天发送
            GUILayout.BeginHorizontal();
            _guildChatDraft = GUILayout.TextField(_guildChatDraft);
            if (GUILayout.Button("Send", GUILayout.Width(52)))
            {
                _ = PostGuildChat();
            }

            GUILayout.EndHorizontal();

            if (_guildApplications != null)
            {
                GUILayout.Label($"<b>Applications ({_guildApplications.Count})</b>");
                foreach (var app in _guildApplications)
                {
                    GUILayout.BeginHorizontal("box");
                    GUILayout.Label(DumpShort(app), GUILayout.Width(280));
                    long userId = GetLong(app, "userId", "applicantId", "id");
                    if (GUILayout.Button("OK", GUILayout.Width(40)))
                    {
                        _ = PostAndRefresh($"/guild/applications/{userId}/approve", "approve");
                    }

                    if (GUILayout.Button("No", GUILayout.Width(36)))
                    {
                        _ = PostAndRefresh($"/guild/applications/{userId}/reject", "reject");
                    }

                    GUILayout.EndHorizontal();
                }
            }

            if (_guildMembers != null)
            {
                GUILayout.Label($"<b>Members ({_guildMembers.Count})</b>");
                foreach (var mem in _guildMembers)
                {
                    GUILayout.BeginHorizontal("box");
                    GUILayout.Label(DumpShort(mem), GUILayout.Width(260));
                    long userId = GetLong(mem, "userId", "id");
                    if (GUILayout.Button("Kick", GUILayout.Width(44)))
                    {
                        _ = PostAndRefresh($"/guild/members/{userId}/kick", "kick");
                    }

                    if (GUILayout.Button("+", GUILayout.Width(28)))
                    {
                        _ = PostAndRefresh($"/guild/members/{userId}/promote", "promote");
                    }

                    if (GUILayout.Button("-", GUILayout.Width(28)))
                    {
                        _ = PostAndRefresh($"/guild/members/{userId}/demote", "demote");
                    }

                    if (GUILayout.Button("T", GUILayout.Width(28)))
                    {
                        _ = PostAndRefresh($"/guild/transfer?userId={userId}", "transfer");
                    }

                    GUILayout.EndHorizontal();
                }
            }

            if (_guildChat != null)
            {
                GUILayout.Label("<b>Guild Chat (recent)</b>");
                foreach (var c in _guildChat)
                {
                    GUILayout.Label(DumpShort(c), "box");
                }
            }
        }

        private void DrawGuildBrowser()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name:", GUILayout.Width(44));
            _guildCreateName = GUILayout.TextField(_guildCreateName);
            if (GUILayout.Button("Create", GUILayout.Width(56)))
            {
                _ = PostAndRefresh("/guild/create", new { name = _guildCreateName }, "create");
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _guildSearch = GUILayout.TextField(_guildSearch);
            if (GUILayout.Button("Search", GUILayout.Width(64)))
            {
                _ = SearchGuildsAsync(_guildSearch);
            }

            GUILayout.EndHorizontal();

            if (_guildList == null)
            {
                return;
            }

            if (_guildList.Count == 0)
            {
                GUILayout.Label("(no guilds matched)");
                return;
            }

            foreach (var g in _guildList)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(DumpShort(g), GUILayout.Width(340));
                long guildId = GetLong(g, "id", "guildId");
                if (GUILayout.Button("Apply", GUILayout.Width(52)))
                {
                    _ = PostAndRefresh($"/guild/apply?guildId={guildId}", "apply");
                }

                GUILayout.EndHorizontal();
            }
        }

        private async Task SearchGuildsAsync(string keyword)
        {
            var kw = Uri.EscapeDataString(keyword ?? "");
            var result = await Rest
                .GetAsync<Dictionary<string, object>>($"/guild/list?keyword={kw}&page=0&size=20")
                .ConfigureAwait(true);
            if (!result.IsOk || result.Data == null)
            {
                _statusLine = $"list failed: {result.Code} {result.Message}";
                return;
            }

            _guildList = new List<Dictionary<string, object>>();
            if (result.Data.TryGetValue("items", out var o) && o is List<object> list)
            {
                foreach (var row in list)
                {
                    if (row is Dictionary<string, object> d)
                    {
                        _guildList.Add(d);
                    }
                }
            }

            _statusLine = $"guilds: {_guildList.Count}";
        }

        private async Task LoadGuildSub(string path, Action<List<Dictionary<string, object>>> sink)
        {
            var result = await Rest.GetAsync<List<Dictionary<string, object>>>(path).ConfigureAwait(true);
            if (result.IsOk)
            {
                sink(result.Data);
                _statusLine = $"{path}: {result.Data?.Count ?? 0} rows";
            }
            else
            {
                _statusLine = $"{path} failed: {result.Code} {result.Message}";
            }
        }

        private async Task PostGuildChat()
        {
            if (string.IsNullOrEmpty(_guildChatDraft?.Trim()))
            {
                return;
            }

            var result = await Rest.PostAsync<ChatSendResultDto>("/guild/chat", new { content = _guildChatDraft })
                .ConfigureAwait(true);
            _statusLine = $"guild chat: ok={result.IsOk} {result.Message}";
            if (result.IsOk)
            {
                _guildChatDraft = "";
                _ = LoadGuildSub("/guild/chat/recent?limit=30", d => _guildChat = d);
            }
        }

        // ---------- Friend requests ----------

        private void DrawFriendRequestActions(Dictionary<string, object> row)
        {
            long fromId = GetLong(row, "fromId", "userId", "applicantId", "id");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Accept", GUILayout.Width(60)))
            {
                _ = PostAndRefresh($"/social/friends/accept?fromId={fromId}", "accept");
            }

            if (GUILayout.Button("Reject", GUILayout.Width(60)))
            {
                _ = PostAndRefresh($"/social/friends/reject?fromId={fromId}", "reject");
            }

            GUILayout.EndHorizontal();
        }

        private void DumpListWithActions(string label, List<Dictionary<string, object>> list,
            Action<Dictionary<string, object>> rowActions)
        {
            GUILayout.Label(_statusLine);
            if (list == null || list.Count == 0)
            {
                GUILayout.Label($"({label}: no data)");
                return;
            }

            foreach (var row in list)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(DumpShort(row), GUILayout.Width(320));
                GUILayout.BeginVertical();
                rowActions?.Invoke(row);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
        }

        // ---------- Report（单局战报） ----------

        private void DrawReports()
        {
            GUILayout.Label(_statusLine);

            if (_reportDetail != null)
            {
                GUILayout.BeginVertical("box");
                GUILayout.Label("<b>Battle Report</b>");
                GUILayout.Label(Dump(_reportDetail));
                if (GUILayout.Button("Close"))
                {
                    _reportDetail = null;
                }

                GUILayout.EndVertical();
            }

            if (_reports == null || _reports.Count == 0)
            {
                GUILayout.Label("(no battles)");
                return;
            }

            foreach (var row in _reports)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(DumpShort(row), GUILayout.Width(360));
                if (GUILayout.Button("Report", GUILayout.Width(60)))
                {
                    _ = LoadReport(GetString(row, "battleId", "id"));
                }

                GUILayout.EndHorizontal();
            }
        }

        private async Task LoadReport(string battleId)
        {
            if (string.IsNullOrEmpty(battleId))
            {
                _statusLine = "no battleId in row";
                return;
            }

            var result = await Rest.GetAsync<Dictionary<string, object>>($"/battle/report/{battleId}").ConfigureAwait(true);
            if (result.IsOk)
            {
                _reportDetail = result.Data;
                _statusLine = $"report {battleId} loaded";
            }
            else
            {
                _statusLine = $"report failed: {result.Code} {result.Message}";
            }
        }

        // ---------- 工具 ----------

        private static long GetLong(Dictionary<string, object> dict, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (dict.TryGetValue(key, out var v) && v != null)
                {
                    try
                    {
                        return Convert.ToInt64(v);
                    }
                    catch
                    {
                        // 非 long 形态（如字符串 battleId），尝试下一个键
                    }
                }
            }

            return 0;
        }

        private static string GetString(Dictionary<string, object> dict, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (dict.TryGetValue(key, out var v) && v != null)
                {
                    return Convert.ToString(v);
                }
            }

            return null;
        }

        private static string DumpShort(Dictionary<string, object> dict)
        {
            if (dict == null)
            {
                return "(empty)";
            }

            var parts = new List<string>();
            foreach (var kv in dict)
            {
                if (kv.Value is Dictionary<string, object> || kv.Value is List<object>)
                {
                    continue; // 嵌套结构不进短 dump
                }

                parts.Add($"{kv.Key}={kv.Value}");
            }

            return string.Join("  ", parts);
        }

        private static string Dump(Dictionary<string, object> dict)
        {
            if (dict == null)
            {
                return "(empty)";
            }

            var parts = new List<string>();
            foreach (var kv in dict)
            {
                parts.Add($"{kv.Key}={kv.Value}");
            }

            return string.Join("\n", parts);
        }

        private async Task PostAndRefresh(string path, string tag)
        {
            var result = await Rest.PostAsync<Dictionary<string, object>>(path, null).ConfigureAwait(true);
            _statusLine = $"{tag}: ok={result.IsOk} {result.Message}{(result.Data != null ? " " + Dump(result.Data) : "")}";
            Debug.Log($"[More] {tag} {path} -> ok={result.IsOk} {result.Message}");
            _ = RefreshCurrentTab();
        }

        private async Task PostAndRefresh(string path, object body, string tag)
        {
            var result = await Rest.PostAsync<Dictionary<string, object>>(path, body).ConfigureAwait(true);
            _statusLine = $"{tag}: ok={result.IsOk} {result.Message}";
            Debug.Log($"[More] {tag} {path} -> ok={result.IsOk} {result.Message}");
            _ = RefreshCurrentTab();
        }

        private async Task PutAndRefresh(string path, object body, string tag)
        {
            var result = await Rest.PutAsync<Dictionary<string, object>>(path, body).ConfigureAwait(true);
            _statusLine = $"{tag}: ok={result.IsOk} {result.Message}";
            Debug.Log($"[More] {tag} {path} -> ok={result.IsOk} {result.Message}");
            _ = RefreshCurrentTab();
        }

        /// <summary>邮件详情里的附件摘要（服务端 MailDetail.attachments）。</summary>
        private static string AttachmentText(MailDetailDto detail)
        {
            if (detail?.Attachments == null || detail.Attachments.Count == 0)
            {
                return "(no attachment)";
            }

            var parts = new List<string>();
            foreach (var a in detail.Attachments)
            {
                parts.Add($"{a.Type} x{a.Amount}");
            }

            return "attachments: " + string.Join(", ", parts);
        }

        // ---------- DTO（服务端 MailService 契约） ----------

        public sealed class MailSummaryDto
        {
            [JsonProperty("id")] public long Id;
            [JsonProperty("senderName")] public string SenderName;
            [JsonProperty("title")] public string Title;
            [JsonProperty("read")] public bool Read;
            [JsonProperty("hasUnclaimed")] public bool HasUnclaimed;
            [JsonProperty("expireAt")] public string ExpireAt;
            [JsonProperty("createdAt")] public string CreatedAt;
        }

        public sealed class MailListDto
        {
            [JsonProperty("total")] public int Total;
            [JsonProperty("unread")] public int Unread;
            [JsonProperty("page")] public int Page;
            [JsonProperty("size")] public int Size;
            [JsonProperty("items")] public List<MailSummaryDto> Items;
        }

        /// <summary>POST /mail/{id}/read 的响应（MailService.MailDetail，含正文与附件）。</summary>
        public sealed class MailAttachmentDto
        {
            [JsonProperty("type")] public string Type;
            [JsonProperty("itemId")] public int ItemId;
            [JsonProperty("amount")] public int Amount;
        }

        public sealed class MailDetailDto
        {
            [JsonProperty("id")] public long Id;
            [JsonProperty("senderName")] public string SenderName;
            [JsonProperty("title")] public string Title;
            [JsonProperty("content")] public string Content;
            [JsonProperty("read")] public bool Read;
            [JsonProperty("expireAt")] public string ExpireAt;
            [JsonProperty("createdAt")] public string CreatedAt;
            [JsonProperty("attachments")] public List<MailAttachmentDto> Attachments;
        }
    }
}
