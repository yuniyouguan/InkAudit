using InkAudit.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InkAudit.Services;

/// <summary>
/// 记录已送检段落在批量拼接文本中的字符偏移区间 [Start, End)。
/// </summary>
/// <param name="Item">对应的段落视图模型。</param>
/// <param name="Start">该段在拼接请求串中的起始下标（含）。</param>
/// <param name="End">该段在拼接请求串中的结束下标（不含）。</param>
public sealed record SentParagraphSpan(ParagraphItem Item, int Start, int End);

/// <summary>
/// 逐段批量送检的构造结果，分离出实际送检内容、偏移映射表与因过短被跳过的段落。
/// </summary>
/// <param name="Payload">用换行符拼接后的待检测请求文本。</param>
/// <param name="SentSpans">所有参与本次送检的段落及其偏移区间。</param>
/// <param name="SkippedItems">字数低于阈值而被跳过的短段落集合。</param>
public sealed record BatchPayloadResult(
    string Payload,
    List<SentParagraphSpan> SentSpans,
    List<ParagraphItem> SkippedItems);

/// <summary>
/// 章节切段、短段过滤与朱雀分段结果回填映射工具类。
/// 支持朱雀接口的 [start, length] 位置协议、多段打包映射以及超长段多子片段加权聚合。
/// </summary>
public static class ParagraphMapper
{
    /// <summary>
    /// 将整章原始文本按换行切分为有效非空自然段列表。
    /// </summary>
    /// <param name="rawText">原始章节文本。</param>
    /// <param name="showHeatmapTint">段落初始是否启用高风险淡底色。</param>
    /// <returns>按顺序编号的段落视图模型列表。</returns>
    public static List<ParagraphItem> SplitIntoParagraphs(string rawText, bool showHeatmapTint)
    {
        var result = new List<ParagraphItem>();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return result;
        }

        var normalized = rawText.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalized.Split('\n');

        int index = 1;
        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            result.Add(new ParagraphItem
            {
                Index = index++,
                Text = trimmed,
                EditDraft = trimmed,
                Status = ParagraphAuditStatus.Pending,
                ShowHeatmapTint = showHeatmapTint
            });
        }

        return result;
    }

    /// <summary>
    /// 将段落列表用双换行拼接为适合复制或整章编辑的可读文本。
    /// </summary>
    /// <param name="items">段落集合。</param>
    /// <returns>以空行分隔的完整章节文本。</returns>
    public static string JoinParagraphs(IEnumerable<ParagraphItem> items)
        => string.Join("\n\n", items.Select(p => p.Text));

    /// <summary>
    /// 将段落列表用单换行拼接为提交给朱雀全文合并接口的紧凑文本。
    /// </summary>
    /// <param name="items">段落集合。</param>
    /// <returns>以单换行分隔的完整章节文本。</returns>
    public static string JoinParagraphsForApi(IEnumerable<ParagraphItem> items)
        => string.Join("\n", items.Select(p => p.Text));

    /// <summary>
    /// 按短段跳过策略过滤段落，构造逐段批量检测（is_merge: false）的请求体与字符偏移表。
    /// </summary>
    /// <param name="items">全章段落集合。</param>
    /// <param name="enableShortSkip">是否启用短段跳过。</param>
    /// <param name="shortThreshold">跳过最小非空白字数阈值。</param>
    /// <returns>包含拼接文本、偏移表和跳过列表的构造结果。</returns>
    public static BatchPayloadResult BuildBatchPayload(
        IReadOnlyList<ParagraphItem> items,
        bool enableShortSkip,
        int shortThreshold)
    {
        var spans = new List<SentParagraphSpan>();
        var skipped = new List<ParagraphItem>();
        var sb = new StringBuilder();

        foreach (var item in items)
        {
            if (enableShortSkip && shortThreshold > 0 && item.CharCount < shortThreshold)
            {
                skipped.Add(item);
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            int start = sb.Length;
            sb.Append(item.Text);
            int end = sb.Length;
            spans.Add(new SentParagraphSpan(item, start, end));
        }

        return new BatchPayloadResult(sb.ToString(), spans, skipped);
    }

    /// <summary>
    /// 将逐段批量检测响应中的 segment_labels 精确映射回对应的段落视图模型。
    /// </summary>
    /// <param name="spans">送检时记录的段落偏移映射表。</param>
    /// <param name="response">朱雀接口返回的批量响应。</param>
    public static void ApplyBatchResponse(
        IReadOnlyList<SentParagraphSpan> spans,
        ZhuqueClassifyResponse response)
    {
        var segments = response.SegmentLabels ?? new List<ZhuqueSegmentLabel>();
        var buckets = new Dictionary<ParagraphItem, List<ZhuqueSegmentLabel>>();
        foreach (var span in spans)
        {
            buckets[span.Item] = new List<ZhuqueSegmentLabel>();
        }

        var unmatchedSegments = new List<ZhuqueSegmentLabel>();

        foreach (var seg in segments)
        {
            var matchedSpans = new List<SentParagraphSpan>();

            // 策略 1：朱雀接口实测 position 为 [start, length]，按区间重叠支持“多段共享同一窗口”与“长段拆多片段”
            if (seg.Position is { Count: >= 2 } && seg.Position[1] > 0)
            {
                int segStart = seg.Position[0];
                int segEnd = segStart + seg.Position[1];

                foreach (var span in spans)
                {
                    int overlap = Math.Min(span.End, segEnd) - Math.Max(span.Start, segStart);
                    if (overlap > 0)
                    {
                        matchedSpans.Add(span);
                    }
                }
            }

            // 策略 2：位置缺失时按正文包含关系兜底匹配
            if (matchedSpans.Count == 0 && !string.IsNullOrWhiteSpace(seg.Text))
            {
                var segTrim = seg.Text.Trim();
                foreach (var span in spans)
                {
                    if (segTrim.Contains(span.Item.Text, StringComparison.Ordinal) ||
                        span.Item.Text.Contains(segTrim, StringComparison.Ordinal))
                    {
                        matchedSpans.Add(span);
                    }
                }
            }

            if (matchedSpans.Count > 0)
            {
                foreach (var span in matchedSpans)
                {
                    buckets[span.Item].Add(seg);
                }
            }
            else
            {
                unmatchedSegments.Add(seg);
            }
        }

        // 策略 3：剩余未匹配分段按顺序回填至尚未获得结果的段落
        if (unmatchedSegments.Count > 0)
        {
            var emptySpans = spans.Where(s => buckets[s.Item].Count == 0).ToList();
            for (int i = 0; i < Math.Min(emptySpans.Count, unmatchedSegments.Count); i++)
            {
                buckets[emptySpans[i].Item].Add(unmatchedSegments[i]);
            }
        }

        foreach (var span in spans)
        {
            var item = span.Item;
            var segs = buckets[item];

            if (segs.Count == 0)
            {
                item.Status = ParagraphAuditStatus.Error;
                item.ErrorMessage = LocalizationService.Current.ErrSegmentMissing;
                continue;
            }

            ApplySegmentsToItem(item, segs, recordDelta: false);
        }
    }

    /// <summary>
    /// 将单段独立重检的响应应用到目标段落，并保留旧置信度以展示改稿降幅。
    /// </summary>
    /// <param name="item">待更新的目标段落。</param>
    /// <param name="response">单段请求的朱雀响应。</param>
    public static void ApplySingleResponse(ParagraphItem item, ZhuqueClassifyResponse response)
    {
        var segs = response.SegmentLabels;
        if (segs is { Count: > 0 })
        {
            ApplySegmentsToItem(item, segs, recordDelta: true);
            return;
        }

        // 若 segment_labels 为空则回退至顶层汇总字段
        var prevConf = item.Label.HasValue ? item.Confidence : (double?)null;
        var h = response.GetLabelRatio(0);
        var a = response.GetLabelRatio(1);
        var s = response.GetLabelRatio(2);

        int dominantLabel = 0;
        if (a >= h && a >= s)
        {
            dominantLabel = 1;
        }
        else if (s >= h && s >= a)
        {
            dominantLabel = 2;
        }

        item.PreviousConfidence = prevConf;
        item.Label = dominantLabel;
        item.Confidence = response.SoftmaxConfidence;
        item.HasMultiSegmentBreakdown = false;
        item.HumanRatio = h;
        item.SuspectedRatio = s;
        item.AiRatio = a;
        item.LastAuditedText = item.Text;
        item.ErrorMessage = null;
        item.Status = ParagraphAuditStatus.Audited;
    }

    /// <summary>
    /// 将匹配到某一段落的 1 个或多个朱雀分段结果写入该段落模型；
    /// 当存在多个子分段时，按字符长度加权聚合人工 / 疑似 / AI 三项占比与平均置信度。
    /// </summary>
    /// <param name="item">目标段落视图模型。</param>
    /// <param name="segs">归属于该段落的分段列表。</param>
    /// <param name="recordDelta">是否记录重检前的旧置信度用于变化量展示。</param>
    private static void ApplySegmentsToItem(
        ParagraphItem item,
        List<ZhuqueSegmentLabel> segs,
        bool recordDelta)
    {
        var prevConf = recordDelta && item.Label.HasValue ? item.Confidence : (double?)null;

        if (segs.Count == 1)
        {
            var seg = segs[0];
            item.PreviousConfidence = prevConf;
            item.Label = seg.Label;
            item.Confidence = seg.Conf;
            item.HasMultiSegmentBreakdown = false;
            item.HumanRatio = seg.Label == 0 ? 1 : 0;
            item.AiRatio = seg.Label == 1 ? 1 : 0;
            item.SuspectedRatio = seg.Label == 2 ? 1 : 0;
            item.LastAuditedText = item.Text;
            item.ErrorMessage = null;
            item.Status = ParagraphAuditStatus.Audited;
            return;
        }

        double totalWeight = 0;
        double humanWeight = 0;
        double aiWeight = 0;
        double suspectedWeight = 0;
        double weightedConf = 0;

        foreach (var seg in segs)
        {
            double w = Math.Max(1, seg.Text?.Length ?? 1);
            totalWeight += w;
            weightedConf += seg.Conf * w;

            switch (seg.Label)
            {
                case 0:
                    humanWeight += w;
                    break;
                case 1:
                    aiWeight += w;
                    break;
                case 2:
                    suspectedWeight += w;
                    break;
                default:
                    humanWeight += w;
                    break;
            }
        }

        double humanRatio = humanWeight / totalWeight;
        double aiRatio = aiWeight / totalWeight;
        double suspectedRatio = suspectedWeight / totalWeight;
        double avgConf = weightedConf / totalWeight;

        int dominantLabel = 0;
        if (aiRatio >= suspectedRatio && aiRatio >= humanRatio)
        {
            dominantLabel = 1;
        }
        else if (suspectedRatio >= humanRatio)
        {
            dominantLabel = 2;
        }

        item.PreviousConfidence = prevConf;
        item.Label = dominantLabel;
        item.Confidence = avgConf;
        item.HasMultiSegmentBreakdown = true;
        item.HumanRatio = humanRatio;
        item.SuspectedRatio = suspectedRatio;
        item.AiRatio = aiRatio;
        item.LastAuditedText = item.Text;
        item.ErrorMessage = null;
        item.Status = ParagraphAuditStatus.Audited;
    }
}
