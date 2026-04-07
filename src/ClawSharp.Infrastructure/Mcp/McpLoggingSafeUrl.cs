using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public static class McpLoggingSafeUrl
{
    public static string? GetLoggingSafeMcpBaseUrl(McpServerConfig config)
    {
        if (config is not McpSseServerConfig && config is not McpHttpServerConfig)
        {
            return null;
        }

        var url = config switch
        {
            McpSseServerConfig sse => sse.Url,
            McpHttpServerConfig http => http.Url,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        var builder = new UriBuilder(parsed)
        {
            Query = string.Empty
        };

        return builder.Uri.ToString().TrimEnd('/');
    }
}
