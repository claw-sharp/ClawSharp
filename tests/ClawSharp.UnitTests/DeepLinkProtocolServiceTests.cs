using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class DeepLinkProtocolServiceTests
{
    [Fact]
    public void Parse_Extracts_Query_Cwd_And_Repo()
    {
        var service = new DeepLinkProtocolService();

        var cwd = OperatingSystem.IsWindows() ? @"C:\work" : "/work";
        var encodedCwd = Uri.EscapeDataString(cwd);
        var action = service.Parse($"claude-cli://open?q=hello%20world&cwd={encodedCwd}&repo=owner%2Frepo");

        Assert.Equal("hello world", action.Query);
        Assert.Equal(cwd, action.Cwd);
        Assert.Equal("owner/repo", action.Repo);
    }

    [Fact]
    public void Parse_Rejects_Control_Characters()
    {
        var service = new DeepLinkProtocolService();

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.Parse("claude-cli://open?q=hello%0Aworld"));

        Assert.Contains("disallowed control characters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWindowsCommandValue_Uses_Handle_Uri_Entry_Point()
    {
        var commandValue = DeepLinkProtocolService.BuildWindowsCommandValue(@"C:\ClawSharp\ClawSharp.exe");

        Assert.Equal(@"""C:\ClawSharp\ClawSharp.exe"" --handle-uri ""%1""", commandValue);
    }
}
