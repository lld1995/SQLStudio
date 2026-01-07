using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SQLStudio.Core.Services;

/// <summary>
/// 场景知识数据模型
/// </summary>
public class ScenarioKnowledge
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 场景知识服务，负责场景知识的增删改查和检索
/// </summary>
public class ScenarioKnowledgeService
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SQLStudio");
    
    private static readonly string KnowledgeFilePath = Path.Combine(SettingsDirectory, "scenario_knowledge.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private List<ScenarioKnowledge> _knowledgeList = new();

    public ScenarioKnowledgeService()
    {
        Load();
    }

    /// <summary>
    /// 加载场景知识列表
    /// </summary>
    private void Load()
    {
        try
        {
            if (!File.Exists(KnowledgeFilePath))
            {
                _knowledgeList = new List<ScenarioKnowledge>();
                return;
            }

            var json = File.ReadAllText(KnowledgeFilePath);
            _knowledgeList = JsonSerializer.Deserialize<List<ScenarioKnowledge>>(json, JsonOptions) 
                ?? new List<ScenarioKnowledge>();
        }
        catch (Exception)
        {
            _knowledgeList = new List<ScenarioKnowledge>();
        }
    }

    /// <summary>
    /// 保存场景知识列表
    /// </summary>
    private void Save()
    {
        try
        {
            if (!Directory.Exists(SettingsDirectory))
            {
                Directory.CreateDirectory(SettingsDirectory);
            }

            var json = JsonSerializer.Serialize(_knowledgeList, JsonOptions);
            File.WriteAllText(KnowledgeFilePath, json, Encoding.UTF8);
        }
        catch (Exception)
        {
            // Silently fail
        }
    }

    /// <summary>
    /// 获取所有场景知识
    /// </summary>
    public List<ScenarioKnowledge> GetAll()
    {
        return _knowledgeList.ToList();
    }

    /// <summary>
    /// 根据ID获取场景知识
    /// </summary>
    public ScenarioKnowledge? GetById(string id)
    {
        return _knowledgeList.FirstOrDefault(k => k.Id == id);
    }

    /// <summary>
    /// 添加场景知识
    /// </summary>
    public void Add(ScenarioKnowledge knowledge)
    {
        if (string.IsNullOrWhiteSpace(knowledge.Id))
        {
            knowledge.Id = Guid.NewGuid().ToString();
        }
        knowledge.CreatedAt = DateTime.Now;
        knowledge.UpdatedAt = DateTime.Now;
        _knowledgeList.Add(knowledge);
        Save();
    }

    /// <summary>
    /// 更新场景知识
    /// </summary>
    public bool Update(ScenarioKnowledge knowledge)
    {
        var existing = _knowledgeList.FirstOrDefault(k => k.Id == knowledge.Id);
        if (existing == null)
        {
            return false;
        }

        existing.Title = knowledge.Title;
        existing.Content = knowledge.Content;
        existing.Keywords = knowledge.Keywords;
        existing.UpdatedAt = DateTime.Now;
        Save();
        return true;
    }

    /// <summary>
    /// 删除场景知识
    /// </summary>
    public bool Delete(string id)
    {
        var existing = _knowledgeList.FirstOrDefault(k => k.Id == id);
        if (existing == null)
        {
            return false;
        }

        _knowledgeList.Remove(existing);
        Save();
        return true;
    }

    /// <summary>
    /// 根据用户提问检索相关的场景知识
    /// </summary>
    public List<ScenarioKnowledge> SearchRelevantKnowledge(string userQuery, int maxResults = 5)
    {
        if (string.IsNullOrWhiteSpace(userQuery))
        {
            return new List<ScenarioKnowledge>();
        }

        var queryLower = userQuery.ToLowerInvariant();
        var queryWords = queryLower.Split(new[] { ' ', '，', '。', ',', '.', '?', '？', '!', '！' }, 
            StringSplitOptions.RemoveEmptyEntries);

        // 计算每个场景知识的匹配分数
        var scoredKnowledge = _knowledgeList.Select(knowledge =>
        {
            var score = 0.0;

            // 标题匹配（权重更高）
            var titleLower = knowledge.Title.ToLowerInvariant();
            if (titleLower.Contains(queryLower))
            {
                score += 10;
            }
            foreach (var word in queryWords)
            {
                if (titleLower.Contains(word))
                {
                    score += 5;
                }
            }

            // 内容匹配
            var contentLower = knowledge.Content.ToLowerInvariant();
            if (contentLower.Contains(queryLower))
            {
                score += 5;
            }
            foreach (var word in queryWords)
            {
                if (contentLower.Contains(word))
                {
                    score += 2;
                }
            }

            // 关键词匹配（权重最高）
            foreach (var keyword in knowledge.Keywords)
            {
                var keywordLower = keyword.ToLowerInvariant();
                if (queryLower.Contains(keywordLower))
                {
                    score += 8;
                }
                foreach (var word in queryWords)
                {
                    if (keywordLower.Contains(word) || word.Contains(keywordLower))
                    {
                        score += 6;
                    }
                }
            }

            return new { Knowledge = knowledge, Score = score };
        })
        .Where(x => x.Score > 0)
        .OrderByDescending(x => x.Score)
        .Take(maxResults)
        .Select(x => x.Knowledge)
        .ToList();

        return scoredKnowledge;
    }

    /// <summary>
    /// 将检索到的场景知识格式化为上下文字符串
    /// </summary>
    public string FormatKnowledgeAsContext(List<ScenarioKnowledge> knowledgeList)
    {
        if (knowledgeList == null || knowledgeList.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== 相关场景知识 ===");
        sb.AppendLine();

        for (int i = 0; i < knowledgeList.Count; i++)
        {
            var knowledge = knowledgeList[i];
            sb.AppendLine($"{i + 1}. {knowledge.Title}");
            sb.AppendLine($"   内容: {knowledge.Content}");
            if (knowledge.Keywords != null && knowledge.Keywords.Count > 0)
            {
                sb.AppendLine($"   关键词: {string.Join(", ", knowledge.Keywords)}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
