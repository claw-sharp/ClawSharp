// TS origin: ./state/store.ts, ./state/AppStateStore.ts
namespace ClawSharp.Core;

public interface IClawSharpAppStateStore
{
    ClawSharpAppState GetState();

    void SetState(Func<ClawSharpAppState, ClawSharpAppState> updater);

    IDisposable Subscribe(Action listener);
}
