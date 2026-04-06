// TS origin: ./utils/messageQueueManager.ts, ./utils/queueProcessor.ts, ./utils/handlePromptSubmit.ts, ./query.ts
// TS parity status: drains queued task-notification commands into user-role transcript messages between turns; full 1:1 parity still depends on the real model-backed query loop and REPL queue processor.
using ClawSharp.Core;
using ClawSharp.Tasks;

namespace ClawSharp.Query;

public sealed class QueuedTaskNotificationDrainer
{
    private readonly IQueuedCommandQueue _queuedCommandQueue;
    private readonly ITranscriptStore _transcriptStore;
    private readonly TaskRegistry? _tasks;

    public QueuedTaskNotificationDrainer(
        IQueuedCommandQueue queuedCommandQueue,
        ITranscriptStore transcriptStore,
        TaskRegistry? tasks = null)
    {
        _queuedCommandQueue = queuedCommandQueue;
        _transcriptStore = transcriptStore;
        _tasks = tasks;
    }

    public async Task<IReadOnlyList<ChatMessage>> DrainAsync(
        ConversationSession session,
        CancellationToken cancellationToken = default)
    {
        if (_queuedCommandQueue.Peek(IsTaskNotification) is null)
        {
            return Array.Empty<ChatMessage>();
        }

        var commands = _queuedCommandQueue.DequeueAllMatching(IsTaskNotification);
        if (commands.Count == 0)
        {
            return Array.Empty<ChatMessage>();
        }

        var messages = commands
            .Select(command => ChatMessageFactory.CreateText(MessageRole.User, command.Value))
            .ToArray();

        foreach (var message in messages)
        {
            session.Add(message);
        }

        await _transcriptStore.RecordTranscriptAsync(session, session.Messages, cancellationToken);
        _tasks?.SweepEvictableTerminalTasks();
        return messages;
    }

    private static bool IsTaskNotification(QueuedCommand command)
    {
        return command.Mode == PromptInputMode.TaskNotification;
    }
}
