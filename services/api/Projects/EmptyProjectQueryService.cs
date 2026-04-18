using ClawSharp.Application.Projects;
using ClawSharp.Contracts.Projects;

namespace ClawSharp.Api.Projects;

public sealed class EmptyProjectQueryService : IProjectQueryService
{
    public Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ProjectSummary>>([]);
    }
}
