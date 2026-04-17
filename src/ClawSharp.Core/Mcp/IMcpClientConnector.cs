namespace ClawSharp.Core;

public interface IMcpClientConnector
{
    Task<McpServerConnection> ConnectAsync(
        string name,
        ScopedMcpServerConfig server,
        McpServerConnectionStatistics? serverStatistics = null,
        bool allowInteractiveAuth = true,
        CancellationToken cancellationToken = default);
}
