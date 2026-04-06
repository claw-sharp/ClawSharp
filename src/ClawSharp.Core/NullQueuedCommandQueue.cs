namespace ClawSharp.Core;

public sealed class NullQueuedCommandQueue : IQueuedCommandQueue
{
    public void Enqueue(QueuedCommand command)
    {
    }

    public void EnqueuePendingNotification(QueuedCommand command)
    {
    }

    public QueuedCommand? Dequeue(Func<QueuedCommand, bool>? filter = null)
    {
        return null;
    }

    public QueuedCommand? Peek(Func<QueuedCommand, bool>? filter = null)
    {
        return null;
    }

    public IReadOnlyList<QueuedCommand> DequeueAllMatching(Func<QueuedCommand, bool> predicate)
    {
        return Array.Empty<QueuedCommand>();
    }

    public IReadOnlyList<QueuedCommand> Snapshot()
    {
        return Array.Empty<QueuedCommand>();
    }
}
