// TS origin: ./state/store.ts, ./state/AppStateStore.ts
using ClawSharp.Tasks;

namespace ClawSharp.Core;

public sealed class NullClawSharpAppStateStore : IClawSharpAppStateStore
{
    private readonly ClawSharpAppState _state;

    public NullClawSharpAppStateStore(string workspaceRoot)
    {
        _state = ClawSharpAppState.CreateDefault(
            workspaceRoot,
            StartupEnvironment.Capture(),
            new ClawSharpSettings(),
            [],
            [],
            [],
            [],
            [],
            [],
            ToolPermissionContexts.CreateEmpty());
    }

    public ClawSharpAppState GetState() => _state;

    public void SetState(Func<ClawSharpAppState, ClawSharpAppState> updater)
    {
    }

    public IDisposable Subscribe(Action listener) => NoopDisposable.Instance;

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
