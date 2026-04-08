namespace ClawSharp.AgentHost.Ipc;

public sealed class AgentHostEventDispatcher
{
    private readonly Lock _lock = new();
    private Func<AgentHostEventEnvelope, CancellationToken, Task>? _publisher;

    public void SetPublisher(Func<AgentHostEventEnvelope, CancellationToken, Task> publisher)
    {
        lock (_lock)
        {
            _publisher = publisher;
        }
    }

    public Task PublishAsync(string eventName, object payload, CancellationToken cancellationToken = default)
    {
        Func<AgentHostEventEnvelope, CancellationToken, Task>? publisher;
        lock (_lock)
        {
            publisher = _publisher;
        }

        if (publisher is null)
        {
            return Task.CompletedTask;
        }

        return publisher(
            new AgentHostEventEnvelope(
                eventName,
                DateTimeOffset.UtcNow,
                payload),
            cancellationToken);
    }
}
