namespace TurnBasedGame.Model
{
    /// 行动者的动作类型
    public enum ActorAnimType
    {
        None,
        Attack, // 普通攻击/物理技能
        Cast,   // 施法/治疗
        Defend, // 防御
        Item,   // 使用道具
        Wait    // 待机/无动作
    }

    /// 目标的反应类型
    public enum TargetReactionType
    {
        None,
        Hit,    // 受击
        Dodge,  // 闪避
        Heal,   // 被治疗
        Die     // 死亡
    }
}