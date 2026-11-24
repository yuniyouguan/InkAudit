using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Globalization;

namespace InkAudit.Services;

/// <summary>
/// 应用本地化资源服务，基于 WinUI 3 内置 MRT Core（<see cref="ResourceLoader"/>）
/// 按本机 Windows 系统语言自动加载 <c>Strings/zh-CN/Resources.resw</c> 或 <c>Strings/en-US/Resources.resw</c>。
/// </summary>
public sealed class LocalizationService
{
    /// <summary>
    /// 全局单例本地化访问入口。
    /// </summary>
    public static LocalizationService Current { get; } = new();

    /// <summary>
    /// WinUI 3 内置 MRT Core 资源加载器（自动匹配本机语言环境）。
    /// </summary>
    private readonly ResourceLoader? _loader;

    /// <summary>
    /// 当 PRI 未加载时用于兜底判断本机系统是否为英文界面。
    /// </summary>
    private readonly bool _fallbackIsEnglish;

    /// <summary>
    /// 初始化本地化服务并绑定 WinUI 3 内置资源加载器。
    /// </summary>
    private LocalizationService()
    {
        _fallbackIsEnglish = !CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        try
        {
            _loader = new ResourceLoader();
        }
        catch
        {
            _loader = null;
        }
    }

    /// <summary>
    /// 从 MRT Core 资源表读取本地化文本，未命中时使用代码内双语兜底值。
    /// </summary>
    /// <param name="key">Resources.resw 中的资源键名。</param>
    /// <param name="zhFallback">简体中文兜底文案。</param>
    /// <param name="enFallback">英文兜底文案。</param>
    /// <returns>匹配本机语言环境的最终文本。</returns>
    private string Get(string key, string zhFallback, string enFallback)
    {
        if (_loader is not null)
        {
            try
            {
                var value = _loader.GetString(key);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            catch
            {
                // 回退至文化兜底文案
            }
        }

        return _fallbackIsEnglish ? enFallback : zhFallback;
    }

    // ==================== 通用与标题栏文本 ====================

    /// <summary>应用主标题。</summary>
    public string AppTitle => Get(nameof(AppTitle), "墨鉴", "InkAudit");

    /// <summary>粘贴按钮文本。</summary>
    public string ActionPaste => Get(nameof(ActionPaste), "粘贴", "Paste");

    /// <summary>导入文件按钮文本。</summary>
    public string ActionImport => Get(nameof(ActionImport), "导入", "Import");

    /// <summary>编辑按钮文本。</summary>
    public string ActionEdit => Get(nameof(ActionEdit), "编辑", "Edit");

    /// <summary>复制按钮文本。</summary>
    public string ActionCopy => Get(nameof(ActionCopy), "复制", "Copy");

    /// <summary>取消按钮文本。</summary>
    public string ActionCancel => Get(nameof(ActionCancel), "取消", "Cancel");

    /// <summary>应用改动按钮文本。</summary>
    public string ActionApply => Get(nameof(ActionApply), "应用", "Apply");

    /// <summary>保存按钮文本。</summary>
    public string ActionSave => Get(nameof(ActionSave), "保存", "Save");

    /// <summary>保存并重检按钮文本。</summary>
    public string ActionSaveAndRecheck => Get(nameof(ActionSaveAndRecheck), "保存并重检", "Save & Recheck");

    /// <summary>清除筛选按钮文本。</summary>
    public string ActionClearFilter => Get(nameof(ActionClearFilter), "清除", "Clear");

    /// <summary>开始检测主按钮文本。</summary>
    public string ActionStartAudit => Get(nameof(ActionStartAudit), "开始检测", "Audit");

    /// <summary>粘贴并检测按钮文本。</summary>
    public string ActionPasteAndAudit => Get(nameof(ActionPasteAndAudit), "粘贴并检测", "Paste & Audit");

    /// <summary>下拉菜单：仅全文合并复核。</summary>
    public string ActionMergedOnly => Get(nameof(ActionMergedOnly), "仅全文合并复核", "Full-Text Merge Only");

    /// <summary>下拉菜单：仅重检已改段落。</summary>
    public string ActionRecheckModified => Get(nameof(ActionRecheckModified), "仅重检已改段落", "Recheck Edited");

    /// <summary>设置弹窗标题与入口按钮文本。</summary>
    public string ActionSettings => Get(nameof(ActionSettings), "设置", "Settings");

    /// <summary>连通性测试按钮文本。</summary>
    public string ActionTest => Get(nameof(ActionTest), "测试", "Test");

    /// <summary>空输入框占位文本。</summary>
    public string PlaceholderPaste => Get(nameof(PlaceholderPaste), "粘贴正文…", "Paste text…");

    // ==================== 诊断与段落标签 ====================

    /// <summary>右侧面板标题：全文检测。</summary>
    public string LabelFullTextAudit => Get(nameof(LabelFullTextAudit), "全文检测", "Full Text");

    /// <summary>右侧开关标签：合并复核。</summary>
    public string LabelMergeCheck => Get(nameof(LabelMergeCheck), "合并复核", "Merge");

    /// <summary>段落改动后提示需重检的精简文案。</summary>
    public string LabelStaleNotice => Get(nameof(LabelStaleNotice), "段落已改，需重检", "Edited — re-audit needed");

    /// <summary>分类标签：人工特征。</summary>
    public string LabelHuman => Get(nameof(LabelHuman), "人工", "Human");

    /// <summary>分类标签：疑似 AI。</summary>
    public string LabelSuspected => Get(nameof(LabelSuspected), "疑似", "Suspected");

    /// <summary>分类标签：AI 特征。</summary>
    public string LabelAi => Get(nameof(LabelAi), "AI", "AI");

    /// <summary>辅助指标：整体置信度。</summary>
    public string LabelConfidence => Get(nameof(LabelConfidence), "置信度", "Confidence");

    /// <summary>辅助指标：疑似风险占比。</summary>
    public string LabelRiskRatio => Get(nameof(LabelRiskRatio), "风险占比", "Risk Ratio");

    /// <summary>右侧段落热力图标题。</summary>
    public string LabelDistribution => Get(nameof(LabelDistribution), "段落分布", "Paragraphs");

    /// <summary>段落状态：短段跳过。</summary>
    public string LabelSkipped => Get(nameof(LabelSkipped), "跳过", "Skipped");

    /// <summary>段落状态：已修改。</summary>
    public string LabelModified => Get(nameof(LabelModified), "已改", "Edited");

    /// <summary>段落状态：失败。</summary>
    public string LabelFailed => Get(nameof(LabelFailed), "失败", "Failed");

    /// <summary>单段操作：重检。</summary>
    public string LabelRecheck => Get(nameof(LabelRecheck), "重检", "Recheck");

    /// <summary>短段强制操作：检测。</summary>
    public string LabelDetect => Get(nameof(LabelDetect), "检测", "Check");

    /// <summary>错误段落操作：重试。</summary>
    public string LabelRetry => Get(nameof(LabelRetry), "重试", "Retry");

    // ==================== 设置项文本 ====================

    /// <summary>设置项：API Key。</summary>
    public string SettingsApiKey => Get(nameof(SettingsApiKey), "API Key", "API Key");

    /// <summary>设置项：接口地址。</summary>
    public string SettingsEndpoint => Get(nameof(SettingsEndpoint), "接口地址", "Endpoint");

    /// <summary>设置项：短段跳过阈值。</summary>
    public string SettingsSkipShort => Get(nameof(SettingsSkipShort), "跳过短段（字数）", "Skip short (chars)");

    /// <summary>设置项：全文合并复核。</summary>
    public string SettingsFullMerge => Get(nameof(SettingsFullMerge), "全文合并复核", "Full-text merge check");

    /// <summary>设置项：高风险段落底色。</summary>
    public string SettingsHeatmapTint => Get(nameof(SettingsHeatmapTint), "高风险段落底色", "Highlight risky rows");

    /// <summary>设置项：主题。</summary>
    public string SettingsTheme => Get(nameof(SettingsTheme), "主题", "Theme");

    /// <summary>主题选项：跟随系统。</summary>
    public string ThemeDefault => Get(nameof(ThemeDefault), "跟随系统", "System");

    /// <summary>主题选项：浅色。</summary>
    public string ThemeLight => Get(nameof(ThemeLight), "浅色", "Light");

    /// <summary>主题选项：深色。</summary>
    public string ThemeDark => Get(nameof(ThemeDark), "深色", "Dark");

    // ==================== 轻提示与校验错误 ====================

    /// <summary>轻提示：已复制全文。</summary>
    public string ToastCopiedAll => Get(nameof(ToastCopiedAll), "已复制全文", "Copied");

    /// <summary>轻提示：检测完成。</summary>
    public string ToastAuditDone => Get(nameof(ToastAuditDone), "检测完成", "Done");

    /// <summary>轻提示：重检完成。</summary>
    public string ToastRecheckDone => Get(nameof(ToastRecheckDone), "重检完成", "Rechecked");

    /// <summary>轻提示：已保存。</summary>
    public string ToastSaved => Get(nameof(ToastSaved), "已保存", "Saved");

    /// <summary>校验提示：剪贴板无文本。</summary>
    public string ErrClipboardNoText => Get(nameof(ErrClipboardNoText), "剪贴板无文本内容", "Clipboard has no text");

    /// <summary>校验提示：剪贴板为空。</summary>
    public string ErrClipboardEmpty => Get(nameof(ErrClipboardEmpty), "剪贴板为空", "Clipboard is empty");

    /// <summary>校验提示：文件格式不受支持。</summary>
    public string ErrUnsupportedFile => Get(nameof(ErrUnsupportedFile), "仅支持 .txt / .md 文件", "Only .txt / .md supported");

    /// <summary>校验提示：正文为空。</summary>
    public string ErrEmptyText => Get(nameof(ErrEmptyText), "请输入待检测正文", "Please enter text to audit");

    /// <summary>校验提示：未配置 API Key。</summary>
    public string ErrMissingApiKey => Get(nameof(ErrMissingApiKey), "请先配置 API Key", "Configure API Key first");

    /// <summary>校验提示：接口地址格式无效。</summary>
    public string ErrInvalidEndpoint => Get(nameof(ErrInvalidEndpoint), "接口地址格式无效", "Invalid endpoint URL");

    /// <summary>校验提示：请求超时。</summary>
    public string ErrTimeout => Get(nameof(ErrTimeout), "请求超时（超过 60 秒）", "Request timed out (60s)");

    /// <summary>校验提示：401 鉴权失败。</summary>
    public string ErrUnauthorized => Get(nameof(ErrUnauthorized), "鉴权失败（401）：API Key 无效或已过期", "Unauthorized (401): invalid API Key");

    /// <summary>校验提示：403 无权限。</summary>
    public string ErrForbidden => Get(nameof(ErrForbidden), "访问被拒绝（403）：请检查权限或额度", "Forbidden (403): check permissions or quota");

    /// <summary>校验提示：429 限流。</summary>
    public string ErrRateLimited => Get(nameof(ErrRateLimited), "请求过于频繁（429）：触发限流，请稍后再试", "Rate limited (429): retry later");

    /// <summary>校验提示：接口返回空响应。</summary>
    public string ErrEmptyResponse => Get(nameof(ErrEmptyResponse), "接口返回空响应", "Empty API response");

    /// <summary>校验提示：未返回该段结果。</summary>
    public string ErrSegmentMissing => Get(nameof(ErrSegmentMissing), "接口未返回该段结果", "No segment result returned");

    /// <summary>
    /// 格式化单段复制成功轻提示。
    /// </summary>
    /// <param name="indexDisplay">段落序号文本（如 "01"）。</param>
    /// <returns>本地化提示字符串。</returns>
    public string FormatCopiedParagraph(string indexDisplay)
        => string.Format(Get(nameof(FormatCopiedParagraph), "已复制 {0}", "Copied #{0}"), indexDisplay);

    /// <summary>
    /// 格式化标题栏字数与段落数统计文本。
    /// </summary>
    /// <param name="chars">总非空白字符数。</param>
    /// <param name="paragraphs">有效段落总数。</param>
    /// <returns>本地化统计字符串。</returns>
    public string FormatChapterStats(int chars, int paragraphs)
        => string.Format(Get(nameof(FormatChapterStats), "{0} 字 · {1} 段", "{0} chars · {1} paras"), $"{chars:N0}", paragraphs);

    /// <summary>
    /// 格式化逐段检测三项占比参考文本。
    /// </summary>
    /// <param name="human">人工占比（0~1）。</param>
    /// <param name="suspected">疑似 AI 占比（0~1）。</param>
    /// <param name="ai">AI 占比（0~1）。</param>
    /// <returns>本地化对比字符串。</returns>
    public string FormatSegmentComparison(double human, double suspected, double ai)
        => string.Format(
            Get(nameof(FormatSegmentComparison), "逐段 {0} / {1} / {2}", "Seg {0} / {1} / {2}"),
            $"{human * 100:F1}%",
            $"{suspected * 100:F1}%",
            $"{ai * 100:F1}%");

    /// <summary>
    /// 格式化连通性测试成功提示。
    /// </summary>
    /// <param name="tokens">本次测试消耗的 Makers Token 数。</param>
    /// <returns>本地化测试成功文案。</returns>
    public string FormatTestSuccess(int tokens)
    {
        if (tokens > 0)
        {
            return string.Format(Get("FormatTestSuccessWithTokens", "连接成功（Token {0}）", "Connected (Token {0})"), tokens);
        }

        return Get("FormatTestSuccess", "连接成功", "Connected");
    }

    /// <summary>
    /// 格式化全文复核失败提示。
    /// </summary>
    /// <param name="detail">具体错误原因。</param>
    /// <returns>本地化错误信息。</returns>
    public string FormatMergeError(string detail)
        => string.Format(Get(nameof(FormatMergeError), "全文复核失败：{0}", "Merge check failed: {0}"), detail);

    /// <summary>
    /// 格式化逐段检测失败提示。
    /// </summary>
    /// <param name="detail">具体错误原因。</param>
    /// <returns>本地化错误信息。</returns>
    public string FormatSegmentError(string detail)
        => string.Format(Get(nameof(FormatSegmentError), "逐段检测失败：{0}", "Paragraph audit failed: {0}"), detail);

    /// <summary>
    /// 格式化网络连接失败提示。
    /// </summary>
    /// <param name="detail">异常详情。</param>
    /// <returns>本地化错误信息。</returns>
    public string FormatNetworkError(string detail)
        => string.Format(Get(nameof(FormatNetworkError), "网络连接失败：{0}", "Network error: {0}"), detail);

    /// <summary>
    /// 格式化 JSON 解析失败提示。
    /// </summary>
    /// <param name="detail">解析异常消息。</param>
    /// <returns>本地化错误信息。</returns>
    public string FormatJsonParseError(string detail)
        => string.Format(Get(nameof(FormatJsonParseError), "返回数据解析失败：{0}", "Invalid response JSON: {0}"), detail);

    /// <summary>
    /// 格式化通用 HTTP 状态码错误提示。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="detail">接口返回的附加错误消息。</param>
    /// <returns>本地化错误信息。</returns>
    public string FormatHttpError(int statusCode, string? detail)
    {
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" — {detail}";
        return string.Format(Get(nameof(FormatHttpError), "请求失败（HTTP {0}）{1}", "HTTP {0}{1}"), statusCode, suffix);
    }
}
