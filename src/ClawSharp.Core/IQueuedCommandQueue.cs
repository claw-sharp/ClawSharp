// TS origin: ./utils/messageQueueManager.ts
namespace ClawSharp.Core;

public interface IQueuedCommandQueue
{
    void Enqueue(QueuedCommand command);

    void EnqueuePendingNotification(QueuedCommand command);

    QueuedCommand? Dequeue(Func<QueuedCommand, bool>? filter = null);

    QueuedCommand? Peek(Func<QueuedCommand, bool>? filter = null);

    IReadOnlyList<QueuedCommand> DequeueAllMatching(Func<QueuedCommand, bool> predicate);

    IReadOnlyList<QueuedCommand> Snapshot();
}
