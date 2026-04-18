using ClawSharp.Contracts.Review;

namespace ClawSharp.Application.Review;

public interface IReviewQueryService
{
    Task<IReadOnlyList<ChangedFileSummary>> ListChangedFilesAsync(
        string projectId,
        string? threadId = null,
        CancellationToken cancellationToken = default);

    Task<FileDiff?> GetDiffAsync(
        string projectId,
        string filePath,
        string? threadId = null,
        CancellationToken cancellationToken = default);
}
