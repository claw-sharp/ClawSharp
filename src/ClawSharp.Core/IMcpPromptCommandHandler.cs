namespace ClawSharp.Core;

public interface IMcpPromptCommandHandler
{
    CommandDescriptor Descriptor { get; }

    Task<IReadOnlyList<ChatMessage>> GetPromptMessagesAsync(
        string arguments,
        CommandExecutionContext context,
        CancellationToken cancellationToken = default);
}
