using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class AddClaudeKeyCommandHandler : ICommandHandler
{
    public CommandDescriptor Descriptor { get; } =
        new(
            "add-claude-key",
            "Store a Claude API key for this user",
            "/add-claude-key <api-key>",
            IsInteractive: true);

    public Task<CommandResult> ExecuteAsync(
        string input,
        CommandExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var apiKey = ParseArgument(input);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Task.FromResult(
                new CommandResult(
                    true,
                    "Usage: /add-claude-key <api-key>"));
        }

        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", apiKey);
        context.AppStateStore.SetState(state => ClawSharpAppStateMutations.WithClaudeApiKey(state, apiKey));

        return Task.FromResult(
            new CommandResult(
                true,
                "Claude API key saved. Future requests in this session will use it for authentication."));
    }

    private static string? ParseArgument(string input)
    {
        var trimmed = input.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace < 0 || firstSpace == trimmed.Length - 1)
        {
            return null;
        }

        return trimmed[(firstSpace + 1)..].Trim();
    }
}
