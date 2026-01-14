using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SQLStudio.Core.Database;

namespace SQLStudio.Core.AI;

public interface ISqlGeneratorAgent
{
    Task<TableAnalysisResult> AnalyzeRequiredTablesAsync(
        TableAnalysisRequest request,
        Action<string> onTokenReceived,
        CancellationToken cancellationToken = default);

    Task<SqlGenerationResult> GenerateSqlAsync(
        SqlGenerationRequest request, 
        CancellationToken cancellationToken = default);
    
    Task<SqlGenerationResult> GenerateSqlStreamingAsync(
        SqlGenerationRequest request,
        Action<string> onTokenReceived,
        CancellationToken cancellationToken = default);
    
    Task<SqlGenerationResult> RegenerateSqlWithErrorAsync(
        SqlGenerationRequest request,
        string previousSql,
        string errorMessage,
        CancellationToken cancellationToken = default);
    
    Task<SqlGenerationResult> RegenerateSqlWithErrorStreamingAsync(
        SqlGenerationRequest request,
        string previousSql,
        string errorMessage,
        Action<string> onTokenReceived,
        CancellationToken cancellationToken = default);

    (string SystemPrompt, string UserPrompt) GetPrompts(SqlGenerationRequest request);
    string GetTableAnalysisPrompt(TableAnalysisRequest request);
}

public record SqlGenerationRequest
{
    public required string UserQuery { get; init; }
    public required DatabaseSchema Schema { get; init; }
    public required string DatabaseType { get; init; }
    public string? AdditionalContext { get; init; }
    public List<SqlGenerationHistory>? History { get; init; }
}

public record SqlGenerationResult
{
    public bool Success { get; init; }
    public string? GeneratedSql { get; init; }
    public string? Explanation { get; init; }
    public string? ErrorMessage { get; init; }
    public int TokensUsed { get; init; }
}

public record SqlGenerationHistory
{
    public required string Role { get; init; }
    public required string Content { get; init; }
}

public record TableAnalysisRequest
{
    public required string UserQuery { get; init; }
    public required DatabaseSchema FullSchema { get; init; }
    public required string DatabaseType { get; init; }
    public string? AdditionalContext { get; init; }
}

public record TableAnalysisResult
{
    public bool Success { get; init; }
    public List<string> RequiredTables { get; init; } = new();
    public string? Reasoning { get; init; }
    public string? ErrorMessage { get; init; }
}
