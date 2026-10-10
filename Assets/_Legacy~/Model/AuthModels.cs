using System;

namespace TurnBasedGame.Model
{
    // HTTP 注册请求参数
    [Serializable]
    public class RegisterRequest
    {
        public string username;
        public string password;
        public string nickname;
        public string charType;
    }

    // HTTP 登录请求参数
    [Serializable]
    public class LoginRequestHttp
    {
        public string username;
        public string password;
    }

    // 扁平化的认证响应类，与后端返回的数据结构匹配
    [Serializable]
    public class AuthResponseRaw
    {
        public bool success;
        public string message;
        public string token;
        public long userId;
        public string username;
        public string nickname;
        public int eloRating;
        public int gold;

        public int totalBattles;
        public int winCount;
        public string winRate;

        public int avatarFrameId;

        public CharacterInfo character;
    }

    [Serializable]
    public class CharacterInfo
    {
        public string charType; // "warrior", "mage"
        public string charName;
        public int level;
        public int maxHp;
        public int attack;
        public int defense;
        public int speed;
    }
}