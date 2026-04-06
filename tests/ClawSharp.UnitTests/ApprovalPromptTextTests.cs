using ClawSharp.Core;

namespace ClawSharp.UnitTests;

public sealed class ApprovalPromptTextTests
{
    [Fact]
    public void CreateToolPermissionRequestMessage_MatchesTsBasePrompt()
    {
        var message = ApprovalPromptText.CreateToolPermissionRequestMessage("Bash");

        Assert.Equal("Claude requested permissions to use Bash, but you haven't granted it yet.", message);
    }

    [Fact]
    public void CreateReadPermissionRequestMessage_MatchesTsBasePrompt()
    {
        var message = ApprovalPromptText.CreateReadPermissionRequestMessage("/tmp/file.txt");

        Assert.Equal("Claude requested permissions to read from /tmp/file.txt, but you haven't granted it yet.", message);
    }

    [Fact]
    public void CreateWritePermissionRequestMessage_MatchesTsBasePrompt()
    {
        var message = ApprovalPromptText.CreateWritePermissionRequestMessage("/tmp/file.txt");

        Assert.Equal("Claude requested permissions to write to /tmp/file.txt, but you haven't granted it yet.", message);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ShouldShowAlwaysAllowOptions_ReflectsManagedOnlyPolicy(bool allowManagedOnly, bool expected)
    {
        var result = ApprovalPromptText.ShouldShowAlwaysAllowOptions(allowManagedOnly);

        Assert.Equal(expected, result);
    }
}
