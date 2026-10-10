using GameClient.Proto;
using System;
using TurnBasedGame.Network;

namespace TurnBasedGame.Services
{
    /// <summary>
    /// 战斗服务，处理所有战斗相关的网络通信
    /// 将网络消息转换为事件，供Controller层订阅处理
    /// </summary>
    public class BattleService
    {
        private static BattleService _instance;
        public static BattleService Instance => _instance ??= new BattleService();

        // 事件定义
        public event Action<MatchSuccessResponse> OnMatchSuccess;
        public event Action<BattleStartResponse> OnBattleStart;
        public event Action<BattleUpdateResponse> OnBattleUpdate;
        public event Action<BattleEndResponse> OnBattleEnd;
        public event Action<BattleRejoinResponse> OnBattleRejoin;

        /// <summary>
        /// 私有构造函数，注册网络消息处理
        /// 将不同消息类型分发到对应的事件处理器
        /// </summary>
        private BattleService()
        {
            NetworkManager.Instance.OnMessageReceived += OnNetworkMessage;
        }

        /// <summary>
        /// 发送匹配请求，开始寻找对手
        /// 匹配成功后通过OnMatchSuccess事件通知
        /// </summary>
        public void SendMatchRequest()
        {
            var msg = new GameMessage
            {
                Type = MessageType.MatchRequest,
                Token = AuthService.Instance.CurrentToken,
                MatchRequest = new MatchRequest { UserId = AuthService.Instance.CurrentUserId }
            };
            NetworkManager.Instance.Send(msg);
        }

        /// <summary>
        /// 发送战斗准备就绪消息
        /// </summary>
        public void SendBattleReady(string battleId)
        {
            var msg = new GameMessage
            {
                Type = MessageType.BattleReady,
                Token = AuthService.Instance.CurrentToken,
                BattleReadyRequest = new BattleReadyRequest
                {
                    BattleId = battleId,
                    UserId = AuthService.Instance.CurrentUserId
                }
            };
            NetworkManager.Instance.Send(msg);
        }

        /// <summary>
        /// 发送战斗指令
        /// actionType: 1=技能, 2=防御, 3=道具
        /// paramId: 技能ID或道具ID
        /// </summary>
        public void SendBattleAction(string battleId, int actionType, int paramId)
        {
            var msg = new GameMessage
            {
                Type = MessageType.BattleAction,
                Token = AuthService.Instance.CurrentToken,
                BattleActionRequest = new BattleActionRequest
                {
                    BattleId = battleId,
                    UserId = AuthService.Instance.CurrentUserId,
                    ActionType = actionType,
                    ParamId = paramId
                }
            };
            NetworkManager.Instance.Send(msg);
        }

        /// <summary>
        /// 发送申请重新连接战斗消息
        /// </summary>
        public void SendBattleRejoin()
        {
            var msg = new GameMessage
            {
                Type = MessageType.BattleRejoin,
                Token = AuthService.Instance.CurrentToken,
                BattleRejoinRequest = new BattleRejoinRequest { UserId = AuthService.Instance.CurrentUserId }
            };
            NetworkManager.Instance.Send(msg);
        }

        /// <summary>
        /// 发送战斗投降消息
        /// </summary>
        public void SendBattleSurrender(string battleId)
        {
            var msg = new GameMessage
            {
                Type = MessageType.BattleSurrender,
                Token = AuthService.Instance.CurrentToken,
                BattleSurrenderRequest = new BattleSurrenderRequest
                {
                    BattleId = battleId,
                    UserId = AuthService.Instance.CurrentUserId
                }
            };
            NetworkManager.Instance.Send(msg);
        }

        /// <summary>
        /// 将网络层收到的原始消息（GameMessage）剥离外壳，
        /// 通过 C# 事件分发给 UI 控制层（Controller），实现网络层与视图层的解耦。
        /// </summary>
        private void OnNetworkMessage(GameMessage msg)
        {
            switch (msg.Type)
            {
                case MessageType.MatchSuccess:
                    OnMatchSuccess?.Invoke(msg.MatchSuccessResponse);
                    break;
                case MessageType.BattleStart:
                    OnBattleStart?.Invoke(msg.BattleStartResponse);
                    break;
                case MessageType.BattleUpdate:
                    OnBattleUpdate?.Invoke(msg.BattleUpdateResponse);
                    break;
                case MessageType.BattleEnd:
                    OnBattleEnd?.Invoke(msg.BattleEndResponse);
                    break;
                case MessageType.BattleRejoinResponse:
                    OnBattleRejoin?.Invoke(msg.BattleRejoinResponse);
                    break;
            }
        }
    }
}