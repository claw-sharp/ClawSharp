using System.Text;
using ClawSharp.Core;

namespace ClawSharp.Ui.Terminal;

public sealed class PromptInputReader
{
    public async Task<PromptSubmission?> ReadSubmissionAsync(
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder();
        string? rawInput = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            await output.WriteAsync("> ");
            var line = await input.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return rawInput is null
                    ? null
                    : BuildSubmission(rawInput, builder.ToString());
            }

            rawInput ??= line;
            if (line.EndsWith('\\'))
            {
                builder.Append(line[..^1]);
                builder.AppendLine();
                continue;
            }

            builder.Append(line);
            return BuildSubmission(rawInput, builder.ToString());
        }

        return null;
    }

    private static PromptSubmission BuildSubmission(string rawInput, string fullInput)
    {
        var mode = PromptInputModeHelpers.GetModeFromInput(rawInput);
        var value = mode == PromptInputMode.Prompt
            ? fullInput
            : PromptInputModeHelpers.GetValueFromInput(fullInput);
        return new PromptSubmission(mode, value, fullInput);
    }
}
