using ClawSharp.Contracts.Runs;

namespace ClawSharp.Application.Runs;

public interface IRunCommandService
{
    Task StartRunAsync(StartRunRequest request, CancellationToken cancellationToken = default);

    Task CancelRunAsync(CancelRunRequest request, CancellationToken cancellationToken = default);

    Task RetryRunAsync(RetryRunRequest request, CancellationToken cancellationToken = default);

    Task ArchiveThreadAsync(ArchiveThreadRequest request, CancellationToken cancellationToken = default);
}
