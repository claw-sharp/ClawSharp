using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class WindowsPathConversionTests
{
    [Theory]
    [InlineData(@"C:\Users\foo", "/c/Users/foo")]
    [InlineData(@"C:/Users/foo", "/c/Users/foo")]
    [InlineData(@"\\server\share\dir", "//server/share/dir")]
    [InlineData(@"relative\path", "relative/path")]
    public void WindowsPathToPosixPath_Matches_Ts_Conversion(string input, string expected)
    {
        Assert.Equal(expected, WindowsPathConversion.WindowsPathToPosixPath(input));
    }

    [Theory]
    [InlineData("//server/share/dir", @"\\server\share\dir")]
    [InlineData("/cygdrive/c/Users/foo", @"C:\Users\foo")]
    [InlineData("/c/Users/foo", @"C:\Users\foo")]
    [InlineData("/c", @"C:\")]
    [InlineData("relative/path", @"relative\path")]
    public void PosixPathToWindowsPath_Matches_Ts_Conversion(string input, string expected)
    {
        Assert.Equal(expected, WindowsPathConversion.PosixPathToWindowsPath(input));
    }
}
