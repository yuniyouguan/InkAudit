using InkAudit.Models;
using InkAudit.Services;
using InkAudit.Theming;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;

namespace InkAudit.Views;

/// <summary>
/// 左侧连续正文纸面组件，封装文档页眉工具栏、无边框 <c>RichEditBox</c> 排版与纯文本粘贴、
/// 朱雀精确字符区间红/黄/绿高亮渲染、鼠标悬停片段占比气泡、文件拖拽及底部弹性轻提示。
/// </summary>
public sealed partial class ManuscriptCanvasView : UserControl
{
    /// <summary>编辑器默认行距倍数。</summary>
    private const float DefaultLineSpacing = 1.48f;

    /// <summary>编辑器默认段后间距（pt）。</summary>
    private const float DefaultParagraphSpaceAfter = 6f;

    /// <summary>最近一次检测映射得到的精确字符片段只读缓存。</summary>
    private IReadOnlyList<AuditSegmentItem> _segments = Array.Empty<AuditSegmentItem>();

    /// <summary>是否允许在正文中渲染红/黄/绿背景高亮。</summary>
    private bool _showHighlights = true;

    /// <summary>当前激活的分类高亮聚焦维度。</summary>
    private SegmentFilterKind _activeFilter = SegmentFilterKind.All;

    /// <summary>当前是否处于检测请求执行中。</summary>
    private bool _isBusy;

    /// <summary>当前是否已有可供导出的检测结果。</summary>
    private bool _hasReport;

    /// <summary>内部同步正文或高亮格式时的重入保护标志。</summary>
    private bool _isUpdatingEditor;

    /// <summary>正文在最近一次检测后是否已被用户手动改动。</summary>
    private bool _isEditedAfterAudit;

    /// <summary>上一次已处理的正文纯文本快照，用于过滤仅格式变更引发的异步 TextChanged 事件。</summary>
    private string _lastTextPayload = string.Empty;

    /// <summary>最近一次检测完成时的正文快照，用于用户撤销回检测态时自动恢复状态。</summary>
    private string _lastAuditedPayload = string.Empty;

    /// <summary>当前鼠标悬停命中的片段对象。</summary>
    private AuditSegmentItem? _hoveredSegment;

    /// <summary>缓存的 <c>RichEditBox</c> 内部滚动容器。</summary>
    private ScrollViewer? _editorScrollViewer;

    /// <summary>底部轻提示自动消失定时器的取消令牌源。</summary>
    private CancellationTokenSource? _toastCts;

    /// <summary>全局本地化字符串入口。</summary>
    public LocalizationService Strings => LocalizationService.Current;

    /// <summary>组件内自适应字重配置对象。</summary>
    public AdaptiveTypography TypeScale { get; } = new();

    /// <summary>当前纸面是否包含非空白正文。</summary>
    public bool HasContent { get; private set; }

    /// <summary>用户点击页眉“粘贴”图标时触发。</summary>
    public event EventHandler? PasteRequested;

    /// <summary>用户点击页眉“导入”图标时触发。</summary>
    public event EventHandler? ImportRequested;

    /// <summary>用户点击页眉“导出报告”图标或按 Ctrl+S 时触发。</summary>
    public event EventHandler? ExportReportRequested;

    /// <summary>用户点击页眉“复制”图标时触发。</summary>
    public event EventHandler? CopyAllRequested;

    /// <summary>用户点击页眉“清空”图标时触发。</summary>
    public event EventHandler? ClearAllRequested;

    /// <summary>用户点击页眉高亮聚焦胶囊清除筛选时触发。</summary>
    public event EventHandler? ResetFilterRequested;

    /// <summary>用户将受支持的 .txt / .md 文件拖入纸面并松手时触发。</summary>
    public event EventHandler<StorageFile>? FileDropped;

    /// <summary>拖拽文件格式不受支持或读取异常时触发。</summary>
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>用户编辑正文导致内容或非空状态发生变化时触发（参数指示是否包含正文）。</summary>
    public event EventHandler<bool>? ContentChanged;

    /// <summary>已存在检测结果时用户手动编辑正文，通知宿主标记结果过期。</summary>
    public event EventHandler? EditedAfterAudit;

    /// <summary>用户通过 Ctrl+Z 将正文撤回至与最近一次检测完全一致时触发，通知宿主恢复新鲜状态。</summary>
    public event EventHandler? RestoredToAuditedText;

    /// <summary>
    /// 初始化纸面组件并挂载指针悬停事件监听。
    /// </summary>
    public ManuscriptCanvasView()
    {
        InitializeComponent();

        // 使用 handledEventsToo: true 确保 RichEditBox 内部消费指针事件后仍可实时驱动悬停气泡
        ManuscriptEditor.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnManuscriptPointerMoved), true);
        ManuscriptEditor.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(OnManuscriptPointerExited), true);
    }

    /// <summary>
    /// 读取与编辑器字符下标 1:1 对齐的送检正文文本。
    /// </summary>
    /// <returns>换行已归一化为 <c>\n</c> 的正文。</returns>
    public string GetPayload()
    {
        ManuscriptEditor.Document.GetText(TextGetOptions.None, out string raw);
        return ParagraphMapper.ExtractPayloadFromEditorText(raw);
    }

    /// <summary>
    /// 使用全新纯文本替换纸面正文，清除旧高亮、清空撤销栈并刷新字数统计。
    /// </summary>
    /// <param name="rawText">待载入的原始文本。</param>
    public void SetPlainText(string rawText)
    {
        string normalized = ParagraphMapper.NormalizeLineEndings(rawText);
        bool wasReadOnly = ManuscriptEditor.IsReadOnly;

        _isUpdatingEditor = true;
        try
        {
            if (wasReadOnly)
            {
                ManuscriptEditor.IsReadOnly = false;
            }

            var doc = ManuscriptEditor.Document;
            doc.BatchDisplayUpdates();
            doc.SetText(TextSetOptions.None, normalized);

            int totalLen = Math.Max(1, normalized.Length + 1);
            var fullRange = doc.GetRange(0, totalLen);
            fullRange.CharacterFormat.BackgroundColor = Colors.Transparent;

            var paraFormat = fullRange.ParagraphFormat;
            paraFormat.SetLineSpacing(LineSpacingRule.Multiple, DefaultLineSpacing);
            paraFormat.SpaceAfter = DefaultParagraphSpaceAfter;

            doc.Selection.SetRange(0, 0);
            doc.ApplyDisplayUpdates();
            doc.ClearUndoRedoHistory();
        }
        finally
        {
            if (wasReadOnly)
            {
                ManuscriptEditor.IsReadOnly = true;
            }
            _isUpdatingEditor = false;
        }

        _segments = Array.Empty<AuditSegmentItem>();
        _activeFilter = SegmentFilterKind.All;
        _isEditedAfterAudit = false;
        _lastTextPayload = normalized;
        _lastAuditedPayload = string.Empty;

        HideHoverBubble();
        RefreshDocumentStats(normalized);
        UpdateFilterBanner();
        UpdateToolbarButtonStates();
    }

    /// <summary>
    /// 将输入焦点交给连续正文编辑器。
    /// </summary>
    public void FocusEditor()
    {
        DispatcherQueue.TryEnqueue(() => ManuscriptEditor.Focus(FocusState.Programmatic));
    }

    /// <summary>
    /// 同步检测请求忙碌状态（控制纸面顶缘进度条、编辑器只读淡出与按钮锁）。
    /// </summary>
    /// <param name="isBusy">是否处于检测进行中。</param>
    public void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        BusyProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        BusyProgressBar.IsIndeterminate = isBusy;
        ManuscriptEditor.IsReadOnly = isBusy;
        ManuscriptEditor.Opacity = isBusy ? 0.62 : 1.0;

        if (isBusy)
        {
            HideHoverBubble();
        }

        UpdateToolbarButtonStates();
    }

    /// <summary>
    /// 更新是否具备可供导出的检测报告状态，控制页眉导出按钮显示。
    /// </summary>
    /// <param name="hasReport">是否已有检测结果。</param>
    public void SetReportAvailable(bool hasReport)
    {
        _hasReport = hasReport;
        UpdateToolbarButtonStates();
    }

    /// <summary>
    /// 根据右侧诊断栏是否展开自动调整连续正文的舒适阅读最大行宽。
    /// </summary>
    /// <param name="isFullWidth">为 true 表示右侧栏隐藏时的单栏宽纸面模式。</param>
    public void SetExpandedWidth(bool isFullWidth)
    {
        ManuscriptEditor.MaxWidth = isFullWidth ? 760 : 860;
    }

    /// <summary>
    /// 接收新一轮检测产出的精确字符片段，立即刷上红/黄/绿背景高亮并锁死撤销基线（防止 Ctrl+Z 撤回检测结果与高亮）。
    /// </summary>
    /// <param name="segments">按偏移排序的精确字符片段集合。</param>
    /// <param name="auditedPayload">本次检测对应的正文纯文本快照。</param>
    /// <param name="showHighlights">是否启用背景高亮着色。</param>
    /// <param name="filter">当前分类聚焦维度。</param>
    public void ApplyAuditSegments(
        IReadOnlyList<AuditSegmentItem> segments,
        string auditedPayload,
        bool showHighlights,
        SegmentFilterKind filter)
    {
        _segments = segments;
        _lastTextPayload = auditedPayload;
        _lastAuditedPayload = auditedPayload;
        _isEditedAfterAudit = false;
        _showHighlights = showHighlights;
        _activeFilter = filter;

        HideHoverBubble();
        UpdateFilterBanner();
        ApplyHighlightsInternal(clearUndoHistory: true);
    }

    /// <summary>
    /// 在高亮开关或右侧分类聚焦维度变化时刷新正文背景着色与页眉聚焦胶囊。
    /// </summary>
    /// <param name="showHighlights">是否启用背景高亮着色。</param>
    /// <param name="filter">目标分类聚焦维度。</param>
    public void UpdateHighlights(bool showHighlights, SegmentFilterKind filter)
    {
        _showHighlights = showHighlights;
        _activeFilter = filter;
        HideHoverBubble();
        UpdateFilterBanner();
        ApplyHighlightsInternal(clearUndoHistory: !_isEditedAfterAudit);
    }

    /// <summary>
    /// 平滑滚动并选中编辑器中对应的精确字符区间。
    /// </summary>
    /// <param name="target">需要定位的目标片段。</param>
    public void SelectAndScrollToSegment(AuditSegmentItem target)
    {
        int docLen = GetPayload().Length;
        if (docLen <= 0)
        {
            return;
        }

        int start = Math.Clamp(target.Start, 0, docLen);
        int end = Math.Clamp(target.End, start, docLen);

        var selection = ManuscriptEditor.Document.Selection;
        selection.SetRange(start, end);
        selection.ScrollIntoView(PointOptions.Start);
        ManuscriptEditor.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// 同步深浅色主题至自适应字重并重刷与当前纸面底色匹配的红/黄/绿字符高亮。
    /// </summary>
    /// <param name="isDark">当前是否为暗色模式。</param>
    public void SyncTheme(bool isDark)
    {
        TypeScale.IsDark = isDark;
        ApplyHighlightsInternal(clearUndoHistory: !_isEditedAfterAudit);
    }

    /// <summary>
    /// 为页眉复制图标播放欠阻尼弹性反馈动效。
    /// </summary>
    public void PlayCopyBounce()
    {
        PlayUnderdampedBounce(CopyCheckScale);
    }

    /// <summary>
    /// 展示底部反色弹性完成提示胶囊，并在 2.5 秒后自动淡出隐藏。
    /// </summary>
    /// <param name="text">提示文案。</param>
    public async Task ShowToastAsync(string text)
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _toastCts = new CancellationTokenSource();
        var ct = _toastCts.Token;

        ActionToastText.Text = text;
        ActionToastPill.Visibility = Visibility.Visible;
        ActionToastPill.Opacity = 1;
        PlayUnderdampedBounce(ToastScaleTransform);

        try
        {
            await Task.Delay(2500, ct);
            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(200)
            };
            var sb = new Storyboard();
            Storyboard.SetTarget(fadeOut, ActionToastPill);
            Storyboard.SetTargetProperty(fadeOut, "Opacity");
            sb.Children.Add(fadeOut);
            sb.Begin();

            await Task.Delay(210, ct);
            ActionToastPill.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            // 新提示到达，取消旧淡出周期
        }
    }

    // ==================== 编辑器排版、粘贴与高亮渲染 ====================

    /// <summary>
    /// 编辑器首次加载完成时配置舒适行距、段后间距并监听内部滚动容器。
    /// </summary>
    private void OnManuscriptEditorLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var format = ManuscriptEditor.Document.GetDefaultParagraphFormat();
            format.SetLineSpacing(LineSpacingRule.Multiple, DefaultLineSpacing);
            format.SpaceAfter = DefaultParagraphSpaceAfter;
            ManuscriptEditor.Document.SetDefaultParagraphFormat(format);
        }
        catch
        {
            // 忽略极端环境下的排版初始化异常
        }

        _editorScrollViewer = FindDescendant<ScrollViewer>(ManuscriptEditor);
        if (_editorScrollViewer is not null)
        {
            _editorScrollViewer.ViewChanged += (_, _) => HideHoverBubble();
        }
    }

    /// <summary>
    /// 拦截默认富文本粘贴，强制转为纯文本插入并保持统一纸面排版。
    /// </summary>
    private async void OnManuscriptPaste(object sender, TextControlPasteEventArgs e)
    {
        e.Handled = true;
        if (_isBusy)
        {
            return;
        }

        try
        {
            var view = Clipboard.GetContent();
            if (!view.Contains(StandardDataFormats.Text))
            {
                return;
            }

            var text = await view.GetTextAsync();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            string normalized = ParagraphMapper.NormalizeLineEndings(text);
            var doc = ManuscriptEditor.Document;
            doc.BatchDisplayUpdates();
            var selection = doc.Selection;
            selection.SetText(TextSetOptions.None, normalized);
            selection.CharacterFormat.BackgroundColor = Colors.Transparent;
            var paraFormat = selection.ParagraphFormat;
            paraFormat.SetLineSpacing(LineSpacingRule.Multiple, DefaultLineSpacing);
            paraFormat.SpaceAfter = DefaultParagraphSpaceAfter;
            selection.StartPosition = selection.EndPosition;
            doc.ApplyDisplayUpdates();
        }
        catch
        {
            // 剪贴板不可用时静默忽略
        }
    }

    /// <summary>
    /// 监听正文变动：通过纯文本比对彻底屏蔽仅着色/格式变更触发的异步 TextChanged，
    /// 仅在用户真实增删字符时更新统计与过期态，且支持 Ctrl+Z 撤回至原检测文本后自动复原。
    /// </summary>
    private void OnManuscriptTextChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingEditor)
        {
            return;
        }

        string payload = GetPayload();

        // 若正文字符序列完全未变，说明仅由高亮着色或格式切换触发异步事件，直接忽略
        if (string.Equals(payload, _lastTextPayload, StringComparison.Ordinal))
        {
            return;
        }

        _lastTextPayload = payload;
        RefreshDocumentStats(payload);

        if (!HasContent)
        {
            _segments = Array.Empty<AuditSegmentItem>();
            _activeFilter = SegmentFilterKind.All;
            _isEditedAfterAudit = false;
            _lastAuditedPayload = string.Empty;
            HideHoverBubble();
            UpdateFilterBanner();
        }
        else if (_segments.Count > 0 && string.Equals(payload, _lastAuditedPayload, StringComparison.Ordinal))
        {
            // 用户将手动改动撤回至与检测快照完全一致时，恢复高亮与悬停气泡
            _isEditedAfterAudit = false;
            ApplyHighlightsInternal(clearUndoHistory: false);
            RestoredToAuditedText?.Invoke(this, EventArgs.Empty);
        }
        else if (_segments.Count > 0 || _hasReport)
        {
            _isEditedAfterAudit = true;
            HideHoverBubble();
            EditedAfterAudit?.Invoke(this, EventArgs.Empty);
        }

        UpdateToolbarButtonStates();
        ContentChanged?.Invoke(this, HasContent);
    }

    /// <summary>
    /// 按当前片段列表、高亮开关与聚焦维度在 <c>RichEditBox</c> 中精确着色。
    /// </summary>
    /// <param name="clearUndoHistory">着色完成后是否清空撤销栈以防止 Ctrl+Z 撤回检测高亮。</param>
    private void ApplyHighlightsInternal(bool clearUndoHistory)
    {
        if (ManuscriptEditor is null)
        {
            return;
        }

        int docLen = GetPayload().Length;
        bool wasReadOnly = ManuscriptEditor.IsReadOnly;

        _isUpdatingEditor = true;
        try
        {
            if (wasReadOnly)
            {
                ManuscriptEditor.IsReadOnly = false;
            }

            var doc = ManuscriptEditor.Document;
            doc.BatchDisplayUpdates();

            if (docLen > 0)
            {
                doc.GetRange(0, docLen + 1).CharacterFormat.BackgroundColor = Colors.Transparent;
            }

            if (_showHighlights && _segments.Count > 0 && docLen > 0)
            {
                foreach (var seg in _segments)
                {
                    if (!IsSegmentVisibleInFilter(seg))
                    {
                        continue;
                    }

                    int start = Math.Clamp(seg.Start, 0, docLen);
                    int end = Math.Clamp(seg.End, start, docLen);
                    if (end > start)
                    {
                        doc.GetRange(start, end).CharacterFormat.BackgroundColor = seg.HighlightColor;
                    }
                }
            }

            doc.ApplyDisplayUpdates();

            if (clearUndoHistory)
            {
                doc.ClearUndoRedoHistory();
            }
        }
        finally
        {
            if (wasReadOnly)
            {
                ManuscriptEditor.IsReadOnly = true;
            }
            _isUpdatingEditor = false;
        }
    }

    // ==================== 鼠标悬停片段占比气泡 ====================

    /// <summary>
    /// 响应指针在正文纸面内的移动，精准定位对应朱雀片段并更新悬浮占比气泡位置。
    /// </summary>
    private void OnManuscriptPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_segments.Count == 0 || _isBusy || _isEditedAfterAudit || _lastAuditedPayload.Length <= 0)
        {
            HideHoverBubble();
            return;
        }

        var sv = _editorScrollViewer ??= FindDescendant<ScrollViewer>(ManuscriptEditor);
        Point ptInHost = e.GetCurrentPoint(EditorHostGrid).Position;
        Point ptInEditor = e.GetCurrentPoint(ManuscriptEditor).Position;

        double padLeft = ManuscriptEditor.Padding.Left;
        double padTop = ManuscriptEditor.Padding.Top;
        double scrollX = sv?.HorizontalOffset ?? 0;
        double scrollY = sv?.VerticalOffset ?? 0;

        if (ptInEditor.X < padLeft - 4 || ptInEditor.Y < padTop - 4)
        {
            HideHoverBubble();
            return;
        }

        try
        {
            var doc = ManuscriptEditor.Document;

            // 自校准 ClientCoordinates 坐标系（带 AllowOffClient，避免滚出顶屏时 char 0 越界异常）
            bool clientIncludesPadding = false;
            bool clientIsViewportRelative = false;
            try
            {
                doc.GetRange(0, 1).GetRect(PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var firstCharRect, out _);
                clientIncludesPadding = firstCharRect.X > padLeft * 0.5;
                clientIsViewportRelative = scrollY > padTop + 4 && firstCharRect.Y < -2;
            }
            catch
            {
                // 回退至标准文档内边距与滚动偏移换算
            }

            double docX = ptInEditor.X - (clientIncludesPadding ? 0 : padLeft) + (clientIsViewportRelative ? 0 : scrollX);
            double docY = ptInEditor.Y - (clientIncludesPadding ? 0 : padTop) + (clientIsViewportRelative ? 0 : scrollY);

            var hitRange = doc.GetRangeFromPoint(new Point(docX, docY), PointOptions.ClientCoordinates);
            int pos = hitRange.StartPosition;
            if (pos < 0 || pos >= _lastAuditedPayload.Length)
            {
                HideHoverBubble();
                return;
            }

            // 若 GetRect 返回有效字符框，则校验指针确实落在文字行附近，避免空白纸面误弹气泡
            try
            {
                var charRange = doc.GetRange(pos, Math.Min(pos + 1, _lastAuditedPayload.Length));
                charRange.GetRect(PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var charRect, out _);
                if (charRect.Height > 1)
                {
                    bool onText = docY >= charRect.Y - 8 &&
                                  docY <= charRect.Y + charRect.Height + 10 &&
                                  docX >= charRect.X - 24 &&
                                  docX <= charRect.X + Math.Max(charRect.Width, 24) + 32;
                    if (!onText)
                    {
                        HideHoverBubble();
                        return;
                    }
                }
            }
            catch
            {
                // 个别边界字符 GetRect 失败时仍按 GetRangeFromPoint 命中结果展示气泡
            }

            var segment = FindSegmentAt(pos);
            if (segment is null || !IsSegmentVisibleInFilter(segment))
            {
                HideHoverBubble();
                return;
            }

            ShowOrMoveHoverBubble(segment, ptInHost);
        }
        catch
        {
            HideHoverBubble();
        }
    }

    /// <summary>
    /// 鼠标移出正文纸面时隐藏悬浮气泡。
    /// </summary>
    private void OnManuscriptPointerExited(object sender, PointerRoutedEventArgs e)
    {
        HideHoverBubble();
    }

    /// <summary>
    /// 查找包含指定字符下标的朱雀片段。
    /// </summary>
    /// <param name="charOffset">UTF-16 字符下标。</param>
    /// <returns>命中的片段，未命中返回 null。</returns>
    private AuditSegmentItem? FindSegmentAt(int charOffset)
    {
        foreach (var seg in _segments)
        {
            if (seg.ContainsPosition(charOffset))
            {
                return seg;
            }
        }

        return null;
    }

    /// <summary>
    /// 显示并平滑定位悬浮占比气泡，自动规避纸面边界溢出。
    /// </summary>
    /// <param name="segment">命中的片段对象。</param>
    /// <param name="ptInHost">指针在编辑容器内的坐标。</param>
    private void ShowOrMoveHoverBubble(AuditSegmentItem segment, Point ptInHost)
    {
        if (!ReferenceEquals(_hoveredSegment, segment))
        {
            _hoveredSegment = segment;
            HoverBubbleDot.Fill = segment.BubbleDotBrush;
            HoverBubbleLabel.Text = segment.LabelDisplay;
            HoverBubbleMeta.Text = $"#{segment.IndexDisplay} · {Strings.FormatSegmentCharCount(segment.CharCount)}";
            HoverBubbleRatios.Text = Strings.FormatSegmentBubbleRatios(segment.AiPercentText, segment.HumanPercentText);
            SegmentHoverBubble.Visibility = Visibility.Visible;
            SegmentHoverBubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        else if (SegmentHoverBubble.Visibility != Visibility.Visible)
        {
            SegmentHoverBubble.Visibility = Visibility.Visible;
        }

        double bw = SegmentHoverBubble.ActualWidth > 0 ? SegmentHoverBubble.ActualWidth : SegmentHoverBubble.DesiredSize.Width;
        double bh = SegmentHoverBubble.ActualHeight > 0 ? SegmentHoverBubble.ActualHeight : SegmentHoverBubble.DesiredSize.Height;

        double maxX = Math.Max(12, EditorHostGrid.ActualWidth - bw - 12);
        double x = Math.Clamp(ptInHost.X - (bw / 2.0), 12, maxX);
        double y = ptInHost.Y - bh - 14;
        if (y < 10)
        {
            y = Math.Min(ptInHost.Y + 22, Math.Max(10, EditorHostGrid.ActualHeight - bh - 10));
        }

        Canvas.SetLeft(SegmentHoverBubble, x);
        Canvas.SetTop(SegmentHoverBubble, y);
    }

    /// <summary>
    /// 隐藏悬浮占比气泡并清空悬停缓存。
    /// </summary>
    private void HideHoverBubble()
    {
        if (SegmentHoverBubble is not null && SegmentHoverBubble.Visibility != Visibility.Collapsed)
        {
            SegmentHoverBubble.Visibility = Visibility.Collapsed;
        }

        _hoveredSegment = null;
    }

    // ==================== 页眉统计、按钮转发与拖拽 ====================

    /// <summary>
    /// 刷新页眉字数与段数统计以及 <see cref="HasContent"/> 标志。
    /// </summary>
    /// <param name="payload">当前正文文本。</param>
    private void RefreshDocumentStats(string payload)
    {
        var (chars, paras) = ParagraphMapper.CountTextStats(payload);
        HasContent = chars > 0;

        if (!HasContent)
        {
            ChapterStatsText.Visibility = Visibility.Collapsed;
        }
        else
        {
            ChapterStatsText.Text = Strings.FormatChapterStats(chars, paras);
            ChapterStatsText.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// 刷新页眉右侧图标按钮组的显隐与可用性。
    /// </summary>
    private void UpdateToolbarButtonStates()
    {
        DocActionsPanel.Visibility = HasContent ? Visibility.Visible : Visibility.Collapsed;
        ExportReportButton.Visibility = _hasReport ? Visibility.Visible : Visibility.Collapsed;
        ExportReportButton.IsEnabled = _hasReport && !_isBusy;
        PasteButton.IsEnabled = !_isBusy;
        ImportButton.IsEnabled = !_isBusy;
        CopyAllButton.IsEnabled = HasContent;
        ClearAllButton.IsEnabled = HasContent && !_isBusy;
    }

    /// <summary>
    /// 刷新页眉左侧分类高亮聚焦胶囊。
    /// </summary>
    private void UpdateFilterBanner()
    {
        if (_activeFilter == SegmentFilterKind.All || _segments.Count == 0)
        {
            ActiveFilterBanner.Visibility = Visibility.Collapsed;
            return;
        }

        int count = _segments.Count(IsSegmentVisibleInFilter);
        string label = _activeFilter switch
        {
            SegmentFilterKind.Ai => Strings.LabelAi,
            SegmentFilterKind.Suspected => Strings.LabelSuspected,
            SegmentFilterKind.Human => Strings.LabelHuman,
            _ => string.Empty
        };
        ActiveFilterLabel.Text = $"{label} · {count}";
        ActiveFilterBanner.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 判断指定片段在当前聚焦筛选下是否允许显示高亮与悬停气泡。
    /// </summary>
    /// <param name="seg">待判定片段。</param>
    /// <returns>允许显示返回 true。</returns>
    private bool IsSegmentVisibleInFilter(AuditSegmentItem seg) => _activeFilter switch
    {
        SegmentFilterKind.All => true,
        SegmentFilterKind.Human => seg.Label == 0,
        SegmentFilterKind.Ai => seg.Label == 1,
        SegmentFilterKind.Suspected => seg.Label == 2,
        _ => true
    };

    /// <summary>处理拖拽文件悬停事件。</summary>
    private void OnEditorDragOver(object sender, DragEventArgs e)
    {
        if (!_isBusy && e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    /// <summary>处理松开拖入文件事件，校验扩展名后通知宿主载入。</summary>
    private async void OnEditorDrop(object sender, DragEventArgs e)
    {
        if (_isBusy || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var file = items.OfType<StorageFile>().FirstOrDefault(f =>
                f.FileType.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                f.FileType.Equals(".md", StringComparison.OrdinalIgnoreCase));

            if (file is null)
            {
                ErrorOccurred?.Invoke(this, Strings.ErrUnsupportedFile);
                return;
            }

            FileDropped?.Invoke(this, file);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
    }

    /// <summary>转发粘贴按钮点击事件。</summary>
    private void OnPasteClick(object sender, RoutedEventArgs e) => PasteRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>转发导入按钮点击事件。</summary>
    private void OnImportClick(object sender, RoutedEventArgs e) => ImportRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>转发导出报告按钮点击事件。</summary>
    private void OnExportReportClick(object sender, RoutedEventArgs e) => ExportReportRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>转发复制全文按钮点击事件。</summary>
    private void OnCopyAllClick(object sender, RoutedEventArgs e) => CopyAllRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>转发清空按钮点击事件。</summary>
    private void OnClearAllClick(object sender, RoutedEventArgs e) => ClearAllRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>转发清除高亮聚焦按钮点击事件。</summary>
    private void OnResetFilterClick(object sender, RoutedEventArgs e) => ResetFilterRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 对目标缩放变换施加欠阻尼过冲（BackEase）弹性动画。
    /// </summary>
    /// <param name="transform">待驱动的缩放变换对象。</param>
    private static void PlayUnderdampedBounce(ScaleTransform transform)
    {
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
        var animX = new DoubleAnimation { From = 0.72, To = 1.0, Duration = TimeSpan.FromMilliseconds(260), EasingFunction = ease };
        var animY = new DoubleAnimation { From = 0.72, To = 1.0, Duration = TimeSpan.FromMilliseconds(260), EasingFunction = ease };

        var sb = new Storyboard();
        Storyboard.SetTarget(animX, transform);
        Storyboard.SetTargetProperty(animX, "ScaleX");
        Storyboard.SetTarget(animY, transform);
        Storyboard.SetTargetProperty(animY, "ScaleY");
        sb.Children.Add(animX);
        sb.Children.Add(animY);
        sb.Begin();
    }

    /// <summary>
    /// 在可视化树中深度优先查找指定类型的首个子控件。
    /// </summary>
    /// <typeparam name="T">目标控件类型。</typeparam>
    /// <param name="root">起始节点。</param>
    /// <returns>匹配到的首个子控件，未找到返回 null。</returns>
    private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null)
        {
            return null;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T matched)
            {
                return matched;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
