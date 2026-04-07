using ClawSharp.AgentHost.Ipc;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Services;
using ClawSharp.AgentHost.Sessions;

Console.InputEncoding = System.Text.Encoding.UTF8;
Console.OutputEncoding = System.Text.Encoding.UTF8;

var recentProjectStore = new RecentProjectStore();
var applicationRegistry = new WorkspaceApplicationRegistry();
var threadCatalog = new ThreadCatalogService(applicationRegistry, recentProjectStore);
var projectCatalog = new ProjectCatalogService(recentProjectStore, threadCatalog);
var commandRouter = new AgentHostCommandRouter(projectCatalog, threadCatalog);
var host = new AgentHostStdioServer(Console.In, Console.Out, commandRouter);

await host.RunAsync();
