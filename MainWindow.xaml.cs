using InkAudit.Models;
using InkAudit.Services;
using InkAudit.Theming;
using InkAudit.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace InkAudit;

/// <summary>
/// 墨鉴（InkAudit）主工作台窗口协调器，负责原生窗口栏、主题同步、朱雀接口检测调度、
/// 系统文件导入与 Markdown 报告导出，并衔接 <see cref="ManuscriptCanvasView"/> 与 <see cref="AuditSidebarView"/>。
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>本地配置持久化服务。</summary>
    private readonly SettingsService _settingsService = new();

    /// <summary>朱雀 AI 文本检测接口服务。</summary>
    private readonly ZhuqueApiService _apiService = new();

    /// <summary>当前生效的应用配置。</summary>
    private AppSettings _settings = new();

    /// <summary>当前激活的高亮聚焦维度。</summary>
    private SegmentFilterKind _activeFilter = SegmentFilterKind.All;

    /// <summary>是否有检测请求正在执行。</summary>
    private bool _isBusy;

    /// <summary>当前检测任务的取消令牌源。</summary>
    private CancellationTokenSource? _auditCts;

    /// <summary>全局本地化字符串入口。</summary>
    public LocalizationService Strings => LocalizationService.Current;

    /// <summary>窗口顶栏自适应字重配置对象。</summary>
    public AdaptiveTypography TypeScale { get; } = new();

    /// <summary>右侧诊断组件持有的全文汇总模型快捷访问。</summary>
    private AuditSummary Summary => SidebarView.Summary;

    /// <summary>
    /// 初始化主窗口、配置自定义标题栏并挂载首次激活加载事件。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Title = Strings.AppTitle;
        ConfigureWindowChrome();
        Activated += OnFirstActivated;
    }

    /// <summary>
    /// 配置无边界原生标题栏、Tall 标题栏按钮高度与默认舒适窗口尺寸。
    /// </summary>
    private void ConfigureWindowChrome()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        try
        {
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            AppWindow.Resize(new SizeInt32(1280, 820));
        }
        catch
        {
            // 在低分辨率或受限显示器上忽略尺寸与标题栏高度调整异常
        }
    }

    /// <summary>
    /// 窗口首次激活时异步加载本地配置、应用外观主题并聚焦正文输入纸面。
    /// </summary>
    /// <param name="sender">窗口对象。</param>
    /// <param name="args">窗口激活事件参数。</param>
    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;

        _settings = await _settingsService.LoadAsync();
        ApplySettingsToUi();
        SyncWorkspaceLayout();
        CanvasView.FocusEditor();
    }

    /// <summary>
    /// 将当前配置同步至主题、子组件高亮开关及主按钮状态。
    /// </summary>
    private void ApplySettingsToUi()
    {
        ApplyTheme(_settings.ThemeMode);
        CanvasView.UpdateHighlights(_settings.ShowHeatmapTint, _activeFilter);
        UpdatePrimaryActionState();
    }

    /// <summary>
    /// 按用户选择的主题模式切换根容器外观并向子组件广播主题刷新。
    /// </summary>
    /// <param name="themeMode">"Light"、"Dark" 或 "Default"。</param>
    private void ApplyTheme(string themeMode)
    {
        RootGrid.RequestedTheme = themeMode switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        SyncThemeTokens();
    }

    /// <summary>
    /// 响应系统或应用实际主题变更事件，实时刷新全部动态色阶与字重。
    /// </summary>
    private void OnRootActualThemeChanged(FrameworkElement sender, object args)
    {
        SyncThemeTokens();
    }

    /// <summary>
    /// 同步当前深浅色标志到语义调色板、顶栏系统按钮与左右两个工作区组件。
    /// </summary>
    private void SyncThemeTokens()
    {
        bool isDark = RootGrid.ActualTheme == ElementTheme.Dark;
        SemanticPalette.IsDark = isDark;
        TypeScale.IsDark = isDark;
        SyncTitleBarCaptionColors(isDark);
        SidebarView.SyncTheme(isDark);
        CanvasView.SyncTheme(isDark);
    }

    /// <summary>
    /// 同步窗口右上角系统标题栏按钮的前景与悬停底色。
    /// </summary>
    /// <param name="isDark">当前是否为暗色模式。</param>
    private void SyncTitleBarCaptionColors(bool isDark)
    {
        try
        {
            var titleBar = AppWindow.TitleBar;
            if (isDark)
            {
                titleBar.ButtonForegroundColor = Color.FromArgb(255, 234, 233, 229);
                titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
                titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 214, 213, 209);
                titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 117, 116, 111);
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(24, 255, 255, 255);
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(16, 255, 255, 255);
            }
            else
            {
                titleBar.ButtonForegroundColor = Color.FromArgb(255, 26, 26, 25);
                titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 26, 26, 25);
                titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 92, 91, 87);
                titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 143, 142, 137);
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(16, 26, 26, 25);
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(24, 26, 26, 25);
            }

            titleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        }
        catch
        {
            // 非支持自定义标题栏按钮着色的系统环境下忽略异常
        }
    }

    // ==================== 布局协调与顶部主按钮状态 ====================

    /// <summary>
    /// 根据忙碌状态与检测结果同步单栏/双栏布局、纸面组件状态与主检测按钮。
    /// </summary>
    private void SyncWorkspaceLayout()
    {
        bool hasReport = Summary.HasResult || SidebarView.Segments.Count > 0;
        bool showSidebar = _isBusy || hasReport;

        SidebarView.Visibility = showSidebar ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumnSpan(CanvasView, showSidebar ? 1 : 2);
        CanvasView.SetExpandedWidth(!showSidebar);
        CanvasView.SetReportAvailable(hasReport);
        CanvasView.SetBusy(_isBusy);

        UpdatePrimaryActionState();
    }

    /// <summary>
    /// 刷新顶部唯一检测主按钮的样式、文案、加载环与快捷键提示。
    /// </summary>
    private void UpdatePrimaryActionState()
    {
        if (_isBusy)
        {
            PrimaryAuditButton.Style = (Style)Application.Current.Resources["BusyPrimaryButtonStyle"];
            PrimaryAuditButton.IsEnabled = true;
            PrimaryAuditRing.Visibility = Visibility.Visible;
            PrimaryAuditRing.IsActive = true;
            PrimaryAuditLabel.Text = Strings.ActionCancel;
            ToolTipService.SetToolTip(PrimaryAuditButton, Strings.TooltipCancelAudit);

            ForceFullAuditMenuItem.IsEnabled = false;
            MergedOnlyMenuItem.IsEnabled = false;
            return;
        }

        PrimaryAuditButton.Style = (Style)Application.Current.Resources["MinimalPrimaryButtonStyle"];
        PrimaryAuditRing.IsActive = false;
        PrimaryAuditRing.Visibility = Visibility.Collapsed;

        bool hasContent = CanvasView.HasContent;
        bool hasAuditHistory = Summary.HasResult || SidebarView.Segments.Count > 0;
        string label = hasAuditHistory ? Strings.ActionReaudit : Strings.ActionStartAudit;

        PrimaryAuditButton.IsEnabled = hasContent;
        PrimaryAuditLabel.Text = label;
        ToolTipService.SetToolTip(PrimaryAuditButton, hasContent ? Strings.FormatAuditShortcut(label) : null);

        ForceFullAuditMenuItem.IsEnabled = hasContent;
        MergedOnlyMenuItem.IsEnabled = hasContent;
    }

    /// <summary>处理全局 Ctrl+Enter 快捷键触发检测。</summary>
    private void OnAuditAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_isBusy || !PrimaryAuditButton.IsEnabled)
        {
            return;
        }

        args.Handled = true;
        OnPrimaryAuditClick(PrimaryAuditButton, new RoutedEventArgs());
    }

    /// <summary>处理全局 Esc 快捷键：仅在检测进行中时中止请求。</summary>
    private void OnEscapeAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_isBusy)
        {
            return;
        }

        args.Handled = true;
        _auditCts?.Cancel();
    }

    // ==================== 左侧纸面组件事件响应 ====================

    /// <summary>
    /// 将新载入的纯文本写入纸面并清空侧栏历史结果与筛选状态。
    /// </summary>
    /// <param name="text">待载入文本。</param>
    private void LoadFreshDocument(string text)
    {
        _activeFilter = SegmentFilterKind.All;
        Summary.Reset();
        SidebarView.ClearSegments();
        SidebarView.SetActiveFilter(_activeFilter);
        CanvasView.SetPlainText(text);
        HideErrorBar();
        SyncWorkspaceLayout();
    }

    /// <summary>响应纸面组件“粘贴”请求，读取剪贴板覆盖当前正文。</summary>
    private async void OnPasteOverwriteRequested(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        try
        {
            var dataPackageView = Clipboard.GetContent();
            if (!dataPackageView.Contains(StandardDataFormats.Text))
            {
                ShowErrorBar(Strings.ErrClipboardNoText);
                return;
            }

            var text = await dataPackageView.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowErrorBar(Strings.ErrClipboardEmpty);
                return;
            }

            LoadFreshDocument(text);
        }
        catch (Exception ex)
        {
            ShowErrorBar(ex.Message);
        }
    }

    /// <summary>响应纸面组件“导入”请求，打开系统文件选择器载入 .txt / .md。</summary>
    private async void OnImportFileRequested(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".md");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            var text = await FileIO.ReadTextAsync(file);
            LoadFreshDocument(text);
        }
        catch (Exception ex)
        {
            ShowErrorBar(ex.Message);
        }
    }

    /// <summary>响应纸面拖入文件事件并异步读取正文。</summary>
    private async void OnFileDropped(object? sender, StorageFile file)
    {
        if (_isBusy)
        {
            return;
        }

        try
        {
            var text = await FileIO.ReadTextAsync(file);
            LoadFreshDocument(text);
        }
        catch (Exception ex)
        {
            ShowErrorBar(ex.Message);
        }
    }

    /// <summary>响应纸面组件“导出报告”请求，生成并保存标准 Markdown 检测报告。</summary>
    private async void OnExportReportRequested(object? sender, EventArgs e)
    {
        if (_isBusy || (!Summary.HasResult && SidebarView.Segments.Count == 0))
        {
            return;
        }

        try
        {
            var now = DateTime.Now;
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = ReportExportService.BuildSuggestedFileName(now)
            };
            picker.FileTypeChoices.Add("Markdown", new List<string> { ".md" });
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            string payload = CanvasView.GetPayload();
            string markdown = ReportExportService.BuildMarkdownReport(payload, Summary, SidebarView.Segments, now);
            await FileIO.WriteTextAsync(file, markdown);
            HideErrorBar();
            await CanvasView.ShowToastAsync(Strings.ToastReportExported);
        }
        catch (Exception ex)
        {
            ShowErrorBar(ex.Message);
        }
    }

    /// <summary>响应纸面组件“复制全文”请求并播放弹性反馈。</summary>
    private async void OnCopyAllRequested(object? sender, EventArgs e)
    {
        string payload = CanvasView.GetPayload();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(payload);
        Clipboard.SetContent(package);

        CanvasView.PlayCopyBounce();
        await CanvasView.ShowToastAsync(Strings.ToastCopiedAll);
    }

    /// <summary>响应纸面组件“清空”请求，中止请求并回到纯净纸面。</summary>
    private void OnClearAllRequested(object? sender, EventArgs e)
    {
        _auditCts?.Cancel();
        LoadFreshDocument(string.Empty);
        CanvasView.FocusEditor();
    }

    /// <summary>响应纸面正文内容变化，正文被删空时同步复位侧栏。</summary>
    private void OnCanvasContentChanged(object? sender, bool hasContent)
    {
        if (!hasContent)
        {
            _activeFilter = SegmentFilterKind.All;
            Summary.Reset();
            SidebarView.ClearSegments();
            SidebarView.SetActiveFilter(_activeFilter);
        }

        SyncWorkspaceLayout();
    }

    /// <summary>响应已检测文档被用户改写，标记右侧全文汇总为过期状态。</summary>
    private void OnCanvasEditedAfterAudit(object? sender, EventArgs e)
    {
        if (Summary.HasResult)
        {
            Summary.IsStale = true;
        }
    }

    /// <summary>响应用户通过 Ctrl+Z 将正文撤回至原检测快照，自动恢复右侧汇总新鲜态。</summary>
    private void OnCanvasRestoredToAuditedText(object? sender, EventArgs e)
    {
        if (Summary.HasResult)
        {
            Summary.IsStale = false;
        }
    }

    /// <summary>响应纸面组件上报的错误提示。</summary>
    private void OnCanvasErrorOccurred(object? sender, string message)
    {
        ShowErrorBar(message);
    }

    // ==================== 右侧诊断组件事件响应 ====================

    /// <summary>响应右侧图例筛选切换，同步纸面高亮聚焦与侧栏图例激活态。</summary>
    private void OnSidebarFilterToggled(object? sender, SegmentFilterKind kind)
    {
        if (SidebarView.Segments.Count == 0)
        {
            return;
        }

        _activeFilter = _activeFilter == kind ? SegmentFilterKind.All : kind;
        ApplyActiveFilter();
    }

    /// <summary>响应纸面聚焦胶囊清除按钮，恢复展示全部三色高亮。</summary>
    private void OnResetFilterRequested(object? sender, EventArgs e)
    {
        _activeFilter = SegmentFilterKind.All;
        ApplyActiveFilter();
    }

    /// <summary>响应右侧热力方块、风险列表或 Alt+↑/↓ 跳转请求，定位编辑器内对应字符区间。</summary>
    private void OnSidebarSegmentSelected(object? sender, AuditSegmentItem segment)
    {
        if (_activeFilter != SegmentFilterKind.All && !MatchesFilter(segment, _activeFilter))
        {
            _activeFilter = SegmentFilterKind.All;
            ApplyActiveFilter();
        }

        CanvasView.SelectAndScrollToSegment(segment);
    }

    /// <summary>将当前高亮聚焦维度同步到左右两侧组件。</summary>
    private void ApplyActiveFilter()
    {
        SidebarView.SetActiveFilter(_activeFilter);
        CanvasView.UpdateHighlights(_settings.ShowHeatmapTint, _activeFilter);
    }

    /// <summary>判断片段是否命中指定聚焦筛选类别。</summary>
    /// <param name="segment">目标片段。</param>
    /// <param name="filter">聚焦筛选维度。</param>
    /// <returns>命中时返回 true。</returns>
    private static bool MatchesFilter(AuditSegmentItem segment, SegmentFilterKind filter) => filter switch
    {
        SegmentFilterKind.All => true,
        SegmentFilterKind.Human => segment.Label == 0,
        SegmentFilterKind.Ai => segment.Label == 1,
        SegmentFilterKind.Suspected => segment.Label == 2,
        _ => true
    };

    // ==================== 朱雀接口检测调度 ====================

    /// <summary>顶部唯一主按钮点击回调：忙碌时取消，空闲时启动全文检测。</summary>
    private async void OnPrimaryAuditClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            _auditCts?.Cancel();
            return;
        }

        await RunAuditAsync(mergedOnly: false);
    }

    /// <summary>右键菜单回调：强制重新检测全文。</summary>
    private async void OnForceFullAuditClick(object sender, RoutedEventArgs e)
    {
        if (!_isBusy)
        {
            await RunAuditAsync(mergedOnly: false);
        }
    }

    /// <summary>右键菜单回调：仅运行一次全文合并复核（is_merge: true）。</summary>
    private async void OnRunMergedOnlyClick(object sender, RoutedEventArgs e)
    {
        if (!_isBusy)
        {
            await RunAuditAsync(mergedOnly: true);
        }
    }

    /// <summary>
    /// 执行朱雀全文检测并将结果分别派发至左侧高亮纸面与右侧诊断栏。
    /// </summary>
    /// <param name="mergedOnly">为 true 时仅调用一次 is_merge: true 刷新汇总。</param>
    private async Task RunAuditAsync(bool mergedOnly)
    {
        if (_isBusy)
        {
            return;
        }

        string payload = CanvasView.GetPayload();
        if (string.IsNullOrWhiteSpace(payload))
        {
            ShowErrorBar(Strings.ErrEmptyText);
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            ShowErrorBar(Strings.ErrMissingApiKey, showSettingsAction: true);
            return;
        }

        HideErrorBar();

        _auditCts?.Cancel();
        _auditCts?.Dispose();
        _auditCts = new CancellationTokenSource();
        var ct = _auditCts.Token;

        bool runMergeCheck = mergedOnly || _settings.EnableFullTextMergeCheck;
        double? previousAiRatio = Summary.HasResult ? Summary.AiRatio : null;

        SetBusy(true);

        try
        {
            int totalTokens = 0;

            if (mergedOnly)
            {
                var mergedResp = await _apiService.ClassifyAsync(
                    payload,
                    isMerge: true,
                    apiKey: _settings.ApiKey,
                    endpointUrl: _settings.EndpointUrl,
                    cancellationToken: ct);

                totalTokens += mergedResp.MakersModelsUsage?.TotalTokens ?? 0;
                ApplySummaryFromResponse(mergedResp, true, null, previousAiRatio, totalTokens);
                await CanvasView.ShowToastAsync(Strings.ToastAuditDone);
                return;
            }

            Task<ZhuqueClassifyResponse> segmentTask = _apiService.ClassifyAsync(
                payload,
                isMerge: false,
                apiKey: _settings.ApiKey,
                endpointUrl: _settings.EndpointUrl,
                cancellationToken: ct);

            Task<ZhuqueClassifyResponse>? mergeTask = runMergeCheck
                ? _apiService.ClassifyAsync(payload, isMerge: true, apiKey: _settings.ApiKey, endpointUrl: _settings.EndpointUrl, cancellationToken: ct)
                : null;

            ZhuqueClassifyResponse? segResponse = null;
            ZhuqueClassifyResponse? mergeResponse = null;
            Exception? segError = null;
            Exception? mergeError = null;

            if (mergeTask is not null)
            {
                await Task.WhenAll(
                    segmentTask.ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully) segResponse = t.Result;
                        else if (t.IsFaulted) segError = t.Exception?.GetBaseException();
                    }, TaskScheduler.Default),
                    mergeTask.ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully) mergeResponse = t.Result;
                        else if (t.IsFaulted) mergeError = t.Exception?.GetBaseException();
                    }, TaskScheduler.Default));
            }
            else
            {
                segResponse = await segmentTask;
            }

            ct.ThrowIfCancellationRequested();

            if (segResponse is not null)
            {
                totalTokens += segResponse.MakersModelsUsage?.TotalTokens ?? 0;
                var mapped = ParagraphMapper.MapResponseToSegments(payload, segResponse);
                SidebarView.SetSegments(mapped);
                SidebarView.SetActiveFilter(_activeFilter);
                CanvasView.ApplyAuditSegments(mapped, payload, _settings.ShowHeatmapTint, _activeFilter);
            }

            if (mergeResponse is not null)
            {
                totalTokens += mergeResponse.MakersModelsUsage?.TotalTokens ?? 0;
                ApplySummaryFromResponse(mergeResponse, true, segResponse, previousAiRatio, totalTokens);
            }
            else if (segResponse is not null)
            {
                ApplySummaryFromResponse(segResponse, false, null, previousAiRatio, totalTokens);
            }

            if (segError is not null && mergeError is not null)
            {
                ShowErrorBar(segError.Message);
            }
            else if (mergeError is not null)
            {
                ShowErrorBar(Strings.FormatMergeError(mergeError.Message));
            }
            else if (segError is not null)
            {
                ShowErrorBar(Strings.FormatSegmentError(segError.Message));
            }
            else
            {
                _ = CanvasView.ShowToastAsync(Strings.ToastAuditDone);
            }
        }
        catch (OperationCanceledException)
        {
            // 用户主动取消，保留已有正文与历史结果
        }
        catch (Exception ex)
        {
            ShowErrorBar(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// 将朱雀响应数据写入右侧全文汇总模型，并附带可选逐段对照与历史变化量。
    /// </summary>
    /// <param name="primaryResponse">主结论接口响应。</param>
    /// <param name="isMergedSource">主结论是否来自 is_merge: true。</param>
    /// <param name="segmentComparisonResponse">可选的 is_merge: false 对照响应。</param>
    /// <param name="previousAiRatio">上一次检测的 AI 占比。</param>
    /// <param name="totalTokensUsed">本次实扣 Makers Token 总数。</param>
    private void ApplySummaryFromResponse(
        ZhuqueClassifyResponse primaryResponse,
        bool isMergedSource,
        ZhuqueClassifyResponse? segmentComparisonResponse,
        double? previousAiRatio,
        int totalTokensUsed)
    {
        Summary.PreviousAiRatio = previousAiRatio;
        Summary.IsMergedSource = isMergedSource;
        Summary.HumanRatio = primaryResponse.GetLabelRatio(0);
        Summary.AiRatio = primaryResponse.GetLabelRatio(1);
        Summary.SuspectedRatio = primaryResponse.GetLabelRatio(2);
        Summary.SoftmaxConfidence = primaryResponse.SoftmaxConfidence;
        Summary.RatioConfidence = primaryResponse.RatioConfidence;
        Summary.LastTokensUsed = totalTokensUsed;
        Summary.IsStale = false;

        if (isMergedSource && segmentComparisonResponse is not null)
        {
            Summary.HasSegmentComparison = true;
            Summary.SegmentHumanRatio = segmentComparisonResponse.GetLabelRatio(0);
            Summary.SegmentAiRatio = segmentComparisonResponse.GetLabelRatio(1);
            Summary.SegmentSuspectedRatio = segmentComparisonResponse.GetLabelRatio(2);
        }
        else
        {
            Summary.HasSegmentComparison = false;
        }

        Summary.HasResult = true;
    }

    // ==================== 设置弹窗与错误提示 ====================

    /// <summary>打开应用设置对话框，并在保存后持久化与应用新配置。</summary>
    private async void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_settings, _apiService)
        {
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = RootGrid.RequestedTheme
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _settings = dialog.ResultSettings;
            await _settingsService.SaveAsync(_settings);
            ApplySettingsToUi();
            HideErrorBar();
            await CanvasView.ShowToastAsync(Strings.ToastSaved);
        }
    }

    /// <summary>切换请求执行中的忙碌状态并同步布局。</summary>
    /// <param name="isBusy">是否处于检测请求执行中。</param>
    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        SyncWorkspaceLayout();
    }

    /// <summary>显示必要的校验或接口错误提示条。</summary>
    /// <param name="message">面向用户的错误信息。</param>
    /// <param name="showSettingsAction">是否附带“设置”快捷操作按钮。</param>
    private void ShowErrorBar(string message, bool showSettingsAction = false)
    {
        MainInfoBar.Title = string.Empty;
        MainInfoBar.Message = message;
        MainInfoBar.Severity = InfoBarSeverity.Error;

        if (showSettingsAction)
        {
            var btn = new Button { Content = Strings.ActionSettings };
            btn.Click += OnOpenSettingsClick;
            MainInfoBar.ActionButton = btn;
        }
        else
        {
            MainInfoBar.ActionButton = null;
        }

        MainInfoBar.IsOpen = true;
    }

    /// <summary>关闭顶部错误提示条。</summary>
    private void HideErrorBar()
    {
        MainInfoBar.IsOpen = false;
    }
}
