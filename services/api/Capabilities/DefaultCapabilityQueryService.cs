using ClawSharp.Application.Capabilities;
using ClawSharp.Contracts.Capabilities;

namespace ClawSharp.Api.Capabilities;

public sealed class DefaultCapabilityQueryService : ICapabilityQueryService
{
    public Task<ApiCapabilitiesResponse> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            new ApiCapabilitiesResponse(
                [
                    new ApiCapabilitySummary("desktop-local", "Desktop Local AgentHost", true),
                    new ApiCapabilitySummary("desktop-remote", "Desktop Remote API", true),
                    new ApiCapabilitySummary("mobile-remote", "Mobile Remote API", true),
                    new ApiCapabilitySummary("run-streaming", "Run Streaming", false)
                ]));
    }
}
