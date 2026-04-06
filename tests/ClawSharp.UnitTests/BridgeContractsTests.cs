using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeContractsTests
{
    [Fact]
    public void BridgeConstants_Match_Ts_Values()
    {
        Assert.Equal(86_400_000, BridgeConstants.DefaultSessionTimeoutMs);
        Assert.Contains("/login", BridgeConstants.BridgeLoginInstruction, StringComparison.Ordinal);
        Assert.Contains("You must be logged in", BridgeConstants.BridgeLoginError, StringComparison.Ordinal);
        Assert.Equal("Remote Control disconnected.", BridgeConstants.RemoteControlDisconnectedMessage);
    }

    [Fact]
    public void BridgeConfig_Preserves_Ts_Shaped_Startup_Fields()
    {
        var config = new BridgeConfig(
            Dir: "D:\\repo",
            MachineName: "machine",
            Branch: "main",
            GitRepoUrl: "https://example.test/repo.git",
            MaxSessions: 4,
            SpawnMode: SpawnMode.Worktree,
            Verbose: true,
            Sandbox: false,
            BridgeId: "bridge-id",
            WorkerType: "claude_code_assistant",
            EnvironmentId: "env-id",
            ApiBaseUrl: "https://api.example.test",
            SessionIngressUrl: "wss://ingress.example.test",
            ReuseEnvironmentId: "reuse-id",
            DebugFile: "bridge.log",
            SessionTimeoutMs: 5000);

        Assert.Equal("D:\\repo", config.Dir);
        Assert.Equal(SpawnMode.Worktree, config.SpawnMode);
        Assert.Equal("reuse-id", config.ReuseEnvironmentId);
        Assert.Equal("bridge.log", config.DebugFile);
        Assert.Equal(5000, config.SessionTimeoutMs);
    }

    [Fact]
    public void BridgeWorkSecret_Preserves_Ts_Optional_Metadata_Fields()
    {
        var secret = new BridgeWorkSecret(
            Version: 2,
            SessionIngressToken: "token",
            ApiBaseUrl: "https://api.example.test",
            Sources:
            [
                new BridgeWorkSecretSource(
                    "git",
                    new BridgeWorkSecretSourceGitInfo("github", "repo", "main", "secret"))
            ],
            Auth:
            [
                new BridgeWorkSecretAuth("oauth", "auth-token")
            ],
            ClaudeCodeArgs: new Dictionary<string, string>(StringComparer.Ordinal) { ["model"] = "sonnet" },
            McpConfig: new { enabled = true },
            EnvironmentVariables: new Dictionary<string, string>(StringComparer.Ordinal) { ["FOO"] = "bar" },
            UseCodeSessions: true);

        Assert.True(secret.UseCodeSessions);
        Assert.Equal("main", secret.Sources[0].GitInfo?.Ref);
        Assert.Equal("sonnet", secret.ClaudeCodeArgs?["model"]);
        Assert.Equal("bar", secret.EnvironmentVariables?["FOO"]);
    }

    [Fact]
    public void PermissionResponseEvent_Uses_Ts_Shaped_Control_Response_Envelope()
    {
        PermissionResponseEvent @event = new(
            Type: "control_response",
            Response: new PermissionResponsePayload(
                Subtype: "success",
                RequestId: "req-1",
                Response: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["behavior"] = "allow"
                }));

        Assert.Equal("control_response", @event.Type);
        Assert.Equal("success", @event.Response.Subtype);
        Assert.Equal("allow", @event.Response.Response["behavior"]);
    }
}
