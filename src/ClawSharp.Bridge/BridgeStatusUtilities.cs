namespace ClawSharp.Bridge;

public static class BridgeStatusUtilities
{
    public const int ToolDisplayExpiryMs = 30_000;
    public const int ShimmerIntervalMs = 150;
    public const string FailedFooterText = "Something went wrong, please try again";

    public static string Timestamp(DateTime? now = null)
    {
        var timestamp = now ?? DateTime.Now;
        return $"{timestamp.Hour:00}:{timestamp.Minute:00}:{timestamp.Second:00}";
    }

    public static string BuildBridgeConnectUrl(string environmentId, string? ingressUrl = null)
    {
        ArgumentNullException.ThrowIfNull(environmentId);

        var baseUrl = BridgeProductUrls.GetClaudeAiBaseUrl(null, ingressUrl);
        return $"{baseUrl}/code?bridge={environmentId}";
    }

    public static string BuildBridgeSessionUrl(string sessionId, string environmentId, string? ingressUrl = null)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(environmentId);

        return $"{BridgeProductUrls.GetRemoteSessionUrl(sessionId, ingressUrl)}?bridge={environmentId}";
    }

    public static BridgeStatusInfo GetBridgeStatus(
        string? error,
        bool connected,
        bool sessionActive,
        bool reconnecting)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return new BridgeStatusInfo("Remote Control failed", "error");
        }

        if (reconnecting)
        {
            return new BridgeStatusInfo("Remote Control reconnecting", "warning");
        }

        if (sessionActive || connected)
        {
            return new BridgeStatusInfo("Remote Control active", "success");
        }

        return new BridgeStatusInfo("Remote Control connecting…", "warning");
    }

    public static string BuildIdleFooterText(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return $"Code everywhere with the Claude app or {url}";
    }

    public static string BuildActiveFooterText(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return $"Continue coding in the Claude app or {url}";
    }

    public static string WrapWithOsc8Link(string text, string url)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(url);
        return $"\u001b]8;;{url}\u0007{text}\u001b]8;;\u0007";
    }
}

public sealed record BridgeStatusInfo(
    string Label,
    string Color);
