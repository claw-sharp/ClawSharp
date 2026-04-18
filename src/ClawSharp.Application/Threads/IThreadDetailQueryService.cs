using ClawSharp.Contracts.Threads;

namespace ClawSharp.Application.Threads;

public interface IThreadDetailQueryService
{
    Task<ThreadDetail?> GetThreadAsync(
        string projectId,
        string threadId,
        string? beforeMessageId = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default);

    Task<ThreadDetail> CreateThreadAsync(
        CreateThreadRequest request,
        CancellationToken cancellationToken = default);
}
