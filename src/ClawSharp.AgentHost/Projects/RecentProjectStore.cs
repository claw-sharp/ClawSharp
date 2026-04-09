using System.Text.Json;
using ClawSharp.AgentHost.Contracts;
using ClawSharp.Core;

namespace ClawSharp.AgentHost.Projects;

public sealed class RecentProjectStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _storagePath;

    public RecentProjectStore(string? storagePath = null)
    {
        _storagePath = storagePath ?? Path.Combine(
            SessionStoragePaths.GetClaudeConfigHomeDir(),
            "desktop",
            "recent-projects.json");
    }

    public async Task RecordOpenAsync(ProjectSummaryDto project, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var projects = await LoadCoreAsync(cancellationToken);
            projects.RemoveAll(item => string.Equals(item.ProjectId, project.Id, StringComparison.Ordinal));
            projects.Add(new RecentProjectEntry(
                project.Id,
                project.Name,
                project.Path,
                project.LastOpenedAt,
                project.LastUpdatedAt,
                project.ThreadCount,
                project.GitBranch));
            projects.Sort(static (left, right) => right.LastOpenedAt.CompareTo(left.LastOpenedAt));
            await SaveCoreAsync(projects, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<RecentProjectEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var projects = await LoadCoreAsync(cancellationToken);
            var filtered = projects
                .Where(project => Directory.Exists(project.Path))
                .OrderByDescending(project => project.LastOpenedAt)
                .ToArray();
            return filtered;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecentProjectEntry?> FindByIdAsync(string projectId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        var projects = await ListAsync(cancellationToken);
        return projects.FirstOrDefault(project => string.Equals(project.ProjectId, projectId, StringComparison.Ordinal));
    }

    private async Task<List<RecentProjectEntry>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storagePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_storagePath);
        var projects = await JsonSerializer.DeserializeAsync<List<RecentProjectEntry>>(
            stream,
            AgentHost.Ipc.AgentHostProtocol.JsonOptions,
            cancellationToken);
        return projects ?? [];
    }

    private async Task SaveCoreAsync(IReadOnlyList<RecentProjectEntry> projects, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_storagePath);
        await JsonSerializer.SerializeAsync(stream, projects, AgentHost.Ipc.AgentHostProtocol.JsonOptions, cancellationToken);
    }
}

public sealed record RecentProjectEntry(
    string ProjectId,
    string Name,
    string Path,
    DateTimeOffset LastOpenedAt,
    DateTimeOffset? LastUpdatedAt = null,
    int ThreadCount = 0,
    string? GitBranch = null);
