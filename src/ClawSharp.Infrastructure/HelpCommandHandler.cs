using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class HelpCommandHandler : ICommandHandler
{
    private readonly CommandRegistry _commands;

    public HelpCommandHandler(CommandRegistry commands)
    {
        _commands = commands;
    }

    public CommandDescriptor Descriptor { get; } =
        new("help", "Show help and available commands", "/help", IsInteractive: true);

    public Task<CommandResult> ExecuteAsync(
        string input,
        CommandExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var lines = _commands.GetAllDescriptors()
            .Select(descriptor => $"/{descriptor.Name} - {descriptor.Description}");

        return Task.FromResult(new CommandResult(true, string.Join(Environment.NewLine, lines)));
    }
}
