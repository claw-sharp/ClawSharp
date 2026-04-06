using ClawSharp.Bridge;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class BridgeCliEntrypointTests
{
    [Fact]
    public async Task RunAsync_Prints_Help_For_Help_Flag()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = await BridgeCliEntrypoint.RunAsync(
            ["--help"],
            stdout,
            stderr,
            CreateDependencies());

        Assert.Equal(0, exitCode);
        Assert.Contains("Remote Control - Connect your local environment", stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public async Task RunAsync_Rejects_Invalid_Permission_Mode_Before_Runtime_Starts()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var runtimeCalls = 0;

        var exitCode = await BridgeCliEntrypoint.RunAsync(
            ["--permission-mode=bogus"],
            stdout,
            stderr,
            CreateDependencies(runRuntimeAsync: (_, _, _) =>
            {
                runtimeCalls++;
                return Task.FromResult(0);
            }));

        Assert.Equal(1, exitCode);
        Assert.Equal(0, runtimeCalls);
        Assert.Contains("Invalid permission mode 'bogus'", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Resolves_Continue_From_Bridge_Pointer_And_Forces_Single_Session_Mode()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        BridgeRuntimeRequest? capturedRequest = null;

        var exitCode = await BridgeCliEntrypoint.RunAsync(
            ["--continue"],
            stdout,
            stderr,
            CreateDependencies(
                readBridgePointerAcrossWorktreesAsync: (_, _) => Task.FromResult<BridgePointerAcrossWorktreesResult?>(
                    new BridgePointerAcrossWorktreesResult(
                        new BridgePointerWithAge("session_123", "env_123", "standalone", TimeSpan.FromMinutes(14).TotalMilliseconds),
                        @"D:\repo-worktree")),
                runRuntimeAsync: (request, _, _) =>
                {
                    capturedRequest = request;
                    return Task.FromResult(0);
                }));

        Assert.Equal(0, exitCode);
        Assert.NotNull(capturedRequest);
        Assert.Equal("session_123", capturedRequest!.Startup.ResumeSessionId);
        Assert.Equal(@"D:\repo-worktree", capturedRequest.Startup.ResumePointerDir);
        Assert.Equal(SpawnMode.SingleSession, capturedRequest.Startup.Config.SpawnMode);
        Assert.Equal(1, capturedRequest.Startup.Config.MaxSessions);
        Assert.Contains("Resuming session session_123", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Rejects_Continue_When_Combined_With_Spawn_Overrides()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var runtimeCalls = 0;

        var exitCode = await BridgeCliEntrypoint.RunAsync(
            ["--continue", "--spawn=same-dir", "--capacity=12"],
            stdout,
            stderr,
            CreateDependencies(runRuntimeAsync: (_, _, _) =>
            {
                runtimeCalls++;
                return Task.FromResult(0);
            }));

        Assert.Equal(1, exitCode);
        Assert.Equal(0, runtimeCalls);
        Assert.Contains(
            "--session-id and --continue cannot be used with --spawn, --capacity, or --create-session-in-dir.",
            stderr.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Clears_Stale_Pointer_And_Builds_Runtime_Request_For_Same_Dir_Mode()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        BridgeRuntimeRequest? capturedRequest = null;
        var clearedPointerDirs = new List<string>();

        var exitCode = await BridgeCliEntrypoint.RunAsync(
            ["--spawn=same-dir", "--capacity=7", "--no-create-session-in-dir", "--name", "named"],
            stdout,
            stderr,
            CreateDependencies(
                clearBridgePointerAsync: (dir, _) =>
                {
                    clearedPointerDirs.Add(dir);
                    return Task.CompletedTask;
                },
                runRuntimeAsync: (request, _, _) =>
                {
                    capturedRequest = request;
                    return Task.FromResult(0);
                }));

        Assert.Equal(0, exitCode);
        Assert.NotNull(capturedRequest);
        Assert.Equal([@"D:\repo"], clearedPointerDirs);
        Assert.Equal(SpawnMode.SameDir, capturedRequest!.Startup.Config.SpawnMode);
        Assert.Equal(7, capturedRequest.Startup.Config.MaxSessions);
        Assert.False(capturedRequest.Startup.PreCreateSession);
        Assert.Equal("named", capturedRequest.Startup.Title);
        Assert.False(capturedRequest.AllowResumeOnShutdown);
    }

    [Fact]
    public async Task RunAsync_Rejects_Worktree_Mode_When_Runtime_Does_Not_Support_It()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = await BridgeCliEntrypoint.RunAsync(
            ["--spawn=worktree"],
            stdout,
            stderr,
            CreateDependencies(isWorktreeAvailable: _ => true));

        Assert.Equal(1, exitCode);
        Assert.Contains("Worktree mode is not implemented in ClawSharp yet.", stderr.ToString(), StringComparison.Ordinal);
    }

    private static BridgeCliEntrypointDependencies CreateDependencies(
        Func<string>? getCurrentDirectory = null,
        Func<CancellationToken, Task<bool>>? checkHasTrustDialogAcceptedAsync = null,
        Func<string?>? getAccessToken = null,
        Func<string>? getBridgeBaseUrl = null,
        Func<string?>? getSessionIngressUrl = null,
        Func<string, CancellationToken, Task<string>>? getBranchAsync = null,
        Func<string, CancellationToken, Task<string?>>? getRemoteUrlAsync = null,
        Func<string, bool>? isWorktreeAvailable = null,
        Func<string, CancellationToken, Task<BridgePointerAcrossWorktreesResult?>>? readBridgePointerAcrossWorktreesAsync = null,
        Func<string, CancellationToken, Task>? clearBridgePointerAsync = null,
        Func<BridgeRuntimeRequest, Action<string>, CancellationToken, Task<int>>? runRuntimeAsync = null)
    {
        return new BridgeCliEntrypointDependencies(
            GetCurrentDirectory: getCurrentDirectory ?? (() => @"D:\repo"),
            CheckHasTrustDialogAcceptedAsync: checkHasTrustDialogAcceptedAsync ?? (_ => Task.FromResult(true)),
            GetAccessToken: getAccessToken ?? (() => "oauth-token"),
            GetBridgeBaseUrl: getBridgeBaseUrl ?? (() => "https://api.example.com"),
            GetSessionIngressUrl: getSessionIngressUrl ?? (() => "wss://ingress.example.com"),
            GetBranchAsync: getBranchAsync ?? ((_, _) => Task.FromResult("main")),
            GetRemoteUrlAsync: getRemoteUrlAsync ?? ((_, _) => Task.FromResult<string?>("https://github.com/example/repo.git")),
            IsWorktreeAvailable: isWorktreeAvailable ?? (_ => false),
            ReadBridgePointerAcrossWorktreesAsync: readBridgePointerAcrossWorktreesAsync ?? ((_, _) => Task.FromResult<BridgePointerAcrossWorktreesResult?>(null)),
            ClearBridgePointerAsync: clearBridgePointerAsync ?? ((_, _) => Task.CompletedTask),
            RunRuntimeAsync: runRuntimeAsync ?? ((_, _, _) => Task.FromResult(0)));
    }
}
