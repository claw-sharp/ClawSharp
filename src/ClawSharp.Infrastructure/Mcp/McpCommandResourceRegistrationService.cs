using System.Text;
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Tools;

namespace ClawSharp.Infrastructure;

public sealed class McpCommandResourceRegistrationService
{
    private readonly McpLifecycleManager _lifecycleManager;
    private readonly McpPromptCommandRegistry _promptCommands;
    private readonly McpResourceCatalog _resourceCatalog;

    public McpCommandResourceRegistrationService(
        McpLifecycleManager lifecycleManager,
        McpPromptCommandRegistry promptCommands,
        McpResourceCatalog resourceCatalog)
    {
        _lifecycleManager = lifecycleManager;
        _promptCommands = promptCommands;
        _resourceCatalog = resourceCatalog;
    }

    public async Task<IReadOnlyList<IMcpPromptCommandHandler>> FetchCommandsForConnectionAsync(
        McpServerConnection connection,
        CancellationToken cancellationToken = default)
    {
        if (connection is not ConnectedMcpServerConnection connected ||
            !connected.Capabilities.ContainsKey("prompts"))
        {
            return [];
        }

        var prompts = await connected.Client.ListPromptsAsync(cancellationToken).ConfigureAwait(false);
        return prompts
            .Select(prompt => (IMcpPromptCommandHandler)new RegisteredMcpPromptCommandHandler(_lifecycleManager, connected.Name, connected.Config, prompt))
            .ToArray();
    }

    public async Task<IReadOnlyList<McpServerResource>> FetchResourcesForConnectionAsync(
        McpServerConnection connection,
        CancellationToken cancellationToken = default)
    {
        if (connection is not ConnectedMcpServerConnection connected ||
            !connected.Capabilities.ContainsKey("resources"))
        {
            return [];
        }

        var resources = await connected.Client.ListResourcesAsync(cancellationToken).ConfigureAwait(false);
        return resources
            .Select(
                resource => new McpServerResource(
                    connected.Name,
                    resource.Uri,
                    resource.Name,
                    resource.MimeType,
                    resource.Description))
            .ToArray();
    }

    public async Task RegisterForConnectionsAsync(
        ToolRegistry toolRegistry,
        IReadOnlyList<McpServerConnection> connections,
        CancellationToken cancellationToken = default)
    {
        var shouldRegisterResourceTools = false;

        foreach (var connection in connections)
        {
            foreach (var command in await FetchCommandsForConnectionAsync(connection, cancellationToken).ConfigureAwait(false))
            {
                _promptCommands.RegisterOrReplace(command);
            }

            if (connection is not ConnectedMcpServerConnection connected)
            {
                continue;
            }

            var resources = await FetchResourcesForConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (resources.Count > 0)
            {
                _resourceCatalog.RegisterOrReplace(connected.Name, connected.Config, resources);
            }

            shouldRegisterResourceTools |= connected.Capabilities.ContainsKey("resources");
        }

        if (shouldRegisterResourceTools)
        {
            toolRegistry.RegisterOrReplace(new ListMcpResourcesTool(_resourceCatalog));
            toolRegistry.RegisterOrReplace(new ReadMcpResourceTool(_resourceCatalog, _lifecycleManager));
        }
    }

    private sealed class RegisteredMcpPromptCommandHandler : IMcpPromptCommandHandler
    {
        private const int MaxSessionRetries = 1;
        private readonly McpLifecycleManager _lifecycleManager;
        private readonly string _serverName;
        private readonly ScopedMcpServerConfig _config;
        private readonly McpPromptDefinition _prompt;
        private readonly string[] _argumentNames;

        public RegisteredMcpPromptCommandHandler(
            McpLifecycleManager lifecycleManager,
            string serverName,
            ScopedMcpServerConfig config,
            McpPromptDefinition prompt)
        {
            _lifecycleManager = lifecycleManager;
            _serverName = serverName;
            _config = config;
            _prompt = prompt;
            _argumentNames = (prompt.Arguments ?? []).Select(argument => argument.Name).ToArray();
            Descriptor = new CommandDescriptor(
                $"mcp__{McpToolRegistrationService.NormalizeNameForMcp(serverName)}__{prompt.Name}",
                prompt.Description ?? string.Empty,
                $"/mcp__{McpToolRegistrationService.NormalizeNameForMcp(serverName)}__{prompt.Name}");
        }

        public CommandDescriptor Descriptor { get; }

        public async Task<IReadOnlyList<ChatMessage>> GetPromptMessagesAsync(
            string arguments,
            CommandExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            var argsByName = ZipPromptArguments(arguments);
            for (var attempt = 0; ; attempt++)
            {
                var connection = await _lifecycleManager.ConnectToServerAsync(_serverName, _config, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (connection is not ConnectedMcpServerConnection connected)
                {
                    throw new InvalidOperationException($"MCP server '{_serverName}' is not connected.");
                }

                try
                {
                    var promptResult = await connected.Client.GetPromptAsync(_prompt.Name, argsByName, cancellationToken).ConfigureAwait(false);
                    return promptResult.Messages;
                }
                catch (Exception exception) when (attempt < MaxSessionRetries && ShouldRetry(exception))
                {
                    await _lifecycleManager.ClearServerCacheAsync(_serverName, _config, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private IReadOnlyDictionary<string, string?> ZipPromptArguments(string arguments)
        {
            var argsArray = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var zipped = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var index = 0; index < _argumentNames.Length; index++)
            {
                zipped[_argumentNames[index]] = index < argsArray.Length ? argsArray[index] : null;
            }

            return zipped;
        }

        private bool ShouldRetry(Exception exception)
        {
            return McpReconnectClassifier.IsSessionExpiredError(exception) ||
                   McpReconnectClassifier.IsConnectionClosedOnHttp(exception, _config);
        }
    }

    private sealed class ListMcpResourcesTool : IClawSharpTool
    {
        private readonly McpResourceCatalog _resourceCatalog;

        public ListMcpResourcesTool(McpResourceCatalog resourceCatalog)
        {
            _resourceCatalog = resourceCatalog;
            Descriptor = new ToolDescriptor(
                "ListMcpResourcesTool",
                "List resources from connected MCP servers",
                InputSchema: new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["server"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Optional server name to filter resources by"
                        }
                    },
                    ["additionalProperties"] = false
                },
                OutputSchema: new JsonObject
                {
                    ["type"] = "array"
                },
                SearchHint: "list resources from connected MCP servers",
                ShouldDefer: true);
        }

        public ToolDescriptor Descriptor { get; }

        public bool IsEnabled() => true;
        public bool IsConcurrencySafe(string arguments) => true;
        public bool IsReadOnly(string arguments) => true;
        public string? RenderToolUseProgressMessage(IReadOnlyList<ToolProgressUpdate> progressMessages) => null;
        public string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages) => content;
        public Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default) => Task.FromResult(ToolValidationResult.Valid());

        public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            var server = ParseServerFilter(context.Arguments);
            var resources = _resourceCatalog.GetResources(server);
            var output = resources.Count == 0
                ? "No resources found. MCP servers may still provide tools even if they have no resources."
                : JsonSerializerHelper(resources);
            return Task.FromResult(new ToolExecutionResult(true, output));
        }

        private static string? ParseServerFilter(string arguments)
        {
            var parsed = JsonNode.Parse(arguments);
            return parsed?["server"]?.GetValue<string>();
        }
    }

    private sealed class ReadMcpResourceTool : IClawSharpTool
    {
        private const int MaxSessionRetries = 1;
        private readonly McpResourceCatalog _resourceCatalog;
        private readonly McpLifecycleManager _lifecycleManager;

        public ReadMcpResourceTool(McpResourceCatalog resourceCatalog, McpLifecycleManager lifecycleManager)
        {
            _resourceCatalog = resourceCatalog;
            _lifecycleManager = lifecycleManager;
            Descriptor = new ToolDescriptor(
                "ReadMcpResourceTool",
                "Read a specific MCP resource by URI",
                InputSchema: new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["server"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "The MCP server name"
                        },
                        ["uri"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "The resource URI to read"
                        }
                    },
                    ["required"] = new JsonArray("server", "uri"),
                    ["additionalProperties"] = false
                },
                OutputSchema: new JsonObject
                {
                    ["type"] = "object"
                },
                SearchHint: "read a specific MCP resource by URI",
                ShouldDefer: true);
        }

        public ToolDescriptor Descriptor { get; }

        public bool IsEnabled() => true;
        public bool IsConcurrencySafe(string arguments) => true;
        public bool IsReadOnly(string arguments) => true;
        public string? RenderToolUseProgressMessage(IReadOnlyList<ToolProgressUpdate> progressMessages) => null;
        public string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages) => content;
        public Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default) => Task.FromResult(ToolValidationResult.Valid());

        public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            var input = ParseInput(context.Arguments);
            if (!_resourceCatalog.TryGetServer(input.Server, out var resourceSet) || resourceSet is null)
            {
                return new ToolExecutionResult(false, $"Server '{input.Server}' not found.");
            }

            McpReadResourceResult result;
            for (var attempt = 0; ; attempt++)
            {
                var connection = await _lifecycleManager.ConnectToServerAsync(input.Server, resourceSet.Config, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (connection is not ConnectedMcpServerConnection connected)
                {
                    return new ToolExecutionResult(false, $"Server '{input.Server}' is not connected");
                }

                if (!connected.Capabilities.ContainsKey("resources"))
                {
                    return new ToolExecutionResult(false, $"Server '{input.Server}' does not support resources");
                }

                try
                {
                    result = await connected.Client.ReadResourceAsync(input.Uri, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (Exception exception) when (attempt < MaxSessionRetries && ShouldRetry(exception, resourceSet.Config))
                {
                    await _lifecycleManager.ClearServerCacheAsync(input.Server, resourceSet.Config, cancellationToken).ConfigureAwait(false);
                }
            }

            var contents = await Task.WhenAll(
                result.Contents.Select((content, index) => ConvertContentAsync(content, input.Server, context.Session, index, cancellationToken)))
                .ConfigureAwait(false);

            var output = new JsonObject
            {
                ["contents"] = new JsonArray(contents.Cast<JsonNode?>().ToArray())
            };

            return new ToolExecutionResult(true, output.ToJsonString(), output);
        }

        private static async Task<JsonObject> ConvertContentAsync(
            McpResourceContent content,
            string serverName,
            ConversationSession session,
            int index,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = new JsonObject
            {
                ["uri"] = content.Uri
            };

            if (!string.IsNullOrWhiteSpace(content.MimeType))
            {
                result["mimeType"] = content.MimeType;
            }

            if (content.Text is not null)
            {
                result["text"] = content.Text;
                return result;
            }

            if (content.Blob is not null)
            {
                var persisted = await PersistBinaryContentAsync(content.Blob, content.MimeType, session, serverName, index, cancellationToken)
                    .ConfigureAwait(false);
                result["text"] = persisted.Message;
                if (persisted.FilePath is not null)
                {
                    result["blobSavedTo"] = persisted.FilePath;
                }
            }

            return result;
        }

        private static async Task<(string Message, string? FilePath)> PersistBinaryContentAsync(
            byte[] blob,
            string? mimeType,
            ConversationSession session,
            string serverName,
            int index,
            CancellationToken cancellationToken)
        {
            var toolResultsDir = GetToolResultsDir(session);
            Directory.CreateDirectory(toolResultsDir);
            var extension = GetExtensionForMimeType(mimeType);
            var filePath = Path.Combine(toolResultsDir, $"mcp-resource-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{index}-{Guid.NewGuid():N}.{extension}");
            try
            {
                await File.WriteAllBytesAsync(filePath, blob, cancellationToken).ConfigureAwait(false);
                return ($"[Resource from {serverName}] Binary content ({mimeType ?? "unknown type"}, {FormatFileSize(blob.Length)}) saved to {filePath}", filePath);
            }
            catch (Exception exception)
            {
                return ($"Binary content could not be saved to disk: {exception.Message}", null);
            }
        }

        private static string GetToolResultsDir(ConversationSession session)
        {
            return Path.Combine(
                SessionStoragePaths.GetProjectDir(session.ProjectDirectory),
                session.Id,
                "tool-results");
        }

        private static string GetExtensionForMimeType(string? mimeType)
        {
            var normalized = mimeType?.Split(';')[0].Trim().ToLowerInvariant();
            return normalized switch
            {
                "application/pdf" => "pdf",
                "application/json" => "json",
                "text/csv" => "csv",
                "text/plain" => "txt",
                "text/html" => "html",
                "text/markdown" => "md",
                "image/png" => "png",
                "image/jpeg" => "jpg",
                "image/gif" => "gif",
                "image/webp" => "webp",
                _ => "bin"
            };
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }

            var kilobytes = bytes / 1024d;
            if (kilobytes < 1024)
            {
                return $"{kilobytes:0.#} KB";
            }

            return $"{kilobytes / 1024d:0.#} MB";
        }

        private static (string Server, string Uri) ParseInput(string arguments)
        {
            var parsed = JsonNode.Parse(arguments)?.AsObject()
                ?? throw new InvalidOperationException("Expected JSON object arguments.");
            return (
                parsed["server"]?.GetValue<string>() ?? throw new InvalidOperationException("Missing 'server'."),
                parsed["uri"]?.GetValue<string>() ?? throw new InvalidOperationException("Missing 'uri'."));
        }

        private static bool ShouldRetry(Exception exception, ScopedMcpServerConfig config)
        {
            return McpReconnectClassifier.IsSessionExpiredError(exception) ||
                   McpReconnectClassifier.IsConnectionClosedOnHttp(exception, config);
        }
    }

    private static string JsonSerializerHelper<T>(T value)
    {
        return System.Text.Json.JsonSerializer.Serialize(value);
    }
}
