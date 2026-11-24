using InkAudit.Models;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace InkAudit.Services;

/// <summary>
/// 朱雀文本检测接口调用异常，封装 HTTP 状态码与面向用户的本地化错误描述。
/// </summary>
public sealed class ZhuqueApiException : Exception
{
    /// <summary>
    /// 服务端返回的 HTTP 状态码（网络层异常或超时时为 null）。
    /// </summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// 初始化朱雀接口异常实例。
    /// </summary>
    /// <param name="message">面向界面展示的错误消息。</param>
    /// <param name="statusCode">可选 HTTP 状态码。</param>
    /// <param name="inner">底层原始异常。</param>
    public ZhuqueApiException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// 朱雀 AI 文本检测 HTTP 客户端服务，负责 Bearer 鉴权请求、响应反序列化与统一错误处理。
/// </summary>
public sealed class ZhuqueApiService
{
    /// <summary>
    /// 进程内复用的 HttpClient 实例，默认 60 秒请求超时。
    /// </summary>
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    /// <summary>
    /// JSON 序列化配置（忽略大小写以提高兼容性）。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 调用朱雀分类接口检测给定文本的 AI / 疑似 AI / 人工成分。
    /// </summary>
    /// <param name="text">待检测正文。</param>
    /// <param name="isMerge">是否合并段落进行上下文联合推理（true 为全文合并，false 为逐段独立输出）。</param>
    /// <param name="apiKey">Bearer 鉴权密钥。</param>
    /// <param name="endpointUrl">接口完整地址，为空时回退至默认网关地址。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>反序列化后的朱雀分类响应对象。</returns>
    public async Task<ZhuqueClassifyResponse> ClassifyAsync(
        string text,
        bool isMerge,
        string apiKey,
        string endpointUrl,
        CancellationToken cancellationToken = default)
    {
        var l10n = LocalizationService.Current;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ZhuqueApiException(l10n.ErrMissingApiKey);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ZhuqueApiException(l10n.ErrEmptyText);
        }

        var url = string.IsNullOrWhiteSpace(endpointUrl)
            ? AppSettings.DefaultEndpointUrl
            : endpointUrl.Trim();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ZhuqueApiException(l10n.ErrInvalidEndpoint);
        }

        var payload = new ZhuqueClassifyRequest
        {
            Text = text,
            IsMerge = isMerge
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await SharedHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ZhuqueApiException(l10n.ErrTimeout, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ZhuqueApiException(l10n.FormatNetworkError(ex.Message), null, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var detail = TryExtractErrorMessage(body);
                var message = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => l10n.ErrUnauthorized,
                    HttpStatusCode.Forbidden => l10n.ErrForbidden,
                    HttpStatusCode.TooManyRequests => l10n.ErrRateLimited,
                    _ => l10n.FormatHttpError((int)response.StatusCode, detail)
                };
                throw new ZhuqueApiException(message, response.StatusCode);
            }

            ZhuqueClassifyResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<ZhuqueClassifyResponse>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new ZhuqueApiException(l10n.FormatJsonParseError(ex.Message), response.StatusCode, ex);
            }

            if (parsed is null)
            {
                throw new ZhuqueApiException(l10n.ErrEmptyResponse, response.StatusCode);
            }

            if (!string.Equals(parsed.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                var reason = string.IsNullOrWhiteSpace(parsed.Msg)
                    ? $"status: {parsed.Status}"
                    : parsed.Msg;
                throw new ZhuqueApiException(reason, response.StatusCode);
            }

            return parsed;
        }
    }

    /// <summary>
    /// 发送轻量测试请求以验证 API Key 与接口连通性。
    /// </summary>
    /// <param name="apiKey">待验证的 API Key。</param>
    /// <param name="endpointUrl">待验证的接口地址。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次测试实际扣减的 Makers 免费额度 Token 数。</returns>
    public async Task<int> TestConnectionAsync(
        string apiKey,
        string endpointUrl,
        CancellationToken cancellationToken = default)
    {
        var result = await ClassifyAsync(
            "这是一段用于验证墨鉴接口连通性的测试文本。",
            isMerge: true,
            apiKey: apiKey,
            endpointUrl: endpointUrl,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.MakersModelsUsage?.TotalTokens ?? 0;
    }

    /// <summary>
    /// 尝试从非 2xx 响应体中提取可读错误摘要。
    /// </summary>
    /// <param name="body">服务端原始响应字符串。</param>
    /// <returns>提取出的错误文本，解析失败时截断返回原文。</returns>
    private static string? TryExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("msg", out var msg) && msg.ValueKind == JsonValueKind.String)
            {
                return msg.GetString();
            }

            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }

            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString();
                }

                if (error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var errMsg) &&
                    errMsg.ValueKind == JsonValueKind.String)
                {
                    return errMsg.GetString();
                }
            }
        }
        catch
        {
            // 非 JSON 格式错误体直接走原文截断
        }

        var trimmed = body.Trim();
        return trimmed.Length > 160 ? $"{trimmed[..160]}…" : trimmed;
    }
}
