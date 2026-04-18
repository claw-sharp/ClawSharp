using ClawSharp.Application.Settings;
using ClawSharp.Contracts.Settings;

namespace ClawSharp.Api.Settings;

public sealed class DefaultSettingsQueryService : ISettingsQueryService
{
    public Task<RemoteRuntimeSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            new RemoteRuntimeSettings(
                Provider: "anthropic",
                Model: "claude-haiku-4-5-20251001",
                FallbackModel: null,
                PermissionMode: "Default",
                EnableTelemetry: true,
                BaseUrl: "https://api.anthropic.com",
                Transport: "AnthropicMessages"));
    }
}
