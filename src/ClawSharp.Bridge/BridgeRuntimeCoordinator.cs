// TS origin: ./bridge/bridgeMain.ts
namespace ClawSharp.Bridge;

public sealed record BridgeRuntimeLoopResult(
    bool FatalExit,
    IReadOnlyDictionary<string, ISessionHandle> ActiveSessions,
    IReadOnlyDictionary<string, string> SessionWorkIds,
    IReadOnlyDictionary<string, string> SessionCompatIds,
    IReadOnlyDictionary<string, BridgeShutdownWorktree>? SessionWorktrees = null,
    IReadOnlyCollection<Task>? PendingCleanups = null);

public sealed record BridgeRuntimeRequest(
    BridgeStartupRequest Startup,
    bool AllowResumeOnShutdown = false,
    double ShutdownGraceMs = 30_000);

public sealed record BridgeRuntimeResult(
    BridgeStartupResult Startup,
    BridgeRuntimeLoopResult Loop,
    BridgeShutdownResult Shutdown);

public sealed record BridgeRuntimeDependencies(
    BridgeStartupCoordinator StartupCoordinator,
    BridgeShutdownCoordinator ShutdownCoordinator,
    IBridgeLogger Logger,
    Func<BridgeConfig, string, string, string?, CancellationToken, Task<BridgeRuntimeLoopResult>> RunLoopAsync,
    Action<string>? OnDebug = null);

public sealed class BridgeRuntimeCoordinator
{
    private readonly BridgeRuntimeDependencies _dependencies;

    public BridgeRuntimeCoordinator(BridgeRuntimeDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    public async Task<BridgeRuntimeResult> RunAsync(
        BridgeRuntimeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Startup);

        var startup = await _dependencies.StartupCoordinator.StartAsync(request.Startup, cancellationToken);
        BridgeRuntimeLoopResult loopResult;
        try
        {
            loopResult = await _dependencies.RunLoopAsync(
                request.Startup.Config,
                startup.EnvironmentId,
                startup.EnvironmentSecret,
                startup.InitialSessionId,
                cancellationToken);
        }
        finally
        {
            _dependencies.OnDebug?.Invoke(
                $"[bridge:runtime] runLoopAsync exited for environmentId={startup.EnvironmentId}");
        }

        var shutdown = await _dependencies.ShutdownCoordinator.ShutdownAsync(
            new BridgeShutdownRequest(
                Config: request.Startup.Config,
                EnvironmentId: startup.EnvironmentId,
                FatalExit: loopResult.FatalExit,
                InitialSessionId: startup.InitialSessionId,
                ActiveSessions: loopResult.ActiveSessions,
                SessionWorkIds: loopResult.SessionWorkIds,
                SessionCompatIds: loopResult.SessionCompatIds,
                SessionWorktrees: loopResult.SessionWorktrees,
                PendingCleanups: loopResult.PendingCleanups,
                AllowResumeOnShutdown: request.AllowResumeOnShutdown,
                ShutdownGraceMs: request.ShutdownGraceMs),
            cancellationToken);

        return new BridgeRuntimeResult(startup, loopResult, shutdown);
    }
}
