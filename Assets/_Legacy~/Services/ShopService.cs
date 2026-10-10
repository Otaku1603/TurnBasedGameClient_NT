using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TurnBasedGame.Core;
using TurnBasedGame.Model;
using UnityEngine.Networking;

namespace TurnBasedGame.Services
{
    /// <summary>
    /// 商店服务，处理商城购买、装备和背包管理
    /// 协调本地金币缓存和服务器数据的同步
    /// </summary>
    public class ShopService
    {
        private static ShopService _instance;
        public static ShopService Instance => _instance ??= new ShopService();

        /// <summary>
        /// 获取用户背包，包含所有已拥有物品及其数量
        /// </summary>
        public IEnumerator GetInventory(Action<bool, List<UserInventory>> callback)
        {
            yield return GetRequest($"{AppConfig.ApiBaseUrl}/shop/inventory", (success, json) =>
            {
                if (success)
                {
                    var res = JsonConvert.DeserializeObject<ApiResponse<List<UserInventory>>>(json);
                    callback?.Invoke(res.success, res.data);
                }
                else callback?.Invoke(false, null);
            });
        }

        /// <summary>
        /// 购买物品，成功后需要更新本地金币缓存
        /// </summary>
        public IEnumerator BuyItem(int itemId, Action<bool, string> callback)
        {
            var body = new { itemId = itemId };
            yield return PostRequest($"{AppConfig.ApiBaseUrl}/shop/buy", JsonConvert.SerializeObject(body), callback);
        }

        /// <summary>
        /// 装备物品（目前仅支持头像框），成功后更新本地缓存并刷新UI显示
        /// </summary>
        public IEnumerator EquipItem(int itemId, Action<bool, string> callback)
        {
            var body = new { itemId = itemId };
            yield return PostRequest($"{AppConfig.ApiBaseUrl}/shop/equip", JsonConvert.SerializeObject(body), callback);
        }

        // 通用 GET
        private IEnumerator GetRequest(string url, Action<bool, string> callback)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.certificateHandler = AppConfig.GetCertificateHandler();
                // 必须带 Token
                req.SetRequestHeader("Authorization", "Bearer " + AuthService.Instance.CurrentToken);
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                    callback?.Invoke(true, req.downloadHandler.text);
                else
                    callback?.Invoke(false, req.error);
            }
        }

        // 通用 POST
        private IEnumerator PostRequest(string url, string json, Action<bool, string> callback)
        {
            using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.certificateHandler = AppConfig.GetCertificateHandler();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + AuthService.Instance.CurrentToken);

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    var res = JsonConvert.DeserializeObject<ApiResponse<object>>(req.downloadHandler.text);
                    callback?.Invoke(res.success, res.message);
                }
                else
                {
                    callback?.Invoke(false, req.error);
                }
            }
        }
    }
}