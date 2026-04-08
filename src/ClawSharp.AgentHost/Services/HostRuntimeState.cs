namespace ClawSharp.AgentHost.Services;

public sealed class HostRuntimeState
{
    public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;
}
