using System;

namespace TurnBasedGame.Model
{
    [Serializable]
    public class FriendInfo
    {
        public long id;          // 关系记录ID
        public long friendId;    // 好友的用户ID
        public string nickname;  // 好友昵称
        public int status;       // 1=正常

        public bool online;
        public string charType;
    }

    [Serializable]
    public class LeaderboardUser
    {
        public long id;
        public string nickname;
        public int eloRating;
        public int winCount;

        public string charType;
    }

    [Serializable]
    public class MessageInfo
    {
        public long id;
        public long senderId;
        public string senderName;
        public string content;
        public string createTime; // 后端通常返回时间字符串

        public SenderInfo senderUser;
    }

    [Serializable]
    public class SenderInfo
    {
        public long id;
        public string nickname;
        public string username;
    }

    // 用于发送请求的参数
    [Serializable]
    public class AddFriendRequest
    {
        public long friendId;
    }

    [Serializable]
    public class SendMessageRequest
    {
        public long targetId;
        public string content;
    }

    [Serializable]
    public class BattleHistoryItem
    {
        public string battleId;
        public string time;
        public string opponentName;
        public string result; // "WIN", "LOSE"
        public int rounds;
        public int eloChange;
    }

    // 用于接收他人详情
    [Serializable]
    public class UserProfile
    {
        public long userId;
        public string nickname;
        public int eloRating;
        public int gold;
        public int avatarFrameId;

        public int totalBattles;
        public int winCount;
        public string winRate;

        public CharacterInfo character; // 复用 AuthModels 里的 CharacterInfo
    }
}