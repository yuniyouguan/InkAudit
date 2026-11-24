using Microsoft.UI.Xaml;

namespace InkAudit;

/// <summary>
/// 墨鉴（InkAudit）WinUI 3 应用程序入口类，负责初始化全局资源字典并启动主窗口。
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 应用主窗口实例引用。
    /// </summary>
    private Window? _window;

    /// <summary>
    /// 初始化应用程序组件并加载 App.xaml 中定义的主题与样式资源。
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 应用启动生命周期回调：实例化并激活主窗口。
    /// </summary>
    /// <param name="args">系统传入的启动激活参数。</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
