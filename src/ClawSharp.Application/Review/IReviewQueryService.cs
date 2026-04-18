using ClawSharp.Contracts.Review;

namespace ClawSharp.Application.Review;

public interface IReviewQueryService
{
    Task<IReadOnlyList<ChangedFileSummary>> ListChangedFilesAsync(
        string projectId,
        string? threadId = null,
        CancellationToken cancellationToken = default);
}
