using System.Net;
using System.Net.Security;
using UnityEngine.Networking;

namespace Game.Network
{
    /// <summary>
    /// 自签证书旁路（仅限开发与测试环境——服务端 nginx/certs 为自签，见 HANDOVER §5）。
    /// REST：以单例挂在 UnityWebRequest.certificateHandler。
    /// WebSocket(wss)：InstallOnce 安装 ServicePointManager 全局回调（Mono/编辑器有效；
    /// 生产替换正式 CA 后应移除安装）。
    /// </summary>
    public sealed class BypassCertificateHandler : CertificateHandler
    {
        public static readonly BypassCertificateHandler Instance = new BypassCertificateHandler();
        private static bool _installed;

        protected override bool ValidateCertificate(byte[] certificateData) => true;

        public static void InstallOnce()
        {
            if (_installed)
            {
                return;
            }

            _installed = true;
            ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, errors) => true;
        }
    }
}
