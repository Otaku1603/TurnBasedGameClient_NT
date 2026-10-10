using System;

namespace Game.Network
{
    /// <summary>REST DTO：/api/auth/register 与 /api/auth/login（服务端 IdentityController 契约）。</summary>
    [Serializable]
    public sealed class AuthRequest
    {
        public string username;
        public string password;
        /// <summary>仅注册使用（可空）。</summary>
        public string nickname;
    }

    [Serializable]
    public sealed class AuthResponse
    {
        public long userId;
        public string nickname;
        public string token;
    }
}
