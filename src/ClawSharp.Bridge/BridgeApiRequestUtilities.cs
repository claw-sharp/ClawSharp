namespace ClawSharp.Bridge;

public sealed record BridgeApiClientDependencies(
    string BaseUrl,
    Func<string?> GetAccessToken,
    string RunnerVersion,
    Action<string>? OnDebug = null,
    Func<string, Task<bool>>? OnAuth401 = null,
    Func<string?>? GetTrustedDeviceToken = null);

public sealed record BridgeHttpLikeResponse<T>(
    int Status,
    T Data);

public static class BridgeApiRequestUtilities
{
    public const string BetaHeader = "environments-2025-11-01";

    public static IReadOnlyDictionary<string, string> GetHeaders(
        BridgeApiClientDependencies dependencies,
        string accessToken)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(accessToken);

        Dictionary<string, string> headers = new(StringComparer.Ordinal)
        {
            ["Authorization"] = $"Bearer {accessToken}",
            ["Content-Type"] = "application/json",
            ["anthropic-version"] = "2023-06-01",
            ["anthropic-beta"] = BetaHeader,
            ["x-environment-runner-version"] = dependencies.RunnerVersion
        };

        var deviceToken = dependencies.GetTrustedDeviceToken?.Invoke();
        if (!string.IsNullOrEmpty(deviceToken))
        {
            headers["X-Trusted-Device-Token"] = deviceToken;
        }

        return headers;
    }

    public static string ResolveAuth(BridgeApiClientDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        var accessToken = dependencies.GetAccessToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new InvalidOperationException(BridgeConstants.BridgeLoginInstruction);
        }

        return accessToken;
    }

    public static async Task<BridgeHttpLikeResponse<T>> WithOAuthRetryAsync<T>(
        BridgeApiClientDependencies dependencies,
        Func<string, Task<BridgeHttpLikeResponse<T>>> request,
        string context)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var accessToken = ResolveAuth(dependencies);
        var response = await request(accessToken);

        if (response.Status != 401)
        {
            return response;
        }

        if (dependencies.OnAuth401 is null)
        {
            dependencies.OnDebug?.Invoke($"[bridge:api] {context}: 401 received, no refresh handler");
            return response;
        }

        dependencies.OnDebug?.Invoke($"[bridge:api] {context}: 401 received, attempting token refresh");
        var refreshed = await dependencies.OnAuth401(accessToken);
        if (refreshed)
        {
            dependencies.OnDebug?.Invoke($"[bridge:api] {context}: Token refreshed, retrying request");
            var newToken = ResolveAuth(dependencies);
            var retryResponse = await request(newToken);
            if (retryResponse.Status != 401)
            {
                return retryResponse;
            }

            dependencies.OnDebug?.Invoke($"[bridge:api] {context}: Retry after refresh also got 401");
        }
        else
        {
            dependencies.OnDebug?.Invoke($"[bridge:api] {context}: Token refresh failed");
        }

        return response;
    }
}
