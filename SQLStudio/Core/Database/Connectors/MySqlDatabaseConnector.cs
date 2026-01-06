using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;

namespace SQLStudio.Core.Database.Connectors;

public class MySqlDatabaseConnector : BaseDatabaseConnector
{
    public override string DatabaseType => "MySQL";

    public override async Task ConnectAsync(DatabaseConnectionConfig config, CancellationToken cancellationToken = default)
    {
        var connectionString = new MySqlConnectionStringBuilder
        {
            Server = config.Host,
            Port = (uint)config.Port,
            UserID = config.Username,
            Password = config.Password,
            AllowUserVariables = true,
            ConnectionTimeout = 30
        };

        if (!string.IsNullOrEmpty(config.Database))
        {
            connectionString.Database = config.Database;
            _currentDatabase = config.Database;
        }

        foreach (var param in config.AdditionalParameters)
        {
            connectionString[param.Key] = param.Value;
        }

        Connection = new MySqlConnection(connectionString.ConnectionString);
        await Connection.OpenAsync(cancellationToken);
    }

    public override async Task<List<string>> GetDatabasesAsync(CancellationToken cancellationToken = default)
    {
        var databases = new List<string>();
        var result = await ExecuteQueryAsync("SHOW DATABASES", cancellationToken);

        if (result.Success && result.Data != null)
        {
            foreach (DataRow row in result.Data.Rows)
            {
                databases.Add(row[0].ToString() ?? string.Empty);
            }
        }

        return databases;
    }

    public override async Task UseDatabaseAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync($"USE `{databaseName}`", cancellationToken);
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
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'",
            cancellationToken);

        if (result.Success && result.Data != null)
        {
            foreach (DataRow row in result.Data.Rows)
            {
                tables.Add(row["TABLE_NAME"].ToString() ?? string.Empty);
            }
        }

        return tables;
    }

    public override async Task<List<ColumnInfo>> GetTableColumnsAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var columns = new List<ColumnInfo>();
        var sql = $@"
            SELECT 
                COLUMN_NAME,
                DATA_TYPE,
                IS_NULLABLE,
                COLUMN_KEY,
                COLUMN_DEFAULT,
                COLUMN_COMMENT
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}'
            ORDER BY ORDINAL_POSITION";

        var result = await ExecuteQueryAsync(sql, cancellationToken);

        if (result.Success && result.Data != null)
        {
            foreach (DataRow row in result.Data.Rows)
            {
                columns.Add(new ColumnInfo
                {
                    ColumnName = row["COLUMN_NAME"].ToString() ?? string.Empty,
                    DataType = row["DATA_TYPE"].ToString() ?? string.Empty,
                    IsNullable = row["IS_NULLABLE"].ToString() == "YES",
                    IsPrimaryKey = row["COLUMN_KEY"].ToString() == "PRI",
                    DefaultValue = row["COLUMN_DEFAULT"]?.ToString(),
                    Comment = row["COLUMN_COMMENT"]?.ToString()
                });
            }
        }

        return columns;
    }

    protected override string? GetErrorCode(Exception ex)
    {
        return ex is MySqlException mysqlEx ? mysqlEx.Number.ToString() : base.GetErrorCode(ex);
    }
}
