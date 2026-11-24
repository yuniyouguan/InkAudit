using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;
using Windows.UI.Text;

namespace InkAudit.Theming;

/// <summary>
/// 语义化动态调色板，提供暖灰中性色阶、鲜明红/黄/绿语义画刷，
/// 以及按朱雀 <c>conf</c> 确信强度连续插值的正文字符背景高亮色。
/// 禁用纯黑 #000000，统一以近黑 #1A1A19 作为深色基准。
/// </summary>
public static class SemanticPalette
{
    /// <summary>当前是否为暗色模式的内部标志。</summary>
    private static bool _isDark;

    /// <summary>
    /// 指示当前界面是否为暗色模式，用于动态切换语义画刷与字重。
    /// </summary>
    public static bool IsDark
    {
        get => _isDark;
        set => _isDark = value;
    }

    /// <summary>人工特征强调前景色（鲜明翠绿）。</summary>
    public static SolidColorBrush HumanAccent => new(_isDark
        ? Color.FromArgb(255, 34, 197, 94)
        : Color.FromArgb(255, 22, 163, 74));

    /// <summary>疑似 AI 强调前景色（鲜明金黄）。</summary>
    public static SolidColorBrush SuspectedAccent => new(_isDark
        ? Color.FromArgb(255, 250, 204, 21)
        : Color.FromArgb(255, 202, 138, 4));

    /// <summary>AI 特征强调前景色（鲜明正红）。</summary>
    public static SolidColorBrush AiAccent => new(_isDark
        ? Color.FromArgb(255, 255, 90, 82)
        : Color.FromArgb(255, 229, 57, 53));

    /// <summary>深色悬浮气泡内的高亮圆点色（始终在暗色气泡底上保持高对比）。</summary>
    /// <param name="label">分类标签：0=人工，1=AI，2=疑似 AI。</param>
    /// <returns>用于深色气泡内的语义圆点画刷。</returns>
    public static SolidColorBrush GetBubbleDotBrush(int label) => label switch
    {
        0 => new SolidColorBrush(Color.FromArgb(255, 74, 222, 128)),
        1 => new SolidColorBrush(Color.FromArgb(255, 255, 107, 102)),
        2 => new SolidColorBrush(Color.FromArgb(255, 250, 204, 21)),
        _ => new SolidColorBrush(Color.FromArgb(255, 166, 165, 160))
    };

    /// <summary>中性次级强调色。</summary>
    public static SolidColorBrush NeutralAccent => new(_isDark
        ? Color.FromArgb(165, 166, 165, 160)
        : Color.FromArgb(180, 143, 142, 137));

    /// <summary>热力图未检测方块的背景色。</summary>
    public static SolidColorBrush NeutralTileBg => new(_isDark
        ? Color.FromArgb(255, 44, 44, 41)
        : Color.FromArgb(255, 232, 231, 227));

    /// <summary>完全透明画刷。</summary>
    public static SolidColorBrush TransparentBrush => new(Color.FromArgb(0, 0, 0, 0));

    /// <summary>右侧占比图例行处于筛选激活态时的 Surface 3 底色。</summary>
    public static SolidColorBrush ActiveKpiSurface => new(_isDark
        ? Color.FromArgb(255, 42, 42, 39)
        : Color.FromArgb(255, 236, 235, 231));

    /// <summary>右侧占比图例行默认态的透明底色。</summary>
    public static SolidColorBrush DefaultKpiSurface => TransparentBrush;

    /// <summary>自适应正文字重（浅色 400 / 暗色 360）。</summary>
    public static FontWeight AdaptiveBodyWeight => new() { Weight = (ushort)(_isDark ? 360 : 400) };

    /// <summary>自适应中等强调字重（浅色 500 / 暗色 460）。</summary>
    public static FontWeight AdaptiveMediumWeight => new() { Weight = (ushort)(_isDark ? 460 : 500) };

    /// <summary>
    /// 将朱雀返回的 (label, conf) 换算为该类别内的归一化确信强度 [0, 1]：
    /// AI（红）与疑似（黄）为 conf 越高越强；人工（绿）反向计算，conf 越低（越纯正人工）越强。
    /// </summary>
    /// <param name="label">分类标签：0=人工，1=AI，2=疑似 AI。</param>
    /// <param name="conf">朱雀原生 AI 风险置信度（0~1）。</param>
    /// <returns>归一化强度值（0=该类最浅，1=该类最鲜明）。</returns>
    public static double GetConfidenceStrength(int label, double conf)
    {
        double clamped = Math.Clamp(conf, 0.0, 1.0);
        return label switch
        {
            1 => Math.Clamp((clamped - 0.65) / 0.35, 0.0, 1.0),
            2 => Math.Clamp((clamped - 0.45) / 0.40, 0.0, 1.0),
            0 => Math.Clamp((0.50 - clamped) / 0.50, 0.0, 1.0),
            _ => 0.0
        };
    }

    /// <summary>
    /// 按分类标签（红/黄/绿）与确信强度生成预混合至纸面底色的不透明文字背景高亮色，
    /// 同时兼顾鲜艳度梯度与高对比度文本可读性。
    /// </summary>
    /// <param name="label">分类标签：0=人工(绿)，1=AI(红)，2=疑似(黄)。</param>
    /// <param name="conf">朱雀原生 AI 风险置信度（0~1）。</param>
    /// <returns>适用于 <c>RichEditBox</c> 字符背景的预混合 RGB 颜色。</returns>
    public static Color GetSegmentHighlightColor(int label, double conf)
    {
        double s = GetConfidenceStrength(label, conf);

        if (_isDark)
        {
            // 暗色纸面基准：Surface2Brush (#1F1F1D)
            const double bgR = 31;
            const double bgG = 31;
            const double bgB = 29;

            return label switch
            {
                // AI 红：12% -> 36% 鲜红混合
                1 => BlendOpaque(bgR, bgG, bgB, 239, 68, 68, Lerp(0.12, 0.36, s)),
                // 疑似黄：12% -> 34% 亮金黄混合
                2 => BlendOpaque(bgR, bgG, bgB, 245, 190, 20, Lerp(0.12, 0.34, s)),
                // 人工绿：10% -> 28% 翠绿混合
                0 => BlendOpaque(bgR, bgG, bgB, 34, 197, 94, Lerp(0.10, 0.28, s)),
                _ => Color.FromArgb(0, 0, 0, 0)
            };
        }
        else
        {
            // 浅色纸面基准：Surface2Brush (#FFFFFF)
            const double bgR = 255;
            const double bgG = 255;
            const double bgB = 255;

            return label switch
            {
                // AI 红：10% -> 30% 鲜红混合（从柔和粉红渐变至鲜明珊瑚红）
                1 => BlendOpaque(bgR, bgG, bgB, 239, 58, 52, Lerp(0.10, 0.30, s)),
                // 疑似黄：14% -> 46% 亮金黄混合（白底上黄色亮度高，需更高浓度呈现清晰渐变）
                2 => BlendOpaque(bgR, bgG, bgB, 250, 198, 12, Lerp(0.14, 0.46, s)),
                // 人工绿：6% -> 24% 翠绿混合（轻盈透亮且不压过红黄风险焦点）
                0 => BlendOpaque(bgR, bgG, bgB, 34, 197, 94, Lerp(0.06, 0.24, s)),
                _ => Color.FromArgb(0, 0, 0, 0)
            };
        }
    }

    /// <summary>
    /// 按分类标签与确信强度生成右侧片段分布热力方块的纯色画刷，与正文高亮浓淡一一对应。
    /// </summary>
    /// <param name="label">分类标签：0=人工(绿)，1=AI(红)，2=疑似(黄)。</param>
    /// <param name="conf">朱雀原生 AI 风险置信度（0~1）。</param>
    /// <returns>带鲜艳度梯度的热力方块画刷。</returns>
    public static SolidColorBrush GetSegmentTileBrush(int label, double conf)
    {
        double s = GetConfidenceStrength(label, conf);

        if (_isDark)
        {
            const double bgR = 38;
            const double bgG = 38;
            const double bgB = 35;

            return label switch
            {
                1 => new SolidColorBrush(BlendOpaque(bgR, bgG, bgB, 255, 90, 82, Lerp(0.42, 1.0, s))),
                2 => new SolidColorBrush(BlendOpaque(bgR, bgG, bgB, 250, 204, 21, Lerp(0.42, 1.0, s))),
                0 => new SolidColorBrush(BlendOpaque(bgR, bgG, bgB, 34, 197, 94, Lerp(0.38, 0.95, s))),
                _ => NeutralTileBg
            };
        }
        else
        {
            const double bgR = 242;
            const double bgG = 241;
            const double bgB = 238;

            return label switch
            {
                1 => new SolidColorBrush(BlendOpaque(bgR, bgG, bgB, 229, 57, 53, Lerp(0.42, 1.0, s))),
                2 => new SolidColorBrush(BlendOpaque(bgR, bgG, bgB, 240, 178, 8, Lerp(0.44, 1.0, s))),
                0 => new SolidColorBrush(BlendOpaque(bgR, bgG, bgB, 22, 163, 74, Lerp(0.38, 0.96, s))),
                _ => NeutralTileBg
            };
        }
    }

    /// <summary>
    /// 线性插值工具方法。
    /// </summary>
    /// <param name="a">起始值。</param>
    /// <param name="b">结束值。</param>
    /// <param name="t">插值系数 [0, 1]。</param>
    /// <returns>插值结果。</returns>
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>
    /// 将前景色按给定不透明度预混合到背景色上，返回完全不透明 RGB 颜色。
    /// </summary>
    /// <param name="bgR">背景红通道。</param>
    /// <param name="bgG">背景绿通道。</param>
    /// <param name="bgB">背景蓝通道。</param>
    /// <param name="fgR">前景红通道。</param>
    /// <param name="fgG">前景绿通道。</param>
    /// <param name="fgB">前景蓝通道。</param>
    /// <param name="alpha">前景混合权重 [0, 1]。</param>
    /// <returns>预混合后的不透明颜色。</returns>
    private static Color BlendOpaque(
        double bgR, double bgG, double bgB,
        double fgR, double fgG, double fgB,
        double alpha)
    {
        byte r = (byte)Math.Clamp(Math.Round(bgR * (1.0 - alpha) + fgR * alpha), 0, 255);
        byte g = (byte)Math.Clamp(Math.Round(bgG * (1.0 - alpha) + fgG * alpha), 0, 255);
        byte b = (byte)Math.Clamp(Math.Round(bgB * (1.0 - alpha) + fgB * alpha), 0, 255);
        return Color.FromArgb(255, r, g, b);
    }
}
