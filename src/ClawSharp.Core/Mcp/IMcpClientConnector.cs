namespace ClawSharp.Core;

public interface IMcpClientConnector
{
    Task<McpServerConnection> ConnectAsync(
        string name,
        ScopedMcpServerConfig server,
        McpServerConnectionStatistics? serverStatistics = null,
        CancellationToken cancellationToken = default);
}
