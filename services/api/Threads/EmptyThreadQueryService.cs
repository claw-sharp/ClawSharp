using ClawSharp.Application.Threads;
using ClawSharp.Contracts.Threads;

namespace ClawSharp.Api.Threads;

public sealed class EmptyThreadQueryService : IThreadQueryService
{
    public Task<IReadOnlyList<ThreadSummary>> ListThreadsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ThreadSummary>>([]);
    }
}
