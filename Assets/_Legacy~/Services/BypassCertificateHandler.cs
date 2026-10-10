using UnityEngine.Networking;

namespace TurnBasedGame.Services
{
    /// <summary>
    /// 用于绕过 SSL 验证（仅限开发与测试环境！）
    /// </summary>
    public class BypassCertificateHandler : CertificateHandler
    {
        /// <summary>
        /// SSL 证书验证绕过方法（仅限开发与测试环境！）
        /// </summary>
        /// <returns>返回“true”绕过证书错误</returns>
        protected override bool ValidateCertificate(byte[] certificateData)
        {
            return true;
        }
    }
}