namespace InkAudit.Models;

/// <summary>
/// 正文字符高亮支持的分类聚焦维度（点击右侧占比图例可仅高亮单一类别）。
/// </summary>
public enum SegmentFilterKind
{
    /// <summary>同时展示红、黄、绿全部类别高亮。</summary>
    All,

    /// <summary>仅高亮 AI 特征字符区间（红）。</summary>
    Ai,

    /// <summary>仅高亮疑似 AI 字符区间（黄）。</summary>
    Suspected,

    /// <summary>仅高亮人工特征字符区间（绿）。</summary>
    Human
}
