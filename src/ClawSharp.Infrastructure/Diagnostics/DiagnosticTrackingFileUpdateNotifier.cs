using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class DiagnosticTrackingFileUpdateNotifier : IFileUpdateNotifier
{
    private readonly DiagnosticTrackingService _diagnosticTrackingService;

    public DiagnosticTrackingFileUpdateNotifier(DiagnosticTrackingService diagnosticTrackingService)
    {
        _diagnosticTrackingService = diagnosticTrackingService;
    }

    public Task HandleQueryStartAsync(CancellationToken cancellationToken = default)
    {
        return _diagnosticTrackingService.HandleQueryStartAsync(cancellationToken);
    }

    public Task BeforeFileEditedAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return _diagnosticTrackingService.BeforeFileEditedAsync(filePath, cancellationToken);
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
