using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.Network
{
    /// <summary>
    /// 统一 REST 客户端：Bearer 鉴权、统一 {code,message} 错误解析、自签证书旁路。
    /// await 在 Unity 主线程续跑（UnityWebRequest 的 SynchronizationContext）。
    /// </summary>
    public sealed class RestClient
    {
        private readonly NetworkConfig _config;
        private string _token;

        public RestClient(NetworkConfig config)
        {
            _config = config;
        }

        /// <summary>登录/注册成功后设置；登出置 null。</summary>
        public void SetToken(string token) => _token = token;

        public Task<RestResult<T>> GetAsync<T>(string path, bool auth = true)
            => SendAsync<T>(UnityWebRequest.kHttpVerbGET, path, null, auth);

        public Task<RestResult<T>> PostAsync<T>(string path, object body, bool auth = true)
            => SendAsync<T>(UnityWebRequest.kHttpVerbPOST, path, body, auth);

        public Task<RestResult<T>> PutAsync<T>(string path, object body, bool auth = true)
            => SendAsync<T>(UnityWebRequest.kHttpVerbPUT, path, body, auth);

        private async Task<RestResult<T>> SendAsync<T>(string verb, string path, object body, bool auth)
        {
            var url = _config.ApiBase + path;
            using (var request = new UnityWebRequest(url, verb))
            {
                if (body != null)
                {
                    var json = JsonConvert.SerializeObject(body);
                    request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json))
                    {
                        contentType = "application/json",
                    };
                }

                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 15;
                request.SetRequestHeader("Accept", "application/json");
                if (auth && !string.IsNullOrEmpty(_token))
                {
                    request.SetRequestHeader("Authorization", "Bearer " + _token);
                }

                if (_config.UseSsl)
                {
                    request.certificateHandler = BypassCertificateHandler.Instance;
                }

                try
                {
                    // UnityWebRequestAsyncOperation 在 2022.3 下不走 awaiter，统一用 completed 回调桥接
                    var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var started = System.Diagnostics.Stopwatch.GetTimestamp();
                    request.SendWebRequest().completed += _ => completion.TrySetResult(true);
                    await completion.Task;
                    var ms = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                    Debug.Log($"[REST] {verb} {path} -> {request.responseCode} ({ms}ms)");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[REST] {verb} {path} -> transport error: {e.Message}");
                    return RestResult<T>.Fail(-1, "transport error: " + e.Message);
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    // 服务端错误体可能仍带 {code,message}
                    var bodyText = request.downloadHandler?.text;
                    var parsed = TryParseError<T>(bodyText, (int)request.responseCode);
                    return parsed ?? RestResult<T>.Fail((int)request.responseCode, request.error);
                }

                var text = request.downloadHandler?.text ?? string.Empty;
                try
                {
                    var data = typeof(T) == typeof(EmptyResponse)
                        ? default
                        : JsonConvert.DeserializeObject<T>(text);
                    return RestResult<T>.Ok(data);
                }
                catch (Exception e)
                {
                    return RestResult<T>.Fail(-2, "bad json: " + e.Message);
                }
            }
        }

        private static RestResult<T> TryParseError<T>(string bodyText, int httpCode)
        {
            if (string.IsNullOrEmpty(bodyText))
            {
                return null;
            }

            try
            {
                var err = JsonConvert.DeserializeObject<ErrorBody>(bodyText);
                if (err != null && (err.code != 0 || !string.IsNullOrEmpty(err.message)))
                {
                    return RestResult<T>.Fail(err.code != 0 ? err.code : httpCode, err.message);
                }
            }
            catch
            {
                // 非 JSON 错误体
            }

            return null;
        }

        private sealed class ErrorBody
        {
            public int code;
            public string message;
        }

        /// <summary>无内容响应用。</summary>
        public sealed class EmptyResponse
        {
        }
    }

    /// <summary>统一 REST 结果：IsOk=true 时 data 有效；否则 code/message 可读（proto ErrorCode 语义）。</summary>
    public sealed class RestResult<T>
    {
        public bool IsOk { get; private set; }
        public T Data { get; private set; }
        public int Code { get; private set; }
        public string Message { get; private set; }

        public static RestResult<T> Ok(T data) => new RestResult<T> { IsOk = true, Data = data };
        public static RestResult<T> Fail(int code, string message) => new RestResult<T> { IsOk = false, Code = code, Message = message };
    }
}
