namespace Game.Network
{
    /// <summary>
    /// 服务器连接配置（协议 v2）。
    /// 默认指向已冻结的服务端集群；DevSettings 界面经 PlayerPrefs 覆盖（键前缀 tbg.net.*）。
    /// </summary>
    public sealed class NetworkConfig
    {
        private const string KeyHost = "tbg.net.host";
        private const string KeySsl = "tbg.net.ssl";

        /// <summary>游戏 WS 入口：ws(s)://host/ws（Nginx 分流两节点）。</summary>
        public string Host { get; set; } = "10.244.216.57";

        /// <summary>wss/https（自签证书需证书旁路，见 BypassCertificateHandler）。</summary>
        public bool UseSsl { get; set; }

        public string WsUrl => (UseSsl ? "wss" : "ws") + "://" + Host + "/ws";
        public string ApiBase => (UseSsl ? "https" : "http") + "://" + Host + "/api";

        public static NetworkConfig Load()
        {
            var cfg = new NetworkConfig();
            var host = UnityEngine.PlayerPrefs.GetString(KeyHost, cfg.Host);
            if (!string.IsNullOrWhiteSpace(host))
            {
                cfg.Host = host.Trim();
            }

            cfg.UseSsl = UnityEngine.PlayerPrefs.GetInt(KeySsl, 0) == 1;
            return cfg;
        }

        public void Save()
        {
            UnityEngine.PlayerPrefs.SetString(KeyHost, Host);
            UnityEngine.PlayerPrefs.SetInt(KeySsl, UseSsl ? 1 : 0);
            UnityEngine.PlayerPrefs.Save();
        }
    }
}
