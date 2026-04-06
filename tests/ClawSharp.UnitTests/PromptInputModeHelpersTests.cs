using ClawSharp.Core;

namespace ClawSharp.UnitTests;

public sealed class PromptInputModeHelpersTests
{
    [Fact]
    public void GetModeFromInput_Returns_Bash_For_Bang_Prefixed_Input()
    {
        Assert.Equal(PromptInputMode.Bash, PromptInputModeHelpers.GetModeFromInput("!pwd"));
    }

    [Fact]
    public void GetValueFromInput_Strips_Bash_Mode_Prefix()
    {
        Assert.Equal("pwd", PromptInputModeHelpers.GetValueFromInput("!pwd"));
    }

    [Fact]
    public void PrependModeCharacterToInput_Prefixes_Bash_Mode()
    {
        Assert.Equal("!pwd", PromptInputModeHelpers.PrependModeCharacterToInput("pwd", PromptInputMode.Bash));
    }
}
