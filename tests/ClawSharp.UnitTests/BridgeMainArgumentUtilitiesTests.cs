using ClawSharp.Bridge;
using System.Text.Json.Nodes;

namespace ClawSharp.UnitTests;

public sealed class BridgeMainArgumentUtilitiesTests
{
    [Fact]
    public void IsConnectionError_Matches_Ts_Code_Allowlist()
    {
        Assert.True(BridgeMainArgumentUtilities.IsConnectionError(new { Code = "ECONNREFUSED" }));
        Assert.True(BridgeMainArgumentUtilities.IsConnectionError(new JsonObject { ["code"] = "ETIMEDOUT" }));
        Assert.False(BridgeMainArgumentUtilities.IsConnectionError(new { Code = "ERR_BAD_RESPONSE" }));
        Assert.False(BridgeMainArgumentUtilities.IsConnectionError(new { }));
    }

    [Fact]
    public void IsServerError_Matches_Ts_Axios_Bad_Response_Code()
    {
        Assert.True(BridgeMainArgumentUtilities.IsServerError(new { Code = "ERR_BAD_RESPONSE" }));
        Assert.False(BridgeMainArgumentUtilities.IsServerError(new { Code = "ECONNRESET" }));
        Assert.False(BridgeMainArgumentUtilities.IsServerError(null));
    }

    [Fact]
    public void ParseArgs_Parses_Flags_And_Resolves_Debug_File_Path()
    {
        var originalDirectory = Environment.CurrentDirectory;
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            Environment.CurrentDirectory = tempDirectory;

            var parsed = BridgeMainArgumentUtilities.ParseArgs(
            [
                "--verbose",
                "--sandbox",
                "--debug-file",
                "./debug.log",
                "--session-timeout=12xyz",
                "--permission-mode=acceptEdits",
                "--name",
                "demo",
                "--spawn=worktree",
                "--capacity",
                "3rest",
                "--create-session-in-dir"
            ]);

            Assert.True(parsed.Verbose);
            Assert.True(parsed.Sandbox);
            Assert.Equal(Path.Combine(Environment.CurrentDirectory, "debug.log"), parsed.DebugFile);
            Assert.Equal(12_000d, parsed.SessionTimeoutMs);
            Assert.Equal("acceptEdits", parsed.PermissionMode);
            Assert.Equal("demo", parsed.Name);
            Assert.Equal(SpawnMode.Worktree, parsed.SpawnMode);
            Assert.Equal(3, parsed.Capacity);
            Assert.True(parsed.CreateSessionInDir);
            Assert.Null(parsed.Error);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public void ParseArgs_Rejects_Invalid_Spawn_Capacity_And_Unknown_Arguments()
    {
        var spawnError = BridgeMainArgumentUtilities.ParseArgs(["--spawn", "bad-mode"]);
        Assert.Equal("--spawn requires one of: session, same-dir, worktree (got: bad-mode)", spawnError.Error);

        var capacityError = BridgeMainArgumentUtilities.ParseArgs(["--capacity"]);
        Assert.Equal("--capacity requires a positive integer (got: <missing>)", capacityError.Error);

        var unknownError = BridgeMainArgumentUtilities.ParseArgs(["--bogus"]);
        Assert.Equal("Unknown argument: --bogus\nRun 'clawsharp remote-control --help' for usage.", unknownError.Error);
    }

    [Fact]
    public void ParseArgs_Rejects_Cross_Validation_Combinations()
    {
        var singleSessionCapacity = BridgeMainArgumentUtilities.ParseArgs(["--spawn=session", "--capacity=2"]);
        Assert.Equal("--capacity cannot be used with --spawn=session (single-session mode has fixed capacity 1).", singleSessionCapacity.Error);

        var bothResumeModes = BridgeMainArgumentUtilities.ParseArgs(
            ["--session-id=abc", "--continue"],
            new BridgeMainParserOptions(KairosEnabled: true));
        Assert.Equal("--session-id and --continue cannot be used together.", bothResumeModes.Error);

        var resumeWithSpawn = BridgeMainArgumentUtilities.ParseArgs(
            ["--session-id=abc", "--spawn=worktree"],
            new BridgeMainParserOptions(KairosEnabled: true));
        Assert.Equal("--session-id and --continue cannot be used with --spawn, --capacity, or --create-session-in-dir.", resumeWithSpawn.Error);
    }

    [Fact]
    public void ParseArgs_Only_Recognizes_Kairos_Resume_Flags_When_Enabled()
    {
        var disabled = BridgeMainArgumentUtilities.ParseArgs(["--continue"]);
        Assert.Equal("Unknown argument: --continue\nRun 'clawsharp remote-control --help' for usage.", disabled.Error);

        var enabled = BridgeMainArgumentUtilities.ParseArgs(
            ["--continue", "--session-id=bridge-session"],
            new BridgeMainParserOptions(KairosEnabled: true));

        Assert.True(enabled.ContinueSession);
        Assert.Equal("bridge-session", enabled.SessionId);
    }
}
