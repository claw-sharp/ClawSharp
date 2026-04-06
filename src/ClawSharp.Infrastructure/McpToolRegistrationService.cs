using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Tools;

namespace ClawSharp.Infrastructure;

public sealed class McpToolRegistrationService
{
    internal const int MaxMcpDescriptionLength = 2048;
    private const string ClaudeAiServerPrefix = "claude.ai ";

    private readonly McpLifecycleManager _lifecycleManager;

    public McpToolRegistrationService(McpLifecycleManager lifecycleManager)
    {
        _lifecycleManager = lifecycleManager;
    }

    public async Task<IReadOnlyList<IClawSharpTool>> FetchToolsForConnectionAsync(
        McpServerConnection connection,
        CancellationToken cancellationToken = default)
    {
        if (connection is not ConnectedMcpServerConnection connected)
        {
            return [];
        }

        if (!HasToolsCapability(connected.Capabilities))
        {
            return [];
        }

        var tools = await connected.Client.ListToolsAsync(cancellationToken).ConfigureAwait(false);
        var skipPrefix = ShouldSkipPrefix(connected.Config);

        return tools
            .Select(
                tool => (IClawSharpTool)new RegisteredMcpTool(
                    _lifecycleManager,
                    connected.Name,
                    connected.Config,
                    tool,
                    skipPrefix))
            .ToArray();
    }

    public async Task<IReadOnlyList<IClawSharpTool>> RegisterToolsAsync(
        ToolRegistry toolRegistry,
        IReadOnlyList<McpServerConnection> connections,
        CancellationToken cancellationToken = default)
    {
        var registered = new List<IClawSharpTool>();
        foreach (var connection in connections)
        {
            var tools = await FetchToolsForConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            foreach (var tool in tools)
            {
                toolRegistry.RegisterOrReplace(tool);
                registered.Add(tool);
            }
        }

        return registered;
    }

    internal static bool ShouldSkipPrefix(ScopedMcpServerConfig config)
    {
        return string.Equals(config.Type, "sdk", StringComparison.Ordinal) &&
               IsEnvTruthy(Environment.GetEnvironmentVariable("CLAUDE_AGENT_SDK_MCP_NO_PREFIX"));
    }

    internal static string NormalizeNameForMcp(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_');
        }

        var normalized = builder.ToString();
        if (name.StartsWith(ClaudeAiServerPrefix, StringComparison.Ordinal))
        {
            normalized = CollapseUnderscores(normalized).Trim('_');
        }

        return normalized;
    }

    internal static string BuildMcpToolName(string serverName, string toolName)
    {
        return $"mcp__{NormalizeNameForMcp(serverName)}__{NormalizeNameForMcp(toolName)}";
    }

    internal static string? NormalizeSearchHint(JsonObject? meta)
    {
        if (meta?["anthropic/searchHint"] is not JsonValue searchHintValue ||
            !searchHintValue.TryGetValue<string>(out var searchHint) ||
            string.IsNullOrWhiteSpace(searchHint))
        {
            return null;
        }

        var builder = new StringBuilder(searchHint.Length);
        var previousWhitespace = false;
        foreach (var character in searchHint)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                }

                previousWhitespace = true;
                continue;
            }

            builder.Append(character);
            previousWhitespace = false;
        }

        var collapsed = builder.ToString().Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }

    internal static string BuildPromptDescription(string? description)
    {
        description ??= string.Empty;
        return description.Length > MaxMcpDescriptionLength
            ? $"{description[..MaxMcpDescriptionLength]}… [truncated]"
            : description;
    }

    private static bool HasToolsCapability(IReadOnlyDictionary<string, object?> capabilities)
    {
        return capabilities.ContainsKey("tools");
    }

    private static bool IsEnvTruthy(string? value)
    {
        return value is not null &&
               !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static string CollapseUnderscores(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasUnderscore = false;
        foreach (var character in value)
        {
            if (character == '_')
            {
                if (!previousWasUnderscore)
                {
                    builder.Append(character);
                }

                previousWasUnderscore = true;
                continue;
            }

            builder.Append(character);
            previousWasUnderscore = false;
        }

        return builder.ToString();
    }

    private sealed class RegisteredMcpTool : IClawSharpTool
    {
        private const int MaxSessionRetries = 1;
        private readonly McpLifecycleManager _lifecycleManager;
        private readonly string _serverName;
        private readonly ScopedMcpServerConfig _serverConfig;
        private readonly McpToolDefinition _tool;

        public RegisteredMcpTool(
            McpLifecycleManager lifecycleManager,
            string serverName,
            ScopedMcpServerConfig serverConfig,
            McpToolDefinition tool,
            bool skipPrefix)
        {
            _lifecycleManager = lifecycleManager;
            _serverName = serverName;
            _serverConfig = serverConfig;
            _tool = tool;

            var fullyQualifiedName = BuildMcpToolName(serverName, tool.Name);
            Descriptor = new ToolDescriptor(
                skipPrefix ? tool.Name : fullyQualifiedName,
                tool.Description ?? string.Empty,
                Parameters: null,
                SearchHint: NormalizeSearchHint(tool.Meta),
                AlwaysLoad: tool.Meta?["anthropic/alwaysLoad"]?.GetValue<bool>() == true,
                InputSchema: tool.InputSchema?.DeepClone().AsObject(),
                OutputSchema: new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "MCP tool execution result"
                });
        }

        public ToolDescriptor Descriptor { get; }

        public bool IsEnabled()
        {
            return true;
        }

        public bool IsConcurrencySafe(string arguments)
        {
            return _tool.Annotations?.ReadOnlyHint ?? false;
        }

        public bool IsReadOnly(string arguments)
        {
            return _tool.Annotations?.ReadOnlyHint ?? false;
        }

        public string? RenderToolUseProgressMessage(IReadOnlyList<ToolProgressUpdate> progressMessages)
        {
            return progressMessages.Count == 0 ? null : $"{_serverName} - {_tool.Name} (MCP) running";
        }

        public string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages)
        {
            return content;
        }

        public Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                ParseArguments(context.Arguments);
                return Task.FromResult(ToolValidationResult.Valid());
            }
            catch (JsonException exception)
            {
                return Task.FromResult(ToolValidationResult.Invalid($"Invalid MCP tool arguments JSON: {exception.Message}"));
            }
        }

        public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            var arguments = ParseArguments(context.Arguments);
            var startTime = DateTimeOffset.UtcNow;
            context.ReportProgress(
                Descriptor.Name,
                new JsonObject
                {
                    ["type"] = "mcp_progress",
                    ["status"] = "started",
                    ["serverName"] = _serverName,
                    ["toolName"] = _tool.Name
                });

            try
            {
                McpToolCallResult result;
                for (var attempt = 0; ; attempt++)
                {
                    var connected = await _lifecycleManager.ConnectToServerAsync(_serverName, _serverConfig, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (connected is not ConnectedMcpServerConnection activeConnection)
                    {
                        return new ToolExecutionResult(
                            false,
                            $"MCP server '{_serverName}' is not connected.");
                    }

                    try
                    {
                        result = await activeConnection.Client.CallToolAsync(
                            _tool.Name,
                            arguments,
                            notification => ReportSdkProgress(context, notification),
                            cancellationToken).ConfigureAwait(false);
                        break;
                    }
                    catch (Exception exception) when (attempt < MaxSessionRetries && ShouldRetry(exception))
                    {
                        await _lifecycleManager.ClearServerCacheAsync(_serverName, _serverConfig, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                context.ReportProgress(
                    Descriptor.Name,
                    new JsonObject
                    {
                        ["type"] = "mcp_progress",
                        ["status"] = "completed",
                        ["serverName"] = _serverName,
                        ["toolName"] = _tool.Name,
                        ["elapsedTimeMs"] = (long)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds
                    });

                return new ToolExecutionResult(true, result.Content, result.StructuredContent);
            }
            catch
            {
                context.ReportProgress(
                    Descriptor.Name,
                    new JsonObject
                    {
                        ["type"] = "mcp_progress",
                        ["status"] = "failed",
                        ["serverName"] = _serverName,
                        ["toolName"] = _tool.Name,
                        ["elapsedTimeMs"] = (long)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds
                    });
                throw;
            }
        }

        private bool ShouldRetry(Exception exception)
        {
            var isSessionExpired = McpReconnectClassifier.IsSessionExpiredError(exception);
            var isConnectionClosedOnHttp = McpReconnectClassifier.IsConnectionClosedOnHttp(exception, _serverConfig);
            return isSessionExpired || isConnectionClosedOnHttp;
        }

        private void ReportSdkProgress(ToolExecutionContext context, McpToolProgressNotification notification)
        {
            var progress = new JsonObject
            {
                ["type"] = "mcp_progress",
                ["status"] = "progress",
                ["serverName"] = _serverName,
                ["toolName"] = _tool.Name
            };

            if (notification.Progress is not null)
            {
                progress["progress"] = notification.Progress.Value;
            }

            if (notification.Total is not null)
            {
                progress["total"] = notification.Total.Value;
            }

            if (!string.IsNullOrWhiteSpace(notification.ProgressMessage))
            {
                progress["progressMessage"] = notification.ProgressMessage;
            }

            context.ReportProgress(Descriptor.Name, progress);
        }

        private static JsonObject ParseArguments(string arguments)
        {
            var parsed = JsonNode.Parse(arguments);
            if (parsed is not JsonObject jsonObject)
            {
                throw new JsonException("Expected a JSON object.");
            }

            return jsonObject;
        }
    }
}
