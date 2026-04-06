// TS origin: ./query/config.ts, ./utils/api.ts, ./services/api/claude.ts
// TS parity status: focused C# unit coverage for the current request-construction foundation; live transport and streaming API integration remain blocked on later milestones.
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public class QueryRequestBuilderTests
{
    [Fact]
    public void SplitSystemPromptPrefix_Uses_Global_Boundary_And_Cache_Scopes()
    {
        var blocks = QueryRequestBuilder.BuildSystemPromptBlocks(
            [
                "x-anthropic-billing-header: cc_version=test;",
                "You are Claude Code, Anthropic's official CLI for Claude.",
                "static one",
                QueryRequestBuilder.SystemPromptDynamicBoundary,
                "dynamic one"
            ],
            enablePromptCaching: true,
            useGlobalCacheScope: true);

        Assert.Equal(4, blocks.Count);
        Assert.Null(blocks[0].CacheScope);
        Assert.Null(blocks[0].CacheControl);
        Assert.Null(blocks[1].CacheScope);
        Assert.Null(blocks[1].CacheControl);
        Assert.Equal("global", blocks[2].CacheScope);
        Assert.Equal("ephemeral", blocks[2].CacheControl?.Type);
        Assert.Equal("global", blocks[2].CacheControl?.Scope);
        Assert.Null(blocks[3].CacheScope);
        Assert.Null(blocks[3].CacheControl);
    }

    [Fact]
    public void PrependUserContext_Inserts_System_Reminder_User_Message()
    {
        var original = ChatMessageFactory.CreateText(MessageRole.User, "hello");

        var messages = QueryRequestBuilder.PrependUserContext(
            [original],
            new Dictionary<string, string>
            {
                ["cwd"] = "/workspace"
            },
            includeInTestEnvironment: true);

        Assert.Equal(2, messages.Count);
        Assert.Equal(MessageRole.User, messages[0].Role);
        Assert.Contains("<system-reminder>", messages[0].Content, StringComparison.Ordinal);
        Assert.Contains("# cwd", messages[0].Content, StringComparison.Ordinal);
        Assert.Same(original, messages[1]);
    }

    [Fact]
    public void PrependUserContext_Skips_System_Reminder_In_Test_Environment_By_Default()
    {
        var originalNodeEnv = Environment.GetEnvironmentVariable("NODE_ENV");
        Environment.SetEnvironmentVariable("NODE_ENV", "test");

        try
        {
            var original = ChatMessageFactory.CreateText(MessageRole.User, "hello");

            var messages = QueryRequestBuilder.PrependUserContext(
                [original],
                new Dictionary<string, string>
                {
                    ["cwd"] = "/workspace"
                });

            Assert.Single(messages);
            Assert.Same(original, messages[0]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NODE_ENV", originalNodeEnv);
        }
    }

    [Fact]
    public void Build_Shapes_Request_With_Cache_Breakpoints_Task_Budget_And_Tool_Schemas()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-builder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var taskRegistry = new TaskRegistry();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(
            session,
            "Read sample.txt",
            [
                new ToolCallRequest("tooluse-read", "Read", "sample.txt")
            ]);

        session.Add(ChatMessageFactory.CreateText(MessageRole.User, "Read sample.txt"));
        session.Add(ChatMessageFactory.CreateToolUse([("tooluse-read", "Read", "sample.txt")]));

        var builder = new QueryRequestBuilder();
        var modelRequest = builder.Build(
            request,
            session,
            new ClawSharpSettings(),
            toolRegistry.All,
            new QueryRequestBuildOptions(
                SystemPrompt:
                [
                    "You are Claude Code, Anthropic's official CLI for Claude.",
                    "system body"
                ],
                SystemContext: new Dictionary<string, string>
                {
                    ["platform"] = "windows"
                },
                UserContext: new Dictionary<string, string>
                {
                    ["cwd"] = tempDir
                },
                EnablePromptCaching: true,
                IncludeUserContextInTestEnvironment: true,
                TaskBudget: new QueryTaskBudget(1200, 700),
                ShouldIncludeFirstPartyOnlyBetas: true));

        Assert.Equal(MainLoopModelResolver.DefaultMainLoopModel, modelRequest.Model);
        Assert.Equal(32_000, modelRequest.MaxTokens);
        Assert.Equal(request.SessionId, modelRequest.SessionId);
        Assert.Equal(QueryRequestBuilder.TaskBudgetsBetaHeader, Assert.Single(modelRequest.Betas));
        Assert.Equal(1200, modelRequest.OutputConfig.TaskBudget?.Total);
        Assert.Equal(700, modelRequest.OutputConfig.TaskBudget?.Remaining);
        Assert.Contains(modelRequest.System, block => block.Text.Contains("platform: windows", StringComparison.Ordinal));
        Assert.Equal(3, modelRequest.Messages.Count);
        Assert.Equal("user", modelRequest.Messages[0].Role);
        Assert.Contains("<system-reminder>", modelRequest.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Null(modelRequest.Messages[0].Content[0].CacheControl);
        Assert.Equal("assistant", modelRequest.Messages[2].Role);
        Assert.Equal("ephemeral", modelRequest.Messages[2].Content[0].CacheControl?.Type);
        Assert.Contains(modelRequest.Tools, tool => tool.Name == "Read" && tool.Strict);
    }

    [Fact]
    public void BuildFromMessages_Shapes_Request_Using_Provided_Loop_State_Messages()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-builder-state-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var taskRegistry = new TaskRegistry();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(session, "Read sample.txt");
        var stateMessages = new[]
        {
            ChatMessageFactory.CreateText(MessageRole.User, "state user"),
            ChatMessageFactory.CreateText(MessageRole.Assistant, "state assistant")
        };

        var builder = new QueryRequestBuilder();
        var modelRequest = builder.BuildFromMessages(
            request,
            stateMessages,
            new ClawSharpSettings(),
            toolRegistry.All,
            new QueryRequestBuildOptions(
                SystemPrompt:
                [
                    "system body"
                ]));

        Assert.Equal(2, modelRequest.Messages.Count);
        Assert.Equal("state user", modelRequest.Messages[0].Content[0].Text);
        Assert.Equal("state assistant", modelRequest.Messages[1].Content[0].Text);
        Assert.Equal("system body", modelRequest.System[0].Text);
        Assert.Equal(32_000, modelRequest.MaxTokens);
    }

    [Fact]
    public void BuildFromMessages_Prepends_User_Context_In_Live_Environment_By_Default()
    {
        var originalNodeEnv = Environment.GetEnvironmentVariable("NODE_ENV");
        Environment.SetEnvironmentVariable("NODE_ENV", null);

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-builder-live-user-context-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var taskRegistry = new TaskRegistry();
            var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
            var session = new DefaultSessionFactory(tempDir).Create();
            var request = QueryTurnRequest.Create(session, "Read sample.txt");
            var stateMessages = new[]
            {
                ChatMessageFactory.CreateText(MessageRole.User, "state user")
            };

            var builder = new QueryRequestBuilder();
            var modelRequest = builder.BuildFromMessages(
                request,
                stateMessages,
                new ClawSharpSettings(),
                toolRegistry.All,
                new QueryRequestBuildOptions(
                    UserContext: new Dictionary<string, string>
                    {
                        ["cwd"] = tempDir
                    }));

            Assert.Equal(2, modelRequest.Messages.Count);
            Assert.Equal("user", modelRequest.Messages[0].Role);
            Assert.Contains("<system-reminder>", modelRequest.Messages[0].Content[0].Text, StringComparison.Ordinal);
            Assert.Contains("# cwd", modelRequest.Messages[0].Content[0].Text, StringComparison.Ordinal);
            Assert.Equal("state user", modelRequest.Messages[1].Content[0].Text);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NODE_ENV", originalNodeEnv);
        }
    }

    [Fact]
    public void QueryTurnRequest_Create_Seeds_Repl_Main_Thread_Model_Context()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-turn-request-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();

        var request = QueryTurnRequest.Create(session, "hello");

        Assert.NotNull(request.ModelTurnContext);
        Assert.Equal("repl_main_thread", request.ModelTurnContext!.QuerySource);
        Assert.Empty(request.ModelTurnContext.SystemPrompt);
        Assert.Empty(request.ModelTurnContext.UserContext);
        Assert.Empty(request.ModelTurnContext.SystemContext);
    }

    [Fact]
    public void QueryModelIterationRequestBuilder_Forwards_Request_Task_Budget()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-iteration-budget-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(session, "hello") with
        {
            TaskBudget = new QueryTaskBudget(1_500, 900)
        };
        var state = QueryLoopStateFactory.CreateInitial(
            [ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var builder = new QueryModelIterationRequestBuilder();

        var streamingRequest = builder.Build(request, state, new ClawSharpSettings());

        Assert.Equal(1_500, streamingRequest.Request.OutputConfig.TaskBudget?.Total);
        Assert.Equal(900, streamingRequest.Request.OutputConfig.TaskBudget?.Remaining);
        Assert.Contains(QueryRequestBuilder.TaskBudgetsBetaHeader, streamingRequest.Request.Betas);
    }

    [Fact]
    public void QueryModelIterationRequestBuilder_Forwards_Loop_Max_Output_Tokens_Override()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-iteration-max-output-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial(
            [ChatMessageFactory.CreateText(MessageRole.User, "hello")],
            maxOutputTokensOverride: QueryMaxOutputTokensRecoveryPolicy.EscalatedMaxTokens);
        var builder = new QueryModelIterationRequestBuilder();

        var streamingRequest = builder.Build(request, state, new ClawSharpSettings());

        Assert.Equal(QueryMaxOutputTokensRecoveryPolicy.EscalatedMaxTokens, streamingRequest.Request.MaxTokens);
    }

    [Fact]
    public void QueryModelIterationRequestBuilder_Uses_Default_Max_Output_Tokens_When_No_Override_Is_Present()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-iteration-default-max-output-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial(
            [ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var builder = new QueryModelIterationRequestBuilder();

        var streamingRequest = builder.Build(request, state, new ClawSharpSettings());

        Assert.Equal(32_000, streamingRequest.Request.MaxTokens);
    }

    [Fact]
    public async Task QueryEngine_Result_Carries_Request_Snapshot()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-engine-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "note.txt");
        await File.WriteAllTextAsync(filePath, "hello");

        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(orchestrator);
        var queue = new InMemoryQueuedCommandQueue();
        var queuedTaskNotificationDrainer = new QueuedTaskNotificationDrainer(queue, transcriptStore);
        var queryEngine = new QueryEngine(
            settings,
            eventSink,
            transcriptStore,
            queryTurnRunner,
            queuedTaskNotificationDrainer,
            toolRegistry: toolRegistry);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(
            session,
            "Read note.txt",
            [
                new ToolCallRequest("tooluse-note", "Read", "note.txt")
            ]);

        var result = await queryEngine.RunTurnAsync(session, request);

        Assert.NotNull(result.Request);
        Assert.Equal(session.Id, result.Request!.SessionId);
        Assert.Equal(MainLoopModelResolver.DefaultMainLoopModel, result.Request.Model);
        Assert.Contains(result.Request.Tools, tool => tool.Name == "Read");
        Assert.Equal("user", result.Request.Messages[0].Role);
    }

    [Fact]
    public async Task QueryEngine_Uses_Model_Turn_Context_When_Building_Request_Snapshot()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-context-engine-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "note.txt");
        await File.WriteAllTextAsync(filePath, "hello");

        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(orchestrator);
        var queue = new InMemoryQueuedCommandQueue();
        var queuedTaskNotificationDrainer = new QueuedTaskNotificationDrainer(queue, transcriptStore);
        var queryEngine = new QueryEngine(
            settings,
            eventSink,
            transcriptStore,
            queryTurnRunner,
            queuedTaskNotificationDrainer,
            toolRegistry: toolRegistry);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(
            session,
            "Read note.txt",
            [
                new ToolCallRequest("tooluse-note", "Read", "note.txt")
            ]) with
        {
            ModelTurnContext = new QueryModelTurnContext(
                ["system preface", "system details"],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["cwd"] = tempDir
                },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["platform"] = "windows"
                },
                "repl_main_thread:outputStyle:custom")
        };

        var result = await queryEngine.RunTurnAsync(session, request);

        Assert.NotNull(result.Request);
        Assert.Contains(result.Request!.System, block => block.Text.Contains("system preface", StringComparison.Ordinal));
        Assert.Contains(result.Request.System, block => block.Text.Contains("platform: windows", StringComparison.Ordinal));
        Assert.Equal("user", result.Request.Messages[0].Role);
        Assert.Contains("<system-reminder>", result.Request.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Equal("user", result.Request.Messages[1].Role);
        Assert.Equal("Read note.txt", result.Request.Messages[1].Content[0].Text);
    }

    [Fact]
    public async Task QueryEngine_Populates_Default_Repl_Model_Context_From_Provider()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-default-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "note.txt");
        await File.WriteAllTextAsync(filePath, "hello");

        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(orchestrator);
        var queue = new InMemoryQueuedCommandQueue();
        var queuedTaskNotificationDrainer = new QueuedTaskNotificationDrainer(queue, transcriptStore);
        var modelTurnContextProvider = new FixedQueryModelTurnContextProvider(
            new QueryModelTurnContext(
                ["provider system prompt"],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["currentDate"] = "Today's date is 2026-04-03."
                },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["gitStatus"] = "Current branch: main"
                },
                QueryModelTurnContext.ReplMainThread.QuerySource));
        var queryEngine = new QueryEngine(
            settings,
            eventSink,
            transcriptStore,
            queryTurnRunner,
            queuedTaskNotificationDrainer,
            toolRegistry: toolRegistry,
            modelTurnContextProvider: modelTurnContextProvider);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(
            session,
            "Read note.txt",
            [
                new ToolCallRequest("tooluse-note", "Read", "note.txt")
            ]);

        var result = await queryEngine.RunTurnAsync(session, request);

        Assert.NotNull(result.Request);
        Assert.Contains(result.Request!.System, block => block.Text.Contains("provider system prompt", StringComparison.Ordinal));
        Assert.Contains(result.Request.System, block => block.Text.Contains("gitStatus: Current branch: main", StringComparison.Ordinal));
        Assert.Contains("Today's date is 2026-04-03.", result.Request.Messages[0].Content[0].Text, StringComparison.Ordinal);
    }

    private sealed class FixedQueryModelTurnContextProvider : IQueryModelTurnContextProvider
    {
        private readonly QueryModelTurnContext _context;

        public FixedQueryModelTurnContextProvider(QueryModelTurnContext context)
        {
            _context = context;
        }

        public Task<QueryModelTurnContext> GetReplMainThreadContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_context);
        }
    }
}
