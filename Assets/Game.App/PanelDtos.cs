using Newtonsoft.Json;

namespace Game.App
{
    /// <summary>面板数据 DTO（服务端 Shop/Social/Rank/Economy/Battle 只读接口契约）。</summary>

    // GET /shop/items → ShopService.ShopItemView
    public sealed class ShopItemDto
    {
        [JsonProperty("id")] public int Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("type")] public string Type;
        [JsonProperty("price")] public int Price;
        [JsonProperty("effectValue")] public int EffectValue;
    }

    // POST /shop/buy → ShopService.BuyResult
    public sealed class BuyResultDto
    {
        [JsonProperty("itemId")] public int ItemId;
        [JsonProperty("itemName")] public string ItemName;
        [JsonProperty("count")] public int Count;
        [JsonProperty("totalCost")] public long TotalCost;
        [JsonProperty("goldLeft")] public long GoldLeft;
    }

    // GET /economy/inventory → InventoryItemView
    public sealed class InventoryDto
    {
        [JsonProperty("itemId")] public int ItemId;
        [JsonProperty("name")] public string Name;
        [JsonProperty("type")] public string Type;
        [JsonProperty("count")] public int Count;
        [JsonProperty("equipped")] public bool Equipped;
    }

    // GET /rank/elo?limit → RankBoard{type, entries}
    public sealed class RankBoardDto
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("entries")] public System.Collections.Generic.List<RankEntryDto> Entries;
    }

    public sealed class RankEntryDto
    {
        [JsonProperty("rank")] public int Rank;
        [JsonProperty("userId")] public long UserId;
        [JsonProperty("nickname")] public string Nickname;
        [JsonProperty("eloRating")] public int? EloRating;
        [JsonProperty("gold")] public long? Gold;
    }

    // GET /social/friends → FriendView
    public sealed class FriendDto
    {
        [JsonProperty("userId")] public long UserId;
        [JsonProperty("nickname")] public string Nickname;
        [JsonProperty("eloRating")] public int EloRating;
        [JsonProperty("online")] public bool Online;
    }

    // GET /social/profile → SocialService.ProfileView（v2 无角色字段，charType 由战斗盘面提供）
    public sealed class ProfileDto
    {
        [JsonProperty("userId")] public long UserId;
        [JsonProperty("nickname")] public string Nickname;
        [JsonProperty("eloRating")] public int EloRating;
        [JsonProperty("winCount")] public int WinCount;
        [JsonProperty("totalBattles")] public int TotalBattles;
        [JsonProperty("guildId")] public long? GuildId;
        [JsonProperty("guildName")] public string GuildName;
        [JsonProperty("gold")] public long? Gold;

        public string WinRate => TotalBattles > 0 ? $"{(int)(100f * WinCount / TotalBattles)}%" : "N/A";
    }

    // GET /battle/history → rows（渲染仅取确定字段）
    public sealed class HistoryDto
    {
        [JsonProperty("battleId")] public string BattleId;
        [JsonProperty("win")] public bool Win;
        [JsonProperty("opponentNickname")] public string OpponentNickname;
        [JsonProperty("totalRounds")] public int? TotalRounds;
        [JsonProperty("eloAfter")] public int? EloAfter;
        [JsonProperty("eloBefore")] public int? EloBefore;
    }

    // GET /social/chat/world/recent → ChatView
    public sealed class ChatDto
    {
        [JsonProperty("id")] public long Id;
        [JsonProperty("channel")] public string Channel;
        [JsonProperty("fromId")] public long FromId;
        [JsonProperty("fromName")] public string FromName;
        [JsonProperty("content")] public string Content;
        [JsonProperty("createdAt")] public string CreatedAt;
    }

    // POST /social/chat/world|private、/guild/chat 的响应（ChatView / GuildChatView 同形状核心字段）
    public sealed class ChatSendResultDto
    {
        [JsonProperty("id")] public long Id;
        [JsonProperty("channel")] public string Channel;
        [JsonProperty("fromId")] public long FromId;
        [JsonProperty("fromName")] public string FromName;
        [JsonProperty("content")] public string Content;
    }
}
