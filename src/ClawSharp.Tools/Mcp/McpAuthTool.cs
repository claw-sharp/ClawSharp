using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools.Mcp;

public sealed class McpAuthTool : IClawSharpTool
{
    private readonly string _serverName;
    private readonly ScopedMcpServerConfig _config;
    private readonly string _transport;
    private readonly string _location;

    public McpAuthTool(string serverName, ScopedMcpServerConfig config)
    {
        _serverName = serverName;
        _config = config;
        _transport = config.Type ?? "stdio";
        _location = ResolveLocation(config.Config, _transport);

        var fullyQualifiedName = $"mcp__{NormalizeName(_serverName)}__authenticate";
        var description =
            $"The `{serverName}` MCP server ({_location}) is installed but requires authentication. " +
            $"Call this tool to start the OAuth flow — you'll receive an authorization URL to share with the user. " +
            $"Once the user completes authorization in their browser, the server's real tools will become available automatically.";

        Descriptor = new ToolDescriptor(
            fullyQualifiedName,
            description,
            InputSchema: new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(), ["additionalProperties"] = false },
            OutputSchema: new JsonObject { ["type"] = "object" });
    }

    public ToolDescriptor Descriptor { get; }

    public bool IsEnabled() => true;
    public bool IsConcurrencySafe(string arguments) => false;
    public bool IsReadOnly(string arguments) => false;

    public string? RenderToolUseProgressMessage(IReadOnlyList<ToolProgressUpdate> progressMessages) => $"Authenticating {_serverName} (MCP)";
    public string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages) => content;

    public Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default) 
        => Task.FromResult(ToolValidationResult.Valid());

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (context.ToolRegistry == null)
        {
            return new ToolExecutionResult(false, "Tool registry is not available.");
        }

        if (context.McpToolRuntimeCoordinator == null)
        {
            return new ToolExecutionResult(false, "MCP tool runtime coordinator is not available.");
        }

        if (_config.Type == "claudeai-proxy")
        {
            return new ToolExecutionResult(true, $"This is a claude.ai MCP connector. Ask the user to run /mcp and select \"{_serverName}\" to authenticate.");
        }

        if (_config.Type != "sse" && _config.Type != "http")
        {
            return new ToolExecutionResult(true, $"Server \"{_serverName}\" uses {_transport} transport which does not support OAuth from this tool. Ask the user to run /mcp and authenticate manually.");
        }

        context.ReportProgress(Descriptor.Name, new JsonObject
        {
            ["status"] = "starting_oauth",
            ["server"] = _serverName
        });

        var result = await context.McpToolRuntimeCoordinator.AuthenticateAsync(
            _serverName,
            _config,
            context.ToolRegistry,
            Descriptor.Name,
            cancellationToken).ConfigureAwait(false);

        context.ReportProgress(Descriptor.Name, new JsonObject
        {
            ["status"] = result.Success ? "authenticated" : "authentication_failed",
            ["server"] = _serverName
        });

        return new ToolExecutionResult(result.Success, result.Message);
    }

    private static string NormalizeName(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_');
        }
        return builder.ToString();
    }

    private static string ResolveLocation(McpServerConfig config, string fallbackTransport)
    {
        return config switch
        {
            McpHttpServerConfig http => http.Url,
            McpSseServerConfig sse => sse.Url,
            McpSseIdeServerConfig sseIde => sseIde.Url,
            McpWebSocketServerConfig webSocket => webSocket.Url,
            McpWebSocketIdeServerConfig webSocketIde => webSocketIde.Url,
            McpClaudeAiProxyServerConfig claudeAiProxy => claudeAiProxy.Url,
            McpSdkServerConfig sdk => sdk.Name,
            McpStdioServerConfig stdio => stdio.Command,
            _ => fallbackTransport
        };
    }
}
