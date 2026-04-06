// TS origin: ./services/mcp/client.ts
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

internal interface ISdkMcpClient : IAsyncDisposable
{
    ServerCapabilities ServerCapabilities { get; }
    Implementation ServerInfo { get; }
    string? ServerInstructions { get; }
    ValueTask<IList<McpClientTool>> ListToolsAsync(CancellationToken cancellationToken = default);
    ValueTask<IList<McpClientPrompt>> ListPromptsAsync(CancellationToken cancellationToken = default);
    ValueTask<GetPromptResult> GetPromptAsync(
        string name,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default);
    ValueTask<IList<McpClientResource>> ListResourcesAsync(CancellationToken cancellationToken = default);
    ValueTask<ReadResourceResult> ReadResourceAsync(string uri, CancellationToken cancellationToken = default);
    ValueTask SendNotificationAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default);
    ValueTask<CallToolResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default);
}

internal sealed class SdkMcpClientFacade : ISdkMcpClient
{
    private readonly McpClient _client;

    public SdkMcpClientFacade(McpClient client)
    {
        _client = client;
    }

    public ServerCapabilities ServerCapabilities => _client.ServerCapabilities;
    public Implementation ServerInfo => _client.ServerInfo;
    public string? ServerInstructions => _client.ServerInstructions;

    public ValueTask<IList<McpClientTool>> ListToolsAsync(CancellationToken cancellationToken = default)
        => _client.ListToolsAsync(cancellationToken: cancellationToken);

    public ValueTask<IList<McpClientPrompt>> ListPromptsAsync(CancellationToken cancellationToken = default)
        => _client.ListPromptsAsync(cancellationToken: cancellationToken);

    public ValueTask<GetPromptResult> GetPromptAsync(
        string name,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default)
        => _client.GetPromptAsync(name, arguments, cancellationToken: cancellationToken);

    public ValueTask<IList<McpClientResource>> ListResourcesAsync(CancellationToken cancellationToken = default)
        => _client.ListResourcesAsync(cancellationToken: cancellationToken);

    public ValueTask<ReadResourceResult> ReadResourceAsync(string uri, CancellationToken cancellationToken = default)
        => _client.ReadResourceAsync(uri, cancellationToken: cancellationToken);

    public ValueTask SendNotificationAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
        => parameters is null
            ? new ValueTask(_client.SendNotificationAsync(method, cancellationToken))
            : new ValueTask(_client.SendNotificationAsync(method, parameters, cancellationToken: cancellationToken));

    public ValueTask<CallToolResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
        => _client.CallToolAsync(toolName, arguments, progress, cancellationToken: cancellationToken);

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}

internal sealed class SdkMcpClientSession : IMcpClientSession
{
    private readonly ISdkMcpClient _client;
    private Func<McpElicitationRequestContext, Task<McpElicitResult>>? _elicitationRequestHandler;
    private Action<string>? _elicitationCompletionHandler;

    public SdkMcpClientSession(ISdkMcpClient client)
    {
        _client = client;
    }

    public ServerCapabilities ServerCapabilities => _client.ServerCapabilities;

    public Implementation ServerInfo => _client.ServerInfo;

    public string? ServerInstructions => _client.ServerInstructions;

    public async Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        var tools = await _client.ListToolsAsync(cancellationToken).ConfigureAwait(false);
        return tools.Select(
                tool => new McpToolDefinition(
                    tool.Name,
                    tool.Description,
                    ConvertToJsonObject(tool.ProtocolTool.InputSchema),
                    tool.ProtocolTool.Annotations is null
                        ? null
                        : new McpToolAnnotations(
                            tool.ProtocolTool.Annotations.ReadOnlyHint,
                            tool.ProtocolTool.Annotations.DestructiveHint,
                            tool.ProtocolTool.Annotations.OpenWorldHint,
                            tool.ProtocolTool.Annotations.Title),
                    tool.ProtocolTool.Meta?.DeepClone().AsObject()))
            .ToArray();
    }

    public async Task<IReadOnlyList<McpPromptDefinition>> ListPromptsAsync(CancellationToken cancellationToken = default)
    {
        var prompts = await _client.ListPromptsAsync(cancellationToken).ConfigureAwait(false);
        return prompts.Select(
                prompt => new McpPromptDefinition(
                    prompt.Name,
                    prompt.Description,
                    prompt.ProtocolPrompt.Arguments?.Select(
                            argument => new McpPromptArgumentDefinition(
                                argument.Name,
                                argument.Required == true,
                                argument.Description))
                        .ToArray()))
            .ToArray();
    }

    public async Task<McpPromptResult> GetPromptAsync(
        string promptName,
        IReadOnlyDictionary<string, string?> arguments,
        CancellationToken cancellationToken = default)
    {
        var sdkArguments = arguments.ToDictionary(
            pair => pair.Key,
            pair => (object?)pair.Value,
            StringComparer.Ordinal);
        var result = await _client.GetPromptAsync(promptName, sdkArguments, cancellationToken).ConfigureAwait(false);
        return new McpPromptResult(result.Messages.Select(ConvertPromptMessage).ToArray());
    }

    public async Task<IReadOnlyList<McpResourceDefinition>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        var resources = await _client.ListResourcesAsync(cancellationToken).ConfigureAwait(false);
        return resources.Select(
                resource => new McpResourceDefinition(
                    resource.Uri,
                    resource.Name,
                    resource.MimeType,
                    resource.Description))
            .ToArray();
    }

    public async Task<McpReadResourceResult> ReadResourceAsync(
        string uri,
        CancellationToken cancellationToken = default)
    {
        var result = await _client.ReadResourceAsync(uri, cancellationToken).ConfigureAwait(false);
        return new McpReadResourceResult(result.Contents.Select(ConvertResourceContent).ToArray());
    }

    public async Task SendNotificationAsync(
        string method,
        JsonObject? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await _client.SendNotificationAsync(method, parameters, cancellationToken).ConfigureAwait(false);
    }

    public async Task<McpToolCallResult> CallToolAsync(
        string toolName,
        JsonObject arguments,
        Action<McpToolProgressNotification>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var sdkArguments = arguments.ToDictionary(
            pair => pair.Key,
            pair => (object?)pair.Value?.Deserialize<object>());

        IProgress<ProgressNotificationValue>? progress = null;
        if (onProgress is not null)
        {
            progress = new Progress<ProgressNotificationValue>(
                value => onProgress(
                    new McpToolProgressNotification(
                        value.Progress,
                        value.Total,
                        value.Message)));
        }

        var result = await _client.CallToolAsync(toolName, sdkArguments, progress, cancellationToken).ConfigureAwait(false);
        var structuredContent = result.StructuredContent.HasValue
            ? JsonNode.Parse(result.StructuredContent.Value.GetRawText())
            : null;
        var meta = result.Meta?.DeepClone().AsObject();
        return new McpToolCallResult(
            FlattenToolResultContent(result.Content),
            meta,
            structuredContent);
    }

    public void SetElicitationRequestHandler(Func<McpElicitationRequestContext, Task<McpElicitResult>> handler)
    {
        _elicitationRequestHandler = handler;
    }

    public void SetElicitationCompletionHandler(Action<string> handler)
    {
        _elicitationCompletionHandler = handler;
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private static McpResourceContent ConvertResourceContent(ResourceContents content)
    {
        return content switch
        {
            TextResourceContents text => new McpResourceContent(text.Uri, text.MimeType, text.Text),
            BlobResourceContents blob => new McpResourceContent(blob.Uri, blob.MimeType, Blob: blob.Blob.ToArray()),
            _ => throw new InvalidOperationException($"Unsupported MCP resource content type '{content.GetType().Name}'.")
        };
    }

    private static ChatMessage ConvertPromptMessage(PromptMessage message)
    {
        var role = message.Role == Role.Assistant ? MessageRole.Assistant : MessageRole.User;
        var block = message.Content switch
        {
            TextContentBlock text => new MessageContentBlock(MessageContentKind.Text, text.Text),
            _ => new MessageContentBlock(MessageContentKind.Text, FlattenContentBlock(message.Content))
        };

        return new ChatMessage(Guid.NewGuid().ToString("N"), role, [block], DateTimeOffset.UtcNow);
    }

    private static JsonObject? ConvertToJsonObject(JsonElement element)
    {
        return JsonNode.Parse(element.GetRawText()) as JsonObject;
    }

    private static string FlattenToolResultContent(IList<ContentBlock> content)
    {
        if (content.Count == 0)
        {
            return string.Empty;
        }

        var parts = content.Select(FlattenContentBlock).Where(static part => !string.IsNullOrWhiteSpace(part)).ToArray();
        return string.Join(Environment.NewLine, parts);
    }

    private static string FlattenContentBlock(ContentBlock block)
    {
        return block switch
        {
            TextContentBlock text => text.Text,
            EmbeddedResourceBlock resourceBlock => resourceBlock.Resource switch
            {
                TextResourceContents text => $"[Resource at {text.Uri}] {text.Text}",
                BlobResourceContents blob => $"[Resource at {blob.Uri}] Binary content ({blob.MimeType ?? "unknown type"}, {blob.Blob.Length} bytes)",
                _ => "[Resource]"
            },
            ResourceLinkBlock link => $"[Resource link: {link.Name}] {link.Uri}{(string.IsNullOrWhiteSpace(link.Description) ? string.Empty : $" ({link.Description})")}",
            ImageContentBlock image => $"[Image content: {image.MimeType}, {image.Data.Length} bytes]",
            AudioContentBlock audio => $"[Audio content: {audio.MimeType}, {audio.Data.Length} bytes]",
            ToolResultContentBlock toolResult => FlattenToolResultContent(toolResult.Content),
            ToolUseContentBlock toolUse => $"[Tool use: {toolUse.Name}]",
            _ => block.ToString() ?? string.Empty
        };
    }
}
