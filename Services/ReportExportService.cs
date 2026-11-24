using InkAudit.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace InkAudit.Services;

/// <summary>
/// 检测报告导出服务，将全文汇总统计与朱雀精确字符片段结果序列化为结构化 Markdown 文档。
/// </summary>
public static class ReportExportService
{
    /// <summary>
    /// 生成建议的默认导出文件名（不含扩展名，含本地时间戳）。
    /// </summary>
    /// <param name="timestamp">导出时间。</param>
    /// <returns>建议的 Markdown 文件名。</returns>
    public static string BuildSuggestedFileName(DateTime timestamp)
        => $"{LocalizationService.Current.ReportFileNamePrefix}-{timestamp:yyyyMMdd-HHmmss}";

    /// <summary>
    /// 将当前检测正文、全文汇总与精确切片列表组装为标准 GFM Markdown 检测报告。
    /// </summary>
    /// <param name="payload">当前正文内容。</param>
    /// <param name="summary">全文检测汇总模型。</param>
    /// <param name="segments">朱雀返回的精确字符区间片段列表。</param>
    /// <param name="timestamp">报告导出时间。</param>
    /// <returns>完整的 Markdown 报告文本。</returns>
    public static string BuildMarkdownReport(
        string payload,
        AuditSummary summary,
        IReadOnlyList<AuditSegmentItem> segments,
        DateTime timestamp)
    {
        var l10n = LocalizationService.Current;
        var (chars, paras) = ParagraphMapper.CountTextStats(payload);
        var sb = new StringBuilder(Math.Max(1024, payload.Length * 2));

        // 标题与元信息区块
        sb.Append("# ").AppendLine(l10n.ReportTitle);
        sb.AppendLine();
        sb.Append("- **").Append(l10n.ReportMetaTime).Append("**：").AppendLine($"{timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.Append("- **").Append(l10n.ReportMetaLength).Append("**：").AppendLine(l10n.FormatChapterStats(chars, paras));

        string modeLabel = ResolveAuditModeLabel(summary, segments.Count, l10n);
        sb.Append("- **").Append(l10n.ReportMetaMode).Append("**：").AppendLine(modeLabel);

        if (summary.LastTokensUsed > 0)
        {
            sb.Append("- **").Append(l10n.ReportMetaTokens).Append("**：").AppendLine($"{summary.LastTokensUsed:N0}");
        }

        if (summary.IsStale)
        {
            sb.AppendLine();
            sb.Append("> ").AppendLine(l10n.LabelStaleNotice);
        }

        // 全文总览表格
        if (summary.HasResult)
        {
            sb.AppendLine();
            sb.Append("## ").AppendLine(l10n.ReportSectionSummary);
            sb.AppendLine();
            sb.Append("| ").Append(l10n.ReportColMetric).Append(" | ").Append(l10n.ReportColValue).AppendLine(" |");
            sb.AppendLine("| --- | ---: |");
            sb.Append("| ").Append(l10n.LabelAiRatio).Append(" | ").Append(summary.AiPercentText).AppendLine(" |");
            sb.Append("| ").Append(l10n.LabelSuspected).Append(" | ").Append(summary.SuspectedPercentText).AppendLine(" |");
            sb.Append("| ").Append(l10n.LabelHuman).Append(" | ").Append(summary.HumanPercentText).AppendLine(" |");
            sb.Append("| ").Append(l10n.LabelConfidence).Append(" | ").Append(summary.SoftmaxConfidenceText).AppendLine(" |");
            sb.Append("| ").Append(l10n.LabelRiskRatio).Append(" | ").Append(summary.RatioConfidenceText).AppendLine(" |");

            if (summary.HasSegmentComparison)
            {
                string compare = $"{summary.SegmentHumanRatio * 100:F1}% / {summary.SegmentSuspectedRatio * 100:F1}% / {summary.SegmentAiRatio * 100:F1}%";
                sb.Append("| ").Append(l10n.ReportRowSegmentCompare).Append(" | ").Append(compare).AppendLine(" |");
            }
        }

        // 朱雀原始片段明细表与切片正文
        if (segments.Count > 0)
        {
            sb.AppendLine();
            sb.Append("## ").AppendLine(l10n.ReportSectionSegments);
            sb.AppendLine();
            sb.Append("| ")
              .Append(l10n.ReportColIndex).Append(" | ")
              .Append(l10n.ReportColLabel).Append(" | ")
              .Append(l10n.ReportColAiProb).Append(" | ")
              .Append(l10n.ReportColHumanProb).Append(" | ")
              .Append(l10n.ReportColChars).Append(" | ")
              .Append(l10n.ReportColRange).AppendLine(" |");
            sb.AppendLine("| --- | --- | ---: | ---: | ---: | ---: |");

            foreach (var seg in segments)
            {
                sb.Append("| #").Append(seg.IndexDisplay)
                  .Append(" | ").Append(seg.LabelDisplay)
                  .Append(" | ").Append(seg.AiPercentText)
                  .Append(" | ").Append(seg.HumanPercentText)
                  .Append(" | ").Append(seg.CharCount)
                  .Append(" | `[").Append(seg.Start).Append(", ").Append(seg.Length).AppendLine("]` |");
            }

            sb.AppendLine();
            sb.Append("## ").AppendLine(l10n.ReportSectionDetails);

            foreach (var seg in segments)
            {
                sb.AppendLine();
                sb.Append("### #").Append(seg.IndexDisplay)
                  .Append(" · ").Append(seg.LabelDisplay)
                  .Append("（").Append(l10n.FormatSegmentBubbleRatios(seg.AiPercentText, seg.HumanPercentText))
                  .Append(" · ").Append(l10n.FormatSegmentCharCount(seg.CharCount)).AppendLine("）");
                sb.AppendLine();
                AppendBlockquote(sb, seg.Text);
            }
        }
        else if (!string.IsNullOrWhiteSpace(payload))
        {
            sb.AppendLine();
            sb.Append("## ").AppendLine(l10n.ReportSectionFullText);
            sb.AppendLine();
            AppendBlockquote(sb, payload);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 解析本次报告对应的检测模式本地化名称。
    /// </summary>
    /// <param name="summary">全文汇总模型。</param>
    /// <param name="segmentCount">返回切片总数。</param>
    /// <param name="l10n">本地化服务实例。</param>
    /// <returns>检测方式说明文本。</returns>
    private static string ResolveAuditModeLabel(AuditSummary summary, int segmentCount, LocalizationService l10n)
    {
        if (summary.IsMergedSource && segmentCount > 0)
        {
            return l10n.ReportModeMergedAndSegments;
        }

        if (summary.IsMergedSource)
        {
            return l10n.ReportModeMergedOnly;
        }

        return l10n.ReportModeSegmentsOnly;
    }

    /// <summary>
    /// 将多行原文以标准 Markdown 引用块格式追加至缓冲区。
    /// </summary>
    /// <param name="sb">目标字符串构造器。</param>
    /// <param name="text">待引用的文本片段。</param>
    private static void AppendBlockquote(StringBuilder sb, string text)
    {
        string normalized = ParagraphMapper.NormalizeLineEndings(text).Trim();
        var lines = normalized.Split('\n');
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                sb.AppendLine(">");
            }
            else
            {
                sb.Append("> ").AppendLine(line);
            }
        }
    }
}
