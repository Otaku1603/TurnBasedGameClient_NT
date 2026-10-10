using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using TurnBasedGame.Core;
using TurnBasedGame.Model;
using UnityEngine;
using UnityEngine.Networking;

namespace TurnBasedGame.Services
{
    /// <summary>
    /// 配置管理器，负责加载游戏静态配置数据
    /// 包括技能、道具、职业模板等，是游戏运行的基础
    /// </summary>
    public class ConfigManager
    {
        private static ConfigManager _instance;
        public static ConfigManager Instance => _instance ??= new ConfigManager();

        public Dictionary<int, SkillConfig> SkillDict { get; private set; } = new Dictionary<int, SkillConfig>();
        public Dictionary<int, ItemConfig> ItemDict { get; private set; } = new Dictionary<int, ItemConfig>();
        public List<TemplateConfig> TemplateList { get; private set; } = new List<TemplateConfig>();

        public bool IsLoaded { get; private set; } = false;

        /// <summary>
        /// 阻塞式加载所有游戏配置（技能、道具、职业模板）
        /// 必须在进入游戏前完成，否则游戏功能无法正常使用
        /// </summary>
        public IEnumerator LoadAllConfigs(Action<bool> onComplete)
        {
            IsLoaded = false; // 重置状态

            // 使用标志位记录是否出错
            bool hasError = false;

            // 1. 加载技能
            yield return LoadRequest<List<SkillConfig>>($"{AppConfig.ApiBaseUrl}/gamedata/skills", (data) =>
            {
                SkillDict.Clear();
                foreach (var s in data) SkillDict[s.id] = s;
            }, () => hasError = true);

            if (hasError) { Fail(onComplete); yield break; }

            // 2. 加载道具
            yield return LoadRequest<List<ItemConfig>>($"{AppConfig.ApiBaseUrl}/gamedata/items", (data) =>
            {
                ItemDict.Clear();
                foreach (var i in data) ItemDict[i.id] = i;
            }, () => hasError = true);

            if (hasError) { Fail(onComplete); yield break; }

            // 3. 加载模板
            yield return LoadRequest<List<TemplateConfig>>($"{AppConfig.ApiBaseUrl}/gamedata/templates", (data) =>
            {
                TemplateList = data;
            }, () => hasError = true);

            if (hasError) { Fail(onComplete); yield break; }

            // 全部成功
            IsLoaded = true;
            onComplete?.Invoke(true);
        }

        private void Fail(Action<bool> onComplete)
        {
            Debug.LogError("[Config] Load Failed. One or more requests failed.");
            IsLoaded = false;
            onComplete?.Invoke(false);
        }

        /// <summary>
        /// 通用的HTTP配置加载协程
        /// 统一处理网络请求、JSON解析和错误处理
        /// </summary>
        private IEnumerator LoadRequest<T>(string url, Action<T> onSuccess, Action onFail)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                // 挂载证书处理器
                req.certificateHandler = AppConfig.GetCertificateHandler();

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[Config] Request Error ({url}): {req.error}");
                    onFail?.Invoke();
                }
                else
                {
                    try
                    {
                        var res = JsonConvert.DeserializeObject<ApiResponse<T>>(req.downloadHandler.text);
                        if (res != null && res.success)
                        {
                            onSuccess?.Invoke(res.data);
                        }
                        else
                        {
                            Debug.LogError($"[Config] API Error ({url}): {res?.message}");
                            onFail?.Invoke();
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Config] Parse Error ({url}): {e.Message}");
                        onFail?.Invoke();
                    }
                }
            }
        }

        /// <summary>
        /// 重置状态，强制下次重新加载
        /// </summary>
        public void Reset()
        {
            IsLoaded = false;
            SkillDict.Clear();
            ItemDict.Clear();
            TemplateList.Clear();
        }

        public SkillConfig GetSkill(int id) => SkillDict.ContainsKey(id) ? SkillDict[id] : null;
        public ItemConfig GetItem(int id) => ItemDict.ContainsKey(id) ? ItemDict[id] : null;
    }
}