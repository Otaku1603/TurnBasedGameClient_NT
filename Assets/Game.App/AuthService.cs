using System;
using System.Threading.Tasks;
using Game.Network;
using UnityEngine;

namespace Game.App
{
    /// <summary>
    /// 认证编排：REST 注册/登录 → 取 token → WS 连接并 LOGIN（协议 v2：token 仅 LOGIN 携带）。
    /// HTTP 与 WS 分离的时序与旧客户端一致，传输层换成 v2。
    /// </summary>
    public sealed class AuthService
    {
        private readonly RestClient _rest;
        private readonly ConnectionStateMachine _connection;

        public long UserId { get; private set; }
        public string Nickname { get; private set; }
        public bool HasToken { get; private set; }

        /// <summary>WS 认证成功（首次）。重连认证见 ConnectionStateMachine.Reconnected。</summary>
        public event Action Authenticated;

        public AuthService(RestClient rest, ConnectionStateMachine connection)
        {
            _rest = rest;
            _connection = connection;
            _connection.Authenticated += () => Authenticated?.Invoke();
        }

        /// <summary>注册（用户名 3~32 字母数字下划线、密码 6~32，服务端校验）。</summary>
        public Task<RestResult<AuthResponse>> RegisterAsync(string username, string password, string nickname)
        {
            return AuthAsync(_rest.PostAsync<AuthResponse>("/auth/register",
                new AuthRequest { username = username, password = password, nickname = nickname }));
        }

        public Task<RestResult<AuthResponse>> LoginAsync(string username, string password)
        {
            return AuthAsync(_rest.PostAsync<AuthResponse>("/auth/login",
                new AuthRequest { username = username, password = password }));
        }

        private async Task<RestResult<AuthResponse>> AuthAsync(Task<RestResult<AuthResponse>> call)
        {
            var result = await call.ConfigureAwait(true);
            if (!result.IsOk)
            {
                return result;
            }

            UserId = result.Data.userId;
            Nickname = result.Data.nickname;
            HasToken = true;
            _rest.SetToken(result.Data.token);
            _connection.Connect(result.Data.token);
            Debug.Log($"[Auth] ok userId={UserId} nickname={Nickname}");
            return result;
        }
    }
}
