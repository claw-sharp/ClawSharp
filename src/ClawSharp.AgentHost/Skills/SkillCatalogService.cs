using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Services;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.AgentHost.Skills;

public sealed class SkillCatalogService
{
    private readonly WorkspaceApplicationRegistry _applicationRegistry;
    private readonly RecentProjectStore _recentProjectStore;

    public SkillCatalogService(
        WorkspaceApplicationRegistry applicationRegistry,
        RecentProjectStore recentProjectStore)
    {
        _applicationRegistry = applicationRegistry;
        _recentProjectStore = recentProjectStore;
    }

    public async Task<ListSkillsResponse> ListSkillsAsync(
        ListSkillsRequest request,
        CancellationToken cancellationToken = default)
    {
        var (projectId, app) = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        var state = app.AppStateStore.GetState();
        var skills = state.Skills
            .OrderBy(static skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static skill => new SkillSummaryDto(
                skill.Name,
                skill.Source,
                skill.FilePath,
                skill.BaseDirectory))
            .ToArray();

        return new ListSkillsResponse(projectId, state.WorkspaceRoot, skills);
    }

    private async Task<(string ProjectId, ClawSharpApplication App)> ResolveApplicationAsync(
        string? projectId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            var project = await _recentProjectStore.FindByIdAsync(projectId, cancellationToken);
            if (project is null)
            {
                throw new AgentHostException("project_not_found", $"Project '{projectId}' is not known to AgentHost yet.");
            }

            return (project.ProjectId, await _applicationRegistry.GetOrCreateAsync(project.Path, cancellationToken));
        }

        var recentProject = (await _recentProjectStore.ListAsync(cancellationToken)).FirstOrDefault();
        if (recentProject is null)
        {
            throw new AgentHostException("project_not_found", "No project is currently open.");
        }

        return (recentProject.ProjectId, await _applicationRegistry.GetOrCreateAsync(recentProject.Path, cancellationToken));
    }
}
