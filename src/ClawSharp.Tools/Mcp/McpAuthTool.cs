using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Tools.Registry;

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
        _location = (config.Config as dynamic)?.url ?? _transport;

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
        if (context.McpLifecycle == null)
        {
            return new ToolExecutionResult(false, "MCP lifecycle manager is not available.");
        }

        if (_config.Type == "claudeai-proxy")
        {
            return new ToolExecutionResult(true, $"This is a claude.ai MCP connector. Ask the user to run /mcp and select \"{_serverName}\" to authenticate.");
        }

        if (_config.Type != "sse" && _config.Type != "http")
        {
            return new ToolExecutionResult(true, $"Server \"{_serverName}\" uses {_transport} transport which does not support OAuth from this tool. Ask the user to run /mcp and authenticate manually.");
        }

        // Note: Full OAuth flow implementation in C# might require additional services not yet present.
        // For now, we mimic the TS behavior but we might need to implement performMCPOAuthFlow.
        
        return new ToolExecutionResult(true, $"OAuth flow for {_serverName} started. (Wait for implementation of OAuth flow in C# infrastructure)");
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
}
