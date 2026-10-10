using System;

namespace TurnBasedGame.Model
{
    // 后端返回的背包数据 (只包含 ID 和 数量)
    [Serializable]
    public class UserInventory
    {
        public int id;          // 记录ID (无关紧要)
        public ItemConfig item;
        public int count;       // 数量
        public bool isEquipped; // 是否装备
    }

    // 背包物品的视图模型 (ViewModel)
    // 聚合了“动态数据”(服务器下发的 UserInventory) 和 “静态配置”(ConfigManager 中的 ItemConfig)
    // UI 层直接使用这个对象渲染，避免在 View 代码里频繁查表。
    public class InventoryItemViewModel
    {
        // 动态数据 (来自 UserInventory)
        public int ItemId;
        public int Count;
        public bool IsEquipped;

        // 静态数据 (来自 ConfigManager)
        public string Name;
        public string Description;
        public string Type; // "POTION" or "AVATAR_FRAME"
        public int Price;
        public UnityEngine.Sprite Icon;
    }
}