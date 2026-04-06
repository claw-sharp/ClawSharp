// TS origin: ./bridge/codeSessionApi.ts, ./bridge/bridgeMain.ts
using System.Text.RegularExpressions;

namespace ClawSharp.Bridge;

public sealed record CodeSessionWorkerRegistrarDependencies(
    HttpClient HttpClient,
    Func<string?> GetAccessToken,
    Func<string?>? GetTrustedDeviceToken = null,
    Action<string>? OnDebug = null);

public static partial class CodeSessionWorkerRegistrar
{
    public static async Task<RemoteCredentials> RegisterWorkerAsync(
        CodeSessionWorkerRegistrarDependencies dependencies,
        string sdkUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentException.ThrowIfNullOrWhiteSpace(sdkUrl);

        var accessToken = dependencies.GetAccessToken()
                          ?? throw new InvalidOperationException("No OAuth access token available for code-session worker registration.");
        var registrationTarget = ParseRegistrationTarget(sdkUrl);
        var credentials = await CodeSessionApiClient.FetchRemoteCredentialsAsync(
            new CodeSessionApiDependencies(
                dependencies.HttpClient,
                dependencies.OnDebug),
            registrationTarget.SessionId,
            registrationTarget.BaseUrl,
            accessToken,
            timeoutMs: 10_000,
            trustedDeviceToken: dependencies.GetTrustedDeviceToken?.Invoke(),
            cancellationToken: cancellationToken);
        return credentials
               ?? throw new InvalidOperationException(
                   $"Worker registration failed for code session {registrationTarget.SessionId}.");
    }

    private static CodeSessionRegistrationTarget ParseRegistrationTarget(string sdkUrl)
    {
        if (!Uri.TryCreate(sdkUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid code-session sdk URL: {sdkUrl}");
        }

        var match = CodeSessionPathRegex().Match(uri.AbsolutePath);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Code-session sdk URL did not match /v1/code/sessions/<id>: {sdkUrl}");
        }

        var sessionId = match.Groups["sessionId"].Value;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException($"Code-session sdk URL was missing a session id: {sdkUrl}");
        }

        return new CodeSessionRegistrationTarget(
            $"{uri.Scheme}://{uri.Authority}",
            sessionId);
    }

    [GeneratedRegex("^/v1/code/sessions/(?<sessionId>[^/?#]+)/?$", RegexOptions.CultureInvariant)]
    private static partial Regex CodeSessionPathRegex();

    private sealed record CodeSessionRegistrationTarget(
        string BaseUrl,
        string SessionId);
}
