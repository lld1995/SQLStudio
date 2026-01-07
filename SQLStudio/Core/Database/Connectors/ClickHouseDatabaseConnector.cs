using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClickHouse.Client.ADO;

namespace SQLStudio.Core.Database.Connectors;

public class ClickHouseDatabaseConnector : BaseDatabaseConnector
{
    private DatabaseConnectionConfig? _config;
    
    public override string DatabaseType => "ClickHouse";

    public override async Task ConnectAsync(DatabaseConnectionConfig config, CancellationToken cancellationToken = default)
    {
        _config = config;
        _currentDatabase = config.Database;
        
        var connectionString = $"Host={config.Host};Port={config.Port};User={config.Username};Password={config.Password}";
        
        if (!string.IsNullOrEmpty(config.Database))
        {
            connectionString += $";Database={config.Database}";
        }
        
        foreach (var param in config.AdditionalParameters)
        {
            connectionString += $";{param.Key}={param.Value}";
        }

        Connection = new ClickHouseConnection(connectionString);
        await Connection.OpenAsync(cancellationToken);
    }

    // Override ExecuteQueryAsync to handle ClickHouse.Client's Nullable type issue with DataTable.Load
    public override async Task<SqlExecutionResult> ExecuteQueryAsync(string sql, CancellationToken cancellationToken = default)
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
            
            // Manually build DataTable to avoid ClickHouse.Client's Nullable<> issue
            var dataTable = new DataTable();
            
            // Add columns with base types (unwrap Nullable<T> to T)
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var fieldType = reader.GetFieldType(i);
                var baseType = Nullable.GetUnderlyingType(fieldType) ?? fieldType;
                dataTable.Columns.Add(reader.GetName(i), baseType);
            }
            
            // Read data rows
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = dataTable.NewRow();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    row[i] = value ?? DBNull.Value;
                }
                dataTable.Rows.Add(row);
            }
            
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

    public override async Task<List<string>> GetDatabasesAsync(CancellationToken cancellationToken = default)
    {
        var databases = new List<string>();
        var result = await ExecuteQueryAsync("SELECT name FROM system.databases ORDER BY name", cancellationToken);

        if (result.Success && result.Data != null)
        {
            foreach (DataRow row in result.Data.Rows)
            {
                databases.Add(row[0].ToString() ?? string.Empty);
            }
        }
        else if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to load databases: {result.ErrorMessage}");
        }

        return databases;
    }

    public override async Task UseDatabaseAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        if (_config == null) throw new InvalidOperationException("Not connected");
        
        await DisconnectAsync();
        
        var newConfig = _config with { Database = databaseName };
        await ConnectAsync(newConfig, cancellationToken);
        _currentDatabase = databaseName;
    }

    public override async Task<DatabaseSchema> GetSchemaAsync(CancellationToken cancellationToken = default)
    {
        var schema = new DatabaseSchema
        {
            DatabaseName = _currentDatabase ?? string.Empty,
            Tables = new List<TableInfo>()
        };

        var tables = await GetTablesAsync(cancellationToken).ConfigureAwait(false);
        
        // 并行获取所有表的结构信息以提高性能
        var tasks = tables.Select(async tableName =>
        {
            var columns = await GetTableColumnsAsync(tableName, cancellationToken).ConfigureAwait(false);
            var sampleData = await GetTableSampleDataAsync(tableName, columns, cancellationToken).ConfigureAwait(false);
            return new TableInfo
            {
                TableName = tableName,
                Columns = columns,
                SampleData = sampleData
            };
        });

        var tableInfos = await Task.WhenAll(tasks).ConfigureAwait(false);
        schema.Tables.AddRange(tableInfos);

        return schema;
    }

    private async Task<List<Dictionary<string, string>>> GetTableSampleDataAsync(string tableName, List<ColumnInfo> columns, CancellationToken cancellationToken)
    {
        var sampleData = new List<Dictionary<string, string>>();
        
        if (columns.Count == 0)
            return sampleData;

        try
        {
            // 构建列名列表，对每列使用 substring 限制100字符
            var columnSelects = columns.Select(col => 
                $@"CASE 
                    WHEN {col.ColumnName} IS NULL THEN 'NULL'
                    WHEN length(toString({col.ColumnName})) > 100 
                    THEN substring(toString({col.ColumnName}), 1, 100)
                    ELSE toString({col.ColumnName})
                END AS {col.ColumnName}").ToList();

            var sql = $@"
                SELECT {string.Join(", ", columnSelects)}
                FROM {tableName}
                ORDER BY rand()
                LIMIT 2";

            var result = await ExecuteQueryAsync(sql, cancellationToken);

            if (result.Success && result.Data != null)
            {
                foreach (DataRow row in result.Data.Rows)
                {
                    var rowData = new Dictionary<string, string>();
                    foreach (var column in columns)
                    {
                        var value = row[column.ColumnName]?.ToString() ?? "NULL";
                        // 确保每列不超过100个字符
                        if (value.Length > 100)
                        {
                            value = value.Substring(0, 100);
                        }
                        rowData[column.ColumnName] = value;
                    }
                    sampleData.Add(rowData);
                }
            }
        }
        catch
        {
            // 如果查询失败（例如表为空或权限不足），返回空列表
        }

        return sampleData;
    }

    public override async Task<List<string>> GetTablesAsync(CancellationToken cancellationToken = default)
    {
        var tables = new List<string>();
        var result = await ExecuteQueryAsync(
            $"SELECT name FROM system.tables WHERE database = '{_currentDatabase}'",
            cancellationToken);

        if (result.Success && result.Data != null)
        {
            foreach (DataRow row in result.Data.Rows)
            {
                tables.Add(row[0].ToString() ?? string.Empty);
            }
        }

        return tables;
    }

    public override async Task<List<ColumnInfo>> GetTableColumnsAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var columns = new List<ColumnInfo>();
        var sql = $@"
            SELECT 
                name,
                type,
                default_kind,
                default_expression,
                comment,
                is_in_primary_key
            FROM system.columns 
            WHERE database = '{_currentDatabase}' AND table = '{tableName}'
            ORDER BY position";

        var result = await ExecuteQueryAsync(sql, cancellationToken);

        if (result.Success && result.Data != null)
        {
            foreach (DataRow row in result.Data.Rows)
            {
                var dataType = row[1].ToString() ?? string.Empty;
                columns.Add(new ColumnInfo
                {
                    ColumnName = row[0].ToString() ?? string.Empty,
                    DataType = dataType,
                    IsNullable = dataType.StartsWith("Nullable"),
                    IsPrimaryKey = Convert.ToBoolean(row[5]),
                    DefaultValue = row[3]?.ToString(),
                    Comment = row[4]?.ToString()
                });
            }
        }

        return columns;
    }
}
