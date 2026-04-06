using ClawSharp.Core;
using Spectre.Console;

namespace ClawSharp.Ui.Terminal;

public sealed class SpectreInteractionService : IInteractionService
{
    private readonly IAnsiConsole _console;

    public SpectreInteractionService(IAnsiConsole? console = null)
    {
        _console = console ?? AnsiConsole.Console;
    }

    public Task<string?> SelectAsync(string prompt, IEnumerable<string> options, CancellationToken cancellationToken = default)
    {
        var selection = _console.Prompt(
            new SelectionPrompt<string>()
                .Title(prompt)
                .PageSize(10)
                .AddChoices(options));

        return Task.FromResult<string?>(selection);
    }

    public Task<string> AskAsync(string prompt, bool secret = false, CancellationToken cancellationToken = default)
    {
        var textPrompt = new TextPrompt<string>(prompt);
        if (secret)
        {
            textPrompt.PromptStyle("red").Secret();
        }

        return Task.FromResult(_console.Prompt(textPrompt));
    }

    public Task<bool> ConfirmAsync(string prompt, bool defaultValue = true, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_console.Confirm(prompt, defaultValue));
    }
}
