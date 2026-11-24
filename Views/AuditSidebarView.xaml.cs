using InkAudit.Models;
using InkAudit.Services;
using InkAudit.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace InkAudit.Views;

/// <summary>
/// 右侧全文诊断与片段导航组件，负责展示全文 AI 占比汇总、分类高亮聚焦图例、
/// 朱雀原始切片热力图矩阵及高风险片段快速跳转列表。
/// </summary>
public sealed partial class AuditSidebarView : UserControl
{
    /// <summary>最近一次检测返回的全部朱雀精确字符片段。</summary>
    private readonly ObservableCollection<AuditSegmentItem> _allSegments = new();

    /// <summary>命中 AI 或疑似 AI 风险的片段子集。</summary>
    private readonly ObservableCollection<AuditSegmentItem> _riskSegments = new();

    /// <summary>当前激活的高亮聚焦维度。</summary>
    private SegmentFilterKind _activeFilter = SegmentFilterKind.All;

    /// <summary>上下跳转风险片段时的当前游标下标。</summary>
    private int _currentRiskCursor = -1;

    /// <summary>全局本地化字符串入口。</summary>
    public LocalizationService Strings => LocalizationService.Current;

    /// <summary>组件内自适应字重配置对象。</summary>
    public AdaptiveTypography TypeScale { get; } = new();

    /// <summary>全文检测汇总数据模型。</summary>
    public AuditSummary Summary { get; } = new();

    /// <summary>当前展示的全部朱雀精确字符片段只读视图。</summary>
    public IReadOnlyList<AuditSegmentItem> Segments => _allSegments;

    /// <summary>用户点击左侧图例（人工 / 疑似 / AI）切换高亮聚焦时触发。</summary>
    public event EventHandler<SegmentFilterKind>? FilterToggled;

    /// <summary>用户点击热力方块、风险列表项或按 Alt+↑/↓ 跳转至某片段时触发。</summary>
    public event EventHandler<AuditSegmentItem>? SegmentSelected;

    /// <summary>
    /// 初始化侧栏组件并绑定片段集合。
    /// </summary>
    public AuditSidebarView()
    {
        InitializeComponent();
        HeatmapRepeater.ItemsSource = _allSegments;
        RiskListRepeater.ItemsSource = _riskSegments;
    }

    /// <summary>
    /// 替换当前片段分布与高风险速览列表，并同步刷新显隐和跳转按钮可用性。
    /// </summary>
    /// <param name="segments">新一轮检测返回的精确字符片段集合。</param>
    public void SetSegments(IEnumerable<AuditSegmentItem> segments)
    {
        _allSegments.Clear();
        foreach (var item in segments)
        {
            _allSegments.Add(item);
        }

        _riskSegments.Clear();
        foreach (var item in _allSegments.Where(s => s.IsRisky))
        {
            _riskSegments.Add(item);
        }

        _currentRiskCursor = -1;
        DistributionCard.Visibility = _allSegments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RiskListRepeater.Visibility = _riskSegments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PrevRiskButton.IsEnabled = _riskSegments.Count > 0;
        NextRiskButton.IsEnabled = _riskSegments.Count > 0;
    }

    /// <summary>
    /// 清空全部片段热力图与风险列表并隐藏片段分布卡片。
    /// </summary>
    public void ClearSegments()
    {
        _allSegments.Clear();
        _riskSegments.Clear();
        _currentRiskCursor = -1;
        DistributionCard.Visibility = Visibility.Collapsed;
        RiskListRepeater.Visibility = Visibility.Collapsed;
        PrevRiskButton.IsEnabled = false;
        NextRiskButton.IsEnabled = false;
    }

    /// <summary>
    /// 更新右侧三项占比图例行的聚焦激活底色。
    /// </summary>
    /// <param name="filter">当前激活的分类聚焦维度。</param>
    public void SetActiveFilter(SegmentFilterKind filter)
    {
        _activeFilter = filter;
        UpdateFilterTileVisuals();
    }

    /// <summary>
    /// 同步深浅色主题至自适应字重、汇总趋势色、片段色块画刷与图例激活底色。
    /// </summary>
    /// <param name="isDark">当前是否为暗色模式。</param>
    public void SyncTheme(bool isDark)
    {
        TypeScale.IsDark = isDark;
        Summary.RefreshThemeBrushes();
        foreach (var seg in _allSegments)
        {
            seg.RefreshThemeBrushes();
        }

        UpdateFilterTileVisuals();
    }

    /// <summary>
    /// 按当前激活筛选更新三项图例行的微明度底色。
    /// </summary>
    private void UpdateFilterTileVisuals()
    {
        if (HumanKpiBorder is null || SuspectedKpiBorder is null || AiKpiBorder is null)
        {
            return;
        }

        HumanKpiBorder.Background = _activeFilter == SegmentFilterKind.Human
            ? SemanticPalette.ActiveKpiSurface
            : SemanticPalette.DefaultKpiSurface;
        SuspectedKpiBorder.Background = _activeFilter == SegmentFilterKind.Suspected
            ? SemanticPalette.ActiveKpiSurface
            : SemanticPalette.DefaultKpiSurface;
        AiKpiBorder.Background = _activeFilter == SegmentFilterKind.Ai
            ? SemanticPalette.ActiveKpiSurface
            : SemanticPalette.DefaultKpiSurface;
    }

    /// <summary>点击“人工”图例行触发高亮聚焦切换。</summary>
    private void OnToggleHumanFilterClick(object sender, RoutedEventArgs e)
        => FilterToggled?.Invoke(this, SegmentFilterKind.Human);

    /// <summary>点击“疑似”图例行触发高亮聚焦切换。</summary>
    private void OnToggleSuspectedFilterClick(object sender, RoutedEventArgs e)
        => FilterToggled?.Invoke(this, SegmentFilterKind.Suspected);

    /// <summary>点击“AI”图例行触发高亮聚焦切换。</summary>
    private void OnToggleAiFilterClick(object sender, RoutedEventArgs e)
        => FilterToggled?.Invoke(this, SegmentFilterKind.Ai);

    /// <summary>点击热力方块或风险列表项时发出片段定位事件。</summary>
    private void OnHeatmapTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AuditSegmentItem item })
        {
            SegmentSelected?.Invoke(this, item);
        }
    }

    /// <summary>跳转至上一处风险片段（支持 Alt+↑ 快捷键）。</summary>
    private void OnPreviousRiskClick(object sender, RoutedEventArgs e)
    {
        if (_riskSegments.Count == 0)
        {
            return;
        }

        _currentRiskCursor = _currentRiskCursor <= 0 ? _riskSegments.Count - 1 : _currentRiskCursor - 1;
        SegmentSelected?.Invoke(this, _riskSegments[_currentRiskCursor]);
    }

    /// <summary>跳转至下一处风险片段（支持 Alt+↓ 快捷键）。</summary>
    private void OnNextRiskClick(object sender, RoutedEventArgs e)
    {
        if (_riskSegments.Count == 0)
        {
            return;
        }

        _currentRiskCursor = (_currentRiskCursor + 1) % _riskSegments.Count;
        SegmentSelected?.Invoke(this, _riskSegments[_currentRiskCursor]);
    }
}
