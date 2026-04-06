using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class EventSinkFileUpdateNotifier : IFileUpdateNotifier
{
    private readonly IEventSink _eventSink;

    public EventSinkFileUpdateNotifier(IEventSink eventSink)
    {
        _eventSink = eventSink;
    }

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
        _eventSink.Publish(
            new AppEvent(
                AppEventType.NotificationRaised,
                $"Cleared delivered diagnostics for {filePath}",
                DateTimeOffset.UtcNow,
                new Dictionary<string, string>
                {
                    ["notificationType"] = "file-diagnostics-cleared",
                    ["filePath"] = filePath
                }));

        return Task.CompletedTask;
    }

    public Task NotifyFileUpdatedAsync(
        string filePath,
        string? oldContent,
        string? newContent,
        CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, string>
        {
            ["notificationType"] = "file-updated",
            ["filePath"] = filePath
        };

        if (oldContent is not null)
        {
            metadata["oldContent"] = oldContent;
        }

        if (newContent is not null)
        {
            metadata["newContent"] = newContent;
        }

        _eventSink.Publish(
            new AppEvent(
                AppEventType.NotificationRaised,
                $"File updated: {filePath}",
                DateTimeOffset.UtcNow,
                metadata));

        return Task.CompletedTask;
    }
}
