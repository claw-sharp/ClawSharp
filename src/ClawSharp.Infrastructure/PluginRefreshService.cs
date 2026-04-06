using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class PluginRefreshService
{
    private readonly ExtensionBootstrapper _extensionBootstrapper;
    private readonly IClawSharpAppStateStore _appStateStore;

    public PluginRefreshService(
        ExtensionBootstrapper extensionBootstrapper,
        IClawSharpAppStateStore appStateStore)
    {
        _extensionBootstrapper = extensionBootstrapper;
        _appStateStore = appStateStore;
    }

    public async Task<PluginRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var state = _appStateStore.GetState();
        var extensions = await _extensionBootstrapper.LoadAsync(
            state.WorkspaceRoot,
            state.Environment,
            state.Settings,
            cancellationToken);

        _appStateStore.SetState(current => current with
        {
            PluginInstallations = extensions.PluginInstallations,
            Plugins = extensions.Plugins,
            Skills = extensions.Skills,
            Hooks = extensions.Hooks
        });

        stopwatch.Stop();
        ClawSharpTelemetry.LogEvent(
            "tengu_plugin_refresh",
            new Dictionary<string, object?>
            {
                ["enabled_plugin_count"] = extensions.Plugins.Count(static plugin => plugin.Enabled),
                ["disabled_plugin_count"] = extensions.Plugins.Count(static plugin => !plugin.Enabled),
                ["skill_count"] = extensions.Skills.Count,
                ["hook_count"] = extensions.Hooks.Count,
                ["duration_ms"] = stopwatch.Elapsed.TotalMilliseconds
            });
        ClawSharpTelemetry.RecordMetric("plugin.refresh.duration_ms", stopwatch.Elapsed.TotalMilliseconds);

        return new PluginRefreshResult(
            extensions.Plugins.Count(static plugin => plugin.Enabled),
            extensions.Plugins.Count(static plugin => !plugin.Enabled),
            extensions.Skills.Count,
            extensions.Hooks.Count,
            extensions.Plugins.Sum(static plugin => plugin.ValidationIssues.Count));
    }
}

public sealed record PluginRefreshResult(
    int EnabledPluginCount,
    int DisabledPluginCount,
    int SkillCount,
    int HookCount,
    int ValidationIssueCount);
