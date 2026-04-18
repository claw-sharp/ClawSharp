using ClawSharp.Contracts.Capabilities;

namespace ClawSharp.Application.Capabilities;

public interface ICapabilityQueryService
{
    Task<ApiCapabilitiesResponse> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
}
