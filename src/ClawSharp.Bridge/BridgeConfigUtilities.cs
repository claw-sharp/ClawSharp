namespace ClawSharp.Bridge;

public sealed record BridgeConfigDependencies(
    Func<string?>? GetClaudeAiAccessToken = null,
    Func<string>? GetBaseApiUrl = null);

public static class BridgeConfigUtilities
{
    public static string? GetBridgeTokenOverride(
        IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return TryGetValue(environment, "USER_TYPE") == "ant"
            ? TryGetValue(environment, "CLAUDE_BRIDGE_OAUTH_TOKEN")
            : null;
    }

    public static string? GetBridgeBaseUrlOverride(
        IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return TryGetValue(environment, "USER_TYPE") == "ant"
            ? TryGetValue(environment, "CLAUDE_BRIDGE_BASE_URL")
            : null;
    }

    public static string? GetBridgeAccessToken(
        IReadOnlyDictionary<string, string?> environment,
        BridgeConfigDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(dependencies);

        return GetBridgeTokenOverride(environment) ?? dependencies.GetClaudeAiAccessToken?.Invoke();
    }

    public static string GetBridgeBaseUrl(
        IReadOnlyDictionary<string, string?> environment,
        BridgeConfigDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(dependencies);

        return GetBridgeBaseUrlOverride(environment) ??
               dependencies.GetBaseApiUrl?.Invoke() ??
               throw new InvalidOperationException("Base API URL is required.");
    }

    private static string? TryGetValue(
        IReadOnlyDictionary<string, string?> environment,
        string key)
    {
        return environment.TryGetValue(key, out var value) ? value : null;
    }
}
