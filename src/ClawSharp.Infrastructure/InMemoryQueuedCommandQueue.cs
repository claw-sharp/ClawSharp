using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class InMemoryQueuedCommandQueue : IQueuedCommandQueue
{
    private readonly List<QueuedCommand> _commands = [];
    private readonly Lock _lock = new();

    public void Enqueue(QueuedCommand command)
    {
        lock (_lock)
        {
            _commands.Add(command with { Priority = command.Priority ?? QueuePriority.Next });
        }
    }

    public void EnqueuePendingNotification(QueuedCommand command)
    {
        lock (_lock)
        {
            _commands.Add(command with { Priority = command.Priority ?? QueuePriority.Later });
        }
    }

    public QueuedCommand? Dequeue(Func<QueuedCommand, bool>? filter = null)
    {
        lock (_lock)
        {
            var bestIndex = FindBestIndex(filter);
            if (bestIndex < 0)
            {
                return null;
            }

            var command = _commands[bestIndex];
            _commands.RemoveAt(bestIndex);
            return command;
        }
    }

    public QueuedCommand? Peek(Func<QueuedCommand, bool>? filter = null)
    {
        lock (_lock)
        {
            var bestIndex = FindBestIndex(filter);
            return bestIndex < 0
                ? null
                : _commands[bestIndex];
        }
    }

    public IReadOnlyList<QueuedCommand> DequeueAllMatching(Func<QueuedCommand, bool> predicate)
    {
        lock (_lock)
        {
            if (_commands.Count == 0)
            {
                return Array.Empty<QueuedCommand>();
            }

            var matched = new List<QueuedCommand>();
            var remaining = new List<QueuedCommand>(_commands.Count);
            foreach (var command in _commands)
            {
                if (predicate(command))
                {
                    matched.Add(command);
                }
                else
                {
                    remaining.Add(command);
                }
            }

            if (matched.Count == 0)
            {
                return Array.Empty<QueuedCommand>();
            }

            _commands.Clear();
            _commands.AddRange(remaining);
            return matched.ToArray();
        }
    }

    public IReadOnlyList<QueuedCommand> Snapshot()
    {
        lock (_lock)
        {
            return _commands.ToArray();
        }
    }

    private int FindBestIndex(Func<QueuedCommand, bool>? filter)
    {
        if (_commands.Count == 0)
        {
            return -1;
        }

        var bestIndex = -1;
        var bestPriority = int.MaxValue;
        for (var index = 0; index < _commands.Count; index++)
        {
            var command = _commands[index];
            if (filter is not null && !filter(command))
            {
                continue;
            }

            var priority = ToSortOrder(command.Priority ?? QueuePriority.Next);
            if (priority < bestPriority)
            {
                bestPriority = priority;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private static int ToSortOrder(QueuePriority priority)
    {
        return priority switch
        {
            QueuePriority.Now => 0,
            QueuePriority.Next => 1,
            _ => 2
        };
    }
}
