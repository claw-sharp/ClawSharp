// TS origin: ./types/command.ts, ./services/mcp/client.ts
namespace ClawSharp.Core;

public interface IMcpPromptCommandHandler
{
    CommandDescriptor Descriptor { get; }

    Task<IReadOnlyList<ChatMessage>> GetPromptMessagesAsync(
        string arguments,
        CommandExecutionContext context,
        CancellationToken cancellationToken = default);
}
