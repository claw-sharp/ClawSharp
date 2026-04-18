using ClawSharp.Application.Review;
using ClawSharp.Contracts.Review;

namespace ClawSharp.Api.Review;

public sealed class EmptyReviewQueryService : IReviewQueryService
{
    public Task<IReadOnlyList<ChangedFileSummary>> ListChangedFilesAsync(
        string projectId,
        string? threadId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ChangedFileSummary>>([]);
    }
}
