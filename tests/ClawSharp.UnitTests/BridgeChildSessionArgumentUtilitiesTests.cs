using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeChildSessionArgumentUtilitiesTests
{
    [Fact]
    public void Parse_Returns_NotBridgeChildMode_When_Print_Flag_Is_Absent()
    {
        var parsed = BridgeChildSessionArgumentUtilities.Parse(["repl"]);

        Assert.False(parsed.IsBridgeChildMode);
        Assert.Null(parsed.Options);
        Assert.Null(parsed.Error);
    }

    [Fact]
    public void Parse_Parses_Ts_Shaped_Bridge_Child_Arguments()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var originalDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = tempDirectory;
            var parsed = BridgeChildSessionArgumentUtilities.Parse(
            [
                "--print",
                "--sdk-url",
                "https://api.example.com/v1/code/sessions/session_123",
                "--session-id=session_123",
                "--input-format=stream-json",
                "--output-format",
                "stream-json",
                "--replay-user-messages",
                "--verbose",
                "--debug-file",
                ".\\bridge.log",
                "--permission-mode=acceptEdits"
            ]);

            Assert.True(parsed.IsBridgeChildMode);
            Assert.NotNull(parsed.Options);
            Assert.Equal("https://api.example.com/v1/code/sessions/session_123", parsed.Options!.SdkUrl);
            Assert.Equal("session_123", parsed.Options.SessionId);
            Assert.Equal("stream-json", parsed.Options.InputFormat);
            Assert.Equal("stream-json", parsed.Options.OutputFormat);
            Assert.True(parsed.Options.ReplayUserMessages);
            Assert.True(parsed.Options.Verbose);
            Assert.Equal(Path.GetFullPath(Path.Combine(tempDirectory, "bridge.log")), parsed.Options.DebugFile);
            Assert.Equal("acceptEdits", parsed.Options.PermissionMode);
            Assert.Null(parsed.Error);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            Directory.Delete(tempDirectory, true);
        }
    }

    [Theory]
    [InlineData("--sdk-url is required with --print", "--print", "--session-id=session_123", "--input-format=stream-json", "--output-format=stream-json")]
    [InlineData("--session-id is required with --print", "--print", "--sdk-url=https://api.example.com", "--input-format=stream-json", "--output-format=stream-json")]
    [InlineData("--input-format must be stream-json", "--print", "--sdk-url=https://api.example.com", "--session-id=session_123", "--input-format=text", "--output-format=stream-json")]
    [InlineData("--output-format must be stream-json", "--print", "--sdk-url=https://api.example.com", "--session-id=session_123", "--input-format=stream-json", "--output-format=text")]
    [InlineData("Unknown bridge child argument: --bogus", "--print", "--sdk-url=https://api.example.com", "--session-id=session_123", "--input-format=stream-json", "--output-format=stream-json", "--bogus")]
    public void Parse_Rejects_Invalid_Bridge_Child_Arguments(string expectedError, params string[] args)
    {
        var parsed = BridgeChildSessionArgumentUtilities.Parse(args);

        Assert.True(parsed.IsBridgeChildMode);
        Assert.Equal(expectedError, parsed.Error);
    }
}
