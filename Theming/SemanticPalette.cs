using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace InkAudit.Theming;

/// <summary>
/// 语义化动态调色板，根据浅色/暗色外观提供克制的中性 Surface 色阶与三类检测结果语义画刷。
/// 禁用纯黑 #000000，统一以近黑 #1A1A19 作为深色基准。
/// </summary>
public static class SemanticPalette
{
    private static bool _isDark;

    /// <summary>
    /// 指示当前界面是否为暗色模式，用于动态切换语义画刷与字重。
    /// </summary>
    public static bool IsDark
    {
        get => _isDark;
        set => _isDark = value;
    }

    /// <summary>人工特征强调前景色。</summary>
    public static SolidColorBrush HumanAccent => new(_isDark
        ? Color.FromArgb(255, 74, 222, 128)
        : Color.FromArgb(255, 21, 128, 61));

    /// <summary>人工特征左侧柔和标识竖条色。</summary>
    public static SolidColorBrush HumanQuietStrip => new(_isDark
        ? Color.FromArgb(95, 74, 222, 128)
        : Color.FromArgb(90, 21, 128, 61));

    /// <summary>人工特征徽章柔和底色。</summary>
    public static SolidColorBrush HumanBadgeBg => new(_isDark
        ? Color.FromArgb(28, 74, 222, 128)
        : Color.FromArgb(18, 21, 128, 61));

    /// <summary>疑似 AI 强调前景色。</summary>
    public static SolidColorBrush SuspectedAccent => new(_isDark
        ? Color.FromArgb(255, 251, 191, 36)
        : Color.FromArgb(255, 180, 83, 9));

    /// <summary>疑似 AI 徽章柔和底色。</summary>
    public static SolidColorBrush SuspectedBadgeBg => new(_isDark
        ? Color.FromArgb(30, 251, 191, 36)
        : Color.FromArgb(20, 217, 119, 6));

    /// <summary>疑似 AI 段落极淡背景着色。</summary>
    public static SolidColorBrush SuspectedRowTint => new(_isDark
        ? Color.FromArgb(16, 251, 191, 36)
        : Color.FromArgb(13, 217, 119, 6));

    /// <summary>AI 特征强调前景色。</summary>
    public static SolidColorBrush AiAccent => new(_isDark
        ? Color.FromArgb(255, 248, 113, 113)
        : Color.FromArgb(255, 220, 38, 38));

    /// <summary>AI 特征徽章柔和底色。</summary>
    public static SolidColorBrush AiBadgeBg => new(_isDark
        ? Color.FromArgb(32, 248, 113, 113)
        : Color.FromArgb(18, 220, 38, 38));

    /// <summary>AI 特征段落极淡背景着色。</summary>
    public static SolidColorBrush AiRowTint => new(_isDark
        ? Color.FromArgb(18, 248, 113, 113)
        : Color.FromArgb(13, 220, 38, 38));

    /// <summary>已修改待重检状态提示色。</summary>
    public static SolidColorBrush ModifiedAccent => new(_isDark
        ? Color.FromArgb(255, 96, 165, 250)
        : Color.FromArgb(255, 37, 99, 235));

    /// <summary>中性次级强调色。</summary>
    public static SolidColorBrush NeutralAccent => new(_isDark
        ? Color.FromArgb(165, 163, 163, 158)
        : Color.FromArgb(180, 140, 140, 135));

    /// <summary>未检测或短段跳过状态的左侧淡竖条色。</summary>
    public static SolidColorBrush NeutralQuietStrip => new(_isDark
        ? Color.FromArgb(55, 163, 163, 158)
        : Color.FromArgb(55, 140, 140, 135));

    /// <summary>热力图未检测方块的背景色。</summary>
    public static SolidColorBrush NeutralTileBg => new(_isDark
        ? Color.FromArgb(255, 39, 39, 36)
        : Color.FromArgb(255, 232, 232, 229));

    /// <summary>完全透明画刷。</summary>
    public static SolidColorBrush TransparentBrush => new(Color.FromArgb(0, 0, 0, 0));

    /// <summary>段落聚焦/选中时的微抬升色阶底色（替代刺眼边框）。</summary>
    public static SolidColorBrush FocusedRowSurface => new(_isDark
        ? Color.FromArgb(255, 34, 34, 31)
        : Color.FromArgb(255, 255, 255, 255));

    /// <summary>通过热力图快速定位跳转时的短时高亮色阶底色。</summary>
    public static SolidColorBrush HighlightedRowSurface => new(_isDark
        ? Color.FromArgb(255, 41, 41, 38)
        : Color.FromArgb(255, 236, 236, 233));

    /// <summary>右侧 KPI 筛选卡片处于激活态时的 Surface 3 底色。</summary>
    public static SolidColorBrush ActiveKpiSurface => new(_isDark
        ? Color.FromArgb(255, 42, 42, 39)
        : Color.FromArgb(255, 236, 236, 233));

    /// <summary>右侧 KPI 筛选卡片默认态的 Surface 2 底色。</summary>
    public static SolidColorBrush DefaultKpiSurface => new(_isDark
        ? Color.FromArgb(255, 32, 32, 30)
        : Color.FromArgb(255, 255, 255, 255));

    /// <summary>自适应正文字重（浅色 400 / 暗色 360）。</summary>
    public static FontWeight AdaptiveBodyWeight => new() { Weight = (ushort)(_isDark ? 360 : 400) };

    /// <summary>自适应中等强调字重（浅色 500 / 暗色 460）。</summary>
    public static FontWeight AdaptiveMediumWeight => new() { Weight = (ushort)(_isDark ? 460 : 500) };
}
