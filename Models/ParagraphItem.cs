using InkAudit.Services;
using InkAudit.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI.Text;

namespace InkAudit.Models;

/// <summary>
/// 单个小说自然段在审校流程中的生命周期状态。
/// </summary>
public enum ParagraphAuditStatus
{
    /// <summary>初始导入尚未检测。</summary>
    Pending,

    /// <summary>正在向朱雀接口请求分析。</summary>
    Checking,

    /// <summary>已完成检测并获得分类与置信度。</summary>
    Audited,

    /// <summary>字数低于短段阈值，已自动跳过逐段检测。</summary>
    SkippedShort,

    /// <summary>原文已编辑修改，等待单段重检或全文重检。</summary>
    Modified,

    /// <summary>检测请求失败或返回异常。</summary>
    Error
}

/// <summary>
/// 单个小说段落的审校视图模型，承载正文内容、检测分数、多子段比例、原地编辑状态及 UI 派生属性。
/// </summary>
public sealed class ParagraphItem : INotifyPropertyChanged
{
    private int _index;
    private string _text = string.Empty;
    private string _editDraft = string.Empty;
    private string? _lastAuditedText;
    private ParagraphAuditStatus _status = ParagraphAuditStatus.Pending;
    private string? _errorMessage;
    private int? _label;
    private double _confidence;
    private double? _previousConfidence;
    private bool _hasMultiSegmentBreakdown;
    private double _humanRatio;
    private double _suspectedRatio;
    private double _aiRatio;
    private bool _isSelected;
    private bool _isEditing;
    private bool _showHeatmapTint = true;
    private bool _isHighlighted;

    /// <summary>
    /// 属性变更通知事件。
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 段落顺序编号（从 1 开始）。
    /// </summary>
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

    /// <summary>
    /// 两位补零格式的段落编号文本（如 "01"）。
    /// </summary>
    public string IndexDisplay => $"{Index:D2}";

    /// <summary>
    /// 当前生效的段落正文文本。
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetField(ref _text, value))
            {
                OnPropertyChanged(nameof(CharCount));
                OnPropertyChanged(nameof(PreviewSnippet));
                OnPropertyChanged(nameof(TooltipSummary));
            }
        }
    }

    /// <summary>
    /// 原地编辑框中的草稿文本（未点击保存前不影响正文）。
    /// </summary>
    public string EditDraft
    {
        get => _editDraft;
        set => SetField(ref _editDraft, value);
    }

    /// <summary>
    /// 最近一次成功通过朱雀检测时的段落快照文本。
    /// </summary>
    public string? LastAuditedText
    {
        get => _lastAuditedText;
        set => SetField(ref _lastAuditedText, value);
    }

    /// <summary>
    /// 段落有效字数（剔除空白字符后的字符数量）。
    /// </summary>
    public int CharCount => CountNonWhitespace(Text);

    /// <summary>
    /// 用于右侧风险列表展示的截断预览片段（最多 22 字符）。
    /// </summary>
    public string PreviewSnippet
    {
        get
        {
            var trimmed = Text.Trim().Replace("\r", " ").Replace("\n", " ");
            return trimmed.Length <= 22 ? trimmed : $"{trimmed[..22]}…";
        }
    }

    /// <summary>
    /// 段落当前审校状态。
    /// </summary>
    public ParagraphAuditStatus Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                NotifyVisualProperties();
            }
        }
    }

    /// <summary>
    /// 当状态为 Error 时记录的具体失败原因。
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(ErrorDisplay));
            }
        }
    }

    /// <summary>
    /// 界面展示的错误文本，缺失详情时回退为本地化默认失败文案。
    /// </summary>
    public string ErrorDisplay => string.IsNullOrWhiteSpace(ErrorMessage)
        ? LocalizationService.Current.LabelFailed
        : ErrorMessage!;

    /// <summary>
    /// 朱雀分类标签：0=人工，1=AI，2=疑似 AI，未检测为 null。
    /// </summary>
    public int? Label
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

    /// <summary>
    /// 朱雀返回的 AI 风险置信度（范围 [0, 1]）。
    /// </summary>
    public double Confidence
    {
        get => _confidence;
        set
        {
            if (SetField(ref _confidence, Math.Clamp(value, 0, 1)))
            {
                NotifyVisualProperties();
            }
        }
    }

    /// <summary>
    /// 单段重检前的旧置信度，用于向作者展示改稿前后的分数变化幅度。
    /// </summary>
    public double? PreviousConfidence
    {
        get => _previousConfidence;
        set
        {
            if (SetField(ref _previousConfidence, value))
            {
                OnPropertyChanged(nameof(HasDelta));
                OnPropertyChanged(nameof(DeltaVisibility));
                OnPropertyChanged(nameof(DeltaText));
                OnPropertyChanged(nameof(DeltaBrush));
            }
        }
    }

    /// <summary>
    /// 标识该超长段是否被朱雀拆分为多个子分段并聚合出了三项占比。
    /// </summary>
    public bool HasMultiSegmentBreakdown
    {
        get => _hasMultiSegmentBreakdown;
        set
        {
            if (SetField(ref _hasMultiSegmentBreakdown, value))
            {
                OnPropertyChanged(nameof(MultiSegmentVisibility));
            }
        }
    }

    /// <summary>
    /// 多子段聚合时的人工特征占比（0~1）。
    /// </summary>
    public double HumanRatio
    {
        get => _humanRatio;
        set
        {
            if (SetField(ref _humanRatio, Math.Clamp(value, 0, 1)))
            {
                NotifyRatioProperties();
            }
        }
    }

    /// <summary>
    /// 多子段聚合时的疑似 AI 占比（0~1）。
    /// </summary>
    public double SuspectedRatio
    {
        get => _suspectedRatio;
        set
        {
            if (SetField(ref _suspectedRatio, Math.Clamp(value, 0, 1)))
            {
                NotifyRatioProperties();
            }
        }
    }

    /// <summary>
    /// 多子段聚合时的 AI 特征占比（0~1）。
    /// </summary>
    public double AiRatio
    {
        get => _aiRatio;
        set
        {
            if (SetField(ref _aiRatio, Math.Clamp(value, 0, 1)))
            {
                NotifyRatioProperties();
            }
        }
    }

    /// <summary>
    /// 指示当前段落是否处于单击聚焦态（展开行内上下文操作条）。
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(ActionBarVisibility));
                OnPropertyChanged(nameof(RowBackground));
            }
        }
    }

    /// <summary>
    /// 指示当前段落是否处于原地文本框编辑态。
    /// </summary>
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (SetField(ref _isEditing, value))
            {
                OnPropertyChanged(nameof(ReadingVisibility));
                OnPropertyChanged(nameof(EditingVisibility));
                OnPropertyChanged(nameof(ActionBarVisibility));
                OnPropertyChanged(nameof(RowBackground));
            }
        }
    }

    /// <summary>
    /// 是否允许对高风险/疑似段落施加极淡背景提示色。
    /// </summary>
    public bool ShowHeatmapTint
    {
        get => _showHeatmapTint;
        set
        {
            if (SetField(ref _showHeatmapTint, value))
            {
                OnPropertyChanged(nameof(RowBackground));
            }
        }
    }

    /// <summary>
    /// 热力图或快捷键跳转定位时的短时高亮标记。
    /// </summary>
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (SetField(ref _isHighlighted, value))
            {
                OnPropertyChanged(nameof(RowBackground));
            }
        }
    }

    /// <summary>自适应正文字重。</summary>
    public FontWeight BodyFontWeight => SemanticPalette.AdaptiveBodyWeight;

    /// <summary>自适应中等强调字重。</summary>
    public FontWeight MediumFontWeight => SemanticPalette.AdaptiveMediumWeight;

    /// <summary>正文阅读态可见性。</summary>
    public Visibility ReadingVisibility => IsEditing ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>原地编辑态可见性。</summary>
    public Visibility EditingVisibility => IsEditing ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>段落上下文操作条可见性（仅在聚焦且非编辑态显示）。</summary>
    public Visibility ActionBarVisibility => (IsSelected && !IsEditing) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>右侧已检指标区可见性。</summary>
    public Visibility AuditedMarginVisibility =>
        Status is ParagraphAuditStatus.Audited or ParagraphAuditStatus.Modified && Label.HasValue
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>右侧待检占位符可见性。</summary>
    public Visibility PendingMarginVisibility => Status == ParagraphAuditStatus.Pending ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>右侧检测中动画可见性。</summary>
    public Visibility CheckingMarginVisibility => Status == ParagraphAuditStatus.Checking ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>右侧短段跳过标识可见性。</summary>
    public Visibility SkippedMarginVisibility => Status == ParagraphAuditStatus.SkippedShort ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>右侧错误提示可见性。</summary>
    public Visibility ErrorMarginVisibility => Status == ParagraphAuditStatus.Error ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>已修改标记可见性。</summary>
    public Visibility ModifiedPillVisibility => Status == ParagraphAuditStatus.Modified ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>超长段三色聚合微条可见性。</summary>
    public Visibility MultiSegmentVisibility =>
        HasMultiSegmentBreakdown && AuditedMarginVisibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>是否具备可供展示的重检前后变化量。</summary>
    public bool HasDelta => Status == ParagraphAuditStatus.Audited && PreviousConfidence.HasValue;

    /// <summary>重检变化量文本可见性。</summary>
    public Visibility DeltaVisibility => HasDelta ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>重检前后置信度变化摘要文本（如 "96.4% → 8.2% ↓"）。</summary>
    public string DeltaText
    {
        get
        {
            if (!PreviousConfidence.HasValue)
            {
                return string.Empty;
            }

            var diffPct = (Confidence - PreviousConfidence.Value) * 100.0;
            var prevPct = PreviousConfidence.Value * 100.0;
            var currPct = Confidence * 100.0;
            if (Math.Abs(diffPct) < 0.05)
            {
                return $"{currPct:F1}%";
            }

            var arrow = diffPct < 0 ? "↓" : "↑";
            return $"{prevPct:F1}% → {currPct:F1}% {arrow}";
        }
    }

    /// <summary>变化量语义色画刷（下降为绿色，上升为红色）。</summary>
    public SolidColorBrush DeltaBrush
    {
        get
        {
            if (!PreviousConfidence.HasValue)
            {
                return SemanticPalette.NeutralAccent;
            }

            var diff = Confidence - PreviousConfidence.Value;
            if (diff < -0.005)
            {
                return SemanticPalette.HumanAccent;
            }

            if (diff > 0.005)
            {
                return SemanticPalette.AiAccent;
            }

            return SemanticPalette.NeutralAccent;
        }
    }

    /// <summary>本地化后的分类标签名称。</summary>
    public string LabelDisplay => Label switch
    {
        0 => LocalizationService.Current.LabelHuman,
        1 => LocalizationService.Current.LabelAi,
        2 => LocalizationService.Current.LabelSuspected,
        _ => "—"
    };

    /// <summary>百分比格式的置信度文本。</summary>
    public string ConfidencePercentText => $"{Confidence * 100.0:F1}%";

    /// <summary>根据段落当前状态动态返回的本地化重检按钮文本。</summary>
    public string RecheckActionLabel => Status == ParagraphAuditStatus.SkippedShort
        ? LocalizationService.Current.LabelDetect
        : Status == ParagraphAuditStatus.Error
            ? LocalizationService.Current.LabelRetry
            : LocalizationService.Current.LabelRecheck;

    /// <summary>本地化“已改”标签文本。</summary>
    public string ModifiedLabelText => LocalizationService.Current.LabelModified;

    /// <summary>本地化“跳过”标签文本。</summary>
    public string SkippedLabelText => LocalizationService.Current.LabelSkipped;

    /// <summary>本地化“编辑”按钮文本。</summary>
    public string EditActionLabel => LocalizationService.Current.ActionEdit;

    /// <summary>本地化“复制”按钮文本。</summary>
    public string CopyActionLabel => LocalizationService.Current.ActionCopy;

    /// <summary>本地化“保存并重检”按钮文本。</summary>
    public string SaveAndRecheckLabel => LocalizationService.Current.ActionSaveAndRecheck;

    /// <summary>本地化“保存”按钮文本。</summary>
    public string SaveActionLabel => LocalizationService.Current.ActionSave;

    /// <summary>本地化“取消”按钮文本。</summary>
    public string CancelActionLabel => LocalizationService.Current.ActionCancel;

    /// <summary>置信度进度条已填充列宽权重。</summary>
    public GridLength ConfidenceFillWidth => Confidence <= 0.001
        ? new GridLength(0, GridUnitType.Pixel)
        : new GridLength(Math.Max(Confidence, 0.03), GridUnitType.Star);

    /// <summary>置信度进度条剩余列宽权重。</summary>
    public GridLength ConfidenceRestWidth => Confidence >= 0.999
        ? new GridLength(0, GridUnitType.Pixel)
        : new GridLength(Math.Max(1.0 - Confidence, 0.03), GridUnitType.Star);

    /// <summary>多子段聚合条中人工列的比例宽度。</summary>
    public GridLength HumanSegmentWidth => HumanRatio <= 0.001
        ? new GridLength(0, GridUnitType.Pixel)
        : new GridLength(HumanRatio, GridUnitType.Star);

    /// <summary>多子段聚合条中疑似 AI 列的比例宽度。</summary>
    public GridLength SuspectedSegmentWidth => SuspectedRatio <= 0.001
        ? new GridLength(0, GridUnitType.Pixel)
        : new GridLength(SuspectedRatio, GridUnitType.Star);

    /// <summary>多子段聚合条中 AI 列的比例宽度。</summary>
    public GridLength AiSegmentWidth => AiRatio <= 0.001
        ? new GridLength(0, GridUnitType.Pixel)
        : new GridLength(AiRatio, GridUnitType.Star);

    /// <summary>多子段聚合三项百分比简明文本。</summary>
    public string MultiSegmentSummaryText =>
        $"{HumanRatio * 100:F0}% / {SuspectedRatio * 100:F0}% / {AiRatio * 100:F0}%";

    /// <summary>指示该段落是否命中 AI 或疑似 AI 风险。</summary>
    public bool IsRisky => Status is ParagraphAuditStatus.Audited or ParagraphAuditStatus.Modified && Label is 1 or 2;

    /// <summary>左侧状态色带画刷。</summary>
    public SolidColorBrush GutterStripBrush => Status switch
    {
        ParagraphAuditStatus.Modified => SemanticPalette.ModifiedAccent,
        ParagraphAuditStatus.Error => SemanticPalette.AiAccent,
        ParagraphAuditStatus.SkippedShort or ParagraphAuditStatus.Pending or ParagraphAuditStatus.Checking => SemanticPalette.NeutralQuietStrip,
        _ => Label switch
        {
            0 => SemanticPalette.HumanQuietStrip,
            1 => SemanticPalette.AiAccent,
            2 => SemanticPalette.SuspectedAccent,
            _ => SemanticPalette.NeutralQuietStrip
        }
    };

    /// <summary>分类标签前景色画刷。</summary>
    public SolidColorBrush LabelBadgeForeground => Label switch
    {
        0 => SemanticPalette.HumanAccent,
        1 => SemanticPalette.AiAccent,
        2 => SemanticPalette.SuspectedAccent,
        _ => SemanticPalette.NeutralAccent
    };

    /// <summary>整行动态背景画刷（通过微明度色阶表达聚焦、跳转高亮与风险着色，无 1px 边框）。</summary>
    public SolidColorBrush RowBackground
    {
        get
        {
            if (IsHighlighted)
            {
                return SemanticPalette.HighlightedRowSurface;
            }

            if (IsSelected || IsEditing)
            {
                return SemanticPalette.FocusedRowSurface;
            }

            if (ShowHeatmapTint && Status is ParagraphAuditStatus.Audited or ParagraphAuditStatus.Modified)
            {
                if (Label == 1)
                {
                    return SemanticPalette.AiRowTint;
                }

                if (Label == 2)
                {
                    return SemanticPalette.SuspectedRowTint;
                }
            }

            return SemanticPalette.TransparentBrush;
        }
    }

    /// <summary>右侧章节热力图中对应方块的填充色画刷。</summary>
    public SolidColorBrush HeatmapTileBrush => Status switch
    {
        ParagraphAuditStatus.Audited or ParagraphAuditStatus.Modified => Label switch
        {
            0 => SemanticPalette.HumanAccent,
            1 => SemanticPalette.AiAccent,
            2 => SemanticPalette.SuspectedAccent,
            _ => SemanticPalette.NeutralTileBg
        },
        ParagraphAuditStatus.Error => SemanticPalette.AiAccent,
        _ => SemanticPalette.NeutralTileBg
    };

    /// <summary>悬停热力图方块时显示的精简提示文案。</summary>
    public string TooltipSummary => Status switch
    {
        ParagraphAuditStatus.Audited => $"{IndexDisplay} · {LabelDisplay} {ConfidencePercentText}",
        ParagraphAuditStatus.Modified => $"{IndexDisplay} · {LocalizationService.Current.LabelModified}",
        ParagraphAuditStatus.SkippedShort => $"{IndexDisplay} · {LocalizationService.Current.LabelSkipped}",
        ParagraphAuditStatus.Error => $"{IndexDisplay} · {LocalizationService.Current.LabelFailed}",
        _ => IndexDisplay
    };

    /// <summary>
    /// 主题切换后刷新全部依赖颜色与字重的派生属性。
    /// </summary>
    public void RefreshThemeBrushes()
    {
        OnPropertyChanged(nameof(BodyFontWeight));
        OnPropertyChanged(nameof(MediumFontWeight));
        NotifyVisualProperties();
    }

    /// <summary>
    /// 语言切换后刷新全部本地化派生文本属性。
    /// </summary>
    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(LabelDisplay));
        OnPropertyChanged(nameof(ErrorDisplay));
        OnPropertyChanged(nameof(RecheckActionLabel));
        OnPropertyChanged(nameof(ModifiedLabelText));
        OnPropertyChanged(nameof(SkippedLabelText));
        OnPropertyChanged(nameof(EditActionLabel));
        OnPropertyChanged(nameof(CopyActionLabel));
        OnPropertyChanged(nameof(SaveAndRecheckLabel));
        OnPropertyChanged(nameof(SaveActionLabel));
        OnPropertyChanged(nameof(CancelActionLabel));
        OnPropertyChanged(nameof(TooltipSummary));
    }

    /// <summary>
    /// 统计字符串中的非空白字符总数，用于短段跳过阈值判定与章节字数统计。
    /// </summary>
    /// <param name="text">待统计的文本。</param>
    /// <returns>剔除空格、制表符和换行符后的字符数。</returns>
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
    /// 批量触发视觉与状态派生属性通知。
    /// </summary>
    private void NotifyVisualProperties()
    {
        OnPropertyChanged(nameof(AuditedMarginVisibility));
        OnPropertyChanged(nameof(PendingMarginVisibility));
        OnPropertyChanged(nameof(CheckingMarginVisibility));
        OnPropertyChanged(nameof(SkippedMarginVisibility));
        OnPropertyChanged(nameof(ErrorMarginVisibility));
        OnPropertyChanged(nameof(ModifiedPillVisibility));
        OnPropertyChanged(nameof(MultiSegmentVisibility));
        OnPropertyChanged(nameof(ActionBarVisibility));
        OnPropertyChanged(nameof(HasDelta));
        OnPropertyChanged(nameof(DeltaVisibility));
        OnPropertyChanged(nameof(DeltaText));
        OnPropertyChanged(nameof(DeltaBrush));
        OnPropertyChanged(nameof(LabelDisplay));
        OnPropertyChanged(nameof(ConfidencePercentText));
        OnPropertyChanged(nameof(RecheckActionLabel));
        OnPropertyChanged(nameof(ConfidenceFillWidth));
        OnPropertyChanged(nameof(ConfidenceRestWidth));
        OnPropertyChanged(nameof(GutterStripBrush));
        OnPropertyChanged(nameof(LabelBadgeForeground));
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(HeatmapTileBrush));
        OnPropertyChanged(nameof(TooltipSummary));
        OnPropertyChanged(nameof(IsRisky));
    }

    /// <summary>
    /// 触发多子段比例条相关派生属性通知。
    /// </summary>
    private void NotifyRatioProperties()
    {
        OnPropertyChanged(nameof(HumanSegmentWidth));
        OnPropertyChanged(nameof(SuspectedSegmentWidth));
        OnPropertyChanged(nameof(AiSegmentWidth));
        OnPropertyChanged(nameof(MultiSegmentSummaryText));
    }

    /// <summary>
    /// 更新字段值并在变化时触发属性变更事件。
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
