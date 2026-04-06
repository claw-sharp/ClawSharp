// TS origin: ./services/mcp/types.ts
namespace ClawSharp.Core;

public sealed record McpServerInfo(
    string Name,
    string Version);

public abstract record McpServerConnection(
    string Name,
    McpConnectionStatus Status,
    ScopedMcpServerConfig Config);

public sealed record ConnectedMcpServerConnection(
    string Name,
    ScopedMcpServerConfig Config,
    IMcpClientSession Client,
    IReadOnlyDictionary<string, object?> Capabilities,
    Func<Task> CleanupAsync,
    McpServerInfo? ServerInfo = null,
    string? Instructions = null)
    : McpServerConnection(Name, McpConnectionStatus.Connected, Config);

public sealed record FailedMcpServerConnection(
    string Name,
    ScopedMcpServerConfig Config,
    string? Error = null)
    : McpServerConnection(Name, McpConnectionStatus.Failed, Config);

public sealed record NeedsAuthMcpServerConnection(
    string Name,
    ScopedMcpServerConfig Config)
    : McpServerConnection(Name, McpConnectionStatus.NeedsAuth, Config);

public sealed record PendingMcpServerConnection(
    string Name,
    ScopedMcpServerConfig Config,
    int? ReconnectAttempt = null,
    int? MaxReconnectAttempts = null)
    : McpServerConnection(Name, McpConnectionStatus.Pending, Config);

public sealed record DisabledMcpServerConnection(
    string Name,
    ScopedMcpServerConfig Config)
    : McpServerConnection(Name, McpConnectionStatus.Disabled, Config);
