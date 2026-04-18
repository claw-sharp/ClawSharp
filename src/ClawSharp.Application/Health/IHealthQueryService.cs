using ClawSharp.Contracts.Health;

namespace ClawSharp.Application.Health;

public interface IHealthQueryService
{
    Task<ApiHealthResponse> GetHealthAsync(CancellationToken cancellationToken = default);
}
