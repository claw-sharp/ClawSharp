namespace ClawSharp.Core;

public sealed class NullEventSink : IEventSink
{
    public void Publish(AppEvent appEvent)
    {
    }
}
