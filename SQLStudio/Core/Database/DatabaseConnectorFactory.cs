using System;
using System.Collections.Generic;
using SQLStudio.Core.Database.Connectors;

namespace SQLStudio.Core.Database;

public enum DatabaseType
{
    MySQL,
    ClickHouse,
    PostgreSQL,
    SqlServer
}

public static class DatabaseConnectorFactory
{
    private static readonly Dictionary<DatabaseType, Func<IDatabaseConnector>> ConnectorFactories = new()
    {
        { DatabaseType.MySQL, () => new MySqlDatabaseConnector() },
        { DatabaseType.ClickHouse, () => new ClickHouseDatabaseConnector() },
        { DatabaseType.PostgreSQL, () => new PostgreSqlDatabaseConnector() },
        { DatabaseType.SqlServer, () => new SqlServerDatabaseConnector() }
    };

    public static IDatabaseConnector Create(DatabaseType databaseType)
    {
        if (ConnectorFactories.TryGetValue(databaseType, out var factory))
        {
            return factory();
        }
        
        throw new NotSupportedException($"Database type '{databaseType}' is not supported.");
    }

    public static IDatabaseConnector Create(string databaseTypeName)
    {
        if (Enum.TryParse<DatabaseType>(databaseTypeName, true, out var databaseType))
        {
            return Create(databaseType);
        }
        
        throw new NotSupportedException($"Database type '{databaseTypeName}' is not supported.");
    }

    public static void RegisterConnector(DatabaseType databaseType, Func<IDatabaseConnector> factory)
    {
        ConnectorFactories[databaseType] = factory;
    }

    public static IEnumerable<DatabaseType> GetSupportedDatabaseTypes()
    {
        return ConnectorFactories.Keys;
    }

    public static int GetDefaultPort(DatabaseType databaseType)
    {
        return databaseType switch
        {
            DatabaseType.MySQL => 3306,
            DatabaseType.ClickHouse => 8123,
            DatabaseType.PostgreSQL => 5432,
            DatabaseType.SqlServer => 1433,
            _ => 0
        };
    }
}
