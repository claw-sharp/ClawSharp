using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.AgentHost.Mapping;
using ClawSharp.AgentHost.Sessions;

namespace ClawSharp.AgentHost.Projects;

public sealed class ProjectCatalogService
{
    private readonly RecentProjectStore _recentProjectStore;
    private readonly ThreadCatalogService _threadCatalogService;

    public ProjectCatalogService(
        RecentProjectStore recentProjectStore,
        ThreadCatalogService threadCatalogService)
    {
        _recentProjectStore = recentProjectStore;
        _threadCatalogService = threadCatalogService;
    }

    public async Task<OpenProjectResponse> OpenProjectAsync(
        OpenProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = Services.WorkspaceApplicationRegistry.NormalizeWorkspaceRoot(request.ProjectPath);
        var threads = await _threadCatalogService.ListThreadsByPathAsync(normalizedPath, cancellationToken);
        var project = DesktopContractMapper.MapProject(normalizedPath, threads, DateTimeOffset.UtcNow);
        await _recentProjectStore.RecordOpenAsync(project, cancellationToken);
        return new OpenProjectResponse(project, threads);
    }

    public async Task<ListRecentProjectsResponse> ListRecentProjectsAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _recentProjectStore.ListAsync(cancellationToken);
        var projects = new List<ProjectSummaryDto>(entries.Count);
        foreach (var entry in entries)
        {
            var threads = await _threadCatalogService.ListThreadsByPathAsync(entry.Path, cancellationToken);
            projects.Add(DesktopContractMapper.MapProject(entry.Path, threads, entry.LastOpenedAt));
        }

        return new ListRecentProjectsResponse(projects);
    }

    public async Task<string> ResolveProjectPathAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var project = await _recentProjectStore.FindByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            throw new AgentHostException(
                "project_not_found",
                $"Project '{projectId}' is not known to AgentHost yet. Open the project first.");
        }

        return project.Path;
    }
}
