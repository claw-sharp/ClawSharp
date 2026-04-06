// TS origin: ./services/mcp/client.ts
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class NullMcpClientConnector : IMcpClientConnector
{
    public Task<McpServerConnection> ConnectAsync(
        string name,
        ScopedMcpServerConfig server,
        McpServerConnectionStatistics? serverStatistics = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<McpServerConnection>(
            new FailedMcpServerConnection(
                name,
                server,
                "MCP transport connection is not implemented yet in ClawSharp."));
    }
}
