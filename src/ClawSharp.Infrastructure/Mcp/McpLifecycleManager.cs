using System.Collections.Concurrent;
using System.Text.Json;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class McpLifecycleManager : IMcpLifecycleManager
{
    private readonly IMcpClientConnector _connector;
    private readonly McpAuthStateService? _authStateService;
    private readonly McpNeedsAuthCache? _needsAuthCache;
    private readonly ConcurrentDictionary<string, Lazy<Task<McpServerConnection>>> _connectionCache =
        new(StringComparer.Ordinal);

    public McpLifecycleManager(
        IMcpClientConnector connector,
        McpAuthStateService? authStateService = null,
        McpNeedsAuthCache? needsAuthCache = null)
    {
        _connector = connector;
        _authStateService = authStateService;
        _needsAuthCache = needsAuthCache;
    }

    public static int GetMcpServerConnectionBatchSize()
    {
        return int.TryParse(Environment.GetEnvironmentVariable("MCP_SERVER_CONNECTION_BATCH_SIZE"), out var value) &&
               value > 0
            ? value
            : 3;
    }

    public static int GetRemoteMcpServerConnectionBatchSize()
    {
        return int.TryParse(Environment.GetEnvironmentVariable("MCP_REMOTE_SERVER_CONNECTION_BATCH_SIZE"), out var value) &&
               value > 0
            ? value
            : 20;
    }

    public static bool IsLocalMcpServer(ScopedMcpServerConfig config)
    {
        return config.Type is "stdio" or "sdk";
    }

    public static string GetServerCacheKey(string name, ScopedMcpServerConfig server)
    {
        return $"{name}-{JsonSerializer.Serialize(server)}";
    }

    public Task<McpServerConnection> ConnectToServerAsync(
        string name,
        ScopedMcpServerConfig server,
        McpServerConnectionStatistics? serverStatistics = null,
        CancellationToken cancellationToken = default)
    {
        return ConnectToServerCoreAsync(name, server, serverStatistics, allowInteractiveAuth: true, cancellationToken);
    }

    public Task<McpServerConnection> ConnectToServerWithoutInteractiveAuthAsync(
        string name,
        ScopedMcpServerConfig server,
        McpServerConnectionStatistics? serverStatistics = null,
        CancellationToken cancellationToken = default)
    {
        return ConnectToServerCoreAsync(name, server, serverStatistics, allowInteractiveAuth: false, cancellationToken);
    }

    private Task<McpServerConnection> ConnectToServerCoreAsync(
        string name,
        ScopedMcpServerConfig server,
        McpServerConnectionStatistics? serverStatistics,
        bool allowInteractiveAuth,
        CancellationToken cancellationToken)
    {
        var cacheKey = GetServerCacheKey(name, server);
        var lazyTask = _connectionCache.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<McpServerConnection>>(
                () => _connector.ConnectAsync(name, server, serverStatistics, allowInteractiveAuth, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyTask.Value;
    }

    public async Task ClearServerCacheAsync(
        string name,
        ScopedMcpServerConfig server,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GetServerCacheKey(name, server);
        if (!_connectionCache.TryRemove(cacheKey, out var cached) || !cached.IsValueCreated)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var connection = await cached.Value.ConfigureAwait(false);
        if (connection is ConnectedMcpServerConnection connected)
        {
            await connected.CleanupAsync().ConfigureAwait(false);
        }
    }

    public async Task<McpServerConnection> ReconnectToServerAsync(
        string name,
        ScopedMcpServerConfig server,
        McpServerConnectionStatistics? serverStatistics = null,
        CancellationToken cancellationToken = default)
    {
        return await ReconnectToServerCoreAsync(name, server, serverStatistics, allowInteractiveAuth: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<McpServerConnection> ReconnectToServerWithoutInteractiveAuthAsync(
        string name,
        ScopedMcpServerConfig server,
        McpServerConnectionStatistics? serverStatistics = null,
        CancellationToken cancellationToken = default)
    {
        return await ReconnectToServerCoreAsync(name, server, serverStatistics, allowInteractiveAuth: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpServerConnection> ReconnectToServerCoreAsync(
        string name,
        ScopedMcpServerConfig server,
        McpServerConnectionStatistics? serverStatistics,
        bool allowInteractiveAuth,
        CancellationToken cancellationToken)
    {
        await ClearServerCacheAsync(name, server, cancellationToken).ConfigureAwait(false);
        return await ConnectToServerCoreAsync(name, server, serverStatistics, allowInteractiveAuth, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<McpServerConnection>> ConnectServersAsync(
        IReadOnlyDictionary<string, ScopedMcpServerConfig> servers,
        Func<string, bool>? isDisabled = null,
        Action<McpServerConnection>? onConnectionAttempt = null,
        CancellationToken cancellationToken = default)
    {
        return await ConnectServersCoreAsync(
            servers,
            isDisabled,
            onConnectionAttempt,
            allowInteractiveAuth: true,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<McpServerConnection>> ConnectServersWithoutInteractiveAuthAsync(
        IReadOnlyDictionary<string, ScopedMcpServerConfig> servers,
        Func<string, bool>? isDisabled = null,
        Action<McpServerConnection>? onConnectionAttempt = null,
        CancellationToken cancellationToken = default)
    {
        return await ConnectServersCoreAsync(
            servers,
            isDisabled,
            onConnectionAttempt,
            allowInteractiveAuth: false,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<McpServerConnection>> ConnectServersCoreAsync(
        IReadOnlyDictionary<string, ScopedMcpServerConfig> servers,
        Func<string, bool>? isDisabled,
        Action<McpServerConnection>? onConnectionAttempt,
        bool allowInteractiveAuth,
        CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<McpServerConnection>();
        var activeServers = new List<KeyValuePair<string, ScopedMcpServerConfig>>();
        foreach (var server in servers)
        {
            if (isDisabled?.Invoke(server.Key) == true)
            {
                var disabled = new DisabledMcpServerConnection(server.Key, server.Value);
                results.Add(disabled);
                onConnectionAttempt?.Invoke(disabled);
                continue;
            }

            activeServers.Add(server);
        }

        var statistics = BuildStatistics(activeServers);
        var localServers = activeServers.Where(entry => IsLocalMcpServer(entry.Value)).ToArray();
        var remoteServers = activeServers.Where(entry => !IsLocalMcpServer(entry.Value)).ToArray();

        await Task.WhenAll(
            ProcessBatchedAsync(localServers, GetMcpServerConnectionBatchSize(), statistics, results, onConnectionAttempt, allowInteractiveAuth, cancellationToken),
            ProcessBatchedAsync(remoteServers, GetRemoteMcpServerConnectionBatchSize(), statistics, results, onConnectionAttempt, allowInteractiveAuth, cancellationToken)).ConfigureAwait(false);

        return results
            .OrderBy(connection => connection.Name, GetNameComparer())
            .ToArray();
    }

    private async Task ProcessBatchedAsync(
        IReadOnlyList<KeyValuePair<string, ScopedMcpServerConfig>> servers,
        int concurrency,
        McpServerConnectionStatistics statistics,
        ConcurrentBag<McpServerConnection> results,
        Action<McpServerConnection>? onConnectionAttempt,
        bool allowInteractiveAuth,
        CancellationToken cancellationToken)
    {
        using var throttler = new SemaphoreSlim(concurrency);
        var tasks = servers.Select(async server =>
        {
            await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (await ShouldReturnNeedsAuthAsync(server.Key, server.Value, cancellationToken).ConfigureAwait(false))
                {
                    var needsAuth = new NeedsAuthMcpServerConnection(server.Key, server.Value);
                    results.Add(needsAuth);
                    onConnectionAttempt?.Invoke(needsAuth);
                    return;
                }

                var connection = await ConnectToServerCoreAsync(server.Key, server.Value, statistics, allowInteractiveAuth, cancellationToken).ConfigureAwait(false);
                results.Add(connection);
                onConnectionAttempt?.Invoke(connection);
            }
            finally
            {
                throttler.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<bool> ShouldReturnNeedsAuthAsync(
        string name,
        ScopedMcpServerConfig server,
        CancellationToken cancellationToken)
    {
        if (server.Type is not "http" and not "sse")
        {
            return false;
        }

        if (_needsAuthCache is not null &&
            await _needsAuthCache.IsCachedAsync(name, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return _authStateService is not null &&
               _authStateService.HasDiscoveryButNoToken(name, server.Config);
    }

    private static McpServerConnectionStatistics BuildStatistics(IReadOnlyList<KeyValuePair<string, ScopedMcpServerConfig>> servers)
    {
        return new McpServerConnectionStatistics(
            servers.Count,
            servers.Count(entry => entry.Value.Type == "stdio"),
            servers.Count(entry => entry.Value.Type == "sse"),
            servers.Count(entry => entry.Value.Type == "http"),
            servers.Count(entry => entry.Value.Type == "sse-ide"),
            servers.Count(entry => entry.Value.Type == "ws-ide"));
    }

    private static StringComparer GetNameComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }
}
