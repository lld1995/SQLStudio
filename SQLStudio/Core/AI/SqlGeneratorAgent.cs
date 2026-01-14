using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SQLStudio.Core.Database;

namespace SQLStudio.Core.AI;

public class SqlGeneratorAgent : ISqlGeneratorAgent
{
    private readonly IChatCompletionService _chatService;
    private readonly SqlGeneratorOptions _options;

    public SqlGeneratorAgent(IChatCompletionService chatService, SqlGeneratorOptions? options = null)
    {
        _chatService = chatService;
        _options = options ?? new SqlGeneratorOptions();
    }

    public async Task<SqlGenerationResult> GenerateSqlAsync(
        SqlGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        return await GenerateSqlStreamingAsync(request, _ => { }, cancellationToken);
    }

    public async Task<SqlGenerationResult> GenerateSqlStreamingAsync(
        SqlGenerationRequest request,
        Action<string> onTokenReceived,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(BuildSystemPrompt(request));
            
            if (request.History != null)
            {
                foreach (var historyItem in request.History)
                {
                    if (historyItem.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                    {
                        chatHistory.AddUserMessage(historyItem.Content);
                    }
                    else if (historyItem.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                    {
                        chatHistory.AddAssistantMessage(historyItem.Content);
                    }
                }
            }
            
            chatHistory.AddUserMessage(BuildUserPrompt(request));

            var fullResponse = new StringBuilder();
            var settings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["temperature"] = _options.Temperature,
                    ["max_tokens"] = _options.MaxTokens
                }
            };

            await foreach (var chunk in _chatService.GetStreamingChatMessageContentsAsync(
                chatHistory, settings, cancellationToken: cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    fullResponse.Append(chunk.Content);
                    onTokenReceived(chunk.Content);
                }
            }

            var result = ParseSqlFromResponse(fullResponse.ToString());
            return result;
        }
        catch (Exception ex)
        {
            return new SqlGenerationResult
            {
                Success = false,
                ErrorMessage = $"Failed to generate SQL: {ex.Message}"
            };
        }
    }

    public async Task<SqlGenerationResult> RegenerateSqlWithErrorAsync(
        SqlGenerationRequest request,
        string previousSql,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        return await RegenerateSqlWithErrorStreamingAsync(request, previousSql, errorMessage, _ => { }, cancellationToken);
    }

    public async Task<SqlGenerationResult> RegenerateSqlWithErrorStreamingAsync(
        SqlGenerationRequest request,
        string previousSql,
        string errorMessage,
        Action<string> onTokenReceived,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(BuildSystemPrompt(request));
            chatHistory.AddUserMessage(BuildUserPrompt(request));
            chatHistory.AddAssistantMessage($"```sql\n{previousSql}\n```");
            chatHistory.AddUserMessage(BuildErrorCorrectionPrompt(previousSql, errorMessage));

            var fullResponse = new StringBuilder();
            var settings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["temperature"] = _options.Temperature,
                    ["max_tokens"] = _options.MaxTokens
                }
            };

            await foreach (var chunk in _chatService.GetStreamingChatMessageContentsAsync(
                chatHistory, settings, cancellationToken: cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    fullResponse.Append(chunk.Content);
                    onTokenReceived(chunk.Content);
                }
            }

            var result = ParseSqlFromResponse(fullResponse.ToString());
            return result;
        }
        catch (Exception ex)
        {
            return new SqlGenerationResult
            {
                Success = false,
                ErrorMessage = $"Failed to regenerate SQL: {ex.Message}"
            };
        }
    }

    public async Task<TableAnalysisResult> AnalyzeRequiredTablesAsync(
        TableAnalysisRequest request,
        Action<string> onTokenReceived,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(BuildTableAnalysisSystemPrompt(request));
            chatHistory.AddUserMessage(BuildTableAnalysisUserPrompt(request));

            var fullResponse = new StringBuilder();
            var settings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["temperature"] = 0.0,
                    ["max_tokens"] = 2000
                }
            };

            await foreach (var chunk in _chatService.GetStreamingChatMessageContentsAsync(
                chatHistory, settings, cancellationToken: cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    fullResponse.Append(chunk.Content);
                    onTokenReceived(chunk.Content);
                }
            }

            return ParseTableAnalysisResponse(fullResponse.ToString(), request.FullSchema);
        }
        catch (Exception ex)
        {
            return new TableAnalysisResult
            {
                Success = false,
                ErrorMessage = $"Failed to analyze tables: {ex.Message}"
            };
        }
    }

    public (string SystemPrompt, string UserPrompt) GetPrompts(SqlGenerationRequest request)
    {
        return (BuildSystemPrompt(request), BuildUserPrompt(request));
    }

    public string GetTableAnalysisPrompt(TableAnalysisRequest request)
    {
        return BuildTableAnalysisUserPrompt(request);
    }

    private string BuildTableAnalysisSystemPrompt(TableAnalysisRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"你是一个{request.DatabaseType}数据库专家。任务：分析用户问题，选择所有相关的数据表。");
        sb.AppendLine();
        sb.AppendLine("【重要原则】选表时宁多勿少！遗漏表会导致SQL无法执行，多选表只是稍微影响性能。");
        sb.AppendLine();
        
        // 构建表名列表
        var tableNames = request.FullSchema.Tables.Select(t => t.TableName).ToList();
        sb.AppendLine($"## 数据库共有 {tableNames.Count} 个表");
        sb.AppendLine();
        
        // 先输出表的摘要（表名+注释），便于快速理解
        sb.AppendLine("### 表摘要");
        foreach (var table in request.FullSchema.Tables)
        {
            var comment = !string.IsNullOrEmpty(table.TableComment) ? table.TableComment : "无注释";
            sb.AppendLine($"- **{table.TableName}**: {comment}");
        }
        sb.AppendLine();
        
        // 详细表结构
        sb.AppendLine("### 表详细结构");
        foreach (var table in request.FullSchema.Tables)
        {
            var tableComment = !string.IsNullOrEmpty(table.TableComment) ? $"（{table.TableComment}）" : "";
            sb.AppendLine($"#### {table.TableName} {tableComment}");
            
            foreach (var col in table.Columns)
            {
                var colComment = !string.IsNullOrEmpty(col.Comment) ? $" // {col.Comment}" : "";
                var pkFlag = col.IsPrimaryKey ? " [PK]" : "";
                var fkHint = "";
                if (!col.IsPrimaryKey && (col.ColumnName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) || 
                    col.ColumnName.EndsWith("_id", StringComparison.OrdinalIgnoreCase)))
                {
                    fkHint = " [FK?]";
                }
                sb.AppendLine($"  - {col.ColumnName} ({col.DataType}){pkFlag}{fkHint}{colComment}");
            }
            sb.AppendLine();
        }
        
        sb.AppendLine("## 选表检查清单（必须逐项检查）");
        sb.AppendLine();
        sb.AppendLine("回答问题前，请对照以下清单确认：");
        sb.AppendLine("□ 1. 问题中提到的每个名词/实体，是否都找到了对应的表？");
        sb.AppendLine("□ 2. 如果需要显示某个字段的关联信息（如显示名称而非ID），关联表是否已包含？");
        sb.AppendLine("□ 3. 如果有外键关系（列名以Id结尾），被引用的表是否已包含？");
        sb.AppendLine("□ 4. 如果问题涉及统计/汇总，相关的明细表是否已包含？");
        sb.AppendLine("□ 5. 最终选择的表，能否完整回答用户的所有问题？");
        sb.AppendLine();
        sb.AppendLine("## 输出格式");
        sb.AppendLine();
        sb.AppendLine("ANALYSIS:");
        sb.AppendLine("- 问题意图：[用一句话描述用户想要什么]");
        sb.AppendLine("- 涉及实体：[列出问题中的所有业务实体/名词]");
        sb.AppendLine("- 需要关联：[是/否，如果是，说明哪些表需要关联]");
        sb.AppendLine();
        sb.AppendLine("MAPPING:");
        sb.AppendLine("[对每个实体，写出: 实体名 -> 表名 (依据)]");
        sb.AppendLine();
        sb.AppendLine("CHECK:");
        sb.AppendLine("[逐项回答上面的5个检查项，确保没有遗漏]");
        sb.AppendLine();
        sb.AppendLine("TABLES: 表1, 表2, 表3");
        sb.AppendLine("REASON: [说明选择原因]");
        
        return sb.ToString();
    }

    private string BuildTableAnalysisUserPrompt(TableAnalysisRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 用户问题");
        sb.AppendLine($"「{request.UserQuery}」");
        sb.AppendLine();
        
        // 添加场景知识上下文
        if (!string.IsNullOrEmpty(request.AdditionalContext))
        {
            if (request.AdditionalContext.Contains("=== 相关场景知识 ==="))
            {
                sb.AppendLine("## 业务规则与场景知识（选表时必须考虑）");
                sb.AppendLine();
                var cleanedContext = request.AdditionalContext.Replace("=== 相关场景知识 ===", "").Trim();
                sb.AppendLine(cleanedContext);
                sb.AppendLine();
                sb.AppendLine("【重要】上述场景知识中提到的表必须包含在选择结果中！");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("## 补充说明");
                sb.AppendLine();
                sb.AppendLine(request.AdditionalContext);
                sb.AppendLine();
            }
        }
        
        sb.AppendLine("请严格按照格式输出分析结果。");
        sb.AppendLine();
        sb.AppendLine("【再次提醒】");
        sb.AppendLine("- 仔细检查问题中的每个名词，确保对应的表都被选中");
        sb.AppendLine("- 如果某个表的数据需要通过另一个表来展示（如通过ID关联名称），两个表都要选");
        sb.AppendLine("- TABLES 必须包含所有需要的表，遗漏会导致查询失败");
        return sb.ToString();
    }

    private TableAnalysisResult ParseTableAnalysisResponse(string response, DatabaseSchema schema)
    {
        var tables = new List<string>();
        string? reasoning = null;
        var schemaTableNames = schema.Tables.Select(t => t.TableName).ToList();
        var schemaTableNamesLower = schema.Tables.Select(t => t.TableName.ToLowerInvariant()).ToHashSet();

        // 辅助方法：添加表名（去重）
        void AddTable(string tableName)
        {
            var lowerName = tableName.ToLowerInvariant();
            if (schemaTableNamesLower.Contains(lowerName))
            {
                var actualName = schema.Tables.First(t => 
                    t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase)).TableName;
                if (!tables.Contains(actualName))
                    tables.Add(actualName);
            }
        }

        // 1. 解析 TABLES: 行
        var tablesMatch = Regex.Match(response, @"TABLES[：:]\s*(.+?)(?:\n|REASON|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (tablesMatch.Success)
        {
            var tableList = tablesMatch.Groups[1].Value;
            tableList = Regex.Replace(tableList, @"[`\[\]\*""]", "");
            
            var tableNames = tableList.Split(new[] { ',', '、', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().Trim('.', '-', ' '))
                .Where(t => !string.IsNullOrEmpty(t) && t.Length > 1)
                .ToList();

            foreach (var tableName in tableNames)
            {
                AddTable(tableName);
            }
        }

        // 2. 从 MAPPING 部分额外提取表名（格式: xxx -> 表名）
        var mappingMatches = Regex.Matches(response, @"->\s*(?:表\s*)?([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.IgnoreCase);
        foreach (Match match in mappingMatches)
        {
            AddTable(match.Groups[1].Value.Trim());
        }

        // 3. 解析 REASON: 行
        var reasonMatch = Regex.Match(response, @"REASON[：:]\s*(.+?)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (reasonMatch.Success)
        {
            reasoning = reasonMatch.Groups[1].Value.Trim();
        }

        // 4. 如果仍然没有解析到表，从整个响应中提取（按表名长度降序，避免短名误匹配）
        if (tables.Count == 0)
        {
            foreach (var tableName in schemaTableNames.OrderByDescending(t => t.Length))
            {
                var pattern = $@"\b{Regex.Escape(tableName)}\b";
                if (Regex.IsMatch(response, pattern, RegexOptions.IgnoreCase))
                {
                    if (!tables.Contains(tableName))
                        tables.Add(tableName);
                }
            }
        }

        return new TableAnalysisResult
        {
            Success = tables.Count > 0,
            RequiredTables = tables,
            Reasoning = reasoning ?? response,
            ErrorMessage = tables.Count == 0 ? "Could not identify required tables from the response" : null
        };
    }

    private string BuildSystemPrompt(SqlGenerationRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"你是一个专业的 {request.DatabaseType} 数据库 SQL 开发专家。");
        sb.AppendLine("你的任务是根据用户需求生成准确、高效、安全的 SQL 查询语句。");
        sb.AppendLine();
        sb.AppendLine("## 生成规则");
        sb.AppendLine();
        sb.AppendLine($"1. 生成与 {request.DatabaseType} 兼容的 SQL 语法");
        sb.AppendLine("2. **只能使用 Schema 中存在的表名和列名，禁止使用不存在的字段**");
        sb.AppendLine("3. 查询关联表时使用适当的 JOIN");
        sb.AppendLine("4. 添加适当的 WHERE 子句进行过滤");
        sb.AppendLine("5. 考虑查询性能，合理使用索引");
        sb.AppendLine("6. 如果用户需求涉及多个操作，生成多条 SQL 语句");
        sb.AppendLine("7. 如果用户需求的字段在 Schema 中不存在，请使用最接近的字段或提示无法完成");
        sb.AppendLine();
        sb.AppendLine("## 输出格式要求（必须严格遵守）");
        sb.AppendLine();
        sb.AppendLine("1. 所有 SQL 语句必须放在一个 ```sql 代码块中");
        sb.AppendLine("2. 每条 SQL 语句必须以分号 `;` 结尾");
        sb.AppendLine("3. 多条 SQL 语句之间用空行分隔");
        sb.AppendLine("4. SQL 关键字使用大写（如 SELECT, FROM, WHERE, JOIN 等）");
        sb.AppendLine("5. 复杂查询适当换行和缩进，提高可读性");
        sb.AppendLine();
        sb.AppendLine("## 输出示例");
        sb.AppendLine();
        sb.AppendLine("```sql");
        sb.AppendLine("-- 查询1：获取用户列表");
        sb.AppendLine("SELECT id, name, email");
        sb.AppendLine("FROM users");
        sb.AppendLine("WHERE status = 'active';");
        sb.AppendLine();
        sb.AppendLine("-- 查询2：统计订单数量");
        sb.AppendLine("SELECT COUNT(*) AS order_count");
        sb.AppendLine("FROM orders");
        sb.AppendLine("WHERE created_at >= '2024-01-01';");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## 数据库 Schema");
        sb.AppendLine();
        sb.AppendLine(FormatSchema(request.Schema));
        
        return sb.ToString();
    }

    private string BuildUserPrompt(SqlGenerationRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 用户需求");
        sb.AppendLine();
        sb.AppendLine($"「{request.UserQuery}」");
        
        if (!string.IsNullOrEmpty(request.AdditionalContext))
        {
            sb.AppendLine();
            // 检查是否包含场景知识，使用更明确的标题
            if (request.AdditionalContext.Contains("=== 相关场景知识 ==="))
            {
                sb.AppendLine("## 业务规则与场景知识（请严格遵守）");
                sb.AppendLine();
                // 移除原有的分隔符，使用更简洁的格式
                var cleanedContext = request.AdditionalContext.Replace("=== 相关场景知识 ===", "").Trim();
                sb.AppendLine(cleanedContext);
            }
            else
            {
                sb.AppendLine("## 补充说明");
                sb.AppendLine();
                sb.AppendLine(request.AdditionalContext);
            }
        }
        
        sb.AppendLine();
        sb.AppendLine("请根据以上需求生成 SQL 语句。如果需求涉及多个操作或查询，请生成多条 SQL 语句。");
        sb.AppendLine("严格按照输出格式要求，将所有 SQL 放在一个 ```sql 代码块中。");
        
        return sb.ToString();
    }

    private string BuildErrorCorrectionPrompt(string previousSql, string errorMessage)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## SQL 执行错误，请修复");
        sb.AppendLine();
        sb.AppendLine($"**错误信息:** {errorMessage}");
        sb.AppendLine();
        sb.AppendLine("**出错的 SQL:**");
        sb.AppendLine($"```sql\n{previousSql}\n```");
        sb.AppendLine();
        sb.AppendLine("请分析错误原因并生成修正后的 SQL。");
        sb.AppendLine();
        sb.AppendLine("**重要：只输出修正后的 SQL 代码块，不要输出任何解释或说明文字。**");
        sb.AppendLine("严格按照输出格式要求，将修正后的 SQL 放在一个 ```sql 代码块中。");
        
        return sb.ToString();
    }

    private string FormatSchema(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Database: {schema.DatabaseName}");
        sb.AppendLine();
        
        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"Table: {table.TableName}");
            if (!string.IsNullOrEmpty(table.TableComment))
            {
                sb.AppendLine($"  Comment: {table.TableComment}");
            }
            sb.AppendLine("  Columns:");
            
            foreach (var column in table.Columns)
            {
                var flags = new List<string>();
                if (column.IsPrimaryKey) flags.Add("PK");
                if (!column.IsNullable) flags.Add("NOT NULL");
                if (!string.IsNullOrEmpty(column.DefaultValue)) flags.Add($"DEFAULT: {column.DefaultValue}");
                
                var flagStr = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : "";
                var commentStr = !string.IsNullOrEmpty(column.Comment) ? $" -- {column.Comment}" : "";
                
                sb.AppendLine($"    - {column.ColumnName}: {column.DataType}{flagStr}{commentStr}");
            }
            
            // 添加样例数据
            if (table.SampleData != null && table.SampleData.Count > 0)
            {
                sb.AppendLine("  Sample Data:");
                foreach (var sampleRow in table.SampleData)
                {
                    var rowValues = new List<string>();
                    foreach (var column in table.Columns)
                    {
                        var value = sampleRow.ContainsKey(column.ColumnName) 
                            ? sampleRow[column.ColumnName] 
                            : "NULL";
                        rowValues.Add($"{column.ColumnName}={value}");
                    }
                    sb.AppendLine($"    - {string.Join(", ", rowValues)}");
                }
            }
            
            sb.AppendLine();
        }
        
        return sb.ToString();
    }

    private SqlGenerationResult ParseSqlFromResponse(string response)
    {
        var sql = ExtractSqlFromMarkdown(response);
        
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new SqlGenerationResult
            {
                Success = false,
                ErrorMessage = "Could not extract SQL from the response",
                Explanation = response
            };
        }

        var explanation = response.Replace($"```sql\n{sql}\n```", "").Trim();
        
        return new SqlGenerationResult
        {
            Success = true,
            GeneratedSql = sql,
            Explanation = explanation
        };
    }

    private string ExtractSqlFromMarkdown(string content)
    {
        var startMarker = "```sql";
        var endMarker = "```";
        
        var startIndex = content.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        if (startIndex == -1)
        {
            startMarker = "```";
            startIndex = content.IndexOf(startMarker);
        }
        
        if (startIndex == -1) return content.Trim();
        
        startIndex += startMarker.Length;
        var endIndex = content.IndexOf(endMarker, startIndex);
        
        if (endIndex == -1) return content[startIndex..].Trim();
        
        return content[startIndex..endIndex].Trim();
    }
}

public class SqlGeneratorOptions
{
    public double Temperature { get; set; } = 0.1;
    public int MaxTokens { get; set; } = 2000;
    public int MaxRetries { get; set; } = 3;
}
