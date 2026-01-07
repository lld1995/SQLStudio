using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SQLStudio.Core.Database;
using SQLStudio.Core.Services;

namespace SQLStudio.Core.AI;

public class SqlAgentExecutor
{
    private readonly ISqlGeneratorAgent _sqlGenerator;
    private readonly IDatabaseConnector _databaseConnector;
    private readonly ScenarioKnowledgeService? _knowledgeService;
    private readonly SqlAgentOptions _options;

    public event EventHandler<SqlExecutionEventArgs>? OnSqlGenerated;
    public event EventHandler<SqlExecutionEventArgs>? OnSqlExecuted;
    public event EventHandler<SqlRetryEventArgs>? OnRetrying;
    public event EventHandler<SqlStreamingEventArgs>? OnStreaming;
    public event EventHandler<SqlPromptEventArgs>? OnPromptSending;
    public event EventHandler<TableAnalysisEventArgs>? OnTableAnalysisStarted;
    public event EventHandler<TableAnalysisEventArgs>? OnTableAnalysisCompleted;
    public event EventHandler<StepChangedEventArgs>? OnStepChanged;

    public SqlAgentExecutor(
        ISqlGeneratorAgent sqlGenerator,
        IDatabaseConnector databaseConnector,
        ScenarioKnowledgeService? knowledgeService = null,
        SqlAgentOptions? options = null)
    {
        _sqlGenerator = sqlGenerator;
        _databaseConnector = databaseConnector;
        _knowledgeService = knowledgeService;
        _options = options ?? new SqlAgentOptions();
    }

    public async Task<SqlAgentResult> ExecuteAsync(
        string userQuery,
        string? additionalContext = null,
        List<SqlGenerationHistory>? conversationHistory = null,
        CancellationToken cancellationToken = default,
        List<string>? specifiedTables = null)
    {
        if (string.IsNullOrEmpty(_databaseConnector.CurrentDatabase))
        {
            return new SqlAgentResult
            {
                Success = false,
                ErrorMessage = "请先选择一个数据库",
                Attempts = new List<SqlAttempt>(),
                TotalAttempts = 0
            };
        }

        // Step 1: 获取完整Schema
        OnStepChanged?.Invoke(this, new StepChangedEventArgs
        {
            Step = ExecutionStep.FetchingSchema,
            Message = "正在获取数据库结构..."
        });

        var fullSchema = await _databaseConnector.GetSchemaAsync(cancellationToken);

        // Step 2: 分析需要的表（如果用户指定了表则跳过）
        TableAnalysisResult analysisResult;
        DatabaseSchema filteredSchema;

        if (specifiedTables != null && specifiedTables.Count > 0)
        {
            // 用户通过@指定了表，跳过AI分析
            OnStepChanged?.Invoke(this, new StepChangedEventArgs
            {
                Step = ExecutionStep.AnalyzingTables,
                Message = $"使用用户指定的 {specifiedTables.Count} 个表..."
            });

            // 验证并过滤用户指定的表
            var validTables = specifiedTables
                .Where(t => fullSchema.Tables.Any(st => st.TableName.Equals(t, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            analysisResult = new TableAnalysisResult
            {
                Success = validTables.Count > 0,
                RequiredTables = validTables,
                Reasoning = "用户通过@指定的表"
            };

            filteredSchema = new DatabaseSchema
            {
                DatabaseName = fullSchema.DatabaseName,
                Tables = fullSchema.Tables
                    .Where(t => validTables.Contains(t.TableName, StringComparer.OrdinalIgnoreCase))
                    .ToList()
            };

            OnTableAnalysisCompleted?.Invoke(this, new TableAnalysisEventArgs
            {
                UserQuery = userQuery,
                TotalTables = fullSchema.Tables.Count,
                SelectedTables = validTables,
                Reasoning = "用户通过@指定的表"
            });
        }
        else
        {
            // 正常的AI表分析流程
            OnStepChanged?.Invoke(this, new StepChangedEventArgs
            {
                Step = ExecutionStep.AnalyzingTables,
                Message = "正在分析需要的表..."
            });

            var tableAnalysisRequest = new TableAnalysisRequest
            {
                UserQuery = userQuery,
                FullSchema = fullSchema,
                DatabaseType = _databaseConnector.DatabaseType
            };

            OnTableAnalysisStarted?.Invoke(this, new TableAnalysisEventArgs
            {
                UserQuery = userQuery,
                TotalTables = fullSchema.Tables.Count
            });

            analysisResult = await _sqlGenerator.AnalyzeRequiredTablesAsync(
                tableAnalysisRequest,
                token => OnStreaming?.Invoke(this, new SqlStreamingEventArgs { Token = token, AttemptNumber = 0, Phase = "TableAnalysis" }),
                cancellationToken);

            // 构建过滤后的Schema
            if (analysisResult.Success && analysisResult.RequiredTables.Count > 0)
            {
                filteredSchema = new DatabaseSchema
                {
                    DatabaseName = fullSchema.DatabaseName,
                    Tables = fullSchema.Tables
                        .Where(t => analysisResult.RequiredTables.Contains(t.TableName, StringComparer.OrdinalIgnoreCase))
                        .ToList()
                };
            }
            else
            {
                // 如果分析失败，使用完整Schema
                filteredSchema = fullSchema;
            }

            OnTableAnalysisCompleted?.Invoke(this, new TableAnalysisEventArgs
            {
                UserQuery = userQuery,
                TotalTables = fullSchema.Tables.Count,
                SelectedTables = analysisResult.RequiredTables,
                Reasoning = analysisResult.Reasoning
            });
        }

        // Step 3: 检索相关场景知识
        string? knowledgeContext = null;
        if (_knowledgeService != null)
        {
            OnStepChanged?.Invoke(this, new StepChangedEventArgs
            {
                Step = ExecutionStep.GeneratingSql,
                Message = "正在检索相关场景知识..."
            });

            try
            {
                var relevantKnowledge = _knowledgeService.SearchRelevantKnowledge(userQuery, maxResults: 5);
                if (relevantKnowledge != null && relevantKnowledge.Count > 0)
                {
                    knowledgeContext = _knowledgeService.FormatKnowledgeAsContext(relevantKnowledge);
                }
            }
            catch (Exception ex)
            {
                // 场景知识检索失败不影响SQL生成，只记录错误
                System.Diagnostics.Debug.WriteLine($"场景知识检索失败: {ex.Message}");
            }
        }

        // Step 4: 生成SQL
        OnStepChanged?.Invoke(this, new StepChangedEventArgs
        {
            Step = ExecutionStep.GeneratingSql,
            Message = $"正在生成SQL（使用 {filteredSchema.Tables.Count} 个表）..."
        });

        // 合并场景知识和额外上下文
        var combinedContext = CombineContext(additionalContext, knowledgeContext);
        
        // 调试信息：记录场景知识检索结果
        if (knowledgeContext != null)
        {
            System.Diagnostics.Debug.WriteLine($"场景知识已检索到，共 {knowledgeContext.Length} 字符");
            System.Diagnostics.Debug.WriteLine($"场景知识内容预览: {knowledgeContext.Substring(0, Math.Min(100, knowledgeContext.Length))}...");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("未检索到相关场景知识");
        }
        
        if (combinedContext != null)
        {
            System.Diagnostics.Debug.WriteLine($"最终合并的上下文长度: {combinedContext.Length} 字符");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("最终合并的上下文为 null（没有额外上下文和场景知识）");
        }

        var request = new SqlGenerationRequest
        {
            UserQuery = userQuery,
            Schema = filteredSchema,
            DatabaseType = _databaseConnector.DatabaseType,
            AdditionalContext = combinedContext,
            History = conversationHistory
        };

        var attempts = new List<SqlAttempt>();
        SqlGenerationResult? lastGenerationResult = null;
        SqlExecutionResult? lastExecutionResult = null;

        for (int attempt = 0; attempt < _options.MaxRetries; attempt++)
        {
            Action<string> streamingCallback = token =>
            {
                OnStreaming?.Invoke(this, new SqlStreamingEventArgs
                {
                    Token = token,
                    AttemptNumber = attempt + 1,
                    Phase = "SqlGeneration"
                });
            };

            if (attempt == 0)
            {
                // 只在第一次尝试时发送prompt事件
                var prompts = _sqlGenerator.GetPrompts(request);
                OnPromptSending?.Invoke(this, new SqlPromptEventArgs
                {
                    UserQuery = userQuery,
                    SystemPrompt = prompts.SystemPrompt,
                    UserPrompt = prompts.UserPrompt,
                    AttemptNumber = attempt + 1,
                    FilteredTableCount = filteredSchema.Tables.Count,
                    TotalTableCount = fullSchema.Tables.Count
                });

                lastGenerationResult = await _sqlGenerator.GenerateSqlStreamingAsync(request, streamingCallback, cancellationToken);
            }
            else
            {
                // 重试时触发OnRetrying事件
                OnRetrying?.Invoke(this, new SqlRetryEventArgs
                {
                    AttemptNumber = attempt + 1,
                    MaxAttempts = _options.MaxRetries,
                    PreviousSql = lastExecutionResult?.ExecutedSql ?? string.Empty,
                    ErrorMessage = lastExecutionResult?.ErrorMessage ?? string.Empty
                });

                lastGenerationResult = await _sqlGenerator.RegenerateSqlWithErrorStreamingAsync(
                    request,
                    lastExecutionResult?.ExecutedSql ?? string.Empty,
                    lastExecutionResult?.ErrorMessage ?? string.Empty,
                    streamingCallback,
                    cancellationToken);
            }

            // Step 5: 执行SQL
            OnStepChanged?.Invoke(this, new StepChangedEventArgs
            {
                Step = ExecutionStep.ExecutingSql,
                Message = "正在执行SQL..."
            });

            if (!lastGenerationResult.Success || string.IsNullOrEmpty(lastGenerationResult.GeneratedSql))
            {
                attempts.Add(new SqlAttempt
                {
                    AttemptNumber = attempt + 1,
                    GeneratedSql = lastGenerationResult.GeneratedSql,
                    GenerationError = lastGenerationResult.ErrorMessage,
                    Success = false
                });
                continue;
            }

            OnSqlGenerated?.Invoke(this, new SqlExecutionEventArgs
            {
                Sql = lastGenerationResult.GeneratedSql,
                Explanation = lastGenerationResult.Explanation,
                AttemptNumber = attempt + 1
            });

            lastExecutionResult = await _databaseConnector.ExecuteQueryAsync(
                lastGenerationResult.GeneratedSql,
                cancellationToken);

            OnSqlExecuted?.Invoke(this, new SqlExecutionEventArgs
            {
                Sql = lastGenerationResult.GeneratedSql,
                ExecutionResult = lastExecutionResult,
                AttemptNumber = attempt + 1
            });

            attempts.Add(new SqlAttempt
            {
                AttemptNumber = attempt + 1,
                GeneratedSql = lastGenerationResult.GeneratedSql,
                Explanation = lastGenerationResult.Explanation,
                ExecutionResult = lastExecutionResult,
                Success = lastExecutionResult.Success
            });

            if (lastExecutionResult.Success)
            {
                OnStepChanged?.Invoke(this, new StepChangedEventArgs
                {
                    Step = ExecutionStep.Completed,
                    Message = "执行完成"
                });

                return new SqlAgentResult
                {
                    Success = true,
                    FinalSql = lastGenerationResult.GeneratedSql,
                    FinalExplanation = lastGenerationResult.Explanation,
                    ExecutionResult = lastExecutionResult,
                    Attempts = attempts,
                    TotalAttempts = attempt + 1,
                    AnalyzedTables = analysisResult.RequiredTables,
                    TableAnalysisReasoning = analysisResult.Reasoning
                };
            }

            // Step: 重试
            OnStepChanged?.Invoke(this, new StepChangedEventArgs
            {
                Step = ExecutionStep.Retrying,
                Message = $"SQL执行出错，正在重试 ({attempt + 2}/{_options.MaxRetries})..."
            });
        }

        OnStepChanged?.Invoke(this, new StepChangedEventArgs
        {
            Step = ExecutionStep.Failed,
            Message = "执行失败"
        });

        return new SqlAgentResult
        {
            Success = false,
            FinalSql = lastGenerationResult?.GeneratedSql,
            FinalExplanation = lastGenerationResult?.Explanation,
            ExecutionResult = lastExecutionResult,
            ErrorMessage = $"Failed after {_options.MaxRetries} attempts. Last error: {lastExecutionResult?.ErrorMessage}",
            Attempts = attempts,
            TotalAttempts = _options.MaxRetries,
            AnalyzedTables = analysisResult.RequiredTables,
            TableAnalysisReasoning = analysisResult.Reasoning
        };
    }

    /// <summary>
    /// 合并额外上下文和场景知识上下文
    /// </summary>
    private string? CombineContext(string? additionalContext, string? knowledgeContext)
    {
        if (string.IsNullOrWhiteSpace(additionalContext) && string.IsNullOrWhiteSpace(knowledgeContext))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(additionalContext))
        {
            return knowledgeContext;
        }

        if (string.IsNullOrWhiteSpace(knowledgeContext))
        {
            return additionalContext;
        }

        return $"{additionalContext}\n\n{knowledgeContext}";
    }

    public async Task<SqlAgentResult> ExecuteNonQueryAsync(
        string userQuery,
        string? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_databaseConnector.CurrentDatabase))
        {
            return new SqlAgentResult
            {
                Success = false,
                ErrorMessage = "请先选择一个数据库",
                Attempts = new List<SqlAttempt>(),
                TotalAttempts = 0
            };
        }

        var schema = await _databaseConnector.GetSchemaAsync(cancellationToken);
        
        var request = new SqlGenerationRequest
        {
            UserQuery = userQuery,
            Schema = schema,
            DatabaseType = _databaseConnector.DatabaseType,
            AdditionalContext = additionalContext
        };

        var attempts = new List<SqlAttempt>();
        SqlGenerationResult? lastGenerationResult = null;
        SqlExecutionResult? lastExecutionResult = null;

        for (int attempt = 0; attempt < _options.MaxRetries; attempt++)
        {
            Action<string> streamingCallback = token =>
            {
                OnStreaming?.Invoke(this, new SqlStreamingEventArgs
                {
                    Token = token,
                    AttemptNumber = attempt + 1
                });
            };

            if (attempt == 0)
            {
                // 只在第一次尝试时发送prompt事件
                var prompts = _sqlGenerator.GetPrompts(request);
                OnPromptSending?.Invoke(this, new SqlPromptEventArgs
                {
                    UserQuery = userQuery,
                    SystemPrompt = prompts.SystemPrompt,
                    UserPrompt = prompts.UserPrompt,
                    AttemptNumber = attempt + 1
                });

                lastGenerationResult = await _sqlGenerator.GenerateSqlStreamingAsync(request, streamingCallback, cancellationToken);
            }
            else
            {
                // 重试时触发OnRetrying事件
                OnRetrying?.Invoke(this, new SqlRetryEventArgs
                {
                    AttemptNumber = attempt + 1,
                    MaxAttempts = _options.MaxRetries,
                    PreviousSql = lastExecutionResult?.ExecutedSql ?? string.Empty,
                    ErrorMessage = lastExecutionResult?.ErrorMessage ?? string.Empty
                });

                lastGenerationResult = await _sqlGenerator.RegenerateSqlWithErrorStreamingAsync(
                    request,
                    lastExecutionResult?.ExecutedSql ?? string.Empty,
                    lastExecutionResult?.ErrorMessage ?? string.Empty,
                    streamingCallback,
                    cancellationToken);
            }

            if (!lastGenerationResult.Success || string.IsNullOrEmpty(lastGenerationResult.GeneratedSql))
            {
                attempts.Add(new SqlAttempt
                {
                    AttemptNumber = attempt + 1,
                    GeneratedSql = lastGenerationResult.GeneratedSql,
                    GenerationError = lastGenerationResult.ErrorMessage,
                    Success = false
                });
                continue;
            }

            OnSqlGenerated?.Invoke(this, new SqlExecutionEventArgs
            {
                Sql = lastGenerationResult.GeneratedSql,
                Explanation = lastGenerationResult.Explanation,
                AttemptNumber = attempt + 1
            });

            lastExecutionResult = await _databaseConnector.ExecuteNonQueryAsync(
                lastGenerationResult.GeneratedSql,
                cancellationToken);

            OnSqlExecuted?.Invoke(this, new SqlExecutionEventArgs
            {
                Sql = lastGenerationResult.GeneratedSql,
                ExecutionResult = lastExecutionResult,
                AttemptNumber = attempt + 1
            });

            attempts.Add(new SqlAttempt
            {
                AttemptNumber = attempt + 1,
                GeneratedSql = lastGenerationResult.GeneratedSql,
                Explanation = lastGenerationResult.Explanation,
                ExecutionResult = lastExecutionResult,
                Success = lastExecutionResult.Success
            });

            if (lastExecutionResult.Success)
            {
                return new SqlAgentResult
                {
                    Success = true,
                    FinalSql = lastGenerationResult.GeneratedSql,
                    FinalExplanation = lastGenerationResult.Explanation,
                    ExecutionResult = lastExecutionResult,
                    Attempts = attempts,
                    TotalAttempts = attempt + 1
                };
            }
        }

        return new SqlAgentResult
        {
            Success = false,
            FinalSql = lastGenerationResult?.GeneratedSql,
            FinalExplanation = lastGenerationResult?.Explanation,
            ExecutionResult = lastExecutionResult,
            ErrorMessage = $"Failed after {_options.MaxRetries} attempts. Last error: {lastExecutionResult?.ErrorMessage}",
            Attempts = attempts,
            TotalAttempts = _options.MaxRetries
        };
    }
}

public class SqlAgentOptions
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
}

public class SqlAgentResult
{
    public bool Success { get; init; }
    public string? FinalSql { get; init; }
    public string? FinalExplanation { get; init; }
    public SqlExecutionResult? ExecutionResult { get; init; }
    public string? ErrorMessage { get; init; }
    public List<SqlAttempt> Attempts { get; init; } = new();
    public int TotalAttempts { get; init; }
    public List<string> AnalyzedTables { get; init; } = new();
    public string? TableAnalysisReasoning { get; init; }
}

public class SqlAttempt
{
    public int AttemptNumber { get; init; }
    public string? GeneratedSql { get; init; }
    public string? Explanation { get; init; }
    public string? GenerationError { get; init; }
    public SqlExecutionResult? ExecutionResult { get; init; }
    public bool Success { get; init; }
}

public class SqlExecutionEventArgs : EventArgs
{
    public string Sql { get; init; } = string.Empty;
    public string? Explanation { get; init; }
    public SqlExecutionResult? ExecutionResult { get; init; }
    public int AttemptNumber { get; init; }
}

public class SqlRetryEventArgs : EventArgs
{
    public int AttemptNumber { get; init; }
    public int MaxAttempts { get; init; }
    public string PreviousSql { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
}

public class SqlStreamingEventArgs : EventArgs
{
    public string Token { get; init; } = string.Empty;
    public int AttemptNumber { get; init; }
    public string Phase { get; init; } = "SqlGeneration";
}

public class SqlPromptEventArgs : EventArgs
{
    public string UserQuery { get; init; } = string.Empty;
    public string SystemPrompt { get; init; } = string.Empty;
    public string UserPrompt { get; init; } = string.Empty;
    public int AttemptNumber { get; init; }
    public int FilteredTableCount { get; init; }
    public int TotalTableCount { get; init; }
}

public class TableAnalysisEventArgs : EventArgs
{
    public string UserQuery { get; init; } = string.Empty;
    public int TotalTables { get; init; }
    public List<string> SelectedTables { get; init; } = new();
    public string? Reasoning { get; init; }
}

public class StepChangedEventArgs : EventArgs
{
    public ExecutionStep Step { get; init; }
    public string Message { get; init; } = string.Empty;
}

public enum ExecutionStep
{
    FetchingSchema,
    AnalyzingTables,
    GeneratingSql,
    ExecutingSql,
    Retrying,
    Completed,
    Failed
}
