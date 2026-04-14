using System.Text.Json;
using ClawSharp.AgentHost.Approvals;
using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Diagnostics;
using ClawSharp.AgentHost.Files;
using ClawSharp.AgentHost.Plugins;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Providers;
using ClawSharp.AgentHost.Review;
using ClawSharp.AgentHost.Runs;
using ClawSharp.AgentHost.Services;
using ClawSharp.AgentHost.Sessions;
using ClawSharp.AgentHost.Skills;
using ClawSharp.Core;

namespace ClawSharp.AgentHost.Ipc;

public sealed class AgentHostCommandRouter
{
    private readonly ProjectCatalogService _projectCatalog;
    private readonly ThreadCatalogService _threadCatalog;
    private readonly RunCoordinator _runCoordinator;
    private readonly WorkspaceReviewService _reviewService;
    private readonly DiagnosticsCatalogService _diagnosticsService;
    private readonly PluginCatalogService _pluginCatalog;
    private readonly SkillCatalogService _skillCatalog;
    private readonly WorkspaceFileCatalogService _workspaceFileCatalog;
    private readonly ProviderCatalogService _providerCatalog;
    private readonly ExternalEditorService _externalEditorService;
    private readonly ApprovalCatalogService _approvalCatalog;

    public AgentHostCommandRouter(
        ProjectCatalogService projectCatalog,
        ThreadCatalogService threadCatalog,
        RunCoordinator runCoordinator,
        WorkspaceReviewService reviewService,
        DiagnosticsCatalogService diagnosticsService,
        PluginCatalogService pluginCatalog,
        SkillCatalogService skillCatalog,
        WorkspaceFileCatalogService workspaceFileCatalog,
        ProviderCatalogService providerCatalog,
        ExternalEditorService externalEditorService,
        ApprovalCatalogService approvalCatalog)
    {
        _projectCatalog = projectCatalog;
        _threadCatalog = threadCatalog;
        _runCoordinator = runCoordinator;
        _reviewService = reviewService;
        _diagnosticsService = diagnosticsService;
        _pluginCatalog = pluginCatalog;
        _skillCatalog = skillCatalog;
        _workspaceFileCatalog = workspaceFileCatalog;
        _providerCatalog = providerCatalog;
        _externalEditorService = externalEditorService;
        _approvalCatalog = approvalCatalog;
    }

    public async Task<object> ExecuteAsync(AgentHostRequestEnvelope request, CancellationToken cancellationToken)
    {
        return request.Command switch
        {
            "health" => CreateHealthResponse(),
            "openProject" => await _projectCatalog.OpenProjectAsync(DeserializePayload<OpenProjectRequest>(request), cancellationToken),
            "listRecentProjects" => await _projectCatalog.ListRecentProjectsAsync(cancellationToken),
            "listThreads" => await _threadCatalog.ListThreadsAsync(DeserializePayload<ListThreadsRequest>(request), cancellationToken),
            "createThread" => await _threadCatalog.CreateThreadAsync(DeserializePayload<CreateThreadRequest>(request), cancellationToken),
            "getThread" => await _threadCatalog.GetThreadAsync(DeserializePayload<GetThreadRequest>(request), cancellationToken),
            "renameThread" => await _threadCatalog.RenameThreadAsync(DeserializePayload<RenameThreadRequest>(request), cancellationToken),
            "archiveThread" => await _threadCatalog.ArchiveThreadAsync(DeserializePayload<ArchiveThreadRequest>(request), cancellationToken),
            "startRun" => await _runCoordinator.StartRunAsync(DeserializePayload<StartRunRequest>(request), cancellationToken),
            "cancelRun" => await _runCoordinator.CancelRunAsync(DeserializePayload<CancelRunRequest>(request), cancellationToken),
            "retryRun" => await _runCoordinator.RetryRunAsync(DeserializePayload<RetryRunRequest>(request), cancellationToken),
            "listChangedFiles" => await _reviewService.ListChangedFilesAsync(DeserializePayload<ListChangedFilesRequest>(request), cancellationToken),
            "getDiff" => await _reviewService.GetDiffAsync(DeserializePayload<GetDiffRequest>(request), cancellationToken),
            "openExternalEditor" => await _externalEditorService.OpenAsync(DeserializePayload<OpenExternalEditorRequest>(request), cancellationToken),
            "listDiagnostics" => await _diagnosticsService.ListDiagnosticsAsync(DeserializePayload<ListDiagnosticsRequest>(request), cancellationToken),
            "listPlugins" => await _pluginCatalog.ListPluginsAsync(DeserializePayload<ListPluginsRequest>(request), cancellationToken),
            "listSkills" => await _skillCatalog.ListSkillsAsync(DeserializePayload<ListSkillsRequest>(request), cancellationToken),
            "createSkill" => await _skillCatalog.CreateSkillAsync(DeserializePayload<CreateSkillRequest>(request), cancellationToken),
            "listWorkspaceFiles" => await _workspaceFileCatalog.ListAsync(DeserializePayload<ListWorkspaceFilesRequest>(request), cancellationToken),
            "getSettings" => await _providerCatalog.GetSettingsAsync(DeserializePayload<GetSettingsRequest>(request), cancellationToken),
            "updateSettings" => await _providerCatalog.UpdateSettingsAsync(DeserializePayload<UpdateSettingsRequest>(request), cancellationToken),
            "installPlugin" => await _pluginCatalog.InstallPluginAsync(DeserializePayload<InstallPluginRequest>(request), cancellationToken),
            "setPluginEnabled" => await _pluginCatalog.SetPluginEnabledAsync(DeserializePayload<SetPluginEnabledRequest>(request), cancellationToken),
            "savePluginOptions" => await _pluginCatalog.SavePluginOptionsAsync(DeserializePayload<SavePluginOptionsRequest>(request), cancellationToken),
            "deletePluginOptions" => await _pluginCatalog.DeletePluginOptionsAsync(DeserializePayload<DeletePluginOptionsRequest>(request), cancellationToken),
            "refreshPlugins" => await _pluginCatalog.RefreshPluginsAsync(DeserializePayload<ListPluginsRequest>(request), cancellationToken),
            "listProviders" => await _providerCatalog.ListProvidersAsync(cancellationToken),
            "validateProviderConfig" => await _providerCatalog.ValidateProviderConfigAsync(DeserializePayload<ValidateProviderConfigRequest>(request), cancellationToken),
            "listPendingApprovals" => await _approvalCatalog.ListPendingApprovalsAsync(DeserializePayload<ListPendingApprovalsRequest>(request), cancellationToken),
            "resolveApproval" => await _approvalCatalog.ResolveApprovalAsync(DeserializePayload<ResolveApprovalRequest>(request), cancellationToken),
            _ => throw new AgentHostException("unsupported_command", $"Unsupported AgentHost command '{request.Command}'.")
        };
    }

    private static HealthResponse CreateHealthResponse()
    {
        return new HealthResponse(
            HostName: $"{AppMetadata.Name}.AgentHost",
            HostVersion: AppMetadata.Version,
            ProtocolVersion: AgentHostProtocol.ProtocolVersion,
            CurrentWorkingDirectory: Directory.GetCurrentDirectory(),
            SupportedCommands: AgentHostProtocol.SupportedCommands);
    }

    private static TRequest DeserializePayload<TRequest>(AgentHostRequestEnvelope request)
        where TRequest : class, new()
    {
        if (request.Payload is null ||
            request.Payload.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new TRequest();
        }

        var payload = JsonSerializer.Deserialize<TRequest>(
            request.Payload.Value.GetRawText(),
            AgentHostProtocol.JsonOptions);

        return payload ?? new TRequest();
    }
}
