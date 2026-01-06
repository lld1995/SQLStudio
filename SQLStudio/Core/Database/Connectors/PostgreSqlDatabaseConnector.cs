using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace SQLStudio.Core.Database.Connectors;

public class PostgreSqlDatabaseConnector : BaseDatabaseConnector
{
    private DatabaseConnectionConfig? _config;
    
    public override string DatabaseType => "PostgreSQL";

    public override async Task ConnectAsync(DatabaseConnectionConfig config, CancellationToken cancellationToken = default)
    {
        _config = config;
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = config.Host,
            Port = config.Port,
            Username = config.Username,
            Password = config.Password,
            Timeout = 30
        };

        if (!string.IsNullOrEmpty(config.Database))
        {
            connectionString.Database = config.Database;
            _currentDatabase = config.Database;
        }
        else
        {
            connectionString.Database = "postgres";
        }

        foreach (var param in config.AdditionalParameters)
        {
            connectionString[param.Key] = param.Value;
        }

        Connection = new NpgsqlConnection(connectionString.ConnectionString);
        await Connection.OpenAsync(cancellationToken);
    }

    public override async Task<List<string>> GetDatabasesAsync(CancellationToken cancellationToken = default)
    {
        var databases = new List<string>();
        var result = await ExecuteQueryAsync(
            "SELECT datname FROM pg_database WHERE datistemplate = false ORDER BY datname",
            cancellationToken);

        if (result.Success && result.Data != null)
        {
            foreach (DataRow row in result.Data.Rows)
            {
                databases.Add(row["datname"].ToString() ?? string.Empty);
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
            DatabaseName = Connection?.Database ?? string.Empty,
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
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'",
            cancellationToken);

        if (result.Success && result.Data != null)
        {
            foreach (DataRow row in result.Data.Rows)
            {
                tables.Add(row["table_name"].ToString() ?? string.Empty);
            }
        }

        return tables;
    }

    public override async Task<List<ColumnInfo>> GetTableColumnsAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var columns = new List<ColumnInfo>();
        var sql = $@"
            SELECT 
                c.column_name,
                c.data_type,
                c.is_nullable,
                c.column_default,
                CASE WHEN pk.column_name IS NOT NULL THEN true ELSE false END as is_primary_key,
                pgd.description as column_comment
            FROM information_schema.columns c
            LEFT JOIN (
                SELECT ku.column_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage ku ON tc.constraint_name = ku.constraint_name
                WHERE tc.table_name = '{tableName}' AND tc.constraint_type = 'PRIMARY KEY'
            ) pk ON c.column_name = pk.column_name
            LEFT JOIN pg_catalog.pg_statio_all_tables st ON st.relname = c.table_name
            LEFT JOIN pg_catalog.pg_description pgd ON pgd.objoid = st.relid AND pgd.objsubid = c.ordinal_position
            WHERE c.table_name = '{tableName}' AND c.table_schema = 'public'
            ORDER BY c.ordinal_position";

        var result = await ExecuteQueryAsync(sql, cancellationToken);

        if (result.Success && result.Data != null)
        {
            foreach (DataRow row in result.Data.Rows)
            {
                columns.Add(new ColumnInfo
                {
                    ColumnName = row["column_name"].ToString() ?? string.Empty,
                    DataType = row["data_type"].ToString() ?? string.Empty,
                    IsNullable = row["is_nullable"].ToString() == "YES",
                    IsPrimaryKey = Convert.ToBoolean(row["is_primary_key"]),
                    DefaultValue = row["column_default"]?.ToString(),
                    Comment = row["column_comment"]?.ToString()
                });
            }
        }

        return columns;
    }

    protected override string? GetErrorCode(Exception ex)
    {
        return ex is PostgresException pgEx ? pgEx.SqlState : base.GetErrorCode(ex);
    }
}
