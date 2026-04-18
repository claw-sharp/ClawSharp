using ClawSharp.Application.Runs;
using ClawSharp.Contracts.Runs;

namespace ClawSharp.Api.Runs;

public sealed class NoOpRunCommandService : IRunCommandService
{
    public Task StartRunAsync(StartRunRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task CancelRunAsync(CancelRunRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task RetryRunAsync(RetryRunRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task ArchiveThreadAsync(ArchiveThreadRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
