using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SQLStudio.Core.Database.Connectors;

public class SqlServerDatabaseConnector : BaseDatabaseConnector
{
    private DatabaseConnectionConfig? _config;
    
    public override string DatabaseType => "SqlServer";

    public override async Task ConnectAsync(DatabaseConnectionConfig config, CancellationToken cancellationToken = default)
    {
        _config = config;
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = $"{config.Host},{config.Port}",
            UserID = config.Username,
            Password = config.Password,
            TrustServerCertificate = true,
            ConnectTimeout = 30
        };

        if (!string.IsNullOrEmpty(config.Database))
        {
            connectionString.InitialCatalog = config.Database;
            _currentDatabase = config.Database;
        }
        else
        {
            connectionString.InitialCatalog = "master";
        }

        foreach (var param in config.AdditionalParameters)
        {
            connectionString[param.Key] = param.Value;
        }

        Connection = new SqlConnection(connectionString.ConnectionString);
        await Connection.OpenAsync(cancellationToken);
    }

    public override async Task<List<string>> GetDatabasesAsync(CancellationToken cancellationToken = default)
    {
        var databases = new List<string>();
        var result = await ExecuteQueryAsync(
            "SELECT name FROM sys.databases WHERE state = 0 ORDER BY name",
            cancellationToken);

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
        await ExecuteNonQueryAsync($"USE [{databaseName}]", cancellationToken);
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
            var sampleData = await GetTableSampleDataAsync(tableName, columns, cancellationToken);
            schema.Tables.Add(new TableInfo
            {
                TableName = tableName,
                Columns = columns,
                SampleData = sampleData
            });
        }

        return schema;
    }

    private async Task<List<Dictionary<string, string>>> GetTableSampleDataAsync(string tableName, List<ColumnInfo> columns, CancellationToken cancellationToken)
    {
        var sampleData = new List<Dictionary<string, string>>();
        
        if (columns.Count == 0)
            return sampleData;

        try
        {
            // 构建列名列表，对每列使用 LEFT 或 SUBSTRING 限制100字符
            var columnSelects = columns.Select(col => 
                $@"CASE 
                    WHEN [{col.ColumnName}] IS NULL THEN 'NULL'
                    WHEN LEN(CAST([{col.ColumnName}] AS NVARCHAR(MAX))) > 100 
                    THEN LEFT(CAST([{col.ColumnName}] AS NVARCHAR(MAX)), 100)
                    ELSE CAST([{col.ColumnName}] AS NVARCHAR(MAX))
                END AS [{col.ColumnName}]").ToList();

            var sql = $@"
                SELECT TOP 2 {string.Join(", ", columnSelects)}
                FROM [{tableName}]
                ORDER BY NEWID()";

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
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'",
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
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.IS_NULLABLE,
                c.COLUMN_DEFAULT,
                CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END as IS_PRIMARY_KEY,
                ep.value as COLUMN_COMMENT
            FROM INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN (
                SELECT ku.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                WHERE tc.TABLE_NAME = '{tableName}' AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            ) pk ON c.COLUMN_NAME = pk.COLUMN_NAME
            LEFT JOIN sys.extended_properties ep ON ep.major_id = OBJECT_ID(c.TABLE_NAME) 
                AND ep.minor_id = c.ORDINAL_POSITION AND ep.name = 'MS_Description'
            WHERE c.TABLE_NAME = '{tableName}'
            ORDER BY c.ORDINAL_POSITION";

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
                    IsPrimaryKey = Convert.ToInt32(row["IS_PRIMARY_KEY"]) == 1,
                    DefaultValue = row["COLUMN_DEFAULT"]?.ToString(),
                    Comment = row["COLUMN_COMMENT"]?.ToString()
                });
            }
        }

        return columns;
    }

    protected override string? GetErrorCode(Exception ex)
    {
        return ex is SqlException sqlEx ? sqlEx.Number.ToString() : base.GetErrorCode(ex);
    }
}
