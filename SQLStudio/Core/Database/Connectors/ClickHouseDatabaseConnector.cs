using System;
using System.Collections.Generic;
using System.Data;
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

    public override async Task<List<string>> GetDatabasesAsync(CancellationToken cancellationToken = default)
    {
        var databases = new List<string>();
        var result = await ExecuteQueryAsync("SELECT name FROM system.databases ORDER BY name", cancellationToken);

        if (result.Success && result.Data != null)
        {
            foreach (DataRow row in result.Data.Rows)
            {
                databases.Add(row["name"].ToString() ?? string.Empty);
            }
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

        var tables = await GetTablesAsync(cancellationToken);
        foreach (var tableName in tables)
        {
            var columns = await GetTableColumnsAsync(tableName, cancellationToken);
            schema.Tables.Add(new TableInfo
            {
                TableName = tableName,
                Columns = columns
            });
        }

        return schema;
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
                tables.Add(row["name"].ToString() ?? string.Empty);
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
                var dataType = row["type"].ToString() ?? string.Empty;
                columns.Add(new ColumnInfo
                {
                    ColumnName = row["name"].ToString() ?? string.Empty,
                    DataType = dataType,
                    IsNullable = dataType.StartsWith("Nullable"),
                    IsPrimaryKey = Convert.ToBoolean(row["is_in_primary_key"]),
                    DefaultValue = row["default_expression"]?.ToString(),
                    Comment = row["comment"]?.ToString()
                });
            }
        }

        return columns;
    }
}
