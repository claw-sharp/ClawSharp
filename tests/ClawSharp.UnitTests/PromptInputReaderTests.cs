// TS origin: ./components/PromptInput/PromptInput.tsx, ./components/TextInput.tsx, ./hooks/useTextInput.ts
using ClawSharp.Core;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.UnitTests;

public sealed class PromptInputReaderTests
{
    [Fact]
    public async Task ReadSubmissionAsync_Joins_Backslash_Continued_Lines()
    {
        var reader = new PromptInputReader();
        using var input = new StringReader("hello\\\nworld\n");
        using var output = new StringWriter();

        var submission = await reader.ReadSubmissionAsync(input, output);

        Assert.NotNull(submission);
        Assert.Equal(PromptInputMode.Prompt, submission!.Mode);
        Assert.Equal("hello" + Environment.NewLine + "world", submission.Value);
        Assert.Equal("hello" + Environment.NewLine + "world", submission.RawInput);
        Assert.Equal("> > ", output.ToString());
    }

    [Fact]
    public async Task ReadSubmissionAsync_Strips_Bash_Mode_Prefix_From_Value()
    {
        var reader = new PromptInputReader();
        using var input = new StringReader("!git status\n");
        using var output = new StringWriter();

        var submission = await reader.ReadSubmissionAsync(input, output);

        Assert.NotNull(submission);
        Assert.Equal(PromptInputMode.Bash, submission!.Mode);
        Assert.Equal("git status", submission.Value);
        Assert.Equal("!git status", submission.RawInput);
    }
}
