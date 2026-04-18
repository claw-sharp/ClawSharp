using ClawSharp.Application.Runs;
using ClawSharp.Contracts.Runs;

namespace ClawSharp.Api.Runs;

public sealed class NoOpRunCommandService : IRunCommandService
{
    public Task<string> StartRunAsync(StartRunRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("run-noop");
    }

    public Task CancelRunAsync(CancelRunRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<string> RetryRunAsync(RetryRunRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("run-noop");
    }

    public Task ArchiveThreadAsync(ArchiveThreadRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
