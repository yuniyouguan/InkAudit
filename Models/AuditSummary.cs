using InkAudit.Services;
using InkAudit.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace InkAudit.Models;

/// <summary>
/// 全文检测汇总视图模型，维护三项内容占比、整体置信度、前后对比变化量及 Makers Token 实扣记录。
/// </summary>
public sealed class AuditSummary : INotifyPropertyChanged
{
    private bool _hasResult;
    private bool _isMergedSource;
    private bool _isStale;
    private double _humanRatio;
    private double _suspectedRatio;
    private double _aiRatio;
    private double? _previousAiRatio;
    private double _softmaxConfidence;
    private double _ratioConfidence;
    private bool _hasSegmentComparison;
    private double _segmentHumanRatio;
    private double _segmentSuspectedRatio;
    private double _segmentAiRatio;
    private int _lastTokensUsed;

    /// <summary>
    /// 属性变更通知事件。
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 是否已产生有效的全文检测结果。
    /// </summary>
    public bool HasResult
    {
        get => _hasResult;
        set
        {
            if (SetField(ref _hasResult, value))
            {
                OnPropertyChanged(nameof(ResultVisibility));
                OnPropertyChanged(nameof(AiDeltaVisibility));
                OnPropertyChanged(nameof(SegmentComparisonVisibility));
                NotifyRatioChanged();
                OnPropertyChanged(nameof(SoftmaxConfidenceText));
                OnPropertyChanged(nameof(RatioConfidenceText));
            }
        }
    }

    /// <summary>
    /// 主展示结果是否来源于全文合并复核（is_merge: true）。
    /// </summary>
    public bool IsMergedSource
    {
        get => _isMergedSource;
        set => SetField(ref _isMergedSource, value);
    }

    /// <summary>
    /// 标识段落正文在最近一次全文检测后是否被修改过（提示需重检）。
    /// </summary>
    public bool IsStale
    {
        get => _isStale;
        set
        {
            if (SetField(ref _isStale, value))
            {
                OnPropertyChanged(nameof(StaleVisibility));
            }
        }
    }

    /// <summary>
    /// 全文人工特征内容占比（0~1）。
    /// </summary>
    public double HumanRatio
    {
        get => _humanRatio;
        set
        {
            if (SetField(ref _humanRatio, Math.Clamp(value, 0, 1)))
            {
                NotifyRatioChanged();
            }
        }
    }

    /// <summary>
    /// 全文疑似 AI 内容占比（0~1）。
    /// </summary>
    public double SuspectedRatio
    {
        get => _suspectedRatio;
        set
        {
            if (SetField(ref _suspectedRatio, Math.Clamp(value, 0, 1)))
            {
                NotifyRatioChanged();
            }
        }
    }

    /// <summary>
    /// 全文 AI 特征内容占比（0~1）。
    /// </summary>
    public double AiRatio
    {
        get => _aiRatio;
        set
        {
            if (SetField(ref _aiRatio, Math.Clamp(value, 0, 1)))
            {
                NotifyRatioChanged();
            }
        }
    }

    /// <summary>
    /// 上一次全文检测的 AI 占比，用于展示改稿后的降幅变化。
    /// </summary>
    public double? PreviousAiRatio
    {
        get => _previousAiRatio;
        set
        {
            if (SetField(ref _previousAiRatio, value))
            {
                OnPropertyChanged(nameof(AiDeltaVisibility));
                OnPropertyChanged(nameof(AiDeltaText));
                OnPropertyChanged(nameof(AiDeltaBrush));
            }
        }
    }

    /// <summary>
    /// 接口返回的整体风险置信度（softmax_confidence，0~1）。
    /// </summary>
    public double SoftmaxConfidence
    {
        get => _softmaxConfidence;
        set
        {
            if (SetField(ref _softmaxConfidence, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(SoftmaxConfidenceText));
            }
        }
    }

    /// <summary>
    /// 接口返回的整体疑似风险内容占比（ratio_confidence，0~1）。
    /// </summary>
    public double RatioConfidence
    {
        get => _ratioConfidence;
        set
        {
            if (SetField(ref _ratioConfidence, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(RatioConfidenceText));
            }
        }
    }

    /// <summary>
    /// 是否存在可供对照的逐段独立汇总比例（开启合并复核时启用）。
    /// </summary>
    public bool HasSegmentComparison
    {
        get => _hasSegmentComparison;
        set
        {
            if (SetField(ref _hasSegmentComparison, value))
            {
                OnPropertyChanged(nameof(SegmentComparisonVisibility));
            }
        }
    }

    /// <summary>
    /// 逐段独立检测返回的顶层人工占比（0~1）。
    /// </summary>
    public double SegmentHumanRatio
    {
        get => _segmentHumanRatio;
        set
        {
            if (SetField(ref _segmentHumanRatio, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(SegmentComparisonText));
            }
        }
    }

    /// <summary>
    /// 逐段独立检测返回的顶层疑似 AI 占比（0~1）。
    /// </summary>
    public double SegmentSuspectedRatio
    {
        get => _segmentSuspectedRatio;
        set
        {
            if (SetField(ref _segmentSuspectedRatio, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(SegmentComparisonText));
            }
        }
    }

    /// <summary>
    /// 逐段独立检测返回的顶层 AI 占比（0~1）。
    /// </summary>
    public double SegmentAiRatio
    {
        get => _segmentAiRatio;
        set
        {
            if (SetField(ref _segmentAiRatio, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(SegmentComparisonText));
            }
        }
    }

    /// <summary>
    /// 最近一次检测实际扣减的 Makers 免费额度 Token 总数。
    /// </summary>
    public int LastTokensUsed
    {
        get => _lastTokensUsed;
        set
        {
            if (SetField(ref _lastTokensUsed, value))
            {
                OnPropertyChanged(nameof(TokensDisplay));
                OnPropertyChanged(nameof(TokensVisibility));
            }
        }
    }

    /// <summary>结果区块可见性。</summary>
    public Visibility ResultVisibility => HasResult ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>过期提醒文本可见性。</summary>
    public Visibility StaleVisibility => HasResult && IsStale ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>逐段参考对比行可见性。</summary>
    public Visibility SegmentComparisonVisibility => HasResult && HasSegmentComparison ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Token 消耗统计可见性（有消耗时才显示）。</summary>
    public Visibility TokensVisibility => LastTokensUsed > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>人工占比格式化文本。</summary>
    public string HumanPercentText => HasResult ? $"{HumanRatio * 100.0:F1}%" : "—";

    /// <summary>疑似 AI 占比格式化文本。</summary>
    public string SuspectedPercentText => HasResult ? $"{SuspectedRatio * 100.0:F1}%" : "—";

    /// <summary>AI 占比格式化文本。</summary>
    public string AiPercentText => HasResult ? $"{AiRatio * 100.0:F1}%" : "—";

    /// <summary>整体置信度格式化文本。</summary>
    public string SoftmaxConfidenceText => HasResult ? $"{SoftmaxConfidence * 100.0:F1}%" : "—";

    /// <summary>疑似风险占比格式化文本。</summary>
    public string RatioConfidenceText => HasResult ? $"{RatioConfidence * 100.0:F1}%" : "—";

    /// <summary>本地化后的逐段占比对比文本。</summary>
    public string SegmentComparisonText => LocalizationService.Current.FormatSegmentComparison(
        SegmentHumanRatio,
        SegmentSuspectedRatio,
        SegmentAiRatio);

    /// <summary>Token 消耗格式化文本。</summary>
    public string TokensDisplay => $"Token {LastTokensUsed:N0}";

    /// <summary>三色堆叠条中人工列宽权重。</summary>
    public GridLength HumanBarWidth => HumanRatio <= 0.001
        ? new GridLength(0, GridUnitType.Pixel)
        : new GridLength(HumanRatio, GridUnitType.Star);

    /// <summary>三色堆叠条中疑似 AI 列宽权重。</summary>
    public GridLength SuspectedBarWidth => SuspectedRatio <= 0.001
        ? new GridLength(0, GridUnitType.Pixel)
        : new GridLength(SuspectedRatio, GridUnitType.Star);

    /// <summary>三色堆叠条中 AI 列宽权重。</summary>
    public GridLength AiBarWidth => AiRatio <= 0.001
        ? new GridLength(0, GridUnitType.Pixel)
        : new GridLength(AiRatio, GridUnitType.Star);

    /// <summary>AI 占比变化趋势可见性。</summary>
    public Visibility AiDeltaVisibility => HasResult && PreviousAiRatio.HasValue
        ? Visibility.Visible
        : Visibility.Collapsed;

    /// <summary>AI 占比变化趋势文本（如 "AI ↓14.2%"）。</summary>
    public string AiDeltaText
    {
        get
        {
            if (!PreviousAiRatio.HasValue)
            {
                return string.Empty;
            }

            var diffPct = (AiRatio - PreviousAiRatio.Value) * 100.0;
            if (Math.Abs(diffPct) < 0.05)
            {
                return "0.0%";
            }

            var arrow = diffPct < 0 ? "↓" : "↑";
            return $"AI {arrow}{Math.Abs(diffPct):F1}%";
        }
    }

    /// <summary>AI 占比变化趋势语义色画刷。</summary>
    public SolidColorBrush AiDeltaBrush
    {
        get
        {
            if (!PreviousAiRatio.HasValue)
            {
                return SemanticPalette.NeutralAccent;
            }

            var diff = AiRatio - PreviousAiRatio.Value;
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

    /// <summary>
    /// 重置全部全文统计数据至初始空白状态。
    /// </summary>
    public void Reset()
    {
        HasResult = false;
        IsMergedSource = false;
        IsStale = false;
        HumanRatio = 0;
        SuspectedRatio = 0;
        AiRatio = 0;
        PreviousAiRatio = null;
        SoftmaxConfidence = 0;
        RatioConfidence = 0;
        HasSegmentComparison = false;
        SegmentHumanRatio = 0;
        SegmentSuspectedRatio = 0;
        SegmentAiRatio = 0;
        LastTokensUsed = 0;
        NotifyRatioChanged();
        OnPropertyChanged(nameof(SoftmaxConfidenceText));
        OnPropertyChanged(nameof(RatioConfidenceText));
    }

    /// <summary>
    /// 主题切换后刷新动态语义画刷。
    /// </summary>
    public void RefreshThemeBrushes()
    {
        OnPropertyChanged(nameof(AiDeltaBrush));
    }

    /// <summary>
    /// 语言切换后刷新本地化派生文本。
    /// </summary>
    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(SegmentComparisonText));
    }

    /// <summary>
    /// 批量触发三项占比相关的显示属性更新。
    /// </summary>
    private void NotifyRatioChanged()
    {
        OnPropertyChanged(nameof(HumanPercentText));
        OnPropertyChanged(nameof(SuspectedPercentText));
        OnPropertyChanged(nameof(AiPercentText));
        OnPropertyChanged(nameof(HumanBarWidth));
        OnPropertyChanged(nameof(SuspectedBarWidth));
        OnPropertyChanged(nameof(AiBarWidth));
        OnPropertyChanged(nameof(AiDeltaText));
        OnPropertyChanged(nameof(AiDeltaBrush));
    }

    /// <summary>
    /// 更新字段并在数值变动时触发变更通知。
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
