using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Game.Presentation
{
    /// <summary>
    /// 资源加载服务（M6 Addressables 化）：
    /// - WarmUpAsync：按 AssetCatalog 预热全部 Sprite/模型预制体到内存缓存（异步）；
    /// - Get*：同步读缓存（UI 渲染路径零异步、零 Resources.Load）。
    /// 预热时机：GameEntry 启动后台执行；未命中缓存返回 null（调用方兜底）。
    /// </summary>
    public static class AssetService
    {
        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, GameObject> PrefabCache = new Dictionary<string, GameObject>();
        private static Task _warmUp;
        private static bool _warmed;

        public static bool IsWarmed => _warmed;

        public static Task WarmUpAsync()
        {
            if (_warmUp != null)
            {
                return _warmUp;
            }

            _warmUp = WarmUpInternal();
            return _warmUp;
        }

        private static async Task WarmUpInternal()
        {
            try
            {
                foreach (var key in AssetCatalog.AllKeys)
                {
                    if (key.StartsWith("models/"))
                    {
                        var handle = Addressables.LoadAssetAsync<GameObject>(key);
                        var prefab = await handle.Task.ConfigureAwait(true);
                        if (prefab != null)
                        {
                            PrefabCache[key] = prefab;
                        }
                        else
                        {
                            Addressables.Release(handle);
                        }
                    }
                    else
                    {
                        var handle = Addressables.LoadAssetAsync<Sprite>(key);
                        var sprite = await handle.Task.ConfigureAwait(true);
                        if (sprite != null)
                        {
                            SpriteCache[key] = sprite;
                        }
                        else
                        {
                            Addressables.Release(handle);
                        }
                    }
                }

                _warmed = true;
                Debug.Log($"[AssetService] warmed: {SpriteCache.Count} sprites, {PrefabCache.Count} prefabs");
            }
            catch (Exception e)
            {
                // Build 未执行 Addressables Build 时 catalog 缺失：静默降级（图标/模型走兜底），不阻塞启动
                Debug.LogWarning($"[AssetService] warmup failed (Addressables built?): {e.Message}");
            }
        }

        public static Sprite GetAvatar(string charType)
        {
            return GetSprite($"avatars/{Normalize(charType)}");
        }

        public static Sprite GetItemIcon(string itemName)
        {
            return string.IsNullOrEmpty(itemName) ? null : GetSprite($"items/{itemName.ToLower()}");
        }

        public static Sprite GetFrame(string frameName)
        {
            return string.IsNullOrEmpty(frameName) ? null : GetSprite($"frames/{frameName.ToLower()}");
        }

        public static GameObject GetModelPrefab(string charType)
        {
            var key = $"models/{Normalize(charType)}";
            return PrefabCache.TryGetValue(key, out var prefab) ? prefab : null;
        }

        private static string Normalize(string charType)
        {
            return string.IsNullOrEmpty(charType) ? "warrior" : charType.ToLower();
        }

        private static Sprite GetSprite(string key)
        {
            return SpriteCache.TryGetValue(key, out var sprite) ? sprite : null;
        }
    }
}
