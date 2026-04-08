using System.Text.Json;
using ClawSharp.Core;

namespace ClawSharp.AgentHost.Sessions;

public sealed class ThreadStateStore
{
    private readonly Lock _lock = new();
    private readonly string _path;

    public ThreadStateStore(string? path = null)
    {
        _path = path ?? Path.Combine(SessionStoragePaths.GetClaudeConfigHomeDir(), "desktop-thread-state.json");
    }

    public bool IsArchived(string projectId, string threadId)
    {
        lock (_lock)
        {
            var state = LoadStateUnsafe();
            return state.TryGetValue(projectId, out var projectState) &&
                   projectState.ArchivedThreadIds.Contains(threadId);
        }
    }

    public void Archive(string projectId, string threadId)
    {
        lock (_lock)
        {
            var state = LoadStateUnsafe();
            if (!state.TryGetValue(projectId, out var projectState))
            {
                projectState = new ProjectThreadState([], []);
            }

            var archived = new HashSet<string>(projectState.ArchivedThreadIds, StringComparer.Ordinal)
            {
                threadId
            };
            state[projectId] = projectState with { ArchivedThreadIds = archived.ToArray() };
            SaveStateUnsafe(state);
        }
    }

    private Dictionary<string, ProjectThreadState> LoadStateUnsafe()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, ProjectThreadState>(StringComparer.Ordinal);
        }

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<Dictionary<string, ProjectThreadState>>(json)
                   ?? new Dictionary<string, ProjectThreadState>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, ProjectThreadState>(StringComparer.Ordinal);
        }
    }

    private void SaveStateUnsafe(Dictionary<string, ProjectThreadState> state)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(state));
    }

    private sealed record ProjectThreadState(
        string[] ArchivedThreadIds,
        string[] Reserved);
}
