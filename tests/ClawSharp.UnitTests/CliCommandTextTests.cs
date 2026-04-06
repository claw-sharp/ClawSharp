using ClawSharp.Core;

namespace ClawSharp.UnitTests;

public sealed class CliCommandTextTests
{
    [Fact]
    public void GetTopLevelHelpText_Includes_InstallSurface()
    {
        var helpText = CliCommandText.GetTopLevelHelpText();

        Assert.Contains("clawsharp --help", helpText, StringComparison.Ordinal);
        Assert.Contains("clawsharp completion <bash|zsh|powershell>", helpText, StringComparison.Ordinal);
        Assert.Contains("clawsharp update", helpText, StringComparison.Ordinal);
    }

    [Fact]
    public void GetUpdateText_Uses_Npm_Install_And_Update_Commands()
    {
        var updateText = CliCommandText.GetUpdateText();

        Assert.Contains("npm install -g clawsharp", updateText, StringComparison.Ordinal);
        Assert.Contains("npm update -g clawsharp", updateText, StringComparison.Ordinal);
        Assert.Contains("npm install -g clawsharp@latest", updateText, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetCompletionScript_Returns_Shell_Specific_Script()
    {
        Assert.True(CliCommandText.TryGetCompletionScript("bash", out var bashScript));
        Assert.Contains("complete -F _clawsharp_completions clawsharp", bashScript, StringComparison.Ordinal);

        Assert.True(CliCommandText.TryGetCompletionScript("powershell", out var powerShellScript));
        Assert.Contains("Register-ArgumentCompleter -Native -CommandName clawsharp", powerShellScript, StringComparison.Ordinal);

        Assert.False(CliCommandText.TryGetCompletionScript("fish", out _));
    }
}
