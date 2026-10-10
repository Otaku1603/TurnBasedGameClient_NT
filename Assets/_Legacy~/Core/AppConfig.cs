using TurnBasedGame.Services;
using UnityEngine;
using UnityEngine.Networking;

namespace TurnBasedGame.Core
{
    /// <summary>
    /// 应用配置，集中管理服务器地址和端口与SSL配置
    /// 将 IP 和端口集中管理，便于部署时统一修改连接参数
    /// </summary>
    public static class AppConfig
    {
        public const bool DEFAULT_SSL = true;

        // 服务器 IP
        public const string DEFAULT_IP = "10.242.80.35"; // 按需更改，开发或测试环境下，服务端在本机可以改为“localhost”

        // ================= PlayerPrefs Keys =================
        private const string KEY_USE_CUSTOM = "Config_UseCustom";
        private const string KEY_CUSTOM_IP = "Config_CustomIP";
        private const string KEY_CUSTOM_SSL = "Config_CustomSSL";

        // ================= 动态配置属性 =================

        /// <summary>
        /// 是否使用自定义配置
        /// </summary>
        public static bool IsCustomMode => PlayerPrefs.GetInt(KEY_USE_CUSTOM, 0) == 1;

        /// <summary>
        /// SSL 开关：优先读取本地配置，否则使用默认配置
        /// 注意：关闭 SSL 连接后需要在Unity编辑器中开启HTTP开关
        /// </summary>
        public static bool USE_SSL
        {
            get
            {
                if (IsCustomMode) return PlayerPrefs.GetInt(KEY_CUSTOM_SSL, DEFAULT_SSL ? 1 : 0) == 1;
                return DEFAULT_SSL;
            }
        }

        /// <summary>
        /// 服务器 IP：优先读取本地配置，否则使用默认配置
        /// </summary>
        public static string SERVER_IP
        {
            get
            {
                if (IsCustomMode) return PlayerPrefs.GetString(KEY_CUSTOM_IP, DEFAULT_IP);
                return DEFAULT_IP;
            }
        }

        // ================= 自动计算配置 =================

        // HTTP 端口：SSL模式走 Nginx(443)，非SSL模式走 Tomcat/Jetty(8080)
        public static int HttpPort => USE_SSL ? 443 : 8080;

        // TCP 端口
        public const int TcpPort = 9999;

        // HTTP 协议头
        public static string HttpProtocol => USE_SSL ? "https" : "http";

        // 最终的基础 URL
        public static string ApiBaseUrl => $"{HttpProtocol}://{SERVER_IP}:{HttpPort}/api";

        // ================= 辅助方法：统一获取证书处理器 =================

        /// <summary>
        /// 根据 SSL 开关决定是否需要忽略证书错误
        /// </summary>
        public static CertificateHandler GetCertificateHandler()
        {
            if (USE_SSL)
            {
                // 如果开启了 SSL 且是自签名证书，挂载忽略器
                return new BypassCertificateHandler();
            }
            return null;
        }

        // ================= 配置保存方法 (供 UI 调用) =================
        public static void SaveConfig(bool useCustom, string ip, bool useSSL)
        {
            PlayerPrefs.SetInt(KEY_USE_CUSTOM, useCustom ? 1 : 0);
            PlayerPrefs.SetString(KEY_CUSTOM_IP, ip);
            PlayerPrefs.SetInt(KEY_CUSTOM_SSL, useSSL ? 1 : 0);
            PlayerPrefs.Save();

            Debug.Log($"[AppConfig] Saved: Custom={useCustom}, IP={ip}, SSL={useSSL}");
        }

        /// <summary>
        /// 恢复默认设置
        /// </summary>
        public static void ResetToDefaults()
        {
            // 直接删除 Key，这样属性访问器就会返回默认值
            PlayerPrefs.DeleteKey(KEY_USE_CUSTOM);
            PlayerPrefs.DeleteKey(KEY_CUSTOM_IP);
            PlayerPrefs.DeleteKey(KEY_CUSTOM_SSL);
            PlayerPrefs.Save();

            Debug.LogWarning("[AppConfig] Reset to defaults.");
        }

        // ================= 日志输出方法 (供 Debug 调用) =================
        public static void LogEnvironment()
        {
            string protocol = USE_SSL ? "HTTPS/SSL" : "HTTP/TCP";
            Debug.Log($"[Environment] Mode: {protocol}");
            Debug.Log($"[Environment] Server: {SERVER_IP}");
            Debug.Log($"[Environment] API: {ApiBaseUrl}");
            Debug.Log($"[Environment] TCP Port: {TcpPort}");
        }
    }
}