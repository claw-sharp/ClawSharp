// TS origin: ./state/onChangeAppState.ts
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class OnChangeClawSharpAppState
{
    private readonly ISettingsStore _settingsStore;

    public OnChangeClawSharpAppState(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public void Handle(ClawSharpAppState newState, ClawSharpAppState oldState)
    {
        if (!ReferenceEquals(newState.Settings, oldState.Settings))
        {
            _settingsStore.SaveAsync(newState.Settings).GetAwaiter().GetResult();
        }
    }
}
