using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Network;
using Newtonsoft.Json;

namespace Game.App
{
    // ============ 配置 DTO（服务端 GamedataMapper 契约，camelCase） ============

    [Serializable]
    public sealed class SkillConfig
    {
        [JsonProperty("id")] public int Id;
        [JsonProperty("skillName")] public string SkillName;
        [JsonProperty("skillType")] public string SkillType;
        [JsonProperty("charType")] public string CharType;
        [JsonProperty("power")] public int Power;
        [JsonProperty("cooldown")] public int Cooldown;
        [JsonProperty("mpCost")] public int MpCost;
        [JsonProperty("targetType")] public string TargetType;
    }

    [Serializable]
    public sealed class ItemConfig
    {
        [JsonProperty("id")] public int Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("type")] public string Type;
        [JsonProperty("price")] public int Price;
        [JsonProperty("effectValue")] public int EffectValue;
    }

    [Serializable]
    public sealed class CharacterTemplate
    {
        [JsonProperty("charType")] public string CharType;
        [JsonProperty("displayName")] public string DisplayName;
        [JsonProperty("baseHp")] public int BaseHp;
        [JsonProperty("baseAttack")] public int BaseAttack;
        [JsonProperty("baseDefense")] public int BaseDefense;
        [JsonProperty("baseCritRate")] public int BaseCritRate;
        [JsonProperty("baseDodgeRate")] public int BaseDodgeRate;
    }

    /// <summary>
    /// 配置字典（/api/gamedata/*）：登录后拉取 skills/items/character-templates。
    /// UI 一律查表渲染，禁止硬编码（HANDOVER §6.2）。
    /// </summary>
    public sealed class GameDataService
    {
        private readonly RestClient _rest;

        public List<SkillConfig> Skills { get; private set; } = new List<SkillConfig>();
        public List<ItemConfig> Items { get; private set; } = new List<ItemConfig>();
        public List<CharacterTemplate> Templates { get; private set; } = new List<CharacterTemplate>();
        public bool IsLoaded { get; private set; }

        public GameDataService(RestClient rest)
        {
            _rest = rest;
        }

        public async Task<bool> LoadAllAsync()
        {
            try
            {
                var skills = await _rest.GetAsync<List<SkillConfig>>("/gamedata/skills").ConfigureAwait(true);
                if (!skills.IsOk)
                {
                    return false;
                }

                var items = await _rest.GetAsync<List<ItemConfig>>("/gamedata/items").ConfigureAwait(true);
                if (!items.IsOk)
                {
                    return false;
                }

                var templates = await _rest.GetAsync<List<CharacterTemplate>>("/gamedata/character-templates").ConfigureAwait(true);
                if (!templates.IsOk)
                {
                    return false;
                }

                Skills = skills.Data ?? new List<SkillConfig>();
                Items = items.Data ?? new List<ItemConfig>();
                Templates = templates.Data ?? new List<CharacterTemplate>();
                IsLoaded = true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public SkillConfig GetSkill(int skillId) => Skills.Find(s => s.Id == skillId);
        public ItemConfig GetItem(int itemId) => Items.Find(i => i.Id == itemId);
    }
}
