using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SQLStudio.Core.AI;
using SQLStudio.Core.Database;

namespace SQLStudio.Core.Services;

public static class ServiceConfiguration
{
    public static IServiceCollection AddSqlStudioServices(this IServiceCollection services)
    {
        services.AddSingleton<ConnectionManager>();
        services.AddSingleton<SqlAgentService>();
        return services;
    }
}

public class ConnectionManager
{
    private readonly Dictionary<string, IDatabaseConnector> _connections = new();

    public async Task<IDatabaseConnector> CreateConnectionAsync(
        string connectionId,
        DatabaseType databaseType,
        DatabaseConnectionConfig config,
        CancellationToken cancellationToken = default)
    {
        if (_connections.ContainsKey(connectionId))
        {
            await RemoveConnectionAsync(connectionId);
        }

        var connector = DatabaseConnectorFactory.Create(databaseType);
        await connector.ConnectAsync(config, cancellationToken);
        _connections[connectionId] = connector;
        return connector;
    }

    public IDatabaseConnector? GetConnection(string connectionId)
    {
        return _connections.TryGetValue(connectionId, out var connector) ? connector : null;
    }

    public async Task RemoveConnectionAsync(string connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var connector))
        {
            await connector.DisposeAsync();
            _connections.Remove(connectionId);
        }
    }

    public IEnumerable<string> GetConnectionIds() => _connections.Keys;

    public async ValueTask DisposeAllAsync()
    {
        foreach (var connector in _connections.Values)
        {
            await connector.DisposeAsync();
        }
        _connections.Clear();
    }
}

public class SqlAgentService
{
    private readonly ConnectionManager _connectionManager;
    private ISqlGeneratorAgent? _sqlGenerator;
    private AiServiceConfig? _aiConfig;

    public SqlAgentService(ConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    public void ConfigureAi(AiServiceConfig config)
    {
        _aiConfig = config;
        _sqlGenerator = AiServiceFactory.CreateSqlGenerator(config);
    }

    public SqlAgentExecutor CreateExecutor(string connectionId, SqlAgentOptions? options = null)
    {
        if (_sqlGenerator == null)
        {
            throw new InvalidOperationException("AI service is not configured. Call ConfigureAi first.");
        }

        var connector = _connectionManager.GetConnection(connectionId);
        if (connector == null)
        {
            throw new InvalidOperationException($"Connection '{connectionId}' not found.");
        }

        return new SqlAgentExecutor(_sqlGenerator, connector, options);
    }

    public async Task<SqlAgentResult> ExecuteQueryAsync(
        string connectionId,
        string userQuery,
        string? additionalContext = null,
        SqlAgentOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var executor = CreateExecutor(connectionId, options);
        return await executor.ExecuteAsync(userQuery, additionalContext, null, cancellationToken);
    }
}
