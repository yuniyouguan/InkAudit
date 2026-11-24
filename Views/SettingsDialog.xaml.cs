using InkAudit.Models;
using InkAudit.Services;
using InkAudit.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;

namespace InkAudit.Views;

/// <summary>
/// 应用设置对话框，提供 API Key 鉴权配置、连通性验证、全文复核、高亮开关与外观主题设置。
/// </summary>
public sealed partial class SettingsDialog : ContentDialog
{
    /// <summary>
    /// 用于执行连通性测试的朱雀接口服务实例。
    /// </summary>
    private readonly ZhuqueApiService _apiService;

    /// <summary>
    /// 控制连通性测试请求生命周期的取消令牌源。
    /// </summary>
    private CancellationTokenSource? _testCts;

    /// <summary>
    /// 全局本地化字符串源，供弹窗 XAML 绑定使用。
    /// </summary>
    public LocalizationService Strings => LocalizationService.Current;

    /// <summary>
    /// 用户点击“保存”后产出的最新配置副本。
    /// </summary>
    public AppSettings ResultSettings { get; private set; }

    /// <summary>
    /// 初始化设置对话框并回显当前已生效的应用配置。
    /// </summary>
    /// <param name="currentSettings">当前正在使用的配置对象。</param>
    /// <param name="apiService">朱雀接口客户端实例。</param>
    public SettingsDialog(AppSettings currentSettings, ZhuqueApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;
        ResultSettings = new AppSettings
        {
            ApiKey = currentSettings.ApiKey,
            EndpointUrl = currentSettings.EndpointUrl,
            EnableFullTextMergeCheck = currentSettings.EnableFullTextMergeCheck,
            ShowHeatmapTint = currentSettings.ShowHeatmapTint,
            ThemeMode = currentSettings.ThemeMode
        };

        ApiKeyBox.Password = ResultSettings.ApiKey;
        EndpointBox.Text = ResultSettings.EndpointUrl;
        FullMergeToggle.IsOn = ResultSettings.EnableFullTextMergeCheck;
        HeatmapTintSettingToggle.IsOn = ResultSettings.ShowHeatmapTint;

        ThemeComboBox.SelectedIndex = ResultSettings.ThemeMode switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };

        // 对话框关闭时中止尚未完成的测试请求
        Closed += (_, _) =>
        {
            _testCts?.Cancel();
            _testCts?.Dispose();
        };
    }

    /// <summary>
    /// 点击“测试”按钮时发起轻量探测请求以校验当前填写的 API Key 与网关地址。
    /// </summary>
    /// <param name="sender">测试按钮控件。</param>
    /// <param name="e">路由事件参数。</param>
    private async void OnTestConnectionClick(object sender, RoutedEventArgs e)
    {
        _testCts?.Cancel();
        _testCts?.Dispose();
        _testCts = new CancellationTokenSource();

        var key = ApiKeyBox.Password?.Trim() ?? string.Empty;
        var endpoint = EndpointBox.Text?.Trim() ?? AppSettings.DefaultEndpointUrl;

        TestConnectionButton.IsEnabled = false;
        TestProgressRing.Visibility = Visibility.Visible;
        TestProgressRing.IsActive = true;
        TestStatusText.Visibility = Visibility.Collapsed;

        try
        {
            var tokens = await _apiService.TestConnectionAsync(key, endpoint, _testCts.Token);
            TestStatusText.Text = Strings.FormatTestSuccess(tokens);
            TestStatusText.Foreground = SemanticPalette.HumanAccent;
            TestStatusText.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            // 对话框已关闭，忽略取消异常
        }
        catch (Exception ex)
        {
            TestStatusText.Text = ex.Message;
            TestStatusText.Foreground = SemanticPalette.AiAccent;
            TestStatusText.Visibility = Visibility.Visible;
        }
        finally
        {
            TestProgressRing.IsActive = false;
            TestProgressRing.Visibility = Visibility.Collapsed;
            TestConnectionButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 点击“保存”按钮时收集各表单控件的值并写入 <see cref="ResultSettings"/>。
    /// </summary>
    /// <param name="sender">当前对话框实例。</param>
    /// <param name="args">主按钮点击事件参数。</param>
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ResultSettings.ApiKey = ApiKeyBox.Password?.Trim() ?? string.Empty;
        ResultSettings.EndpointUrl = string.IsNullOrWhiteSpace(EndpointBox.Text)
            ? AppSettings.DefaultEndpointUrl
            : EndpointBox.Text.Trim();
        ResultSettings.EnableFullTextMergeCheck = FullMergeToggle.IsOn;
        ResultSettings.ShowHeatmapTint = HeatmapTintSettingToggle.IsOn;

        if (ThemeComboBox.SelectedItem is ComboBoxItem themeItem && themeItem.Tag is string mode)
        {
            ResultSettings.ThemeMode = mode;
        }
    }
}
