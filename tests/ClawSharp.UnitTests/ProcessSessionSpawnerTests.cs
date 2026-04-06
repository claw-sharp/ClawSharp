using System.Diagnostics;
using System.Text.Json.Nodes;
using ClawSharp.Bridge;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class ProcessSessionSpawnerTests
{
    [Fact]
    public void Spawn_Builds_Ts_Shaped_Args_Env_And_Parses_Output()
    {
        var child = new FakeSessionChildProcess();
        var factory = new FakeSessionChildProcessFactory(child);
        var debug = new List<string>();
        var activities = new List<SessionActivity>();
        var permissionRequests = new List<(string SessionId, JsonObject Message, string AccessToken)>();
        var spawner = new ProcessSessionSpawner(
            new ProcessSessionSpawnerDependencies(
                ExecutablePath: "ClawSharp.Cli.exe",
                ExecutableArgumentsPrefix: ["prefixed-arg"],
                EnvironmentVariables: new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["USER_TYPE"] = "ant",
                    ["CLAUDE_CODE_OAUTH_TOKEN"] = "oauth"
                },
                Verbose: true,
                Sandbox: true,
                DebugFile: Path.Combine("D:", "logs", "bridge.log"),
                PermissionMode: "acceptEdits",
                OnDebug: debug.Add,
                OnActivity: (_, activity) => activities.Add(activity),
                OnPermissionRequest: (sessionId, message, accessToken) => permissionRequests.Add((sessionId, message, accessToken)),
                ProcessFactory: factory));

        var handle = spawner.Spawn(
            new SessionSpawnOptions(
                SessionId: "session_123",
                SdkUrl: "https://api.example.com/v1/code/sessions/session_123",
                AccessToken: "session-token",
                UseCcrV2: true,
                WorkerEpoch: 17,
                OnFirstUserMessage: text => debug.Add($"first:{text}")),
            @"D:\repo");

        Assert.Equal("ClawSharp.Cli.exe", factory.LastStartInfo!.FileName);
        Assert.Equal(@"D:\repo", factory.LastStartInfo.WorkingDirectory);
        Assert.Equal(
        [
            "prefixed-arg",
            "--print",
            "--sdk-url",
            "https://api.example.com/v1/code/sessions/session_123",
            "--session-id",
            "session_123",
            "--input-format",
            "stream-json",
            "--output-format",
            "stream-json",
            "--replay-user-messages",
            "--verbose",
            "--debug-file",
            Path.Combine("D:", "logs", "bridge-session_123.log"),
            "--permission-mode",
            "acceptEdits"
        ], factory.LastStartInfo.ArgumentList.ToArray());
        Assert.False(factory.LastStartInfo.Environment.ContainsKey("CLAUDE_CODE_OAUTH_TOKEN"));
        Assert.Equal("bridge", factory.LastStartInfo.Environment["CLAUDE_CODE_ENVIRONMENT_KIND"]);
        Assert.Equal("session-token", factory.LastStartInfo.Environment["CLAUDE_CODE_SESSION_ACCESS_TOKEN"]);
        Assert.Equal("1", factory.LastStartInfo.Environment["CLAUDE_CODE_FORCE_SANDBOX"]);
        Assert.Equal("1", factory.LastStartInfo.Environment["CLAUDE_CODE_USE_CCR_V2"]);
        Assert.Equal("17", factory.LastStartInfo.Environment["CLAUDE_CODE_WORKER_EPOCH"]);

        child.EmitOutput("""{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Read","input":{"file_path":"app.ts"}},{"type":"text","text":"hello bridge"}]}}""");
        child.EmitOutput("""{"type":"control_request","request_id":"req_1","request":{"subtype":"can_use_tool","tool_name":"Write","input":{"file_path":"app.ts"},"tool_use_id":"tool_123"}}""");
        child.EmitOutput("""{"type":"user","message":{"content":[{"type":"text","text":"  first prompt  "}]}}""");
        child.EmitError("stderr line");

        Assert.Equal(2, activities.Count);
        Assert.Equal("Reading app.ts", activities[0].Summary);
        Assert.Equal("hello bridge", activities[1].Summary);
        Assert.Single(permissionRequests);
        Assert.Equal("session_123", permissionRequests[0].SessionId);
        Assert.Equal("session-token", permissionRequests[0].AccessToken);
        Assert.Equal("stderr line", handle.LastStderr.Single());
        Assert.Contains(debug, line => line.Contains("first:first prompt", StringComparison.Ordinal));

        handle.UpdateAccessToken("fresh-token");

        Assert.Equal("fresh-token", handle.AccessToken);
        Assert.Contains(
            child.StdinWrites,
            line => line.Contains(@"""CLAUDE_CODE_SESSION_ACCESS_TOKEN"":""fresh-token""", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_Done_Maps_Success_And_Kill_To_Ts_Statuses()
    {
        var successChild = new FakeSessionChildProcess();
        var successSpawner = new ProcessSessionSpawner(
            new ProcessSessionSpawnerDependencies(
                ExecutablePath: "ClawSharp.Cli.exe",
                ProcessFactory: new FakeSessionChildProcessFactory(successChild)));
        var successHandle = successSpawner.Spawn(
            new SessionSpawnOptions("session_success", "https://api.example.com/session_success", "token"),
            @"D:\repo");
        successChild.EmitExit(0);

        Assert.Equal(SessionDoneStatus.Completed, await successHandle.Done);

        var interruptedChild = new FakeSessionChildProcess();
        var interruptedSpawner = new ProcessSessionSpawner(
            new ProcessSessionSpawnerDependencies(
                ExecutablePath: "ClawSharp.Cli.exe",
                ProcessFactory: new FakeSessionChildProcessFactory(interruptedChild)));
        var interruptedHandle = interruptedSpawner.Spawn(
            new SessionSpawnOptions("session_interrupt", "https://api.example.com/session_interrupt", "token"),
            @"D:\repo");

        interruptedHandle.Kill();
        interruptedChild.EmitExit(1);

        Assert.True(interruptedChild.Terminated);
        Assert.Equal(SessionDoneStatus.Interrupted, await interruptedHandle.Done);
    }

    private sealed class FakeSessionChildProcessFactory(FakeSessionChildProcess process) : ISessionChildProcessFactory
    {
        public ProcessStartInfo? LastStartInfo { get; private set; }

        public ISessionChildProcess Start(ProcessStartInfo startInfo)
        {
            LastStartInfo = startInfo;
            return process;
        }
    }

    private sealed class FakeSessionChildProcess : ISessionChildProcess
    {
        public int? ProcessId => 42;

        public List<string> StdinWrites { get; } = [];

        public bool Terminated { get; private set; }

        public bool ForceTerminated { get; private set; }

        public event Action<string>? OutputLineReceived;

        public event Action<string>? ErrorLineReceived;

        public event Action<int?>? Exited;

        public void BeginReading()
        {
        }

        public void WriteToStandardInput(string data)
        {
            StdinWrites.Add(data);
        }

        public void Terminate()
        {
            Terminated = true;
        }

        public void ForceTerminate()
        {
            ForceTerminated = true;
        }

        public void EmitOutput(string line)
        {
            OutputLineReceived?.Invoke(line);
        }

        public void EmitError(string line)
        {
            ErrorLineReceived?.Invoke(line);
        }

        public void EmitExit(int? exitCode)
        {
            Exited?.Invoke(exitCode);
        }
    }
}
