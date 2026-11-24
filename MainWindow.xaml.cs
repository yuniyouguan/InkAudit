using InkAudit.Models;
using InkAudit.Services;
using InkAudit.Theming;
using InkAudit.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace InkAudit;

/// <summary>
/// 墨鉴（InkAudit）主工作台窗口，负责章节导入、朱雀逐段与全文合并检测、
/// 段落原地改写重检、数据联动筛选、热力图导航及主题/字重自适应控制。
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// 左侧段落列表支持的筛选维度。
    /// </summary>
    private enum ParagraphFilterKind
    {
        /// <summary>展示全部段落。</summary>
        All,

        /// <summary>仅展示 AI 特征段落。</summary>
        Ai,

        /// <summary>仅展示疑似 AI 段落。</summary>
        Suspected,

        /// <summary>仅展示人工特征段落。</summary>
        Human,

        /// <summary>仅展示短段跳过段落。</summary>
        Skipped,

        /// <summary>仅展示已修改待重检段落。</summary>
        Modified
    }

    /// <summary>本地配置持久化服务。</summary>
    private readonly SettingsService _settingsService = new();

    /// <summary>朱雀 AI 文本检测接口服务。</summary>
    private readonly ZhuqueApiService _apiService = new();

    /// <summary>当前生效的应用配置。</summary>
    private AppSettings _settings = new();

    /// <summary>当前章节的完整段落集合。</summary>
    private readonly ObservableCollection<ParagraphItem> _allParagraphs = new();

    /// <summary>经当前筛选条件过滤后展示在主视图中的段落集合。</summary>
    private readonly ObservableCollection<ParagraphItem> _filteredParagraphs = new();

    /// <summary>命中 AI 或疑似 AI 风险的段落子集，供右侧速览与快捷键跳转使用。</summary>
    private readonly ObservableCollection<ParagraphItem> _riskParagraphs = new();

    /// <summary>当前激活的筛选维度。</summary>
    private ParagraphFilterKind _activeFilter = ParagraphFilterKind.All;

    /// <summary>是否处于整章纯文本编辑覆盖层中。</summary>
    private bool _isBulkEditing;

    /// <summary>是否有批量检测请求正在执行。</summary>
    private bool _isBusy;

    /// <summary>标识界面是否仍处于启动初始化同步阶段，防止控件事件误触发写盘。</summary>
    private bool _isInitializing = true;

    /// <summary>快捷键上下跳转风险段落时的游标索引。</summary>
    private int _currentRiskCursor = -1;

    /// <summary>当前检测任务的取消令牌源。</summary>
    private CancellationTokenSource? _auditCts;

    /// <summary>顶部轻提示自动消失定时器的取消令牌源。</summary>
    private CancellationTokenSource? _toastCts;

    /// <summary>
    /// 全局本地化字符串入口（基于 WinUI 3 MRT Core 按系统语言自动解析）。
    /// </summary>
    public LocalizationService Strings => LocalizationService.Current;

    /// <summary>
    /// 全文检测汇总数据模型，供右侧诊断面板绑定。
    /// </summary>
    public AuditSummary Summary { get; } = new();

    /// <summary>
    /// 自适应字重配置对象，在暗色模式下自动将全站字重下调一档。
    /// </summary>
    public AdaptiveTypography TypeScale { get; } = new();

    /// <summary>
    /// 初始化主窗口、绑定集合视图并挂载首次激活加载事件。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Title = Strings.AppTitle;
        ConfigureWindowChrome();

        ParagraphRepeater.ItemsSource = _filteredParagraphs;
        HeatmapRepeater.ItemsSource = _allParagraphs;
        RiskListRepeater.ItemsSource = _riskParagraphs;

        Activated += OnFirstActivated;
    }

    /// <summary>
    /// 配置无边界自定义标题栏与默认舒适窗口尺寸。
    /// </summary>
    private void ConfigureWindowChrome()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        try
        {
            AppWindow.Resize(new SizeInt32(1280, 820));
        }
        catch
        {
            // 在低分辨率或受限显示器上忽略尺寸调整异常
        }
    }

    /// <summary>
    /// 窗口首次激活时异步加载本地配置并应用主题、开关等偏好设置。
    /// </summary>
    /// <param name="sender">窗口对象。</param>
    /// <param name="args">窗口激活事件参数。</param>
    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;

        _settings = await _settingsService.LoadAsync();
        ApplySettingsToUi();
        _isInitializing = false;
    }

    /// <summary>
    /// 将当前配置同步至主题、开关及所有段落实例。
    /// </summary>
    private void ApplySettingsToUi()
    {
        _isInitializing = true;

        ApplyTheme(_settings.ThemeMode);
        FullMergeToggleSwitch.IsOn = _settings.EnableFullTextMergeCheck;

        foreach (var item in _allParagraphs)
        {
            item.ShowHeatmapTint = _settings.ShowHeatmapTint;
        }

        _isInitializing = false;
    }

    /// <summary>
    /// 按用户选择的主题模式切换根容器外观并刷新动态色阶与字重。
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
    /// 响应系统或应用实际主题变更事件，实时刷新色阶与暗色字重。
    /// </summary>
    private void OnRootActualThemeChanged(FrameworkElement sender, object args)
    {
        SyncThemeTokens();
    }

    /// <summary>
    /// 同步当前深浅色标志到语义调色板、自适应字重以及所有活动模型实例。
    /// </summary>
    private void SyncThemeTokens()
    {
        bool isDark = RootGrid.ActualTheme == ElementTheme.Dark;
        SemanticPalette.IsDark = isDark;
        TypeScale.IsDark = isDark;
        Summary.RefreshThemeBrushes();
        foreach (var item in _allParagraphs)
        {
            item.RefreshThemeBrushes();
        }
        UpdateKpiFilterStates();
    }

    /// <summary>
    /// 响应右侧“合并复核”开关切换并即时持久化至本地配置。
    /// </summary>
    private async void OnFullMergeToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || FullMergeToggleSwitch is null)
        {
            return;
        }

        _settings.EnableFullTextMergeCheck = FullMergeToggleSwitch.IsOn;
        await _settingsService.SaveAsync(_settings);
    }

    // ==================== 视图状态管理 ====================

    /// <summary>
    /// 根据当前章节是否存在内容及是否处于整章编辑态，切换左侧主画布显示状态与按钮可用性。
    /// </summary>
    private void UpdateCanvasState()
    {
        bool hasChapter = _allParagraphs.Count > 0;

        EmptyCanvasPanel.Visibility = (!hasChapter && !_isBulkEditing) ? Visibility.Visible : Visibility.Collapsed;
        ManuscriptSheetContainer.Visibility = (hasChapter && !_isBulkEditing) ? Visibility.Visible : Visibility.Collapsed;
        BulkEditorContainer.Visibility = _isBulkEditing ? Visibility.Visible : Visibility.Collapsed;
        DistributionCard.Visibility = hasChapter ? Visibility.Visible : Visibility.Collapsed;

        BulkEditButton.IsEnabled = hasChapter && !_isBusy;
        CopyAllButton.IsEnabled = hasChapter;
        ClearAllButton.IsEnabled = hasChapter && !_isBusy;
    }

    /// <summary>
    /// 监听空态输入框变化，在有内容时显示直接检测按钮。
    /// </summary>
    private void OnWelcomePasteBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        WelcomeStartButton.Visibility = string.IsNullOrWhiteSpace(WelcomePasteBox.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>
    /// 提交空态输入框正文并立即启动审校检测。
    /// </summary>
    private async void OnCommitWelcomeAndAuditClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(WelcomePasteBox.Text))
        {
            return;
        }

        LoadChapterText(WelcomePasteBox.Text);
        await RunChapterAuditAsync(mergedOnly: false);
    }

    /// <summary>
    /// 一键读取剪贴板覆盖正文并立即执行审校检测。
    /// </summary>
    private async void OnPasteAndAuditClick(object sender, RoutedEventArgs e)
    {
        var pasted = await TryReadClipboardTextAsync();
        if (string.IsNullOrWhiteSpace(pasted))
        {
            return;
        }

        LoadChapterText(pasted);
        HideErrorBar();
        await RunChapterAuditAsync(mergedOnly: false);
    }

    // ==================== 整章无损编辑 ====================

    /// <summary>
    /// 打开整章纯文本编辑覆盖层以便增删或合段。
    /// </summary>
    private void OnOpenBulkEditorClick(object sender, RoutedEventArgs e)
    {
        if (_allParagraphs.Count == 0 || _isBusy)
        {
            return;
        }

        CommitAnyOpenInlineEdits();
        BulkRawTextBox.Text = ParagraphMapper.JoinParagraphs(_allParagraphs);
        _isBulkEditing = true;
        UpdateCanvasState();
    }

    /// <summary>
    /// 取消整章编辑并返回段落审校视图，不丢失任何检测结果。
    /// </summary>
    private void OnCancelBulkEditClick(object sender, RoutedEventArgs e)
    {
        _isBulkEditing = false;
        UpdateCanvasState();
    }

    /// <summary>
    /// 应用整章编辑改动，并智能保留未改动段落的原检测结果。
    /// </summary>
    private void OnApplyBulkEditClick(object sender, RoutedEventArgs e)
    {
        _isBulkEditing = false;
        SyncParagraphsFromRawTextPreservingAudit(BulkRawTextBox.Text);
        UpdateCanvasState();
    }

    /// <summary>
    /// 将整章文本重新切段并与旧段落逐一比对，按正文内容复用未变动段落的检测结果。
    /// </summary>
    /// <param name="rawText">编辑后的完整章节文本。</param>
    private void SyncParagraphsFromRawTextPreservingAudit(string rawText)
    {
        var parsedItems = ParagraphMapper.SplitIntoParagraphs(rawText, _settings.ShowHeatmapTint);

        // 若段落内容序列完全未变，则直接返回以保留全部状态
        if (parsedItems.Count == _allParagraphs.Count)
        {
            bool identical = true;
            for (int i = 0; i < parsedItems.Count; i++)
            {
                if (!string.Equals(parsedItems[i].Text, _allParagraphs[i].Text, StringComparison.Ordinal))
                {
                    identical = false;
                    break;
                }
            }

            if (identical)
            {
                return;
            }
        }

        // 按文本构建先进先出队列，兼容同章出现相同短对话段的无损复用
        var existingByText = new Dictionary<string, Queue<ParagraphItem>>(StringComparer.Ordinal);
        foreach (var existing in _allParagraphs)
        {
            if (!existingByText.TryGetValue(existing.Text, out var queue))
            {
                queue = new Queue<ParagraphItem>();
                existingByText[existing.Text] = queue;
            }
            queue.Enqueue(existing);
        }

        var mergedList = new List<ParagraphItem>(parsedItems.Count);
        int idx = 1;
        foreach (var fresh in parsedItems)
        {
            if (existingByText.TryGetValue(fresh.Text, out var queue) && queue.Count > 0)
            {
                var reused = queue.Dequeue();
                reused.Index = idx++;
                reused.ShowHeatmapTint = _settings.ShowHeatmapTint;
                reused.IsSelected = false;
                reused.IsEditing = false;
                mergedList.Add(reused);
            }
            else
            {
                fresh.Index = idx++;
                mergedList.Add(fresh);
            }
        }

        _allParagraphs.Clear();
        foreach (var item in mergedList)
        {
            _allParagraphs.Add(item);
        }

        if (_allParagraphs.Count == 0)
        {
            Summary.Reset();
            WelcomePasteBox.Text = string.Empty;
        }
        else if (Summary.HasResult)
        {
            Summary.IsStale = true;
        }

        _currentRiskCursor = -1;
        RefreshStatsAndFilters();
    }

    // ==================== 剪贴板、文件导入与清空 ====================

    /// <summary>
    /// 尝试从系统剪贴板读取纯文本，失败或无内容时弹出必要校验错误提示。
    /// </summary>
    /// <returns>读取到的非空文本，失败时返回 null。</returns>
    private async Task<string?> TryReadClipboardTextAsync()
    {
        try
        {
            var dataPackageView = Clipboard.GetContent();
            if (!dataPackageView.Contains(StandardDataFormats.Text))
            {
                ShowErrorBar(Strings.ErrClipboardNoText);
                return null;
            }

            var text = await dataPackageView.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowErrorBar(Strings.ErrClipboardEmpty);
                return null;
            }

            return text;
        }
        catch (Exception ex)
        {
            ShowErrorBar(ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 读取剪贴板文本覆盖当前章节。
    /// </summary>
    private async void OnPasteOverwriteClick(object sender, RoutedEventArgs e)
    {
        var text = await TryReadClipboardTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        LoadChapterText(text);
        HideErrorBar();
    }

    /// <summary>
    /// 打开系统文件选择器导入本地 .txt 或 .md 小说章节。
    /// </summary>
    private async void OnImportFileClick(object sender, RoutedEventArgs e)
    {
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
            LoadChapterText(text);
            HideErrorBar();
        }
        catch (Exception ex)
        {
            ShowErrorBar(ex.Message);
        }
    }

    /// <summary>
    /// 处理拖拽文件悬停事件，允许复制文件进入主工作区。
    /// </summary>
    private void OnEditorDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    /// <summary>
    /// 处理松开拖入的 .txt / .md 文件并载入章节内容。
    /// </summary>
    private async void OnEditorDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
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
                ShowErrorBar(Strings.ErrUnsupportedFile);
                return;
            }

            var text = await FileIO.ReadTextAsync(file);
            LoadChapterText(text);
            HideErrorBar();
        }
        catch (Exception ex)
        {
            ShowErrorBar(ex.Message);
        }
    }

    /// <summary>
    /// 复制当前全部段落（含已改写内容）到剪贴板并播放弹性完成反馈。
    /// </summary>
    private async void OnCopyAllClick(object sender, RoutedEventArgs e)
    {
        if (_allParagraphs.Count == 0)
        {
            return;
        }

        CommitAnyOpenInlineEdits();
        CopyTextToClipboard(ParagraphMapper.JoinParagraphs(_allParagraphs));
        PlayUnderdampedBounce(CopyCheckScale);
        await ShowBouncyToastAsync(Strings.ToastCopiedAll);
    }

    /// <summary>
    /// 清空当前章节正文、检测结果并中止正在进行的请求。
    /// </summary>
    private void OnClearAllClick(object sender, RoutedEventArgs e)
    {
        _auditCts?.Cancel();
        _isBulkEditing = false;
        WelcomePasteBox.Text = string.Empty;
        _allParagraphs.Clear();
        Summary.Reset();
        _activeFilter = ParagraphFilterKind.All;
        _currentRiskCursor = -1;
        RefreshStatsAndFilters();
        UpdateCanvasState();
        HideErrorBar();
    }

    /// <summary>
    /// 使用全新章节文本替换当前工作区内容并重置统计状态。
    /// </summary>
    /// <param name="text">新导入或粘贴的整章原文。</param>
    private void LoadChapterText(string text)
    {
        _isBulkEditing = false;
        _activeFilter = ParagraphFilterKind.All;

        var items = ParagraphMapper.SplitIntoParagraphs(text, _settings.ShowHeatmapTint);
        _allParagraphs.Clear();
        foreach (var item in items)
        {
            _allParagraphs.Add(item);
        }

        Summary.Reset();
        _currentRiskCursor = -1;
        RefreshStatsAndFilters();
        UpdateCanvasState();
    }

    // ==================== 朱雀检测流程 ====================

    /// <summary>
    /// 主按钮点击回调：提交当前输入并执行逐段检测（若“合并复核”开启则并发全文复核）。
    /// </summary>
    private async void OnStartAuditClick(object sender, RoutedEventArgs e)
    {
        if (_allParagraphs.Count == 0 && !string.IsNullOrWhiteSpace(WelcomePasteBox.Text))
        {
            LoadChapterText(WelcomePasteBox.Text);
        }

        if (_isBulkEditing)
        {
            _isBulkEditing = false;
            SyncParagraphsFromRawTextPreservingAudit(BulkRawTextBox.Text);
            UpdateCanvasState();
        }

        await RunChapterAuditAsync(mergedOnly: false);
    }

    /// <summary>
    /// 下拉菜单回调：仅运行一次全文合并复核（is_merge: true）。
    /// </summary>
    private async void OnRunMergedOnlyClick(object sender, RoutedEventArgs e)
    {
        if (_allParagraphs.Count == 0 && !string.IsNullOrWhiteSpace(WelcomePasteBox.Text))
        {
            LoadChapterText(WelcomePasteBox.Text);
        }

        await RunChapterAuditAsync(mergedOnly: true);
    }

    /// <summary>
    /// 执行章节检测核心调度流程：支持仅合并复核、仅逐段检测以及逐段+合并并发检测。
    /// </summary>
    /// <param name="mergedOnly">为 true 时仅调用一次 is_merge: true 刷新全文汇总。</param>
    private async Task RunChapterAuditAsync(bool mergedOnly)
    {
        if (_isBusy)
        {
            return;
        }

        if (_allParagraphs.Count == 0)
        {
            ShowErrorBar(Strings.ErrEmptyText);
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            ShowErrorBar(Strings.ErrMissingApiKey, showSettingsAction: true);
            return;
        }

        CommitAnyOpenInlineEdits();
        HideErrorBar();

        _auditCts?.Cancel();
        _auditCts?.Dispose();
        _auditCts = new CancellationTokenSource();
        var ct = _auditCts.Token;

        bool runMergeCheck = mergedOnly || FullMergeToggleSwitch.IsOn;
        double? previousAiRatio = Summary.HasResult ? Summary.AiRatio : null;

        SetBusy(true);

        try
        {
            int totalTokens = 0;

            if (mergedOnly)
            {
                var fullText = ParagraphMapper.JoinParagraphsForApi(_allParagraphs);
                var mergedResp = await _apiService.ClassifyAsync(
                    fullText,
                    isMerge: true,
                    apiKey: _settings.ApiKey,
                    endpointUrl: _settings.EndpointUrl,
                    cancellationToken: ct);

                totalTokens += mergedResp.MakersModelsUsage?.TotalTokens ?? 0;
                ApplySummaryFromResponse(mergedResp, true, null, previousAiRatio, totalTokens);
                await ShowBouncyToastAsync(Strings.ToastAuditDone);
                return;
            }

            // 按短段跳过策略切分送检段落与跳过段落
            var batch = ParagraphMapper.BuildBatchPayload(
                _allParagraphs,
                _settings.EnableShortParagraphSkip,
                _settings.ShortParagraphThreshold);

            foreach (var skipped in batch.SkippedItems)
            {
                skipped.Status = ParagraphAuditStatus.SkippedShort;
                skipped.Label = null;
                skipped.ErrorMessage = null;
                skipped.PreviousConfidence = null;
            }

            foreach (var span in batch.SentSpans)
            {
                span.Item.Status = ParagraphAuditStatus.Checking;
                span.Item.ErrorMessage = null;
            }

            RefreshStatsAndFilters();

            Task<ZhuqueClassifyResponse>? segmentTask = batch.SentSpans.Count > 0
                ? _apiService.ClassifyAsync(batch.Payload, false, _settings.ApiKey, _settings.EndpointUrl, ct)
                : null;

            Task<ZhuqueClassifyResponse>? mergeTask = runMergeCheck
                ? _apiService.ClassifyAsync(ParagraphMapper.JoinParagraphsForApi(_allParagraphs), true, _settings.ApiKey, _settings.EndpointUrl, ct)
                : null;

            ZhuqueClassifyResponse? segResponse = null;
            ZhuqueClassifyResponse? mergeResponse = null;
            Exception? segError = null;
            Exception? mergeError = null;

            // 当两个请求均开启时并发发出，并独立捕获异常以便单边失败时仍展示可用结果
            if (segmentTask is not null && mergeTask is not null)
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
            else if (segmentTask is not null)
            {
                segResponse = await segmentTask;
            }
            else if (mergeTask is not null)
            {
                mergeResponse = await mergeTask;
            }

            ct.ThrowIfCancellationRequested();

            if (segResponse is not null)
            {
                ParagraphMapper.ApplyBatchResponse(batch.SentSpans, segResponse);
                totalTokens += segResponse.MakersModelsUsage?.TotalTokens ?? 0;
            }
            else if (segError is not null)
            {
                foreach (var span in batch.SentSpans)
                {
                    span.Item.Status = ParagraphAuditStatus.Error;
                    span.Item.ErrorMessage = segError.Message;
                }
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

            RefreshStatsAndFilters();

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
                _ = ShowBouncyToastAsync(Strings.ToastAuditDone);
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var p in _allParagraphs.Where(p => p.Status == ParagraphAuditStatus.Checking))
            {
                p.Status = ParagraphAuditStatus.Pending;
            }
            RefreshStatsAndFilters();
        }
        catch (Exception ex)
        {
            foreach (var p in _allParagraphs.Where(p => p.Status == ParagraphAuditStatus.Checking))
            {
                p.Status = ParagraphAuditStatus.Error;
                p.ErrorMessage = ex.Message;
            }
            RefreshStatsAndFilters();
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
    /// <param name="primaryResponse">作为主结论展示的接口响应。</param>
    /// <param name="isMergedSource">主结论是否来自 is_merge: true。</param>
    /// <param name="segmentComparisonResponse">可选的 is_merge: false 对照响应。</param>
    /// <param name="previousAiRatio">上一次检测的 AI 占比，用于计算降幅。</param>
    /// <param name="totalTokensUsed">本次检测实际扣减的 Makers Token 总数。</param>
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

    /// <summary>
    /// 逐条重检所有处于“已改”状态的段落，节省整章重复送检的 Token 消耗。
    /// </summary>
    private async void OnRecheckModifiedClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var modifiedList = _allParagraphs.Where(p => p.Status == ParagraphAuditStatus.Modified).ToList();
        if (modifiedList.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            ShowErrorBar(Strings.ErrMissingApiKey, showSettingsAction: true);
            return;
        }

        _auditCts?.Cancel();
        _auditCts?.Dispose();
        _auditCts = new CancellationTokenSource();
        var ct = _auditCts.Token;

        SetBusy(true);
        int tokensUsed = 0;

        try
        {
            foreach (var item in modifiedList)
            {
                ct.ThrowIfCancellationRequested();
                item.Status = ParagraphAuditStatus.Checking;

                try
                {
                    var resp = await _apiService.ClassifyAsync(item.Text, false, _settings.ApiKey, _settings.EndpointUrl, ct);
                    tokensUsed += resp.MakersModelsUsage?.TotalTokens ?? 0;
                    ParagraphMapper.ApplySingleResponse(item, resp);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    item.Status = ParagraphAuditStatus.Error;
                    item.ErrorMessage = ex.Message;
                }

                RefreshStatsAndFilters();
            }

            if (tokensUsed > 0)
            {
                Summary.LastTokensUsed = tokensUsed;
            }
            _ = ShowBouncyToastAsync(Strings.ToastRecheckDone);
        }
        catch (OperationCanceledException)
        {
            foreach (var item in modifiedList.Where(p => p.Status == ParagraphAuditStatus.Checking))
            {
                item.Status = ParagraphAuditStatus.Modified;
            }
            RefreshStatsAndFilters();
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// 取消当前正在进行的检测请求。
    /// </summary>
    private void OnCancelAuditClick(object sender, RoutedEventArgs e)
    {
        _auditCts?.Cancel();
    }

    // ==================== 段落聚焦、原地改写与单段重检 ====================

    /// <summary>
    /// 单击段落行时设为当前聚焦段并展开行内快捷操作条。
    /// </summary>
    private void OnParagraphRowTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ParagraphItem item })
        {
            SelectParagraph(item);
        }
    }

    /// <summary>
    /// 将目标段落标记为唯一选中态。
    /// </summary>
    /// <param name="target">目标段落，传 null 表示清除选中。</param>
    private void SelectParagraph(ParagraphItem? target)
    {
        foreach (var p in _allParagraphs)
        {
            p.IsSelected = ReferenceEquals(p, target);
        }
    }

    /// <summary>
    /// 双击段落正文直接进入原地编辑模式。
    /// </summary>
    private void OnParagraphDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ParagraphItem item })
        {
            e.Handled = true;
            BeginEditParagraph(item);
        }
    }

    /// <summary>
    /// 点击行内“编辑”按钮进入原地编辑模式。
    /// </summary>
    private void OnEditParagraphClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ParagraphItem item })
        {
            BeginEditParagraph(item);
        }
    }

    /// <summary>
    /// 将指定段落切换为原地编辑态并初始化草稿文本。
    /// </summary>
    /// <param name="item">目标段落。</param>
    private void BeginEditParagraph(ParagraphItem item)
    {
        if (_isBusy)
        {
            return;
        }

        SelectParagraph(item);
        item.EditDraft = item.Text;
        item.IsEditing = true;
    }

    /// <summary>
    /// 提交所有尚未关闭的段落原地编辑框。
    /// </summary>
    private void CommitAnyOpenInlineEdits()
    {
        foreach (var p in _allParagraphs.Where(p => p.IsEditing))
        {
            CommitParagraphEdit(p);
        }
    }

    /// <summary>
    /// 放弃当前段落编辑草稿并退出编辑态。
    /// </summary>
    private void OnCancelEditParagraphClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ParagraphItem item })
        {
            item.EditDraft = item.Text;
            item.IsEditing = false;
        }
    }

    /// <summary>
    /// 保存当前段落修改但不立即请求接口。
    /// </summary>
    private void OnSaveEditParagraphClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ParagraphItem item })
        {
            CommitParagraphEdit(item);
        }
    }

    /// <summary>
    /// 保存当前段落修改并立即发起单段重检。
    /// </summary>
    private async void OnSaveAndRecheckParagraphClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ParagraphItem item })
        {
            CommitParagraphEdit(item);
            await RecheckSingleParagraphAsync(item);
        }
    }

    /// <summary>
    /// 将草稿文本写入段落正文并在内容变动时标记为“已改”。
    /// </summary>
    /// <param name="item">待提交的段落对象。</param>
    private void CommitParagraphEdit(ParagraphItem item)
    {
        var updated = item.EditDraft.Trim();
        item.IsEditing = false;

        if (string.IsNullOrWhiteSpace(updated) || updated == item.Text)
        {
            return;
        }

        item.Text = updated;
        item.Status = ParagraphAuditStatus.Modified;
        if (Summary.HasResult)
        {
            Summary.IsStale = true;
        }

        RefreshStatsAndFilters();
    }

    /// <summary>
    /// 点击行内“重检 / 检测 / 重试”按钮触发单段独立检测。
    /// </summary>
    private async void OnRecheckSingleParagraphClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ParagraphItem item })
        {
            await RecheckSingleParagraphAsync(item);
        }
    }

    /// <summary>
    /// 单独将某一段落文本送往朱雀接口检测，并记录前后分数对比。
    /// </summary>
    /// <param name="item">待检测段落。</param>
    private async Task RecheckSingleParagraphAsync(ParagraphItem item)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            ShowErrorBar(Strings.ErrMissingApiKey, showSettingsAction: true);
            return;
        }

        if (item.Status == ParagraphAuditStatus.Checking)
        {
            return;
        }

        item.Status = ParagraphAuditStatus.Checking;
        item.ErrorMessage = null;

        try
        {
            var resp = await _apiService.ClassifyAsync(item.Text, false, _settings.ApiKey, _settings.EndpointUrl);
            ParagraphMapper.ApplySingleResponse(item, resp);

            var tokens = resp.MakersModelsUsage?.TotalTokens ?? 0;
            if (tokens > 0)
            {
                Summary.LastTokensUsed = tokens;
            }

            if (Summary.HasResult)
            {
                Summary.IsStale = true;
            }

            RefreshStatsAndFilters();
        }
        catch (Exception ex)
        {
            item.Status = ParagraphAuditStatus.Error;
            item.ErrorMessage = ex.Message;
            RefreshStatsAndFilters();
        }
    }

    /// <summary>
    /// 复制单个段落正文到剪贴板并弹出 2.5s 自动消失轻提示。
    /// </summary>
    private async void OnCopySingleParagraphClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ParagraphItem item })
        {
            CopyTextToClipboard(item.Text);
            await ShowBouncyToastAsync(Strings.FormatCopiedParagraph(item.IndexDisplay));
        }
    }

    // ==================== 数据筛选与热力图快速定位 ====================

    /// <summary>切换“人工”段落筛选。</summary>
    private void OnToggleHumanFilterClick(object sender, RoutedEventArgs e) => ToggleFilter(ParagraphFilterKind.Human);

    /// <summary>切换“疑似”段落筛选。</summary>
    private void OnToggleSuspectedFilterClick(object sender, RoutedEventArgs e) => ToggleFilter(ParagraphFilterKind.Suspected);

    /// <summary>切换“AI”段落筛选。</summary>
    private void OnToggleAiFilterClick(object sender, RoutedEventArgs e) => ToggleFilter(ParagraphFilterKind.Ai);

    /// <summary>切换“跳过”段落筛选。</summary>
    private void OnToggleSkippedFilterClick(object sender, RoutedEventArgs e) => ToggleFilter(ParagraphFilterKind.Skipped);

    /// <summary>切换“已改”段落筛选。</summary>
    private void OnToggleModifiedFilterClick(object sender, RoutedEventArgs e) => ToggleFilter(ParagraphFilterKind.Modified);

    /// <summary>
    /// 切换指定筛选维度（再次点击同一维度则取消筛选恢复全部）。
    /// </summary>
    /// <param name="kind">目标筛选维度。</param>
    private void ToggleFilter(ParagraphFilterKind kind)
    {
        _activeFilter = _activeFilter == kind ? ParagraphFilterKind.All : kind;
        ApplyFilter();
    }

    /// <summary>
    /// 清除当前筛选条件，恢复展示全部段落。
    /// </summary>
    private void OnResetFilterClick(object sender, RoutedEventArgs e)
    {
        _activeFilter = ParagraphFilterKind.All;
        ApplyFilter();
    }

    /// <summary>
    /// 重新计算全章字数、跳过/已改统计及高风险速览列表，并同步刷新筛选结果。
    /// </summary>
    private void RefreshStatsAndFilters()
    {
        int totalChars = _allParagraphs.Sum(p => p.CharCount);
        int totalCount = _allParagraphs.Count;
        int skippedCount = _allParagraphs.Count(p => p.Status == ParagraphAuditStatus.SkippedShort);
        int modifiedCount = _allParagraphs.Count(p => p.Status == ParagraphAuditStatus.Modified);

        if (totalCount == 0)
        {
            ChapterStatsText.Visibility = Visibility.Collapsed;
        }
        else
        {
            ChapterStatsText.Visibility = Visibility.Visible;
            ChapterStatsText.Text = Strings.FormatChapterStats(totalChars, totalCount);
        }

        SkippedFilterLink.Content = $"{Strings.LabelSkipped} {skippedCount}";
        SkippedFilterLink.Visibility = skippedCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        ModifiedFilterLink.Content = $"{Strings.LabelModified} {modifiedCount}";
        ModifiedFilterLink.Visibility = modifiedCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        _riskParagraphs.Clear();
        foreach (var item in _allParagraphs.Where(p => p.IsRisky))
        {
            _riskParagraphs.Add(item);
        }

        RiskListRepeater.Visibility = _riskParagraphs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PrevRiskButton.IsEnabled = _riskParagraphs.Count > 0;
        NextRiskButton.IsEnabled = _riskParagraphs.Count > 0;

        ApplyFilter();
    }

    /// <summary>
    /// 按当前激活的筛选维度重建左侧展示列表，并更新筛选提示栏与右侧瓷砖激活态。
    /// </summary>
    private void ApplyFilter()
    {
        _filteredParagraphs.Clear();

        foreach (var item in _allParagraphs)
        {
            bool match = _activeFilter switch
            {
                ParagraphFilterKind.All => true,
                ParagraphFilterKind.Ai => item.Status is ParagraphAuditStatus.Audited or ParagraphAuditStatus.Modified && item.Label == 1,
                ParagraphFilterKind.Suspected => item.Status is ParagraphAuditStatus.Audited or ParagraphAuditStatus.Modified && item.Label == 2,
                ParagraphFilterKind.Human => item.Status is ParagraphAuditStatus.Audited or ParagraphAuditStatus.Modified && item.Label == 0,
                ParagraphFilterKind.Skipped => item.Status == ParagraphAuditStatus.SkippedShort,
                ParagraphFilterKind.Modified => item.Status == ParagraphAuditStatus.Modified,
                _ => true
            };

            if (match)
            {
                _filteredParagraphs.Add(item);
            }
        }

        if (_activeFilter == ParagraphFilterKind.All)
        {
            ActiveFilterBanner.Visibility = Visibility.Collapsed;
        }
        else
        {
            ActiveFilterBanner.Visibility = Visibility.Visible;
            var label = _activeFilter switch
            {
                ParagraphFilterKind.Ai => Strings.LabelAi,
                ParagraphFilterKind.Suspected => Strings.LabelSuspected,
                ParagraphFilterKind.Human => Strings.LabelHuman,
                ParagraphFilterKind.Skipped => Strings.LabelSkipped,
                ParagraphFilterKind.Modified => Strings.LabelModified,
                _ => string.Empty
            };
            ActiveFilterLabel.Text = $"{label} · {_filteredParagraphs.Count}";
        }

        UpdateKpiFilterStates();
    }

    /// <summary>
    /// 通过微明度色阶（Surface 2 ↔ Surface 3）指示右侧三个占比瓷砖的筛选激活状态。
    /// </summary>
    private void UpdateKpiFilterStates()
    {
        if (HumanKpiBorder is null || SuspectedKpiBorder is null || AiKpiBorder is null)
        {
            return;
        }

        HumanKpiBorder.Background = _activeFilter == ParagraphFilterKind.Human
            ? SemanticPalette.ActiveKpiSurface
            : SemanticPalette.DefaultKpiSurface;
        SuspectedKpiBorder.Background = _activeFilter == ParagraphFilterKind.Suspected
            ? SemanticPalette.ActiveKpiSurface
            : SemanticPalette.DefaultKpiSurface;
        AiKpiBorder.Background = _activeFilter == ParagraphFilterKind.Ai
            ? SemanticPalette.ActiveKpiSurface
            : SemanticPalette.DefaultKpiSurface;
    }

    /// <summary>
    /// 点击热力图色块或风险速览项时，平滑滚动定位至目标段落。
    /// </summary>
    private async void OnHeatmapTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ParagraphItem item })
        {
            await JumpToParagraphAsync(item);
        }
    }

    /// <summary>
    /// 跳转至上一处风险段落（支持 Alt+↑ 快捷键）。
    /// </summary>
    private async void OnPreviousRiskClick(object sender, RoutedEventArgs e)
    {
        if (_riskParagraphs.Count == 0)
        {
            return;
        }

        _currentRiskCursor = _currentRiskCursor <= 0 ? _riskParagraphs.Count - 1 : _currentRiskCursor - 1;
        await JumpToParagraphAsync(_riskParagraphs[_currentRiskCursor]);
    }

    /// <summary>
    /// 跳转至下一处风险段落（支持 Alt+↓ 快捷键）。
    /// </summary>
    private async void OnNextRiskClick(object sender, RoutedEventArgs e)
    {
        if (_riskParagraphs.Count == 0)
        {
            return;
        }

        _currentRiskCursor = (_currentRiskCursor + 1) % _riskParagraphs.Count;
        await JumpToParagraphAsync(_riskParagraphs[_currentRiskCursor]);
    }

    /// <summary>
    /// 平滑滚动左侧视图至指定段落，自动聚焦并短时高亮背景。
    /// </summary>
    /// <param name="target">需要定位的目标段落。</param>
    private async Task JumpToParagraphAsync(ParagraphItem target)
    {
        if (_isBulkEditing)
        {
            _isBulkEditing = false;
            UpdateCanvasState();
        }

        if (!_filteredParagraphs.Contains(target))
        {
            _activeFilter = ParagraphFilterKind.All;
            ApplyFilter();
        }

        SelectParagraph(target);

        int idx = _filteredParagraphs.IndexOf(target);
        if (idx >= 0)
        {
            ParagraphRepeater.UpdateLayout();
            var element = ParagraphRepeater.GetOrCreateElement(idx);
            element?.StartBringIntoView(new BringIntoViewOptions
            {
                AnimationDesired = true,
                VerticalAlignmentRatio = 0.25
            });
        }

        foreach (var p in _allParagraphs)
        {
            p.IsHighlighted = ReferenceEquals(p, target);
        }

        await Task.Delay(1400);
        target.IsHighlighted = false;
    }

    // ==================== 设置弹窗 ====================

    /// <summary>
    /// 打开应用设置对话框，并在用户保存后持久化与应用新配置。
    /// </summary>
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
            await ShowBouncyToastAsync(Strings.ToastSaved);
        }
    }

    // ==================== 欠阻尼弹性反馈动效 ====================

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
    /// 展示带弹性过冲动效的完成提示胶囊，并在 2.5 秒后自动淡出隐藏。
    /// </summary>
    /// <param name="text">提示文本。</param>
    private async Task ShowBouncyToastAsync(string text)
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

    // ==================== 辅助方法 ====================

    /// <summary>
    /// 切换界面请求执行中的忙碌状态与按钮锁。
    /// </summary>
    /// <param name="isBusy">是否处于检测请求执行中。</param>
    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        BusyStatusPanel.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        BusyRing.IsActive = isBusy;
        PrimaryAuditButton.IsEnabled = !isBusy;
        AuditMoreButton.IsEnabled = !isBusy;
        UpdateCanvasState();
    }

    /// <summary>
    /// 显示必要的校验或接口错误提示条。
    /// </summary>
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

    /// <summary>
    /// 关闭顶部错误提示条。
    /// </summary>
    private void HideErrorBar()
    {
        MainInfoBar.IsOpen = false;
    }

    /// <summary>
    /// 将指定字符串写入 Windows 系统剪贴板。
    /// </summary>
    /// <param name="text">待写入剪贴板的文本。</param>
    private static void CopyTextToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
