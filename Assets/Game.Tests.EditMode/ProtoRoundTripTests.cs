using GameClient.Proto;
using Google.Protobuf;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>协议 v2 生成代码往返验证（信封字段 + 强类型枚举）。</summary>
    public class ProtoRoundTripTests
    {
        [Test]
        public void GameMessage_LoginRoundTrip()
        {
            var msg = new GameMessage
            {
                Version = 2,
                RequestId = 123,
                Type = MessageType.Login,
                Token = "jwt-token",
                LoginRequest = new LoginRequest { Username = "player1", Password = "player1pass" },
            };
            var bytes = msg.ToByteArray();

            var back = GameMessage.Parser.ParseFrom(bytes);

            Assert.AreEqual(2, back.Version);
            Assert.AreEqual(123, back.RequestId);
            Assert.AreEqual(MessageType.Login, back.Type);
            Assert.AreEqual("jwt-token", back.Token);
            Assert.AreEqual("player1", back.LoginRequest.Username);
        }

        [Test]
        public void BattleUpdate_ActionTypeStrongTyped()
        {
            var update = new BattleUpdateResponse
            {
                BattleId = "b-1",
                CurrentRound = 3,
                ActionType = ActionType.UseSkill,
                SkillId = 7,
                Damage = 120,
            };
            var back = BattleUpdateResponse.Parser.ParseFrom(update.ToByteArray());

            Assert.AreEqual(ActionType.UseSkill, back.ActionType);
            Assert.AreEqual(7, back.SkillId);
            Assert.AreEqual(120, back.Damage);
        }
    }
}
