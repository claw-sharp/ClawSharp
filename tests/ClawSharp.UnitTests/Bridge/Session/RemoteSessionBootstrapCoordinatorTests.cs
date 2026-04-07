using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class RemoteSessionBootstrapCoordinatorTests
{
    [Fact]
    public async Task TryBootstrapDirectConnectAsync_Returns_Null_When_EnvLess_Is_Disabled_Or_Perpetual()
    {
        var dependencies = CreateDependencies(isEnvLessBridgeEnabled: false);
        var coordinator = new RemoteSessionBootstrapCoordinator(dependencies);

        var disabled = await coordinator.TryBootstrapDirectConnectAsync(
            new RemoteSessionBootstrapRequest("https://api.example.com", "Title"));
        var perpetual = await new RemoteSessionBootstrapCoordinator(CreateDependencies())
            .TryBootstrapDirectConnectAsync(
                new RemoteSessionBootstrapRequest("https://api.example.com", "Title", Perpetual: true));

        Assert.Null(disabled);
        Assert.Null(perpetual);
    }

    [Fact]
    public async Task TryBootstrapDirectConnectAsync_Fails_On_Minimum_Version_Check_With_Ts_State_Message()
    {
        var states = new List<(string State, string? Detail)>();
        var debug = new List<string>();
        var coordinator = new RemoteSessionBootstrapCoordinator(
            CreateDependencies(
                checkMinVersionAsync: _ => Task.FromResult<string?>("too old"),
                onStateChange: (state, detail) => states.Add((state, detail)),
                onDebug: debug.Add));

        var result = await coordinator.TryBootstrapDirectConnectAsync(
            new RemoteSessionBootstrapRequest("https://api.example.com", "Title"));

        Assert.Null(result);
        Assert.Equal([("failed", "run `clawsharp update` to upgrade")], states);
        Assert.Contains(debug, line => line.Contains("Skipping: too old", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryBootstrapDirectConnectAsync_Retries_Create_And_Fetch_Before_Returning_Result()
    {
        var sleepDelays = new List<double>();
        var debug = new List<string>();
        var createAttempts = 0;
        var fetchAttempts = 0;
        var coordinator = new RemoteSessionBootstrapCoordinator(
            CreateDependencies(
                onDebug: debug.Add,
                sleepAsync: delay =>
                {
                    sleepDelays.Add(delay);
                    return Task.CompletedTask;
                },
                nextRandomDouble: () => 0.5d,
                createCodeSessionAsync: (_, _, _, _, _, _) =>
                {
                    createAttempts++;
                    return Task.FromResult<string?>(createAttempts < 2 ? null : "cse_123");
                },
                fetchRemoteCredentialsAsync: (_, _, _, _, _, _) =>
                {
                    fetchAttempts++;
                    return Task.FromResult(
                        fetchAttempts < 2
                            ? null
                            : new RemoteCredentials("jwt", "https://worker.example.com", 3600, 12));
                }));

        var result = await coordinator.TryBootstrapDirectConnectAsync(
            new RemoteSessionBootstrapRequest("https://api.example.com", "Title", Tags: ["tag-1"]));

        Assert.NotNull(result);
        Assert.Equal("cse_123", result!.SessionId);
        Assert.Equal("jwt", result.Credentials.WorkerJwt);
        Assert.Equal(2, createAttempts);
        Assert.Equal(2, fetchAttempts);
        Assert.Equal([500d, 500d], sleepDelays);
        Assert.Contains(debug, line => line.Contains("createCodeSession failed (attempt 1/3), retrying in 500ms", StringComparison.Ordinal));
        Assert.Contains(debug, line => line.Contains("fetchRemoteCredentials failed (attempt 1/3), retrying in 500ms", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryBootstrapDirectConnectAsync_Archives_Session_When_Remote_Credentials_Fail()
    {
        var states = new List<(string State, string? Detail)>();
        var archived = new List<(string SessionId, int TimeoutMs)>();
        var coordinator = new RemoteSessionBootstrapCoordinator(
            CreateDependencies(
                onStateChange: (state, detail) => states.Add((state, detail)),
                fetchRemoteCredentialsAsync: (_, _, _, _, _, _) => Task.FromResult<RemoteCredentials?>(null),
                archiveSessionAsync: (sessionId, timeoutMs, _) =>
                {
                    archived.Add((sessionId, timeoutMs));
                    return Task.CompletedTask;
                }));

        var result = await coordinator.TryBootstrapDirectConnectAsync(
            new RemoteSessionBootstrapRequest("https://api.example.com", "Title"));

        Assert.Null(result);
        Assert.Equal([("failed", "Remote credentials fetch failed — see debug log")], states);
        Assert.Equal([("cse_123", EnvLessBridgeConfig.Default.TeardownArchiveTimeoutMs)], archived);
    }

    private static RemoteSessionBootstrapDependencies CreateDependencies(
        bool isEnvLessBridgeEnabled = true,
        Func<CancellationToken, Task<EnvLessBridgeConfig>>? getConfigAsync = null,
        Func<CancellationToken, Task<string?>>? checkMinVersionAsync = null,
        Func<string?>? getAccessToken = null,
        Func<string, string, string, int, IReadOnlyList<string>?, CancellationToken, Task<string?>>? createCodeSessionAsync = null,
        Func<string, string, string, int, string?, CancellationToken, Task<RemoteCredentials?>>? fetchRemoteCredentialsAsync = null,
        Func<string, int, CancellationToken, Task>? archiveSessionAsync = null,
        Action<string, string?>? onStateChange = null,
        Action<string>? onDebug = null,
        Func<string?>? getTrustedDeviceToken = null,
        Func<double, Task>? sleepAsync = null,
        Func<double>? nextRandomDouble = null)
    {
        return new RemoteSessionBootstrapDependencies(
            IsEnvLessBridgeEnabled: () => isEnvLessBridgeEnabled,
            GetEnvLessBridgeConfigAsync: getConfigAsync ?? (_ => Task.FromResult(EnvLessBridgeConfig.Default)),
            CheckEnvLessBridgeMinVersionAsync: checkMinVersionAsync ?? (_ => Task.FromResult<string?>(null)),
            GetAccessToken: getAccessToken ?? (() => "token"),
            CreateCodeSessionAsync: createCodeSessionAsync ?? ((_, _, _, _, _, _) => Task.FromResult<string?>("cse_123")),
            FetchRemoteCredentialsAsync: fetchRemoteCredentialsAsync ?? ((_, _, _, _, _, _) =>
                Task.FromResult<RemoteCredentials?>(new RemoteCredentials("jwt", "https://worker.example.com", 3600, 12))),
            ArchiveSessionAsync: archiveSessionAsync ?? ((_, _, _) => Task.CompletedTask),
            OnStateChange: onStateChange,
            OnDebug: onDebug,
            GetTrustedDeviceToken: getTrustedDeviceToken,
            SleepAsync: sleepAsync,
            NextRandomDouble: nextRandomDouble);
    }
}
