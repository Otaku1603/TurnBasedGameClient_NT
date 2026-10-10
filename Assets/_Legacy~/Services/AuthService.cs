using GameClient.Proto;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Text;
using TurnBasedGame.Core;
using TurnBasedGame.Model;
using TurnBasedGame.Network;
using UnityEngine;
using UnityEngine.Networking;

namespace TurnBasedGame.Services
{
    /// <summary>
    /// 认证服务，负责用户注册、登录和Token管理
    /// 协调HTTP认证和TCP协议登录的时序问题
    /// </summary>
    public class AuthService
    {
        private static AuthService _instance;
        public static AuthService Instance => _instance ??= new AuthService();

        public string CurrentToken { get; private set; }
        public long CurrentUserId { get; private set; }
        public string CurrentNickname { get; private set; }
        public int CurrentGold { get; private set; }
        public int CurrentElo { get; private set; }
        public int TotalBattles { get; private set; }
        public int WinCount { get; private set; }
        public string WinRate { get; private set; }
        public string MyCharType { get; private set; }
        public int CurrentAvatarFrameId { get; private set; }
        public Model.CharacterInfo MyCharacter { get; private set; }

        /// <summary>
        /// 认证服务的私有构造函数，确保单例模式
        /// 同时注册TCP连接成功事件，用于在连接建立后发送登录协议
        /// </summary>
        private AuthService()
        {
            NetworkManager.Instance.OnConnected += OnTcpConnected;
        }

        /// </summary>
        /// HTTP注册请求，成功后自动获取Token并设置用户信息
        /// </summary>
        public IEnumerator Register(string username, string password, string nickname, string charType, Action<bool, string> callback)
        {
            var reqData = new RegisterRequest
            {
                username = username,
                password = password,
                nickname = nickname,
                charType = charType
            };
            string json = JsonConvert.SerializeObject(reqData);

            // 注册接口返回的也是扁平结构
            Action<bool, string> internalCallback = (success, responseJson) =>
            {
                if (success)
                {
                    try
                    {
                        var response = JsonConvert.DeserializeObject<AuthResponseRaw>(responseJson);
                        if (response != null && response.success)
                        {
                            // 注册成功后后端直接返回了 Token，这里直接缓存实现“注册即登录”，省去登录流程
                            CurrentToken = response.token;
                            CurrentUserId = response.userId;
                            callback?.Invoke(true, "Register Success");
                            if (response.character != null)
                            {
                                MyCharType = response.character.charType;
                            }
                            else
                            {
                                MyCharType = "warrior"; // 兜底
                            }
                        }
                        else
                        {
                            callback?.Invoke(false, response?.message ?? "Unknown Error");
                        }
                    }
                    catch (Exception e)
                    {
                        callback?.Invoke(false, $"Parse Error: {e.Message}");
                    }
                }
                else
                {
                    callback?.Invoke(false, responseJson);
                }
            };

            yield return PostRequest($"{AppConfig.ApiBaseUrl}/auth/register", json, callback);
        }

        /// <summary>
        /// 登录核心流程：
        /// 1. 先发 HTTP 请求获取 Token 和用户基础数据。
        /// 2. 拿到 Token 后，再建立 TCP 长连接，用于后续的实时战斗通信。
        /// </summary>
        public IEnumerator Login(string username, string password, Action<bool, string> callback)
        {
            var reqData = new LoginRequestHttp { username = username, password = password };
            string json = JsonConvert.SerializeObject(reqData);

            Debug.Log($"[Auth] Requesting login for user: {username}");

            Action<bool, string> internalCallback = (success, responseJson) =>
            {
                if (success)
                {
                    try
                    {
                        // 使用 AuthResponseRaw 进行反序列化
                        var response = JsonConvert.DeserializeObject<AuthResponseRaw>(responseJson);

                        if (response != null && response.success)
                        {
                            Debug.Log($"[Auth] <color=green>Login HTTP Success. Token received.</color> UserID: {response.userId}");

                            // 字段从 response 获取
                            CurrentToken = response.token;
                            CurrentUserId = response.userId;
                            CurrentNickname = response.nickname;
                            CurrentGold = response.gold;
                            CurrentElo = response.eloRating;
                            CurrentAvatarFrameId = response.avatarFrameId;
                            TotalBattles = response.totalBattles;
                            WinCount = response.winCount;
                            WinRate = response.winRate;

                            // 提取职业类型
                            if (response.character != null)
                            {
                                MyCharacter = response.character;
                                MyCharType = response.character.charType;
                            }
                            else
                            {
                                Debug.LogWarning("[Auth] Character info is null! Defaulting to warrior.");
                                MyCharType = "warrior";
                            }

                            if (string.IsNullOrEmpty(CurrentToken))
                            {
                                callback?.Invoke(false, "Token is empty in response");
                                return;
                            }

                            // 检查 TCP 状态
                            if (NetworkManager.Instance.IsConnected)
                            {
                                SendLoginProto();
                            }
                            else
                            {
                                NetworkManager.Instance.Connect();
                            }

                            callback?.Invoke(true, "Login Success");
                        }
                        else
                        {
                            Debug.LogError($"[Auth] Login Failed: {response?.message}");
                            callback?.Invoke(false, response?.message ?? "Unknown Error");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Auth] Parse Error: {e}");
                        callback?.Invoke(false, $"Parse Error: {e.Message}");
                    }
                }
                else
                {
                    Debug.LogError($"[Auth] HTTP Error: {responseJson}");
                    callback?.Invoke(false, responseJson);
                }
            };

            yield return PostRequest($"{AppConfig.ApiBaseUrl}/auth/login", json, internalCallback);
        }

        private void OnTcpConnected()
        {
            Debug.Log("[Auth] TCP Connected. Sending Login Proto...");
            // TCP 连接建立后，立即发送带 Token 的握手包，完成长连接的身份认证
            if (!string.IsNullOrEmpty(CurrentToken))
            {
                SendLoginProto();
            }
        }

        /// <summary>
        /// 发送Proto登录消息到游戏服务器
        /// </summary>
        private void SendLoginProto()
        {
            GameMessage msg = new GameMessage
            {
                Type = MessageType.Login,
                Token = CurrentToken,
                LoginRequest = new LoginRequest { Username = "", Password = "" }
            };

            NetworkManager.Instance.Send(msg);
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

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(false, req.error);
                }
                else
                {
                    callback?.Invoke(true, req.downloadHandler.text);
                }
            }
        }

        /// <summary>
        /// 更新本地金币缓存，用于商店购买后实时刷新UI
        /// </summary>
        public void UpdateGold(int newGold)
        {
            CurrentGold = newGold;
        }

        /// <summary>
        /// 更新本地头像框缓存，装备头像框后调用
        /// </summary>
        public void UpdateAvatarFrame(int frameId)
        {
            CurrentAvatarFrameId = frameId;
        }
    }
}