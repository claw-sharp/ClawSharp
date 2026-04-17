using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class FileOpenFileUpdateIntegrationTests
{
    [Fact]
    public async Task DiagnosticTrackingNotifier_CapturesIdeDiagnosticsAfterQueryStart()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-diagnostic-tracking-tests", Guid.NewGuid().ToString("N"));
        var configRoot = Path.Combine(workspaceRoot, ".clawsharp");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(Path.Combine(configRoot, "ide"));
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", configRoot);

        try
        {
            var filePath = Path.Combine(workspaceRoot, "sample.ts");
            await File.WriteAllTextAsync(filePath, "const answer = 42;\n");
            await File.WriteAllTextAsync(
                Path.Combine(configRoot, "ide", "3030.lock"),
                $$"""
                {"workspaceFolders":["{{workspaceRoot.Replace("\\", "\\\\")}}"],"transport":"sse","ideName":"VS Code"}
                """);

            var diagnosticPayload = $$"""[{"uri":"file://{{filePath.Replace("\\", "\\\\")}}","diagnostics":[]}]""";
            var session = new RecordingMcpClientSession(new McpToolCallResult(diagnosticPayload));
            var lifecycleManager = new McpLifecycleManager(new RecordingMcpClientConnector(session));
            var ideIntegrationService = new IdeIntegrationService(workspaceRoot);
            var notifier = new DiagnosticTrackingFileUpdateNotifier(
                new DiagnosticTrackingService(
                    lifecycleManager,
                    new IdeMcpServerConfigResolver(ideIntegrationService)));

            await notifier.HandleQueryStartAsync();
            await notifier.BeforeFileEditedAsync(filePath);

            Assert.Equal("getDiagnostics", session.LastCalledToolName);
            Assert.Equal($"file://{filePath}", session.LastArguments?["uri"]?.GetValue<string>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task VscodeSdkFileUpdateNotifier_SendsFileUpdatedNotificationForAnt()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-vscode-file-update-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var globalConfigPath = Path.Combine(workspaceRoot, "global-settings.json");
        await File.WriteAllTextAsync(
            globalConfigPath,
            """
            {
              "mcpServers": {
                "claude-vscode": {
                  "type": "http",
                  "url": "https://example.test/mcp"
                }
              }
            }
            """);

        try
        {
            var session = new RecordingMcpClientSession(new McpToolCallResult(string.Empty));
            var lifecycleManager = new McpLifecycleManager(new RecordingMcpClientConnector(session));
            var configService = new McpConfigService(workspaceRoot, globalClaudeFilePath: globalConfigPath);
            var notifier = new VscodeSdkFileUpdateNotifier(configService, lifecycleManager, () => "ant");

            await notifier.NotifyFileUpdatedAsync("sample.ts", "before", "after");

            Assert.Equal("file_updated", session.LastNotificationMethod);
            Assert.Equal("sample.ts", session.LastNotificationArguments?["filePath"]?.GetValue<string>());
            Assert.Equal("before", session.LastNotificationArguments?["oldContent"]?.GetValue<string>());
            Assert.Equal("after", session.LastNotificationArguments?["newContent"]?.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task QueryEngine_RunTurnAsync_CallsHandleQueryStartOnFileUpdateNotifier()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-query-file-update-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var notifier = new RecordingFileUpdateNotifier();
            var session = new ConversationSession("session-1", workspaceRoot, Path.Combine(workspaceRoot, "session-1.jsonl"));
            var queryEngine = new QueryEngine(
                new ClawSharpSettings(),
                new InMemoryEventSink(),
                new JsonlTranscriptStore(),
                new SingleMessageQueryTurnRunner(),
                new QueuedTaskNotificationDrainer(new InMemoryQueuedCommandQueue(), new JsonlTranscriptStore()),
                fileUpdateNotifier: notifier);

            await queryEngine.RunTurnAsync(session, "hello");

            Assert.Equal(1, notifier.HandleQueryStartCalls);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    private sealed class RecordingMcpClientConnector : IMcpClientConnector
    {
        private readonly IMcpClientSession _session;

        public RecordingMcpClientConnector(IMcpClientSession session)
        {
            _session = session;
        }

        public Task<McpServerConnection> ConnectAsync(
            string name,
            ScopedMcpServerConfig server,
            McpServerConnectionStatistics? serverStatistics = null,
            bool allowInteractiveAuth = true,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<McpServerConnection>(
                new ConnectedMcpServerConnection(
                    name,
                    server,
                    _session,
                    new Dictionary<string, object?>(StringComparer.Ordinal),
                    CleanupAsync: () => Task.CompletedTask));
        }
    }

    private sealed class RecordingMcpClientSession : IMcpClientSession
    {
        private readonly McpToolCallResult _toolCallResult;

        public RecordingMcpClientSession(McpToolCallResult toolCallResult)
        {
            _toolCallResult = toolCallResult;
        }

        public string? LastCalledToolName { get; private set; }
        public JsonObject? LastArguments { get; private set; }
        public string? LastNotificationMethod { get; private set; }
        public JsonObject? LastNotificationArguments { get; private set; }

        public Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<McpToolDefinition>>([]);

        public Task<IReadOnlyList<McpPromptDefinition>> ListPromptsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<McpPromptDefinition>>([]);

        public Task<McpPromptResult> GetPromptAsync(
            string promptName,
            IReadOnlyDictionary<string, string?> arguments,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new McpPromptResult([]));

        public Task<IReadOnlyList<McpResourceDefinition>> ListResourcesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<McpResourceDefinition>>([]);

        public Task<McpReadResourceResult> ReadResourceAsync(string uri, CancellationToken cancellationToken = default)
            => Task.FromResult(new McpReadResourceResult([]));

        public Task SendNotificationAsync(
            string method,
            JsonObject? parameters = null,
            CancellationToken cancellationToken = default)
        {
            LastNotificationMethod = method;
            LastNotificationArguments = parameters?.DeepClone().AsObject();
            return Task.CompletedTask;
        }

        public Task<McpToolCallResult> CallToolAsync(
            string toolName,
            JsonObject arguments,
            Action<McpToolProgressNotification>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            LastCalledToolName = toolName;
            LastArguments = arguments.DeepClone().AsObject();
            return Task.FromResult(_toolCallResult);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void SetElicitationRequestHandler(Func<McpElicitationRequestContext, Task<McpElicitResult>> handler)
        {
        }

        public void SetElicitationCompletionHandler(Action<string> handler)
        {
        }
    }

    private sealed class RecordingFileUpdateNotifier : IFileUpdateNotifier
    {
        public int HandleQueryStartCalls { get; private set; }

        public Task HandleQueryStartAsync(CancellationToken cancellationToken = default)
        {
            HandleQueryStartCalls++;
            return Task.CompletedTask;
        }

        public Task BeforeFileEditedAsync(string filePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearDiagnosticsForFileAsync(string filePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyFileUpdatedAsync(
            string filePath,
            string? oldContent,
            string? newContent,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class SingleMessageQueryTurnRunner : IQueryTurnRunner
    {
        public async IAsyncEnumerable<QueryRuntimeEvent> RunAsync(
            QueryTurnRequest request,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new QueryRequestStartRuntimeEvent();
            yield return new QueryMessageRuntimeEvent(ChatMessageFactory.CreateText(MessageRole.Assistant, "done"));
            yield return new QueryLoopTerminalRuntimeEvent(
                new QueryLoopTerminal(QueryTerminalReason.Completed),
                QueryLoopStateFactory.CreateInitial([], null));
            await Task.CompletedTask;
        }
    }
}
