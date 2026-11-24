using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace InkAudit.Models;

/// <summary>
/// 朱雀文本检测接口请求体结构。
/// </summary>
public sealed class ZhuqueClassifyRequest
{
    /// <summary>
    /// 待检测的小说文本内容。
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 是否由服务端合并段落推理：true 为全文上下文合并复核，false 为逐段独立输出置信度。
    /// </summary>
    [JsonPropertyName("is_merge")]
    public bool IsMerge { get; set; } = true;
}

/// <summary>
/// 朱雀文本检测接口响应体结构。
/// </summary>
public sealed class ZhuqueClassifyResponse
{
    /// <summary>
    /// 推理执行状态，"success" 表示推理成功。
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 全文整体风险置信度（0~1），数值越大越偏向命中 AI 风险。
    /// </summary>
    [JsonPropertyName("softmax_confidence")]
    public double SoftmaxConfidence { get; set; }

    /// <summary>
    /// 整体疑似风险内容占比（0~1），数值越大越可能命中风险。
    /// </summary>
    [JsonPropertyName("ratio_confidence")]
    public double RatioConfidence { get; set; }

    /// <summary>
    /// 各类型内容占比字典："0" 为人工，"1" 为 AI，"2" 为疑似 AI，取值范围 [0, 1]。
    /// </summary>
    [JsonPropertyName("labels_ratio")]
    public Dictionary<string, double>? LabelsRatio { get; set; }

    /// <summary>
    /// 服务端切分后的各分段分类标签、置信度与原文偏移信息。
    /// </summary>
    [JsonPropertyName("segment_labels")]
    public List<ZhuqueSegmentLabel>? SegmentLabels { get; set; }

    /// <summary>
    /// 朱雀底层模型自身 token 用量统计（与免费额度扣减无关）。
    /// </summary>
    [JsonPropertyName("usage")]
    public ZhuqueUsage? Usage { get; set; }

    /// <summary>
    /// 异常时的错误说明信息，正常情况下为空字符串。
    /// </summary>
    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    /// <summary>
    /// 本次调用实际扣减的 Makers 免费额度 token 用量（核算额度以此为准）。
    /// </summary>
    [JsonPropertyName("makers_models_usage")]
    public ZhuqueUsage? MakersModelsUsage { get; set; }

    /// <summary>
    /// 按标签编号安全读取对应的内容占比。
    /// </summary>
    /// <param name="label">分类标签编号（0=人工，1=AI，2=疑似 AI）。</param>
    /// <returns>范围在 [0, 1] 内的占比值，缺失时返回 0。</returns>
    public double GetLabelRatio(int label)
    {
        if (LabelsRatio is null)
        {
            return 0;
        }

        return LabelsRatio.TryGetValue(label.ToString(), out var value) ? value : 0;
    }
}

/// <summary>
/// 单个分段在朱雀推理结果中的分类与置信度详情。
/// </summary>
public sealed class ZhuqueSegmentLabel
{
    /// <summary>
    /// 该分段对应的文本切片内容。
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 分段分类结果：0 = 人工 (Human)，1 = AI，2 = 疑似 AI。
    /// </summary>
    [JsonPropertyName("label")]
    public int Label { get; set; }

    /// <summary>
    /// 该分段的 AI 风险置信度（0~1）。
    /// </summary>
    [JsonPropertyName("conf")]
    public double Conf { get; set; }

    /// <summary>
    /// 服务端返回的分段顺序编号（从 1 开始）。
    /// </summary>
    [JsonPropertyName("order")]
    public int Order { get; set; }

    /// <summary>
    /// 该分段在请求文本中的位置信息，实测格式为 [起始偏移 start, 片段长度 length]。
    /// </summary>
    [JsonPropertyName("position")]
    public List<int>? Position { get; set; }
}

/// <summary>
/// 接口返回的 Token 消耗统计结构。
/// </summary>
public sealed class ZhuqueUsage
{
    /// <summary>
    /// 本次调用消耗的总 Token 数量。
    /// </summary>
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
