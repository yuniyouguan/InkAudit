namespace InkAudit.Models;

/// <summary>
/// 应用本地持久化配置模型，涵盖朱雀接口鉴权、检测策略与界面主题。
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// 默认朱雀 AI 文本检测网关接口地址。
    /// </summary>
    public const string DefaultEndpointUrl = "https://ai-gateway.edgeone.link/v1/providers/zhuque-text/classify";

    /// <summary>
    /// 朱雀接口鉴权使用的 Bearer API Key（在磁盘上以 DPAPI 加密存储）。
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 朱雀分类接口完整请求地址。
    /// </summary>
    public string EndpointUrl { get; set; } = DefaultEndpointUrl;

    /// <summary>
    /// 是否启用短段落跳过策略（仅对逐段检测生效，全文合并复核始终保留完整正文）。
    /// </summary>
    public bool EnableShortParagraphSkip { get; set; } = true;

    /// <summary>
    /// 短段落跳过字数阈值（按非空白字符数计算）。
    /// </summary>
    public int ShortParagraphThreshold { get; set; } = 15;

    /// <summary>
    /// 是否在点击检测时同步执行全文合并复核（is_merge: true），默认开启。
    /// </summary>
    public bool EnableFullTextMergeCheck { get; set; } = true;

    /// <summary>
    /// 是否为高风险与疑似 AI 段落启用极淡背景底色标识。
    /// </summary>
    public bool ShowHeatmapTint { get; set; } = true;

    /// <summary>
    /// 界面外观主题模式："Default"（跟随系统）| "Light"（浅色）| "Dark"（深色）。
    /// </summary>
    public string ThemeMode { get; set; } = "Default";
}
