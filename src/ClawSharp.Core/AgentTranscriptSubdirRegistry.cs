namespace ClawSharp.Core;

public sealed class AgentTranscriptSubdirRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, string> _subdirs = new(StringComparer.Ordinal);

    public void Set(string agentId, string subdir)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("Agent id is required.", nameof(agentId));
        }

        if (string.IsNullOrWhiteSpace(subdir))
        {
            throw new ArgumentException("Subdirectory is required.", nameof(subdir));
        }

        lock (_lock)
        {
            _subdirs[agentId] = subdir;
        }
    }

    public void Clear(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return;
        }

        lock (_lock)
        {
            _subdirs.Remove(agentId);
        }
    }

    public string? Get(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return null;
        }

        lock (_lock)
        {
            return _subdirs.TryGetValue(agentId, out var subdir) ? subdir : null;
        }
    }
}
