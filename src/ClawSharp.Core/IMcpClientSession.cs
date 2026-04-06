using System.Text.Json.Nodes;

namespace ClawSharp.Core;

public interface IMcpClientSession : IAsyncDisposable, IMcpElicitationSession
{
    Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<McpPromptDefinition>> ListPromptsAsync(CancellationToken cancellationToken = default);

    Task<McpPromptResult> GetPromptAsync(
        string promptName,
        IReadOnlyDictionary<string, string?> arguments,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<McpResourceDefinition>> ListResourcesAsync(CancellationToken cancellationToken = default);

    Task<McpReadResourceResult> ReadResourceAsync(
        string uri,
        CancellationToken cancellationToken = default);

    Task SendNotificationAsync(
        string method,
        JsonObject? parameters = null,
        CancellationToken cancellationToken = default);

    Task<McpToolCallResult> CallToolAsync(
        string toolName,
        JsonObject arguments,
        Action<McpToolProgressNotification>? onProgress = null,
        CancellationToken cancellationToken = default);
}
