using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SQLStudio.Core.Services;

/// <summary>
/// 知识库检索结果项
/// </summary>
public class KnowledgeRetrievalItem
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("knowledge_db_name")]
    public string KnowledgeDbName { get; set; } = string.Empty;
}

/// <summary>
/// 知识库检索响应
/// </summary>
public class KnowledgeRetrievalResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public List<KnowledgeRetrievalItem> Data { get; set; } = new();

    [JsonPropertyName("extra")]
    public Dictionary<string, object>? Extra { get; set; }
}

/// <summary>
/// 知识库检索请求
/// </summary>
public class KnowledgeRetrievalRequest
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("knowledge_db_ids")]
    public List<string> KnowledgeDbIds { get; set; } = new();

    [JsonPropertyName("file_ids")]
    public List<string> FileIds { get; set; } = new();

    [JsonPropertyName("top_k")]
    public int TopK { get; set; } = 10;

    [JsonPropertyName("score_threshold")]
    public double ScoreThreshold { get; set; } = 0;
}

/// <summary>
/// 知识库配置
/// </summary>
public class KnowledgeSettings
{
    public string ApiUrl { get; set; } = "";
    public List<string> KnowledgeDbIds { get; set; } = new();
    public int TopK { get; set; } = 10;
    public double ScoreThreshold { get; set; } = 0;
}

/// <summary>
/// 知识库检索服务，通过远程API检索知识
/// </summary>
public class KnowledgeRetrievalService
{
    private readonly HttpClient _httpClient;
    private KnowledgeSettings _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public KnowledgeRetrievalService()
    {
        _httpClient = new HttpClient();
        _settings = new KnowledgeSettings();
    }

    /// <summary>
    /// 配置知识库服务
    /// </summary>
    public void Configure(KnowledgeSettings settings)
    {
        _settings = settings ?? new KnowledgeSettings();
    }

    /// <summary>
    /// 检查服务是否已配置
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.ApiUrl) && _settings.KnowledgeDbIds.Count > 0;

    /// <summary>
    /// 获取当前配置
    /// </summary>
    public KnowledgeSettings GetSettings() => _settings;

    /// <summary>
    /// 检索相关知识
    /// </summary>
    public async Task<List<KnowledgeRetrievalItem>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new List<KnowledgeRetrievalItem>();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<KnowledgeRetrievalItem>();
        }

        try
        {
            var request = new KnowledgeRetrievalRequest
            {
                Query = query,
                KnowledgeDbIds = _settings.KnowledgeDbIds,
                FileIds = new List<string>(), // 空列表表示全局检索
                TopK = _settings.TopK,
                ScoreThreshold = _settings.ScoreThreshold
            };

            var apiUrl = _settings.ApiUrl.TrimEnd('/') + "/Api/Knowledge/KnowledgeRetrievaler";
            var jsonContent = JsonSerializer.Serialize(request, JsonOptions);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(apiUrl, httpContent, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<KnowledgeRetrievalResponse>(responseJson, JsonOptions);

            // 检查API返回的状态码
            if (result?.Code != 200)
            {
                System.Diagnostics.Debug.WriteLine($"知识库API返回错误: {result?.Message}");
                return new List<KnowledgeRetrievalItem>();
            }

            return result?.Data ?? new List<KnowledgeRetrievalItem>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"知识库检索失败: {ex.Message}");
            return new List<KnowledgeRetrievalItem>();
        }
    }

    /// <summary>
    /// 将检索结果格式化为上下文字符串
    /// </summary>
    public string FormatKnowledgeAsContext(List<KnowledgeRetrievalItem> items)
    {
        if (items == null || items.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== 相关场景知识 ===");
        sb.AppendLine();

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            sb.AppendLine($"{i + 1}. {item.Content}");
            if (!string.IsNullOrEmpty(item.Title))
            {
                sb.AppendLine($"   来源: {item.Title}");
            }
            if (!string.IsNullOrEmpty(item.FileName))
            {
                sb.AppendLine($"   文件: {item.FileName}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
