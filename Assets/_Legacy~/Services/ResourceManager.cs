using System.Collections.Generic;
using TurnBasedGame.Core;
using UnityEngine;

namespace TurnBasedGame.Services
{
    /// <summary>
    /// 资源管理器，负责动态加载和缓存游戏资源
    /// 支持从Resources加载和动态生成兜底资源
    /// 使用对象池复用动态生成的精灵，减少压力
    /// </summary>
    public class ResourceManager
    {
        private static ResourceManager _instance;
        public static ResourceManager Instance => _instance ??= new ResourceManager();

        private Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();
        private Material _sharedMaterial;

        // ========== 对象池实现 ==========
        // 缓存动态生成的纯色精灵，按颜色和尺寸复用
        private Dictionary<string, Sprite> _colorSpritePool = new Dictionary<string, Sprite>();
        // ===============================

        /// <summary>
        /// 获取共享材质，解决Unity默认材质（粉色）问题
        /// </summary>
        public Material GetSharedMaterial()
        {
            if (_sharedMaterial == null)
            {
                Shader shader = Shader.Find("Legacy Shaders/Diffuse");
                if (shader == null) shader = Shader.Find("Standard");
                _sharedMaterial = new Material(shader);
            }
            return _sharedMaterial;
        }

        /// <summary>
        /// 路径清洗工具
        /// 数据库存的是 "/items/icon.png"，Resources.Load 需要 "items/icon"
        /// </summary>
        private string CleanPath(string dbPath)
        {
            if (string.IsNullOrEmpty(dbPath)) return "";

            // 1. 去掉开头的斜杠
            if (dbPath.StartsWith("/")) dbPath = dbPath.Substring(1);

            // 2. 去掉扩展名 (.png, .jpg)
            int extIndex = dbPath.LastIndexOf('.');
            if (extIndex > 0) dbPath = dbPath.Substring(0, extIndex);

            return dbPath;
        }

        /// <summary>
        /// 获取道具图标，优先从Resources加载，失败则生成颜色块
        /// 药水显示蓝色，其他道具显示黄色
        /// </summary>
        public Sprite GetItemIcon(int itemId)
        {
            string key = $"item_{itemId}";
            if (_spriteCache.ContainsKey(key)) return _spriteCache[key];

            var config = ConfigManager.Instance.GetItem(itemId);

            // 1. 尝试加载本地资源
            if (config != null && !string.IsNullOrEmpty(config.iconPath))
            {
                string path = CleanPath(config.iconPath);
                Sprite loaded = Resources.Load<Sprite>(path);
                if (loaded != null)
                {
                    _spriteCache[key] = loaded;
                    return loaded;
                }
            }

            // 2. 失败则从对象池获取或生成色块
            Color color = ColorConfig.GetItemColor(config?.type);
            Sprite sprite = GetOrCreateColorSprite(128, 128, color);

            _spriteCache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// 获取头像框，支持从Resources加载或动态生成镂空框
        /// 根据iconPath中的关键字决定边框颜色（gold=金色，silver=银色等）
        /// </summary>
        public Sprite GetFrameSprite(int frameId)
        {
            if (frameId <= 0) return null;

            string key = $"frame_{frameId}";
            if (_spriteCache.ContainsKey(key)) return _spriteCache[key];

            var config = ConfigManager.Instance.GetItem(frameId);

            // 1. 尝试加载本地资源
            if (config != null && !string.IsNullOrEmpty(config.iconPath))
            {
                string path = CleanPath(config.iconPath);
                Sprite loaded = Resources.Load<Sprite>(path);
                if (loaded != null)
                {
                    _spriteCache[key] = loaded;
                    return loaded;
                }
            }

            // 2. 失败则生成镂空框（不使用对象池，因为镂空效果特殊）
            Color frameColor = ColorConfig.FrameDefault;
            if (config != null && !string.IsNullOrEmpty(config.iconPath))
            {
                frameColor = ColorConfig.GetFrameColor(config.iconPath);
            }

            int size = 128;
            int border = 15;
            Texture2D texture = new Texture2D(size, size);
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (x < border || x >= size - border || y < border || y >= size - border)
                        pixels[y * size + x] = frameColor;
                    else
                        pixels[y * size + x] = Color.clear;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();

            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            _spriteCache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// 获取职业头像，数据库未存储路径，采用硬编码+颜色块兜底
        /// 战士=红色，法师=蓝色，刺客=紫色
        /// </summary>
        public Sprite GetAvatarSprite(string charType)
        {
            if (string.IsNullOrEmpty(charType)) charType = "warrior";
            string key = $"avatar_{charType}";
            if (_spriteCache.ContainsKey(key)) return _spriteCache[key];

            // 1. 尝试加载: avatars/warrior
            Sprite loaded = Resources.Load<Sprite>($"avatars/{charType.ToLower()}");
            if (loaded != null)
            {
                _spriteCache[key] = loaded;
                return loaded;
            }

            // 2. 失败则从对象池获取或生成
            Color color = ColorConfig.GetAvatarColor(charType);
            Sprite sprite = GetOrCreateColorSprite(128, 128, color);

            _spriteCache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// 获取或创建纯色精灵，相同颜色只创建一次
        /// 减少重复创建纹理带来的性能开销
        /// </summary>
        private Sprite GetOrCreateColorSprite(int width, int height, Color color)
        {
            // 生成唯一键：颜色+尺寸
            string poolKey = $"{color.r:F2}_{color.g:F2}_{color.b:F2}_{color.a:F2}_{width}x{height}";

            if (_colorSpritePool.TryGetValue(poolKey, out Sprite cachedSprite))
            {
                return cachedSprite;
            }

            // 对象池中没有，创建新的
            Texture2D texture = new Texture2D(width, height);
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply();

            Sprite newSprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
            _colorSpritePool[poolKey] = newSprite;
            return newSprite;
        }
    }
}