using ClawSharp.Core;
using ClawSharp.Tools;
using ClawSharp.Tools.Mcp;

namespace ClawSharp.Infrastructure;

public sealed class McpToolRuntimeCoordinator : IMcpToolRuntimeCoordinator
{
    private readonly IMcpLifecycleManager _mcpLifecycle;
    private readonly McpToolRegistrationService _mcpToolRegistrationService;

    public McpToolRuntimeCoordinator(
        IMcpLifecycleManager mcpLifecycle,
        McpToolRegistrationService mcpToolRegistrationService)
    {
        _mcpLifecycle = mcpLifecycle;
        _mcpToolRegistrationService = mcpToolRegistrationService;
    }

    public async Task<McpToolAuthenticationResult> AuthenticateAsync(
        string serverName,
        ScopedMcpServerConfig config,
        ToolRegistry toolRegistry,
        string authenticateToolName,
        CancellationToken cancellationToken = default)
    {
        var connection = await _mcpLifecycle.ReconnectToServerAsync(
            serverName,
            config,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (connection is FailedMcpServerConnection failed)
        {
            return new McpToolAuthenticationResult(
                false,
                string.IsNullOrWhiteSpace(failed.Error)
                    ? $"Authentication for {serverName} failed."
                    : $"Authentication for {serverName} failed: {failed.Error}");
        }

        if (connection is NeedsAuthMcpServerConnection)
        {
            return new McpToolAuthenticationResult(
                false,
                $"Authentication for {serverName} did not complete. The browser should open for OAuth; once you finish sign-in, retry the request.");
        }

        await _mcpToolRegistrationService.RegisterToolsAsync(
            toolRegistry,
            [connection],
            cancellationToken).ConfigureAwait(false);

        toolRegistry.UnregisterWhere(name => string.Equals(name, authenticateToolName, StringComparison.OrdinalIgnoreCase));

        return new McpToolAuthenticationResult(
            true,
            $"Authenticated {serverName}. Its MCP tools are now available in this session; continue with the requested {serverName} action.");
    }
}
