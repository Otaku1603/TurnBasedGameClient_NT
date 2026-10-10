using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TurnBasedGame.Core;
using TurnBasedGame.Model;
using UnityEngine.Networking;

namespace TurnBasedGame.Services
{
    /// <summary>
    /// 社交服务，处理好友、排行榜和留言板功能
    /// 支持在线状态查询和好友间通信
    /// </summary>
    public class SocialService
    {
        private static SocialService _instance;
        public static SocialService Instance => _instance ??= new SocialService();

        /// <summary>
        /// 获取排行榜
        /// </summary>
        public IEnumerator GetLeaderboard(Action<bool, List<LeaderboardUser>> callback)
        {
            yield return GetRequest<List<LeaderboardUser>>($"{AppConfig.ApiBaseUrl}/social/leaderboard", callback);
        }

        // === 好友相关 ===
        /// <summary>
        /// 获取好友列表，包含在线状态和职业信息
        /// </summary>
        public IEnumerator GetFriendList(Action<bool, List<FriendInfo>> callback)
        {
            yield return GetRequest<List<FriendInfo>>($"{AppConfig.ApiBaseUrl}/social/friend/list", callback);
        }

        /// <summary>
        /// 添加好友
        /// </summary>
        public IEnumerator AddFriend(long friendId, Action<bool, string> callback)
        {
            var req = new AddFriendRequest { friendId = friendId };
            yield return PostRequest($"{AppConfig.ApiBaseUrl}/social/friend/add", JsonConvert.SerializeObject(req), callback);
        }

        /// <summary>
        /// 获取他人详情
        /// </summary>
        public IEnumerator GetUserProfile(long targetId, Action<bool, UserProfile> callback)
        {
            string url = $"{AppConfig.ApiBaseUrl}/social/user/profile?targetId={targetId}";
            yield return GetRequest<UserProfile>(url, callback);
        }

        // === 留言板相关 ===
        /// <summary>
        /// 获取留言板消息，包含发送者详细信息
        /// </summary>
        public IEnumerator GetMessages(Action<bool, List<MessageInfo>> callback)
        {
            // 获取别人给我的留言
            yield return GetRequest<List<MessageInfo>>($"{AppConfig.ApiBaseUrl}/social/message/list", callback);
        }

        /// <summary>
        /// 发送留言给指定玩家
        /// </summary>
        public IEnumerator SendMessage(long targetId, string content, Action<bool, string> callback)
        {
            var req = new SendMessageRequest { targetId = targetId, content = content };
            yield return PostRequest($"{AppConfig.ApiBaseUrl}/social/message/send", JsonConvert.SerializeObject(req), callback);
        }

        // === 战绩相关 ===
        /// <summary>
        /// 查询战绩，包含对手信息与胜负情况
        /// </summary>
        public IEnumerator GetBattleHistory(long userId, Action<bool, List<BattleHistoryItem>> callback)
        {
            string url = $"{AppConfig.ApiBaseUrl}/social/history?userId={userId}";
            yield return GetRequest<List<BattleHistoryItem>>(url, callback);
        }

        // === 通用请求封装 ===
        private IEnumerator GetRequest<T>(string url, Action<bool, T> callback)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.certificateHandler = AppConfig.GetCertificateHandler();
                req.SetRequestHeader("Authorization", "Bearer " + AuthService.Instance.CurrentToken);
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var res = JsonConvert.DeserializeObject<ApiResponse<T>>(req.downloadHandler.text);
                        callback?.Invoke(res.success, res.data);
                    }
                    catch { callback?.Invoke(false, default); }
                }
                else callback?.Invoke(false, default);
            }
        }

        private IEnumerator PostRequest(string url, string json, Action<bool, string> callback)
        {
            using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.certificateHandler = AppConfig.GetCertificateHandler();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + AuthService.Instance.CurrentToken);

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    var res = JsonConvert.DeserializeObject<ApiResponse<object>>(req.downloadHandler.text);
                    callback?.Invoke(res.success, res.message);
                }
                else callback?.Invoke(false, req.error);
            }
        }
    }
}