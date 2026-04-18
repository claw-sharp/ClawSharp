using ClawSharp.Contracts.Settings;

namespace ClawSharp.Application.Settings;

public interface ISettingsQueryService
{
    Task<RemoteRuntimeSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
}
