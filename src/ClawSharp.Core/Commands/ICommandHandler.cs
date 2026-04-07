namespace ClawSharp.Core;

public interface ICommandHandler
{
    CommandDescriptor Descriptor { get; }
    Task<CommandResult> ExecuteAsync(
        string input,
        CommandExecutionContext context,
        CancellationToken cancellationToken = default);
}
