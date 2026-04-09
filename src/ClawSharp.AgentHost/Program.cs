using ClawSharp.AgentHost.Approvals;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.AgentHost.Diagnostics;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Providers;
using ClawSharp.AgentHost.Review;
using ClawSharp.AgentHost.Runs;
using ClawSharp.AgentHost.Services;
using ClawSharp.AgentHost.Sessions;

Console.InputEncoding = System.Text.Encoding.UTF8;
Console.OutputEncoding = System.Text.Encoding.UTF8;
Log("boot", "AgentHost process starting.");

var runtimeState = new HostRuntimeState();
var eventDispatcher = new AgentHostEventDispatcher();
var recentProjectStore = new RecentProjectStore();
var applicationRegistry = new WorkspaceApplicationRegistry();
var threadStateStore = new ThreadStateStore();
var threadCatalog = new ThreadCatalogService(applicationRegistry, recentProjectStore, threadStateStore);
var projectCatalog = new ProjectCatalogService(recentProjectStore, threadCatalog);
var runCoordinator = new RunCoordinator(applicationRegistry, recentProjectStore, eventDispatcher);
var reviewService = new WorkspaceReviewService(recentProjectStore);
var diagnosticsService = new DiagnosticsCatalogService(applicationRegistry, recentProjectStore, runtimeState);
var providerCatalog = new ProviderCatalogService(applicationRegistry, recentProjectStore);
var externalEditorService = new ExternalEditorService(recentProjectStore);
var approvalCatalog = new ApprovalCatalogService();
var commandRouter = new AgentHostCommandRouter(
    projectCatalog,
    threadCatalog,
    runCoordinator,
    reviewService,
    diagnosticsService,
    providerCatalog,
    externalEditorService,
    approvalCatalog);
var host = new AgentHostStdioServer(Console.In, Console.Out, commandRouter, eventDispatcher);
Log("boot", "AgentHost services initialized. Entering stdio server loop.");

await host.RunAsync();
Log("shutdown", "AgentHost stdio server loop exited.");

static void Log(string category, string message)
{
    Console.Error.WriteLine($"[{DateTimeOffset.UtcNow:O}] [AgentHost:{category}] {message}");
}
