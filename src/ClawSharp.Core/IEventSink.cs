namespace ClawSharp.Core;

public interface IEventSink
{
    void Publish(AppEvent appEvent);
}
