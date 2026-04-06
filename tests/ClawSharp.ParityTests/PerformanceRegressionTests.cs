using System.Diagnostics;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.ParityTests;

public sealed class PerformanceRegressionTests
{
    [Fact]
    public async Task Application_Factory_Starts_Within_Regression_Budget()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var configRoot = CreateTempDirectory("clawsharp-performance-config");

        try
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configRoot);

            var stopwatch = Stopwatch.StartNew();
            _ = await ClawSharpApplicationFactory.CreateDefaultAsync();
            stopwatch.Stop();

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"Startup regression budget exceeded: {stopwatch.Elapsed.TotalMilliseconds:0}ms");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);
            DeleteDirectory(configRoot);
        }
    }

    [Fact]
    public async Task Explicit_Tool_Workflow_Completes_Within_Regression_Budget()
    {
        var workspaceRoot = CreateTempDirectory("clawsharp-performance-workflow");

        try
        {
            var filePath = Path.Combine(workspaceRoot, "note.txt");
            await File.WriteAllTextAsync(filePath, "hello");

            var settings = new ClawSharpSettings();
            var toolRegistry = new ToolRegistry(workspaceRoot, new TaskRegistry(workspaceRoot));
            var eventSink = new InMemoryEventSink();
            var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
            var transcriptStore = new JsonlTranscriptStore();
            var queryEngine = new QueryEngine(
                settings,
                eventSink,
                transcriptStore,
                new ExplicitToolTurnRunner(orchestrator),
                new QueuedTaskNotificationDrainer(new InMemoryQueuedCommandQueue(), transcriptStore));
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            var request = QueryTurnRequest.Create(
                session,
                "Read note.txt",
                [
                    new ToolCallRequest("tooluse-note", "Read", "note.txt")
                ]);

            var stopwatch = Stopwatch.StartNew();
            var result = await queryEngine.RunTurnAsync(session, request);
            stopwatch.Stop();

            Assert.NotNull(result.AssistantMessage);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(5),
                $"Workflow regression budget exceeded: {stopwatch.Elapsed.TotalMilliseconds:0}ms");
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
