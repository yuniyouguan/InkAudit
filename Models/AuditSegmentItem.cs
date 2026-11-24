using InkAudit.Services;
using InkAudit.Theming;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI;
using Windows.UI.Text;

namespace InkAudit.Models;

/// <summary>
/// 朱雀文本检测返回的单个精确字符区间片段模型，承载字符起止偏移、分类标签、
/// AI / 人工双视角置信度以及按 <c>conf</c> 强度动态计算的红/黄/绿高亮色。
/// </summary>
public sealed class AuditSegmentItem : INotifyPropertyChanged
{
    /// <summary>片段顺序编号（从 1 开始）。</summary>
    private int _index;

    /// <summary>该片段在正文中的 UTF-16 起始字符偏移。</summary>
    private int _start;

    /// <summary>该片段在正文中的 UTF-16 字符长度。</summary>
    private int _length;

    /// <summary>该片段对应的原文文本切片。</summary>
    private string _text = string.Empty;

    /// <summary>朱雀分类标签：0=人工，1=AI，2=疑似 AI。</summary>
    private int _label;

    /// <summary>朱雀原生 AI 风险置信度（0~1）。</summary>
    private double _confidence;

    /// <summary>
    /// 属性变更通知事件。
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>片段顺序编号（从 1 开始）。</summary>
    public int Index
    {
        get => _index;
        set
        {
            if (SetField(ref _index, value))
            {
                OnPropertyChanged(nameof(IndexDisplay));
                OnPropertyChanged(nameof(TooltipSummary));
            }
        }
    }

    /// <summary>两位补零格式的片段序号文本（如 "01"）。</summary>
    public string IndexDisplay => $"{Index:D2}";

    /// <summary>该片段在正文中的 UTF-16 起始字符偏移（含）。</summary>
    public int Start
    {
        get => _start;
        set
        {
            if (SetField(ref _start, value))
            {
                OnPropertyChanged(nameof(End));
            }
        }
    }

    /// <summary>该片段在正文中的 UTF-16 字符长度。</summary>
    public int Length
    {
        get => _length;
        set
        {
            if (SetField(ref _length, value))
            {
                OnPropertyChanged(nameof(End));
            }
        }
    }

    /// <summary>该片段在正文中的 UTF-16 结束字符偏移（不含）。</summary>
    public int End => Start + Length;

    /// <summary>该片段对应的原文文本切片。</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetField(ref _text, value))
            {
                OnPropertyChanged(nameof(CharCount));
                OnPropertyChanged(nameof(PreviewSnippet));
            }
        }
    }

    /// <summary>该片段的有效非空白字符数。</summary>
    public int CharCount => CountNonWhitespace(Text);

    /// <summary>用于右侧风险列表展示的截断预览片段（最多 22 字符）。</summary>
    public string PreviewSnippet
    {
        get
        {
            var trimmed = Text.Trim().Replace("\r", " ").Replace("\n", " ");
            return trimmed.Length <= 22 ? trimmed : $"{trimmed[..22]}…";
        }
    }

    /// <summary>朱雀分类标签：0=人工，1=AI，2=疑似 AI。</summary>
    public int Label
    {
        get => _label;
        set
        {
            if (SetField(ref _label, value))
            {
                NotifyVisualProperties();
            }
        }
    }

    /// <summary>朱雀原生 AI 风险置信度（0~1）。</summary>
    public double Confidence
    {
        get => _confidence;
        set
        {
            if (SetField(ref _confidence, Math.Clamp(value, 0.0, 1.0)))
            {
                NotifyVisualProperties();
            }
        }
    }

    /// <summary>由 <see cref="Confidence"/> 推导的人工概率（1 - conf，范围 0~1）。</summary>
    public double HumanConfidence => Math.Clamp(1.0 - Confidence, 0.0, 1.0);

    /// <summary>指示该片段是否命中 AI 或疑似 AI 风险。</summary>
    public bool IsRisky => Label is 1 or 2;

    /// <summary>本地化后的分类标签名称。</summary>
    public string LabelDisplay => Label switch
    {
        0 => LocalizationService.Current.LabelHuman,
        1 => LocalizationService.Current.LabelAi,
        2 => LocalizationService.Current.LabelSuspected,
        _ => "—"
    };

    /// <summary>AI 占比格式化文本（如 "74.6%"）。</summary>
    public string AiPercentText => $"{Confidence * 100.0:F1}%";

    /// <summary>人工占比格式化文本（如 "25.4%"）。</summary>
    public string HumanPercentText => $"{HumanConfidence * 100.0:F1}%";

    /// <summary>自适应中等强调字重。</summary>
    public FontWeight MediumFontWeight => SemanticPalette.AdaptiveMediumWeight;

    /// <summary>分类标签强调前景色画刷（绿/黄/红）。</summary>
    public SolidColorBrush LabelBadgeForeground => Label switch
    {
        0 => SemanticPalette.HumanAccent,
        1 => SemanticPalette.AiAccent,
        2 => SemanticPalette.SuspectedAccent,
        _ => SemanticPalette.NeutralAccent
    };

    /// <summary>右侧片段热力图中对应方块的填充色画刷（随 conf 强度渐变）。</summary>
    public SolidColorBrush HeatmapTileBrush => SemanticPalette.GetSegmentTileBrush(Label, Confidence);

    /// <summary>正文该字符区间的背景高亮颜色（随 conf 强度渐变，已预混合至纸面底色）。</summary>
    public Color HighlightColor => SemanticPalette.GetSegmentHighlightColor(Label, Confidence);

    /// <summary>深色悬浮气泡内的高对比语义圆点画刷。</summary>
    public SolidColorBrush BubbleDotBrush => SemanticPalette.GetBubbleDotBrush(Label);

    /// <summary>悬停右侧热力方块时展示的精简占比提示。</summary>
    public string TooltipSummary => LocalizationService.Current.FormatSegmentTooltip(
        IndexDisplay,
        LabelDisplay,
        AiPercentText,
        HumanPercentText);

    /// <summary>
    /// 判断指定 UTF-16 字符偏移是否落在本片段区间内。
    /// </summary>
    /// <param name="charOffset">文档中的 UTF-16 字符偏移。</param>
    /// <returns>落在 [Start, End) 内时返回 true。</returns>
    public bool ContainsPosition(int charOffset) => charOffset >= Start && charOffset < End;

    /// <summary>
    /// 主题切换后刷新全部依赖颜色与字重的派生属性。
    /// </summary>
    public void RefreshThemeBrushes()
    {
        OnPropertyChanged(nameof(MediumFontWeight));
        NotifyVisualProperties();
    }

    /// <summary>
    /// 统计字符串中的非空白字符总数。
    /// </summary>
    /// <param name="text">待统计文本。</param>
    /// <returns>剔除空白字符后的字数。</returns>
    public static int CountNonWhitespace(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int count = 0;
        foreach (char ch in text)
        {
            if (!char.IsWhiteSpace(ch))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 批量触发颜色、占比文本与提示信息的派生属性通知。
    /// </summary>
    private void NotifyVisualProperties()
    {
        OnPropertyChanged(nameof(HumanConfidence));
        OnPropertyChanged(nameof(IsRisky));
        OnPropertyChanged(nameof(LabelDisplay));
        OnPropertyChanged(nameof(AiPercentText));
        OnPropertyChanged(nameof(HumanPercentText));
        OnPropertyChanged(nameof(LabelBadgeForeground));
        OnPropertyChanged(nameof(HeatmapTileBrush));
        OnPropertyChanged(nameof(HighlightColor));
        OnPropertyChanged(nameof(BubbleDotBrush));
        OnPropertyChanged(nameof(TooltipSummary));
    }

    /// <summary>
    /// 更新字段并在变化时触发属性变更事件。
    /// </summary>
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// 触发指定属性名的变更通知。
    /// </summary>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
