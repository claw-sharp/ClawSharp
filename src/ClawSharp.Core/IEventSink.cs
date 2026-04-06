// TS origin: no direct 1:1 source yet; event flow is currently derived from ./query.ts, ./QueryEngine.ts, and ./utils/commandLifecycle.ts.
namespace ClawSharp.Core;

public interface IEventSink
{
    void Publish(AppEvent appEvent);
}
