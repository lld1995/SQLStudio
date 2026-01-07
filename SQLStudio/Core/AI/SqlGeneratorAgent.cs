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
                    ["temperature"] = 0.1,
                    ["max_tokens"] = 1000
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
        sb.AppendLine($"You are a database expert analyzing {request.DatabaseType} database queries.");
        sb.AppendLine("Your task is to identify which tables are needed to answer the user's question.");
        sb.AppendLine();
        sb.AppendLine("Available tables in the database:");
        foreach (var table in request.FullSchema.Tables)
        {
            var columnNames = string.Join(", ", table.Columns.Select(c => c.ColumnName));
            var comment = !string.IsNullOrEmpty(table.TableComment) ? $" -- {table.TableComment}" : "";
            sb.AppendLine($"- {table.TableName}: [{columnNames}]{comment}");
        }
        sb.AppendLine();
        sb.AppendLine("Instructions:");
        sb.AppendLine("1. Analyze the user's question carefully");
        sb.AppendLine("2. Identify ALL tables that might be needed (including tables for JOINs)");
        sb.AppendLine("3. Return your analysis in the following format:");
        sb.AppendLine();
        sb.AppendLine("TABLES: table1, table2, table3");
        sb.AppendLine("REASON: Brief explanation of why these tables are needed");
        
        return sb.ToString();
    }

    private string BuildTableAnalysisUserPrompt(TableAnalysisRequest request)
    {
        return $"User question: {request.UserQuery}\n\nWhich tables are needed to answer this question?";
    }

    private TableAnalysisResult ParseTableAnalysisResponse(string response, DatabaseSchema schema)
    {
        var tables = new List<string>();
        string? reasoning = null;

        // 解析 TABLES: 行
        var tablesMatch = Regex.Match(response, @"TABLES:\s*(.+?)(?:\n|$)", RegexOptions.IgnoreCase);
        if (tablesMatch.Success)
        {
            var tableList = tablesMatch.Groups[1].Value;
            var tableNames = tableList.Split(new[] { ',', '、', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            // 验证表名是否存在于schema中
            var validTableNames = schema.Tables.Select(t => t.TableName.ToLowerInvariant()).ToHashSet();
            foreach (var tableName in tableNames)
            {
                if (validTableNames.Contains(tableName.ToLowerInvariant()))
                {
                    var actualName = schema.Tables.First(t => 
                        t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase)).TableName;
                    tables.Add(actualName);
                }
            }
        }

        // 解析 REASON: 行
        var reasonMatch = Regex.Match(response, @"REASON:\s*(.+?)(?:\n\n|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (reasonMatch.Success)
        {
            reasoning = reasonMatch.Groups[1].Value.Trim();
        }

        // 如果没有解析到表，尝试从响应中提取表名
        if (tables.Count == 0)
        {
            var validTableNames = schema.Tables.Select(t => t.TableName).ToList();
            foreach (var tableName in validTableNames)
            {
                if (response.Contains(tableName, StringComparison.OrdinalIgnoreCase))
                {
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
        sb.AppendLine($"You are an expert SQL developer specializing in {request.DatabaseType} databases.");
        sb.AppendLine("Your task is to generate accurate, efficient, and safe SQL queries based on user requirements.");
        sb.AppendLine();
        sb.AppendLine("Guidelines:");
        sb.AppendLine($"1. Generate SQL syntax compatible with {request.DatabaseType}");
        sb.AppendLine("2. Use proper table and column names from the provided schema");
        sb.AppendLine("3. Include appropriate JOINs when querying related tables");
        sb.AppendLine("4. Use parameterized queries when dealing with user input values");
        sb.AppendLine("5. Add appropriate WHERE clauses for filtering");
        sb.AppendLine("6. Consider query performance and use indexes when available");
        sb.AppendLine("7. Always return the SQL wrapped in ```sql code blocks");
        sb.AppendLine();
        sb.AppendLine("Database Schema:");
        sb.AppendLine(FormatSchema(request.Schema));
        
        return sb.ToString();
    }

    private string BuildUserPrompt(SqlGenerationRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Please generate a SQL query for the following requirement:");
        sb.AppendLine();
        sb.AppendLine(request.UserQuery);
        
        if (!string.IsNullOrEmpty(request.AdditionalContext))
        {
            sb.AppendLine();
            sb.AppendLine("Additional context:");
            sb.AppendLine(request.AdditionalContext);
        }
        
        return sb.ToString();
    }

    private string BuildErrorCorrectionPrompt(string previousSql, string errorMessage)
    {
        var sb = new StringBuilder();
        sb.AppendLine("The previous SQL query failed with the following error:");
        sb.AppendLine();
        sb.AppendLine($"**Error:** {errorMessage}");
        sb.AppendLine();
        sb.AppendLine("**Failed SQL:**");
        sb.AppendLine($"```sql\n{previousSql}\n```");
        sb.AppendLine();
        sb.AppendLine("Please analyze the error and generate a corrected SQL query.");
        sb.AppendLine("Explain what was wrong and how you fixed it.");
        
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
