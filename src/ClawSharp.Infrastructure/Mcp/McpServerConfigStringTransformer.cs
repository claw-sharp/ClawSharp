using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

internal static class McpServerConfigStringTransformer
{
    public static McpServerConfig Transform(McpServerConfig config, Func<string, string> transform)
    {
        return config switch
        {
            McpStdioServerConfig stdio => stdio with
            {
                Command = transform(stdio.Command),
                Args = stdio.Args.Select(transform).ToArray(),
                Env = TransformDictionary(stdio.Env, transform)
            },
            McpSseServerConfig sse => sse with
            {
                Url = transform(sse.Url),
                Headers = TransformDictionary(sse.Headers, transform),
                HeadersHelper = TransformOptionalString(sse.HeadersHelper, transform),
                OAuth = TransformOAuth(sse.OAuth, transform)
            },
            McpHttpServerConfig http => http with
            {
                Url = transform(http.Url),
                Headers = TransformDictionary(http.Headers, transform),
                HeadersHelper = TransformOptionalString(http.HeadersHelper, transform),
                OAuth = TransformOAuth(http.OAuth, transform)
            },
            McpWebSocketServerConfig webSocket => webSocket with
            {
                Url = transform(webSocket.Url),
                Headers = TransformDictionary(webSocket.Headers, transform),
                HeadersHelper = TransformOptionalString(webSocket.HeadersHelper, transform)
            },
            McpSseIdeServerConfig sseIde => sseIde with
            {
                Url = transform(sseIde.Url),
                IdeName = transform(sseIde.IdeName)
            },
            McpWebSocketIdeServerConfig webSocketIde => webSocketIde with
            {
                Url = transform(webSocketIde.Url),
                IdeName = transform(webSocketIde.IdeName),
                AuthToken = TransformOptionalString(webSocketIde.AuthToken, transform)
            },
            McpSdkServerConfig sdk => sdk with
            {
                Name = transform(sdk.Name)
            },
            McpClaudeAiProxyServerConfig proxy => proxy with
            {
                Url = transform(proxy.Url),
                Id = transform(proxy.Id)
            },
            _ => config
        };
    }

    private static IReadOnlyDictionary<string, string>? TransformDictionary(
        IReadOnlyDictionary<string, string>? values,
        Func<string, string> transform)
    {
        return values?.ToDictionary(pair => pair.Key, pair => transform(pair.Value), StringComparer.Ordinal);
    }

    private static string? TransformOptionalString(string? value, Func<string, string> transform)
    {
        return value is null ? null : transform(value);
    }

    private static McpOAuthConfig? TransformOAuth(McpOAuthConfig? config, Func<string, string> transform)
    {
        return config is null
            ? null
            : config with
            {
                ClientId = TransformOptionalString(config.ClientId, transform),
                AuthServerMetadataUrl = TransformOptionalString(config.AuthServerMetadataUrl, transform)
            };
    }
}
