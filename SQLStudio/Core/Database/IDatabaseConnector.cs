using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace SQLStudio.Core.Database;

public interface IDatabaseConnector : IAsyncDisposable
{
    string DatabaseType { get; }
    bool IsConnected { get; }
    string? CurrentDatabase { get; }
    
    Task ConnectAsync(DatabaseConnectionConfig config, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    
    Task<List<string>> GetDatabasesAsync(CancellationToken cancellationToken = default);
    Task UseDatabaseAsync(string databaseName, CancellationToken cancellationToken = default);
    
    Task<SqlExecutionResult> ExecuteQueryAsync(string sql, CancellationToken cancellationToken = default);
    Task<SqlExecutionResult> ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken = default);
    
    Task<DatabaseSchema> GetSchemaAsync(CancellationToken cancellationToken = default);
    Task<List<string>> GetTablesAsync(CancellationToken cancellationToken = default);
    Task<List<ColumnInfo>> GetTableColumnsAsync(string tableName, CancellationToken cancellationToken = default);
}

public record DatabaseConnectionConfig
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public string? Database { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public Dictionary<string, string> AdditionalParameters { get; init; } = new();
}

public record SqlExecutionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public DataTable? Data { get; init; }
    public int AffectedRows { get; init; }
    public TimeSpan ExecutionTime { get; init; }
    public string ExecutedSql { get; init; } = string.Empty;
}

public record DatabaseSchema
{
    public string DatabaseName { get; init; } = string.Empty;
    public List<TableInfo> Tables { get; init; } = new();
}

public record TableInfo
{
    public string TableName { get; init; } = string.Empty;
    public string? TableComment { get; init; }
    public List<ColumnInfo> Columns { get; init; } = new();
}

public record ColumnInfo
{
    public string ColumnName { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public bool IsNullable { get; init; }
    public bool IsPrimaryKey { get; init; }
    public string? DefaultValue { get; init; }
    public string? Comment { get; init; }
}
