using ClawSharp.AgentHost;
using ClawSharp.AgentHost.Approvals;
using ClawSharp.AgentHost.Ipc;
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
using ClawSharp.Infrastructure;

Console.InputEncoding = System.Text.Encoding.UTF8;
Console.OutputEncoding = System.Text.Encoding.UTF8;
AgentHostLog.Initialize(Directory.GetCurrentDirectory());
AgentHostLog.Info("boot", "AgentHost process starting.");

try
{
    var runtimeState = new HostRuntimeState();
    var eventDispatcher = new AgentHostEventDispatcher();
    var permissionPrompter = new AgentHostApprovalPermissionPrompter(eventDispatcher);
    var factoryOptions = new ClawSharpApplicationFactoryOptions(
        PermissionPrompter: permissionPrompter,
        InitializationMode: ClawSharpApplicationInitializationMode.LazyRuntime,
        ExcludedToolNames: new HashSet<string>(["AskUserQuestion"], StringComparer.OrdinalIgnoreCase));
    var recentProjectStore = new RecentProjectStore();
    var applicationRegistry = new WorkspaceApplicationRegistry(factoryOptions);
    var threadStateStore = new ThreadStateStore();
    var threadCatalog = new ThreadCatalogService(applicationRegistry, recentProjectStore, threadStateStore);
    var projectCatalog = new ProjectCatalogService(recentProjectStore, threadCatalog);
    var runCoordinator = new RunCoordinator(applicationRegistry, recentProjectStore, eventDispatcher);
    await using var cronSchedulerService = new AgentHostCronSchedulerService(recentProjectStore, applicationRegistry, runCoordinator);
    var reviewService = new WorkspaceReviewService(recentProjectStore);
    var diagnosticsService = new DiagnosticsCatalogService(applicationRegistry, recentProjectStore, runtimeState);
    var pluginCatalog = new PluginCatalogService(applicationRegistry, recentProjectStore);
    var skillCatalog = new SkillCatalogService(applicationRegistry, recentProjectStore);
    var workspaceFileCatalog = new WorkspaceFileCatalogService(recentProjectStore);
    var providerCatalog = new ProviderCatalogService(applicationRegistry, recentProjectStore);
    var externalEditorService = new ExternalEditorService(recentProjectStore);
    var approvalCatalog = new ApprovalCatalogService();
    var commandRouter = new AgentHostCommandRouter(
        projectCatalog,
        threadCatalog,
        runCoordinator,
        reviewService,
        diagnosticsService,
        pluginCatalog,
        skillCatalog,
        workspaceFileCatalog,
        providerCatalog,
        externalEditorService,
        approvalCatalog);
    var host = new AgentHostStdioServer(Console.In, Console.Out, commandRouter, eventDispatcher);
    cronSchedulerService.Start();
    AgentHostLog.Info("boot", "AgentHost services initialized. Entering stdio server loop.");

    await host.RunAsync();
    AgentHostLog.Info("shutdown", "AgentHost stdio server loop exited.");
}
catch (Exception ex)
{
    ClawSharpTelemetry.CaptureException(ex, "agenthost.unhandled", fatal: true);
    throw;
}
finally
{
    await ClawSharpTelemetry.FlushAsync();
}
