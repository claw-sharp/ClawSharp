using ClawSharp.Contracts.Runs;

namespace ClawSharp.Application.Runs;

public interface IRunEventStreamService
{
    IAsyncEnumerable<RunEventEnvelope> StreamThreadEventsAsync(
        string threadId,
        CancellationToken cancellationToken = default);
}
