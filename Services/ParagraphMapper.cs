using InkAudit.Models;
using System;
using System.Collections.Generic;

namespace InkAudit.Services;

/// <summary>
/// 正文归一化与朱雀精确字符区间映射工具类。
/// 严格遵循朱雀接口 <c>position = [start, length]</c>（起始偏移 + 片段长度）协议，
/// 并将服务端 Unicode 码点偏移安全换算为 WinUI <c>RichEditBox</c> 使用的 UTF-16 字符索引。
/// </summary>
public static class ParagraphMapper
{
    /// <summary>
    /// 将外部导入或剪贴板文本的换行符统一归一化为单字符 <c>\n</c>，保证送检与编辑器索引一一对应。
    /// </summary>
    /// <param name="rawText">原始文本。</param>
    /// <returns>归一化后的纯文本。</returns>
    public static string NormalizeLineEndings(string? rawText)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return string.Empty;
        }

        return rawText.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    /// <summary>
    /// 将 <c>RichEditBox.Document.GetText</c> 返回的文本转换为送检负载：
    /// 仅移除末尾自动附带的一个 <c>\r</c>，并将段内 <c>\r</c> 等长替换为 <c>\n</c>，
    /// 确保每个字符在请求体中的下标与编辑器内完全一致。
    /// </summary>
    /// <param name="editorText">从 <c>RichEditBox</c> 读出的原始文档文本。</param>
    /// <returns>与编辑器字符偏移 1:1 对应的送检文本。</returns>
    public static string ExtractPayloadFromEditorText(string? editorText)
    {
        if (string.IsNullOrEmpty(editorText))
        {
            return string.Empty;
        }

        string trimmedEnd = editorText.EndsWith('\r')
            ? editorText[..^1]
            : editorText;

        return trimmedEnd.Replace('\r', '\n');
    }

    /// <summary>
    /// 统计正文中的非空白字符总数与非空自然段数，供纸面页眉展示。
    /// </summary>
    /// <param name="payload">换行已归一化的正文。</param>
    /// <returns>有效字数与有效段落数的元组。</returns>
    public static (int CharCount, int ParagraphCount) CountTextStats(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return (0, 0);
        }

        int chars = AuditSegmentItem.CountNonWhitespace(payload);
        int paragraphs = 0;
        bool hasNonWhitespaceInLine = false;

        foreach (char ch in payload)
        {
            if (ch == '\n' || ch == '\r')
            {
                if (hasNonWhitespaceInLine)
                {
                    paragraphs++;
                    hasNonWhitespaceInLine = false;
                }
            }
            else if (!char.IsWhiteSpace(ch))
            {
                hasNonWhitespaceInLine = true;
            }
        }

        if (hasNonWhitespaceInLine)
        {
            paragraphs++;
        }

        return (chars, paragraphs);
    }

    /// <summary>
    /// 将朱雀响应中的 <c>segment_labels</c> 按精确字符区间 <c>[start, length]</c> 映射为有序高亮片段列表。
    /// </summary>
    /// <param name="payload">发送给朱雀接口且与编辑器等长对齐的正文文本。</param>
    /// <param name="response">朱雀分类接口响应。</param>
    /// <returns>按起始偏移升序排列的精确字符片段集合。</returns>
    public static List<AuditSegmentItem> MapResponseToSegments(
        string payload,
        ZhuqueClassifyResponse response)
    {
        var result = new List<AuditSegmentItem>();
        if (string.IsNullOrEmpty(payload))
        {
            return result;
        }

        var segments = response.SegmentLabels;
        if (segments is null || segments.Count == 0)
        {
            var h = response.GetLabelRatio(0);
            var a = response.GetLabelRatio(1);
            var s = response.GetLabelRatio(2);
            int dominant = (a >= h && a >= s) ? 1 : (s >= h && s >= a) ? 2 : 0;

            result.Add(new AuditSegmentItem
            {
                Index = 1,
                Start = 0,
                Length = payload.Length,
                Text = payload,
                Label = dominant,
                Confidence = response.SoftmaxConfidence
            });
            return result;
        }

        // 构造 Unicode 码点索引 -> UTF-16 索引映射表，兼容含代理对字符（如 Emoji）的精确偏移换算
        int[] cpToUtf16 = BuildCodePointToUtf16Map(payload);
        int totalCodePoints = cpToUtf16.Length - 1;
        int fallbackCursor = 0;

        foreach (var seg in segments)
        {
            int utf16Start = -1;
            int utf16Length = 0;

            // 策略 1：严格按朱雀 position = [start, length] 协议解析精确区间
            if (seg.Position is { Count: >= 2 } && seg.Position[1] > 0)
            {
                int cpStart = Math.Clamp(seg.Position[0], 0, totalCodePoints);
                int cpEnd = Math.Clamp(seg.Position[0] + seg.Position[1], cpStart, totalCodePoints);
                utf16Start = cpToUtf16[cpStart];
                utf16Length = cpToUtf16[cpEnd] - utf16Start;
            }

            // 策略 2：若位置缺失或越界，则按片段原文顺序定位兜底
            if (utf16Length <= 0 && !string.IsNullOrEmpty(seg.Text))
            {
                string target = NormalizeLineEndings(seg.Text);
                int found = payload.IndexOf(target, fallbackCursor, StringComparison.Ordinal);
                if (found < 0)
                {
                    found = payload.IndexOf(target.Trim(), fallbackCursor, StringComparison.Ordinal);
                    if (found >= 0)
                    {
                        utf16Start = found;
                        utf16Length = target.Trim().Length;
                    }
                }
                else
                {
                    utf16Start = found;
                    utf16Length = target.Length;
                }
            }

            if (utf16Start < 0 || utf16Length <= 0 || utf16Start >= payload.Length)
            {
                continue;
            }

            int clampedLength = Math.Min(utf16Length, payload.Length - utf16Start);
            if (clampedLength <= 0)
            {
                continue;
            }

            fallbackCursor = Math.Max(fallbackCursor, utf16Start + clampedLength);

            result.Add(new AuditSegmentItem
            {
                Start = utf16Start,
                Length = clampedLength,
                Text = payload.Substring(utf16Start, clampedLength),
                Label = seg.Label,
                Confidence = seg.Conf
            });
        }

        result.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Length.CompareTo(b.Length));
        for (int i = 0; i < result.Count; i++)
        {
            result[i].Index = i + 1;
        }

        return result;
    }

    /// <summary>
    /// 构造 Unicode 码点下标到 C# UTF-16 字符串下标的映射数组（末尾元素为 <c>text.Length</c>）。
    /// </summary>
    /// <param name="text">目标字符串。</param>
    /// <returns>长度为 <c>codePointCount + 1</c> 的下标映射数组。</returns>
    private static int[] BuildCodePointToUtf16Map(string text)
    {
        var map = new List<int>(text.Length + 1);
        for (int i = 0; i < text.Length; i++)
        {
            map.Add(i);
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
        }

        map.Add(text.Length);
        return map.ToArray();
    }
}
