using ClawSharp.Contracts.Projects;

namespace ClawSharp.Application.Projects;

public interface IProjectQueryService
{
    Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(CancellationToken cancellationToken = default);
}
