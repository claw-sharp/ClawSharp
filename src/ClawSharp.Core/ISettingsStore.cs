namespace ClawSharp.Core;

public interface ISettingsStore
{
    Task<ClawSharpSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ClawSharpSettings settings, CancellationToken cancellationToken = default);
}
