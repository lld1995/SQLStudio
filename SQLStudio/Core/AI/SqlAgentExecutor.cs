using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SQLStudio.Core.Database;

namespace SQLStudio.Core.AI;

public class SqlAgentExecutor
{
    private readonly ISqlGeneratorAgent _sqlGenerator;
    private readonly IDatabaseConnector _databaseConnector;
    private readonly SqlAgentOptions _options;

    public event EventHandler<SqlExecutionEventArgs>? OnSqlGenerated;
    public event EventHandler<SqlExecutionEventArgs>? OnSqlExecuted;
    public event EventHandler<SqlRetryEventArgs>? OnRetrying;
    public event EventHandler<SqlStreamingEventArgs>? OnStreaming;
    public event EventHandler<SqlPromptEventArgs>? OnPromptSending;

    public SqlAgentExecutor(
        ISqlGeneratorAgent sqlGenerator,
        IDatabaseConnector databaseConnector,
        SqlAgentOptions? options = null)
    {
        _sqlGenerator = sqlGenerator;
        _databaseConnector = databaseConnector;
        _options = options ?? new SqlAgentOptions();
    }

    public async Task<SqlAgentResult> ExecuteAsync(
        string userQuery,
        string? additionalContext = null,
        List<SqlGenerationHistory>? conversationHistory = null,
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
            AdditionalContext = additionalContext,
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
}

public class SqlPromptEventArgs : EventArgs
{
    public string UserQuery { get; init; } = string.Empty;
    public string SystemPrompt { get; init; } = string.Empty;
    public string UserPrompt { get; init; } = string.Empty;
    public int AttemptNumber { get; init; }
}
