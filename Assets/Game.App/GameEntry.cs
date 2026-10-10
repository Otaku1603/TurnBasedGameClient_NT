using Game.Network;
using UnityEngine;

namespace Game.App
{
    /// <summary>
    /// 应用入口：手工装配网络/服务栈（M2；VContainer 装配在 M3+ 替换），
    /// 驱动主线程泵。DontDestroyOnLoad 单例。
    /// </summary>
    public sealed class GameEntry : MonoBehaviour
    {
        public static GameEntry Instance { get; private set; }

        public NetworkConfig Config { get; private set; }
        public PushEventBus PushBus { get; private set; }
        public GameConnection Connection { get; private set; }
        public ConnectionStateMachine ConnState { get; private set; }
        public RestClient Rest { get; private set; }
        public AuthService Auth { get; private set; }
        public GameDataService GameData { get; private set; }
        public BattleService Battles { get; private set; }
        public ReconnectRecovery Recovery { get; private set; }

        private void Awake()
        {
            if (Instance != null)
            {
                // DevSettings 保存后重载场景：配置变了则整栈重建，否则保留现有实例
                var newCfg = NetworkConfig.Load();
                if (newCfg.Host == Instance.Config.Host && newCfg.UseSsl == Instance.Config.UseSsl)
                {
                    Destroy(gameObject);
                    return;
                }

                Debug.Log($"[GameEntry] config changed -> rebuild stack ({Instance.Config.Host} -> {newCfg.Host})");
                Instance.Shutdown();
                Destroy(Instance.gameObject);
                Instance = null;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;

            Config = NetworkConfig.Load();
            Debug.Log($"[GameEntry] ws={Config.WsUrl} api={Config.ApiBase}");
            if (Config.UseSsl)
            {
                // 自签证书在 Build 的 UnityTls 层会因 hostname 校验直接失败（curl error 35），
                // DevSettings -> Reset 可回 ws/http
                Debug.LogWarning("[GameEntry] SSL mode on: self-signed certs fail in builds; DevSettings->Reset to fall back to ws/http");
            }
            PushBus = new PushEventBus();
            var transport = new WsTransport();
            Connection = new GameConnection(transport) { PushEventBusInstance = PushBus };
            ConnState = new ConnectionStateMachine(Connection, Config);
            Rest = new RestClient(Config);
            Auth = new AuthService(Rest, ConnState);
            GameData = new GameDataService(Rest);
            Battles = new BattleService(Connection, PushBus);
            Recovery = new ReconnectRecovery(ConnState, Battles);
            MorePanel.Attach(gameObject);
        }

        private void Start()
        {
            ConnState.Authenticated += () => Battles.SelfUserId = Auth.UserId;

            // M6：预热 Addressables（头像/图标/模型），UI 同步读缓存
            _ = Game.Presentation.AssetService.WarmUpAsync();

            // 服务端 ERROR 推送统一可见（限流 RATE_LIMITED=7 等）
            PushBus.Subscribe(GameClient.Proto.MessageType.Error, msg =>
            {
                var e = msg.ErrorResponse;
                if (e != null)
                {
                    Debug.LogWarning($"[ServerError] code={e.Code} message={e.Message} reqId={e.RequestId}");
                }
            });

            // 好友申请实时提醒（FRIEND_REQUEST=43）
            PushBus.Subscribe(GameClient.Proto.MessageType.FriendRequest, msg =>
            {
                var p = msg.FriendRequestPush;
                if (p != null)
                {
                    Debug.Log($"[Social] 好友申请：{p.FromName} (ID:{p.FromId}) —— MORE → FrReq 审批");
                }
            });
        }

        private void Update()
        {
            ConnState.Update();
        }

        private void Shutdown()
        {
            ConnState?.Dispose();
            Battles?.Dispose();
            Recovery?.Dispose();
            Connection?.Dispose();
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            Shutdown();
            ConnState?.Dispose();
            Battles?.Dispose();
            Recovery?.Dispose();
            Connection?.Dispose();
            Instance = null;
        }
    }
}
