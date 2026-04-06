// TS origin: ./constants/product.ts, ./bridge/sessionIdCompat.ts
namespace ClawSharp.Bridge;

public static class BridgeProductUrls
{
    public const string ProductUrl = "https://claude.com/claude-code";
    public const string ClaudeAiBaseUrl = "https://claude.ai";
    public const string ClaudeAiStagingBaseUrl = "https://claude-ai.staging.ant.dev";
    public const string ClaudeAiLocalBaseUrl = "http://localhost:4000";

    public static bool IsRemoteSessionStaging(string? sessionId = null, string? ingressUrl = null)
    {
        return sessionId?.Contains("_staging_", StringComparison.Ordinal) == true ||
               ingressUrl?.Contains("staging", StringComparison.Ordinal) == true;
    }

    public static bool IsRemoteSessionLocal(string? sessionId = null, string? ingressUrl = null)
    {
        return sessionId?.Contains("_local_", StringComparison.Ordinal) == true ||
               ingressUrl?.Contains("localhost", StringComparison.Ordinal) == true;
    }

    public static string GetClaudeAiBaseUrl(string? sessionId = null, string? ingressUrl = null)
    {
        if (IsRemoteSessionLocal(sessionId, ingressUrl))
        {
            return ClaudeAiLocalBaseUrl;
        }

        if (IsRemoteSessionStaging(sessionId, ingressUrl))
        {
            return ClaudeAiStagingBaseUrl;
        }

        return ClaudeAiBaseUrl;
    }

    public static string GetRemoteSessionUrl(string sessionId, string? ingressUrl = null)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        var compatId = BridgeSessionIdCompat.ToCompatSessionId(sessionId);
        var baseUrl = GetClaudeAiBaseUrl(compatId, ingressUrl);
        return $"{baseUrl}/code/{compatId}";
    }
}
