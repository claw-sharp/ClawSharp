using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class CompositeFileUpdateNotifier : IFileUpdateNotifier
{
    private readonly IReadOnlyList<IFileUpdateNotifier> _notifiers;

    public CompositeFileUpdateNotifier(params IReadOnlyList<IFileUpdateNotifier> notifiers)
    {
        _notifiers = notifiers;
    }

    public async Task HandleQueryStartAsync(CancellationToken cancellationToken = default)
    {
        foreach (var notifier in _notifiers)
        {
            await notifier.HandleQueryStartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task BeforeFileEditedAsync(string filePath, CancellationToken cancellationToken = default)
    {
        foreach (var notifier in _notifiers)
        {
            await notifier.BeforeFileEditedAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ClearDiagnosticsForFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        foreach (var notifier in _notifiers)
        {
            await notifier.ClearDiagnosticsForFileAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task NotifyFileUpdatedAsync(
        string filePath,
        string? oldContent,
        string? newContent,
        CancellationToken cancellationToken = default)
    {
        foreach (var notifier in _notifiers)
        {
            await notifier.NotifyFileUpdatedAsync(filePath, oldContent, newContent, cancellationToken).ConfigureAwait(false);
        }
    }
}
