// TS origin: ./commands/clear/index.ts, ./commands/clear/clear.ts, ./commands/clear/conversation.ts
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class ClearCommandHandler : ICommandHandler
{
    public CommandDescriptor Descriptor => new(
        "clear",
        "Clear conversation history and free up context",
        Usage: "/clear",
        Aliases: ["reset", "new"],
        Availability: [CommandAvailability.Universal]);

    public async Task<CommandResult> ExecuteAsync(
        string commandExpression,
        CommandExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // 1. Clear in-memory caches
        context.ReadFileState.Clear();
        
        // 2. Clear AppState parts (already handled by TerminalShell when SessionOverride is applied, 
        // but we can explicitly clear what's needed if it's not and we are in a more complex state)
        // TS implementation clears things like attribution and fileHistory in setAppState.
        
        // 3. Create a new session
        var newSession = context.SessionFactory.Create();
        
        // 4. Return the new session as override
        return new CommandResult(true, "Conversation cleared.", newSession);
    }
}
