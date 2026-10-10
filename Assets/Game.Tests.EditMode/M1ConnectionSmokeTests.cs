using System;
using System.Collections;
using System.Threading.Tasks;
using Game.App;
using Game.Network;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// M1 验收：注册 → REST login → WS 连接 → LOGIN 联通真实双 VM 集群（10.244.216.57）。
    /// 需要集群在线；离线环境下跳过（避免 CI/离线误报）。
    /// </summary>
    public class M1ConnectionSmokeTests
    {
        private const string ServerHost = "10.244.216.57";

        [UnityTest]
        public IEnumerator RealServer_Register_Login_WsLogin_Flow()
        {
            if (!IsServerReachable())
            {
                Assert.Ignore("server unreachable, skip");
            }

            Task<bool> task = RunAuthFlowAsync();
            yield return new WaitUntil(() => task.IsCompleted);
            Assert.IsTrue(task.Result, "auth flow failed");
        }

        private static bool IsServerReachable()
        {
            // 轻量探测：集群存活检查（REST 健康由 Nginx 转 node）
            try
            {
                using (var c = new System.Net.Sockets.TcpClient())
                {
                    var connect = c.ConnectAsync(ServerHost, 80);
                    if (!connect.Wait(3000))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> RunAuthFlowAsync()
        {
            var config = new NetworkConfig { Host = ServerHost, UseSsl = false };
            var pushBus = new PushEventBus();
            using (var transport = new WsTransport())
            using (var connection = new GameConnection(transport) { PushEventBusInstance = pushBus })
            {
                var connState = new ConnectionStateMachine(connection, config);
                var rest = new RestClient(config);
                var auth = new AuthService(rest, connState);

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                connState.Authenticated += () => tcs.TrySetResult(true);
                connState.OnAuthFailed += reason => tcs.TrySetResult(false);

                var username = "nt_m1_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var register = await auth.RegisterAsync(username, "test123456", "M1Smoke").ConfigureAwait(true);
                if (!register.IsOk)
                {
                    Debug.LogError($"[M1Smoke] register failed: {register.Code} {register.Message}");
                    return false;
                }

                // WS 认证完成（协议 v2 LOGIN）——主线程泵驱动至事件或 10s 超时
                var deadline = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 10000;
                while (!tcs.Task.IsCompleted && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < deadline)
                {
                    connState.Update();
                    await Task.Delay(100).ConfigureAwait(true);
                }

                var ok = tcs.Task.IsCompleted && tcs.Task.Result;
                Debug.Log($"[M1Smoke] auth flow ok={ok} userId={auth.UserId} rtt={connection.LastRttMs}ms");
                return ok;
            }
        }
    }
}
