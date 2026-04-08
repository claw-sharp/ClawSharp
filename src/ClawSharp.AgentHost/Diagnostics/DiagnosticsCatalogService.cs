using System.Diagnostics;
using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Services;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.AgentHost.Diagnostics;

public sealed class DiagnosticsCatalogService
{
    private readonly WorkspaceApplicationRegistry _applicationRegistry;
    private readonly RecentProjectStore _recentProjectStore;
    private readonly HostRuntimeState _runtimeState;

    public DiagnosticsCatalogService(
        WorkspaceApplicationRegistry applicationRegistry,
        RecentProjectStore recentProjectStore,
        HostRuntimeState runtimeState)
    {
        _applicationRegistry = applicationRegistry;
        _recentProjectStore = recentProjectStore;
        _runtimeState = runtimeState;
    }

    public async Task<ListDiagnosticsResponse> ListDiagnosticsAsync(
        ListDiagnosticsRequest request,
        CancellationToken cancellationToken = default)
    {
        var app = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        var state = app.AppStateStore.GetState();
        var runtime = ProviderRuntimeResolver.Resolve(state.Settings, state.MainLoopModel ?? state.Settings.Runtime.Model);
        var recentEvents = (app.EventSink as Infrastructure.InMemoryEventSink)?.Events
            .TakeLast(25)
            .Select(MapRecentEvent)
            .ToArray() ?? [];
        var issues = state.SettingsIssues.Select(issue => $"{issue.File}: {issue.Message}").ToArray();

        var diagnostics = new DiagnosticsSummaryDto(
            request.ThreadId,
            request.ThreadId,
            runtime.Provider.ToString().ToLowerInvariant(),
            runtime.ResolvedModel,
            runtime.BaseUrl,
            runtime.Transport.ToString(),
            Environment.OSVersion.VersionString,
            ClaudeConfigPaths.GetUserSettingsFilePath(),
            FormatUptime(DateTimeOffset.UtcNow - _runtimeState.StartedAtUtc),
            FormatMemory(Process.GetCurrentProcess().WorkingSet64),
            new DiagnosticsLogPathsDto(
                ClawSharpTelemetry.GetDebugLogPath(),
                ClawSharpTelemetry.GetTelemetryEventsPath(),
                ClawSharpTelemetry.GetMetricsPath(),
                ClawSharpTelemetry.GetCrashPath(),
                ClawSharpTelemetry.GetPerfettoTracePath(),
                Infrastructure.StartupProfiler.GetStartupPerfLogPath(request.ThreadId)),
            issues,
            [],
            recentEvents);
        return new ListDiagnosticsResponse(diagnostics);
    }

    private async Task<Infrastructure.ClawSharpApplication> ResolveApplicationAsync(
        string? projectId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            var recent = (await _recentProjectStore.ListAsync(cancellationToken)).FirstOrDefault();
            if (recent is null)
            {
                throw new AgentHostException("project_not_found", "No project is currently open.");
            }

            return await _applicationRegistry.GetOrCreateAsync(recent.Path, cancellationToken);
        }

        var project = await _recentProjectStore.FindByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            throw new AgentHostException("project_not_found", $"Project '{projectId}' is not known to AgentHost yet.");
        }

        return await _applicationRegistry.GetOrCreateAsync(project.Path, cancellationToken);
    }

    private static DiagnosticsRecentEventDto MapRecentEvent(AppEvent appEvent)
    {
        var level = appEvent.Type is AppEventType.ToolExecutionCompleted or AppEventType.QueryCompleted
            ? "info"
            : appEvent.Type is AppEventType.ToolExecutionStarted or AppEventType.ToolExecutionProgress or AppEventType.QueryStreaming
                ? "debug"
                : "info";
        return new DiagnosticsRecentEventDto(
            Guid.NewGuid().ToString("N"),
            appEvent.Timestamp.ToString("O"),
            level,
            appEvent.Type.ToString(),
            appEvent.Message);
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalHours >= 1)
        {
            return $"{uptime.TotalHours:F1}h";
        }

        if (uptime.TotalMinutes >= 1)
        {
            return $"{uptime.TotalMinutes:F1}m";
        }

        return $"{uptime.TotalSeconds:F0}s";
    }

    private static string FormatMemory(long bytes)
    {
        var megabytes = bytes / 1024d / 1024d;
        return $"{megabytes:F1} MB";
    }
}
