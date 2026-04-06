using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class BrowserLauncherTests
{
    [Fact]
    public void ValidateUrl_Rejects_Non_Http_Schemes()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => BrowserLauncher.ValidateUrl("file:///tmp/test.txt"));

        Assert.Contains("Invalid URL protocol", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetOpenBrowserCommand_Uses_Windows_Rundll32_Default()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var (command, arguments) = BrowserLauncher.GetOpenBrowserCommand("https://example.com", null);

        Assert.Equal("rundll32", command);
        Assert.Equal(["url,OpenURL", "https://example.com"], arguments);
    }

    [Fact]
    public void GetOpenPathCommand_Uses_Explorer_On_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var (command, arguments) = BrowserLauncher.GetOpenPathCommand(@"C:\temp");

        Assert.Equal("explorer", command);
        Assert.Equal([@"C:\temp"], arguments);
    }
}
