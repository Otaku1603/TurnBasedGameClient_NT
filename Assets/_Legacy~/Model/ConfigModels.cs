using System;

namespace TurnBasedGame.Model
{
    // 通用 HTTP 响应包装类
    [Serializable]
    public class ApiResponse<T>
    {
        public bool success;
        public string message;
        public T data;
    }

    // 对应后端 Skill 实体
    [Serializable]
    public class SkillConfig
    {
        public int id;
        public string name;
        public string description;
        public string type;       // "attack" or "heal"
        public string targetType; // "ENEMY" or "SELF"
        public string iconPath;
    }

    // 对应后端 Item 实体
    [Serializable]
    public class ItemConfig
    {
        public int id;
        public string name;
        public string description;
        public string type;       // "POTION" or "AVATAR_FRAME"
        public int price;
        public string iconPath;
        public bool usableInBattle;
    }

    // 职业模板配置
    [Serializable]
    public class TemplateConfig
    {
        public int id;
        public string charType;    // "warrior"
        public string namePrefix;  // "见习战士"
        public int baseMaxHp;
        public int baseAttack;
        public int baseSpeed;
    }
}