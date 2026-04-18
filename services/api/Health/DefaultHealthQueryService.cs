using ClawSharp.Application.Health;
using ClawSharp.Contracts.Health;

namespace ClawSharp.Api.Health;

public sealed class DefaultHealthQueryService : IHealthQueryService
{
    public Task<ApiHealthResponse> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            new ApiHealthResponse(
                "ClawSharp.Api",
                "ok",
                DateTimeOffset.UtcNow,
                ["desktop-remote", "mobile-remote"]));
    }
}
