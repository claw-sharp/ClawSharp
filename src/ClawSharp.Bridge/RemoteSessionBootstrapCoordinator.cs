using ClawSharp.Core;

namespace ClawSharp.Bridge;

public sealed record RemoteSessionBootstrapRequest(
    string BaseUrl,
    string Title,
    bool Perpetual = false,
    IReadOnlyList<string>? Tags = null);

public sealed record RemoteSessionBootstrapResult(
    string SessionId,
    RemoteCredentials Credentials,
    EnvLessBridgeConfig Config);

public sealed record RemoteSessionBootstrapDependencies(
    Func<bool> IsEnvLessBridgeEnabled,
    Func<CancellationToken, Task<EnvLessBridgeConfig>> GetEnvLessBridgeConfigAsync,
    Func<CancellationToken, Task<string?>> CheckEnvLessBridgeMinVersionAsync,
    Func<string?> GetAccessToken,
    Func<string, string, string, int, IReadOnlyList<string>?, CancellationToken, Task<string?>> CreateCodeSessionAsync,
    Func<string, string, string, int, string?, CancellationToken, Task<RemoteCredentials?>> FetchRemoteCredentialsAsync,
    Func<string, int, CancellationToken, Task> ArchiveSessionAsync,
    Action<string, string?>? OnStateChange = null,
    Action<string>? OnDebug = null,
    Func<string?>? GetTrustedDeviceToken = null,
    Func<double, Task>? SleepAsync = null,
    Func<double>? NextRandomDouble = null);

public sealed class RemoteSessionBootstrapCoordinator
{
    private readonly RemoteSessionBootstrapDependencies _dependencies;

    public RemoteSessionBootstrapCoordinator(RemoteSessionBootstrapDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    public async Task<RemoteSessionBootstrapResult?> TryBootstrapDirectConnectAsync(
        RemoteSessionBootstrapRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_dependencies.IsEnvLessBridgeEnabled() || request.Perpetual)
        {
            return null;
        }

        var versionError = await _dependencies.CheckEnvLessBridgeMinVersionAsync(cancellationToken);
        if (versionError is not null)
        {
            _dependencies.OnDebug?.Invoke($"[bridge:repl] Skipping: {versionError}");
            _dependencies.OnStateChange?.Invoke("failed", $"run `{AppMetadata.UpdateCommand}` to upgrade");
            return null;
        }

        var config = await _dependencies.GetEnvLessBridgeConfigAsync(cancellationToken);
        var accessToken = _dependencies.GetAccessToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            _dependencies.OnDebug?.Invoke("[remote-bridge] No OAuth token");
            return null;
        }

        var sessionId = await WithRetryAsync(
            () => _dependencies.CreateCodeSessionAsync(
                request.BaseUrl,
                accessToken,
                request.Title,
                config.HttpTimeoutMs,
                request.Tags,
                cancellationToken),
            "createCodeSession",
            config,
            cancellationToken);
        if (sessionId is null)
        {
            _dependencies.OnStateChange?.Invoke("failed", "Session creation failed — see debug log");
            return null;
        }

        _dependencies.OnDebug?.Invoke($"[remote-bridge] Created session {sessionId}");

        var trustedDeviceToken = _dependencies.GetTrustedDeviceToken?.Invoke();
        var credentials = await WithRetryAsync(
            () => _dependencies.FetchRemoteCredentialsAsync(
                sessionId,
                request.BaseUrl,
                accessToken,
                config.HttpTimeoutMs,
                trustedDeviceToken,
                cancellationToken),
            "fetchRemoteCredentials",
            config,
            cancellationToken);
        if (credentials is null)
        {
            _dependencies.OnStateChange?.Invoke("failed", "Remote credentials fetch failed — see debug log");
            _ = _dependencies.ArchiveSessionAsync(sessionId, config.TeardownArchiveTimeoutMs, CancellationToken.None);
            return null;
        }

        _dependencies.OnDebug?.Invoke(
            $"[remote-bridge] Fetched bridge credentials (expires_in={credentials.ExpiresIn}s)");
        return new RemoteSessionBootstrapResult(sessionId, credentials, config);
    }

    private async Task<T?> WithRetryAsync<T>(
        Func<Task<T?>> operation,
        string label,
        EnvLessBridgeConfig config,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(label);

        var sleepAsync = _dependencies.SleepAsync ??
                         (delay => Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken));
        var nextRandomDouble = _dependencies.NextRandomDouble ?? Random.Shared.NextDouble;
        var maxAttempts = config.InitRetryMaxAttempts;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var result = await operation();
            if (result is not null)
            {
                return result;
            }

            if (attempt >= maxAttempts)
            {
                break;
            }

            var baseDelay = config.InitRetryBaseDelayMs * Math.Pow(2d, attempt - 1);
            var jitter = baseDelay * config.InitRetryJitterFraction * (2d * nextRandomDouble() - 1d);
            var delay = Math.Min(baseDelay + jitter, config.InitRetryMaxDelayMs);
            _dependencies.OnDebug?.Invoke(
                $"[remote-bridge] {label} failed (attempt {attempt}/{maxAttempts}), retrying in {Math.Round(delay)}ms");
            await sleepAsync(delay);
        }

        return null;
    }
}
