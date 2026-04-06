using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class VersionCommandHandler : ICommandHandler
{
    public CommandDescriptor Descriptor { get; } =
        new("version", "Print the version this session is running", "/version");

    public Task<CommandResult> ExecuteAsync(
        string input,
        CommandExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CommandResult(true, AppMetadata.DisplayVersion));
    }
}
