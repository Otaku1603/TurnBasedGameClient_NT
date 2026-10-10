using System.Collections;
using TurnBasedGame.Network;
using TurnBasedGame.Services;
using UnityEngine;

namespace TurnBasedGame.Core
{
    /// <summary>
    /// 游戏入口点（全局单例）
    /// 负责维持心跳包发送，确保 TCP 连接不因超时断开
    /// </summary>
    public class GameEntry : MonoBehaviour
    {
        public static GameEntry Instance { get; private set; }

        // 默认为false，由LoginController负责配置加载
        // GameEntry 现在主要负责保活和全局事件
        public bool loadConfigOnStart = false;

        private Coroutine _heartbeatCoroutine;

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;
        }

        void Start()
        {
            NetworkManager.Instance.OnConnected += StartHeartbeat;
            NetworkManager.Instance.OnDisconnected += StopHeartbeat;

            // 如果你希望 GameEntry 依然负责第一次加载，可以保留，但不要弹窗
            if (loadConfigOnStart)
            {
                StartCoroutine(ConfigManager.Instance.LoadAllConfigs(null));
            }

            // 展示网络连接配置
            AppConfig.LogEnvironment();
        }

        void Update() => NetworkManager.Instance.Update();
        void OnApplicationQuit() => NetworkManager.Instance.Disconnect();

        private void StartHeartbeat()
        {
            if (_heartbeatCoroutine != null) StopCoroutine(_heartbeatCoroutine);
            _heartbeatCoroutine = StartCoroutine(HeartbeatLoop());
        }

        private void StopHeartbeat()
        {
            if (_heartbeatCoroutine != null) StopCoroutine(_heartbeatCoroutine);
            _heartbeatCoroutine = null;
        }

        // 每 5 秒发送一次心跳，防止 NAT 超时或服务器判定掉线
        private IEnumerator HeartbeatLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(5f);
                NetworkManager.Instance.SendHeartbeat();
            }
        }
    }
}