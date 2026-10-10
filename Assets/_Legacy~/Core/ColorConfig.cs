using UnityEngine;

namespace TurnBasedGame.Core
{
    /// <summary>
    /// UI 颜色配置，通过代码动态修改 Image.color
    /// 集中管理所有UI颜色，便于统一调整和主题切换
    /// </summary>
    public static class ColorConfig
    {
        // 道具类型颜色
        public static readonly Color ItemPotion = new Color(0.2f, 0.2f, 0.8f);   // 药水 - 蓝色
        public static readonly Color ItemOther = new Color(1f, 0.8f, 0.2f);      // 其他道具 - 黄色

        // 头像框颜色（根据路径关键字）
        public static readonly Color FrameGold = new Color(1f, 0.84f, 0f);       // 金色边框
        public static readonly Color FrameSilver = new Color(0.75f, 0.75f, 0.75f); // 银色边框
        public static readonly Color FrameRed = Color.red;                       // 红色边框
        public static readonly Color FrameBlue = Color.blue;                     // 蓝色边框
        public static readonly Color FrameDefault = Color.gray;                  // 默认灰色边框

        // 职业头像颜色
        public static readonly Color AvatarWarrior = new Color(1f, 0.4f, 0.4f);  // 战士 - 红色
        public static readonly Color AvatarMage = new Color(0.4f, 0.4f, 1f);     // 法师 - 蓝色
        public static readonly Color AvatarAssassin = new Color(0.6f, 0f, 0.8f); // 刺客 - 紫色
        public static readonly Color AvatarDefault = new Color(1f, 0.4f, 0.4f);  // 默认 - 红色

        // UI状态颜色
        public static readonly Color OnlineStatus = Color.green;                 // 在线状态
        public static readonly Color OfflineStatus = Color.gray;                 // 离线状态

        /// <summary>
        /// 根据道具类型获取对应的颜色
        /// </summary>
        public static Color GetItemColor(string itemType)
        {
            if (string.IsNullOrEmpty(itemType)) return ItemOther;

            return itemType == "POTION" ? ItemPotion : ItemOther;
        }

        /// <summary>
        /// 根据头像框路径关键字获取对应的边框颜色
        /// </summary>
        public static Color GetFrameColor(string iconPath)
        {
            if (string.IsNullOrEmpty(iconPath)) return FrameDefault;

            string path = iconPath.ToLower();
            if (path.Contains("gold")) return FrameGold;
            if (path.Contains("silver")) return FrameSilver;
            if (path.Contains("red")) return FrameRed;
            if (path.Contains("blue")) return FrameBlue;

            return FrameDefault;
        }

        /// <summary>
        /// 根据职业类型获取对应的头像颜色
        /// </summary>
        public static Color GetAvatarColor(string charType)
        {
            if (string.IsNullOrEmpty(charType)) return AvatarDefault;

            switch (charType.ToLower())
            {
                case "mage": return AvatarMage;
                case "assassin": return AvatarAssassin;
                case "warrior": return AvatarWarrior;
                default: return AvatarDefault;
            }
        }
    }
}