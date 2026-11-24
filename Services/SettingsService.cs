using InkAudit.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace InkAudit.Services;

/// <summary>
/// 本地配置持久化服务，将配置写入 %LocalAppData%\InkAudit\settings.json，
/// 并使用 Windows DPAPI（LOCAL=user）对 API Key 进行本机用户级加密保护。
/// </summary>
public sealed class SettingsService
{
    /// <summary>
    /// JSON 格式化输出配置。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 磁盘存储传输对象，区分加密后的 API Key 与普通配置项。
    /// </summary>
    private sealed class StoredSettingsDto
    {
        /// <summary>经 DPAPI 加密并 Base64 编码后的 API Key。</summary>
        public string? ProtectedApiKey { get; set; }

        /// <summary>极少数不支持 DPAPI 环境下的明文回退字段。</summary>
        public string? PlainApiKeyFallback { get; set; }

        /// <summary>朱雀接口地址。</summary>
        public string EndpointUrl { get; set; } = AppSettings.DefaultEndpointUrl;

        /// <summary>是否开启短段跳过策略。</summary>
        public bool EnableShortParagraphSkip { get; set; } = true;

        /// <summary>短段跳过最小字数阈值。</summary>
        public int ShortParagraphThreshold { get; set; } = 15;

        /// <summary>是否开启全文合并复核。</summary>
        public bool EnableFullTextMergeCheck { get; set; } = true;

        /// <summary>是否开启高风险段落淡底色。</summary>
        public bool ShowHeatmapTint { get; set; } = true;

        /// <summary>外观主题模式。</summary>
        public string ThemeMode { get; set; } = "Default";
    }

    /// <summary>
    /// 本地 settings.json 的绝对路径。
    /// </summary>
    private readonly string _settingsFilePath;

    /// <summary>
    /// 初始化设置服务并确保本地应用数据目录存在。
    /// </summary>
    public SettingsService()
    {
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appDataRoot, "InkAudit");
        Directory.CreateDirectory(folder);
        _settingsFilePath = Path.Combine(folder, "settings.json");
    }

    /// <summary>
    /// 异步读取本地配置文件并解密 API Key。
    /// </summary>
    /// <returns>读取成功返回持久化配置，文件缺失或损坏时返回默认配置。</returns>
    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsFilePath).ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<StoredSettingsDto>(json, JsonOptions);
            if (dto is null)
            {
                return new AppSettings();
            }

            var apiKey = string.Empty;
            if (!string.IsNullOrWhiteSpace(dto.ProtectedApiKey))
            {
                apiKey = await UnprotectStringAsync(dto.ProtectedApiKey).ConfigureAwait(false);
            }
            else if (!string.IsNullOrWhiteSpace(dto.PlainApiKeyFallback))
            {
                apiKey = dto.PlainApiKeyFallback;
            }

            return new AppSettings
            {
                ApiKey = apiKey,
                EndpointUrl = string.IsNullOrWhiteSpace(dto.EndpointUrl)
                    ? AppSettings.DefaultEndpointUrl
                    : dto.EndpointUrl,
                EnableShortParagraphSkip = dto.EnableShortParagraphSkip,
                ShortParagraphThreshold = Math.Clamp(dto.ShortParagraphThreshold, 0, 500),
                EnableFullTextMergeCheck = dto.EnableFullTextMergeCheck,
                ShowHeatmapTint = dto.ShowHeatmapTint,
                ThemeMode = dto.ThemeMode is "Light" or "Dark" ? dto.ThemeMode : "Default"
            };
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// 异步加密敏感字段并将最新配置写入本地磁盘。
    /// </summary>
    /// <param name="settings">待保存的应用配置对象。</param>
    public async Task SaveAsync(AppSettings settings)
    {
        var dto = new StoredSettingsDto
        {
            EndpointUrl = string.IsNullOrWhiteSpace(settings.EndpointUrl)
                ? AppSettings.DefaultEndpointUrl
                : settings.EndpointUrl.Trim(),
            EnableShortParagraphSkip = settings.EnableShortParagraphSkip,
            ShortParagraphThreshold = Math.Clamp(settings.ShortParagraphThreshold, 0, 500),
            EnableFullTextMergeCheck = settings.EnableFullTextMergeCheck,
            ShowHeatmapTint = settings.ShowHeatmapTint,
            ThemeMode = settings.ThemeMode
        };

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            var protectedKey = await ProtectStringAsync(settings.ApiKey.Trim()).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(protectedKey))
            {
                dto.ProtectedApiKey = protectedKey;
            }
            else
            {
                dto.PlainApiKeyFallback = settings.ApiKey.Trim();
            }
        }

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        await File.WriteAllTextAsync(_settingsFilePath, json).ConfigureAwait(false);
    }

    /// <summary>
    /// 使用当前 Windows 用户凭据（DPAPI）加密明文字符串。
    /// </summary>
    /// <param name="plainText">待加密明文。</param>
    /// <returns>Base64 密文，环境不支持时返回 null。</returns>
    private static async Task<string?> ProtectStringAsync(string plainText)
    {
        try
        {
            var provider = new DataProtectionProvider("LOCAL=user");
            var inputBuffer = CryptographicBuffer.ConvertStringToBinary(plainText, BinaryStringEncoding.Utf8);
            var protectedBuffer = await provider.ProtectAsync(inputBuffer);
            return CryptographicBuffer.EncodeToBase64String(protectedBuffer);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 使用当前 Windows 用户凭据（DPAPI）解密 Base64 密文。
    /// </summary>
    /// <param name="base64">加密存储的 Base64 字符串。</param>
    /// <returns>解密后的明文，解密失败时返回空字符串。</returns>
    private static async Task<string> UnprotectStringAsync(string base64)
    {
        try
        {
            var provider = new DataProtectionProvider();
            var protectedBuffer = CryptographicBuffer.DecodeFromBase64String(base64);
            var unprotectedBuffer = await provider.UnprotectAsync(protectedBuffer);
            return CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, unprotectedBuffer);
        }
        catch
        {
            return string.Empty;
        }
    }
}
