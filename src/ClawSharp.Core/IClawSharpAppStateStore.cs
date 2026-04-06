namespace ClawSharp.Core;

public interface IClawSharpAppStateStore
{
    ClawSharpAppState GetState();

    void SetState(Func<ClawSharpAppState, ClawSharpAppState> updater);

    IDisposable Subscribe(Action listener);
}
