using ClawSharp.Core;

namespace ClawSharp.Tools.Mcp;

public interface IMcpToolRuntimeCoordinator
{
    Task<McpToolAuthenticationResult> AuthenticateAsync(
        string serverName,
        ScopedMcpServerConfig config,
        ToolRegistry toolRegistry,
        string authenticateToolName,
        CancellationToken cancellationToken = default);
}

public sealed record McpToolAuthenticationResult(
    bool Success,
    string Message);
