using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SQLStudio.Core.Database;

public abstract class BaseDatabaseConnector : IDatabaseConnector
{
    protected DbConnection? Connection;
    protected string? _currentDatabase;
    
    public abstract string DatabaseType { get; }
    public bool IsConnected => Connection?.State == ConnectionState.Open;
    public string? CurrentDatabase => _currentDatabase;

    public abstract Task ConnectAsync(DatabaseConnectionConfig config, CancellationToken cancellationToken = default);
    public abstract Task<List<string>> GetDatabasesAsync(CancellationToken cancellationToken = default);
    public abstract Task UseDatabaseAsync(string databaseName, CancellationToken cancellationToken = default);
    
    public virtual async Task DisconnectAsync()
    {
        if (Connection != null)
        {
            await Connection.CloseAsync();
            await Connection.DisposeAsync();
            Connection = null;
        }
    }

    public virtual async Task<SqlExecutionResult> ExecuteQueryAsync(string sql, CancellationToken cancellationToken = default)
    {
        if (Connection == null || !IsConnected)
        {
            return new SqlExecutionResult
            {
                Success = false,
                ErrorMessage = "Database connection is not established",
                ExecutedSql = sql
            };
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var command = Connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 300;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var dataTable = new DataTable();
            dataTable.Load(reader);
            
            stopwatch.Stop();
            return new SqlExecutionResult
            {
                Success = true,
                Data = dataTable,
                AffectedRows = dataTable.Rows.Count,
                ExecutionTime = stopwatch.Elapsed,
                ExecutedSql = sql
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new SqlExecutionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ErrorCode = GetErrorCode(ex),
                ExecutionTime = stopwatch.Elapsed,
                ExecutedSql = sql
            };
        }
    }

    public virtual async Task<SqlExecutionResult> ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken = default)
    {
        if (Connection == null || !IsConnected)
        {
            return new SqlExecutionResult
            {
                Success = false,
                ErrorMessage = "Database connection is not established",
                ExecutedSql = sql
            };
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var command = Connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 300;

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            
            stopwatch.Stop();
            return new SqlExecutionResult
            {
                Success = true,
                AffectedRows = affectedRows,
                ExecutionTime = stopwatch.Elapsed,
                ExecutedSql = sql
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new SqlExecutionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ErrorCode = GetErrorCode(ex),
                ExecutionTime = stopwatch.Elapsed,
                ExecutedSql = sql
            };
        }
    }

    public abstract Task<DatabaseSchema> GetSchemaAsync(CancellationToken cancellationToken = default);
    public abstract Task<List<string>> GetTablesAsync(CancellationToken cancellationToken = default);
    public abstract Task<List<ColumnInfo>> GetTableColumnsAsync(string tableName, CancellationToken cancellationToken = default);

    protected virtual string? GetErrorCode(Exception ex)
    {
        return ex is DbException dbEx ? dbEx.SqlState : null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        GC.SuppressFinalize(this);
    }
}
