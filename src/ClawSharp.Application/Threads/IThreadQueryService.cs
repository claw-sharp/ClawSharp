using ClawSharp.Contracts.Threads;

namespace ClawSharp.Application.Threads;

public interface IThreadQueryService
{
    Task<IReadOnlyList<ThreadSummary>> ListThreadsAsync(string projectId, CancellationToken cancellationToken = default);
}
