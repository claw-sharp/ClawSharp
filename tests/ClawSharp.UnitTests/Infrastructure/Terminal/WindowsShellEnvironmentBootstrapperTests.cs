using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class WindowsShellEnvironmentBootstrapperTests
{
    [Fact]
    public void Initialize_Sets_Shell_To_Git_Bash_On_Windows()
    {
        string? variableName = null;
        string? variableValue = null;

        WindowsShellEnvironmentBootstrapper.Initialize(
            isWindows: true,
            resolveGitBashPath: static () => @"C:\Program Files\Git\bin\bash.exe",
            setEnvironmentVariable: (name, value) =>
            {
                variableName = name;
                variableValue = value;
            });

        Assert.Equal("SHELL", variableName);
        Assert.Equal(@"C:\Program Files\Git\bin\bash.exe", variableValue);
    }

    [Fact]
    public void Initialize_Does_Nothing_On_Non_Windows()
    {
        var invoked = false;

        WindowsShellEnvironmentBootstrapper.Initialize(
            isWindows: false,
            resolveGitBashPath: static () => throw new InvalidOperationException("should not run"),
            setEnvironmentVariable: (_, _) => invoked = true);

        Assert.False(invoked);
    }
}
