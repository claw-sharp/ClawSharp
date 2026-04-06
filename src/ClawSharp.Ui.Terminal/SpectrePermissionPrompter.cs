using Spectre.Console;
using System.Threading;
using System.Threading.Tasks;
using ClawSharp.Core;

namespace ClawSharp.Ui.Terminal;

public sealed class SpectrePermissionPrompter : IPermissionPrompter
{
    public Task<PromptPermissionDecision> PromptAsync(string message, CancellationToken cancellationToken = default)
    {
        // Use Spectre.Console to show a SelectionPrompt to the user
        var prompt = new SelectionPrompt<string>()
            .Title($"[yellow]Permission requested:[/] {message}")
            .AddChoices([
                "Yes - Allow this operation",
                "No - Deny this operation",
                "Always - Allow this and remember for the session"
            ]);

        // Note: AnsiConsole.Prompt is synchronous and blocking.
        // It does not currently support cancellation tokens directly in Spectre.Console without hacks.
        // For CLI it waits on the main thread.
        var choice = AnsiConsole.Prompt(prompt);

        var decision = choice switch
        {
            "Yes - Allow this operation" => PromptPermissionDecision.Allow,
            "Always - Allow this and remember for the session" => PromptPermissionDecision.AlwaysAllow,
            _ => PromptPermissionDecision.Deny
        };

        return Task.FromResult(decision);
    }
}
