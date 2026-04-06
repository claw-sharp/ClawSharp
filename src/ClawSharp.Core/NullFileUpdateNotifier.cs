namespace ClawSharp.Core;

public sealed class NullFileUpdateNotifier : IFileUpdateNotifier
{
    public Task HandleQueryStartAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task BeforeFileEditedAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task ClearDiagnosticsForFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task NotifyFileUpdatedAsync(
        string filePath,
        string? oldContent,
        string? newContent,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
