using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class RenameCommandHandler : ICommandHandler
{
    public CommandDescriptor Descriptor { get; } =
        new(
            "rename",
            "Rename the current conversation",
            "/rename [name]",
            IsInteractive: true);

    public async Task<CommandResult> ExecuteAsync(
        string input,
        CommandExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var newName = ParseArgument(input);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return new CommandResult(
                true,
                "Automatic session-name generation is not implemented yet. Use /rename <name>.");
        }

        context.Session.SetCustomTitle(newName);
        context.AppStateStore.SetState(state => ClawSharpAppStateMutations.WithActiveSession(state, context.Session));
        await context.TranscriptStore.RecordSessionMetadataAsync(context.Session, cancellationToken);
        return new CommandResult(true, $"Session renamed to: {context.Session.CustomTitle}");
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
