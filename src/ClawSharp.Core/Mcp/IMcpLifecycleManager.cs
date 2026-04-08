namespace ClawSharp.Core;

public interface IMcpLifecycleManager
{
    Task<McpServerConnection> ConnectToServerAsync(
        string name,
        ScopedMcpServerConfig server,
        McpServerConnectionStatistics? serverStatistics = null,
        CancellationToken cancellationToken = default);

    Task ClearServerCacheAsync(
        string name,
        ScopedMcpServerConfig server,
        CancellationToken cancellationToken = default);

    Task<McpServerConnection> ReconnectToServerAsync(
        string name,
        ScopedMcpServerConfig server,
        McpServerConnectionStatistics? serverStatistics = null,
        CancellationToken cancellationToken = default);
}
