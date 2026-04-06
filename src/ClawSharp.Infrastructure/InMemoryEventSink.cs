using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class InMemoryEventSink : IEventSink
{
    private readonly List<AppEvent> _events = [];

    public IReadOnlyList<AppEvent> Events => _events;

    public void Publish(AppEvent appEvent)
    {
        _events.Add(appEvent);
        ClawSharpTelemetry.LogEvent(
            "app_event",
            new Dictionary<string, object?>
            {
                ["type"] = appEvent.Type.ToString(),
                ["message"] = appEvent.Message,
                ["timestamp"] = appEvent.Timestamp.ToUnixTimeMilliseconds(),
                ["metadata_count"] = appEvent.Metadata?.Count ?? 0
            });
        ClawSharpTelemetry.RecordMetric(
            "app_event.count",
            1,
            new Dictionary<string, object?>
            {
                ["type"] = appEvent.Type.ToString()
            });
    }
}
