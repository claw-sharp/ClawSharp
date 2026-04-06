// TS origin: no direct 1:1 source yet; event flow is currently derived from ./query.ts, ./QueryEngine.ts, and ./utils/commandLifecycle.ts.
namespace ClawSharp.Core;

public sealed class NullEventSink : IEventSink
{
    public void Publish(AppEvent appEvent)
    {
    }
}
