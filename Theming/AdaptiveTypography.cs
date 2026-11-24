using System.ComponentModel;
using Windows.UI.Text;

namespace InkAudit.Theming;

/// <summary>
/// 自适应字重配置：在暗色模式下自动将全站字重下调一档（400→360、500→460、600→540），
/// 避免高对比度深色背景上的文字产生光学膨胀。
/// </summary>
public sealed class AdaptiveTypography : INotifyPropertyChanged
{
    private bool _isDark;

    /// <summary>
    /// 字重属性变更事件，通知界面动态刷新字重绑定。
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 当前是否处于暗色外观模式。
    /// </summary>
    public bool IsDark
    {
        get => _isDark;
        set
        {
            if (_isDark == value)
            {
                return;
            }

            _isDark = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BodyWeight)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MediumWeight)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StrongWeight)));
        }
    }

    /// <summary>
    /// 正文常规字重（浅色 400 / 暗色 360）。
    /// </summary>
    public FontWeight BodyWeight => new() { Weight = (ushort)(_isDark ? 360 : 400) };

    /// <summary>
    /// 中等强调字重（浅色 500 / 暗色 460）。
    /// </summary>
    public FontWeight MediumWeight => new() { Weight = (ushort)(_isDark ? 460 : 500) };

    /// <summary>
    /// 标题与关键数值字重（浅色 600 / 暗色 540）。
    /// </summary>
    public FontWeight StrongWeight => new() { Weight = (ushort)(_isDark ? 540 : 600) };
}
