using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.ParityTests;

public sealed class ReferenceImplementationParityTests
{
    [Fact]
    public async Task Registered_Mcp_Tool_Name_And_Progress_Payload_Match_Ts_Shaped_Parity_Scenario()
    {
        var workspaceRoot = CreateTempDirectory("clawsharp-parity-mcp");

        try
        {
            var events = new InMemoryEventSink();
            var toolRegistry = new ToolRegistry(workspaceRoot, new TaskRegistry(workspaceRoot));
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            var lifecycleManager = new McpLifecycleManager(new ParityMcpConnector());
            var registration = new McpToolRegistrationService(lifecycleManager);
            var connections = await lifecycleManager.ConnectServersAsync(
                new Dictionary<string, ScopedMcpServerConfig>(StringComparer.Ordinal)
                {
                    ["claude.ai Demo Server"] = new(
                        "claude.ai Demo Server",
                        new McpStdioServerConfig("demo", [], null),
                        McpConfigScope.User)
                });
            var registeredTool = Assert.Single(await registration.RegisterToolsAsync(toolRegistry, connections));
            var orchestrator = new ToolOrchestrator(toolRegistry, events);

            var records = await orchestrator.RunAsync(
                [
                    new ToolCallRequest("tooluse-mcp", registeredTool.Descriptor.Name, """{"path":"README.md"}""")
                ],
                session,
                new ClawSharpSettings());

            Assert.Equal("mcp__claude_ai_Demo_Server__inspect_file", registeredTool.Descriptor.Name);
            Assert.Single(records);
            var progressEvents = events.Events
                .Where(appEvent => appEvent.Type == AppEventType.ToolExecutionProgress)
                .ToArray();
            Assert.True(progressEvents.Length >= 2);
            Assert.Contains(progressEvents, appEvent => appEvent.Metadata?["status"] == "started");
            Assert.Contains(progressEvents, appEvent => appEvent.Metadata?["status"] == "progress");
            Assert.Contains(progressEvents, appEvent => appEvent.Metadata?["status"] == "completed");
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public void Transcript_Rendering_Matches_Ts_Shaped_Message_Prefixes_And_Grouping()
    {
        var renderer = new TranscriptMessageRenderer();
        var messages = new[]
        {
            ChatMessageFactory.CreateText(MessageRole.User, "first prompt"),
            ChatMessageFactory.CreateText(MessageRole.Assistant, "first answer"),
            ChatMessageFactory.CreateText(MessageRole.Assistant, "continued answer"),
            ChatMessageFactory.CreateText(MessageRole.System, "System notice"),
            ChatMessageFactory.CreateText(
                MessageRole.System,
                "<task-notification>\n<task-id>task-1</task-id>\n<status>completed</status>\n<summary>Background command completed</summary>\n</task-notification>")
        };

        var rendered = RenderTranscript(renderer, messages);

        Assert.Equal(
            string.Join(
                "\n",
                [
                    "> first prompt",
                    string.Empty,
                    "* first answer",
                    "  continued answer",
                    string.Empty,
                    "! System notice",
                    "! ● Background command completed"
                ]),
            rendered);
    }

    [Fact]
    public void Background_Task_List_Rendering_Matches_Ts_Shaped_Grouping_And_Labels()
    {
        var renderer = new BackgroundTasksCommandRenderer();
        var now = new DateTimeOffset(2026, 4, 5, 18, 0, 0, TimeSpan.Zero);
        var tasks = new Dictionary<string, ClawSharpTask>(StringComparer.Ordinal)
        {
            ["task-1"] = new LocalBashTask("task-1", "run build", ClawSharp.Tasks.TaskStatus.Running, now, "build.log", "dotnet build"),
            ["task-2"] = new RemoteAgentTask("task-2", "remote review", ClawSharp.Tasks.TaskStatus.Pending, now.AddSeconds(-10), "remote.log", "remote-session", "review", "Remote Review"),
            ["task-3"] = new LocalAgentTask("task-3", "Review diff", ClawSharp.Tasks.TaskStatus.Running, now.AddSeconds(-20), "agent.log", "Review the diff", "reviewer")
        };

        var rendered = renderer.RenderList(tasks).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal(
            string.Join(
                "\n",
                [
                    "Background tasks",
                    "1 shell · 1 remote agent · 1 agent",
                    string.Empty,
                    "shells",
                    "- task-1 [running] dotnet build",
                    string.Empty,
                    "remote agents",
                    "- task-2 [pending] Remote Review",
                    string.Empty,
                    "agents",
                    "- task-3 [running] Review diff",
                    string.Empty,
                    "Use /tasks <task-id> to inspect a task."
                ]),
            rendered);
    }

    private static string RenderTranscript(TranscriptMessageRenderer renderer, IReadOnlyList<ChatMessage> messages)
    {
        var lines = new List<string>();
        MessageRole? previousRole = null;
        foreach (var message in messages)
        {
            lines.AddRange(renderer.Render(message, previousRole));
            previousRole = message.Role;
        }

        return string.Join("\n", lines);
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

    private sealed class ParityMcpConnector : IMcpClientConnector
    {
        private readonly IReadOnlyDictionary<string, object?> _capabilities =
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["tools"] = true
            };

        public Task<McpServerConnection> ConnectAsync(
            string name,
            ScopedMcpServerConfig server,
            McpServerConnectionStatistics? serverStatistics = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<McpServerConnection>(
                new ConnectedMcpServerConnection(
                    name,
                    server,
                    new ParityMcpSession(),
                    _capabilities,
                    CleanupAsync: () => Task.CompletedTask));
        }
    }

    private sealed class ParityMcpSession : IMcpClientSession
    {
        public Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<McpToolDefinition>>(
                [
                    new McpToolDefinition(
                        "inspect.file",
                        "Inspect a file",
                        new JsonObject
                        {
                            ["type"] = "object"
                        })
                ]);
        }

        public Task<IReadOnlyList<McpPromptDefinition>> ListPromptsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<McpPromptDefinition>>([]);
        }

        public Task<McpPromptResult> GetPromptAsync(
            string promptName,
            IReadOnlyDictionary<string, string?> arguments,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<McpResourceDefinition>> ListResourcesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<McpResourceDefinition>>([]);
        }

        public Task<McpReadResourceResult> ReadResourceAsync(
            string uri,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SendNotificationAsync(
            string method,
            JsonObject? parameters = null,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<McpToolCallResult> CallToolAsync(
            string toolName,
            JsonObject arguments,
            Action<McpToolProgressNotification>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            onProgress?.Invoke(new McpToolProgressNotification(0.5d, 1d, "halfway"));
            return Task.FromResult(
                new McpToolCallResult(
                    "ok",
                    StructuredContent: new JsonObject
                    {
                        ["status"] = "ok"
                    }));
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public void SetElicitationRequestHandler(Func<McpElicitationRequestContext, Task<McpElicitResult>> handler)
        {
        }

        public void SetElicitationCompletionHandler(Action<string> handler)
        {
        }
    }
}
