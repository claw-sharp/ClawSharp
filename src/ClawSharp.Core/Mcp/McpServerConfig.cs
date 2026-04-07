namespace ClawSharp.Core;

public sealed record McpOAuthConfig(
    string? ClientId,
    int? CallbackPort,
    string? AuthServerMetadataUrl,
    bool? Xaa);

public abstract record McpServerConfig(string Type);

public sealed record McpStdioServerConfig(
    string Command,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string>? Env) : McpServerConfig("stdio");

public sealed record McpSseServerConfig(
    string Url,
    IReadOnlyDictionary<string, string>? Headers,
    string? HeadersHelper,
    McpOAuthConfig? OAuth) : McpServerConfig("sse");

public sealed record McpSseIdeServerConfig(
    string Url,
    string IdeName,
    bool? IdeRunningInWindows) : McpServerConfig("sse-ide");

public sealed record McpWebSocketIdeServerConfig(
    string Url,
    string IdeName,
    string? AuthToken,
    bool? IdeRunningInWindows) : McpServerConfig("ws-ide");

public sealed record McpHttpServerConfig(
    string Url,
    IReadOnlyDictionary<string, string>? Headers,
    string? HeadersHelper,
    McpOAuthConfig? OAuth) : McpServerConfig("http");

public sealed record McpWebSocketServerConfig(
    string Url,
    IReadOnlyDictionary<string, string>? Headers,
    string? HeadersHelper) : McpServerConfig("ws");

public sealed record McpSdkServerConfig(string Name) : McpServerConfig("sdk");

public sealed record McpClaudeAiProxyServerConfig(
    string Url,
    string Id) : McpServerConfig("claudeai-proxy");

public sealed record McpJsonConfig(
    IReadOnlyDictionary<string, McpServerConfig> McpServers);

public sealed record ScopedMcpServerConfig(
    string Name,
    McpServerConfig Config,
    McpConfigScope Scope,
    string? PluginSource = null)
{
    public string Type => Config.Type;
}
