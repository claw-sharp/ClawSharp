// TS parity status: focused C# unit coverage for the current request-construction foundation; live transport and streaming API integration remain blocked on later milestones.
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Query.Attachments;
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

        Assert.Equal(ProviderRuntimeResolver.GetDefaultModelForCurrentProvider(), modelRequest.Model);
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
        var appStateStore = new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                tempDir,
                StartupEnvironment.Capture(),
                settings,
                [],
                [],
                [],
                [],
                [],
                []));
        var queryEngine = new QueryEngine(
            settings,
            eventSink,
            transcriptStore,
            queryTurnRunner,
            queuedTaskNotificationDrainer,
            toolRegistry: toolRegistry,
            appStateStore: appStateStore);
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
        Assert.Equal(ProviderRuntimeResolver.GetDefaultModelForCurrentProvider(), result.Request.Model);
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

    [Fact]
    public async Task QueryEngine_Expands_Prompt_Plugin_References_Into_User_Context()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-plugin-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        await File.WriteAllTextAsync(Path.Combine(tempDir, "note.txt"), "hello");

        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(orchestrator);
        var queue = new InMemoryQueuedCommandQueue();
        var queuedTaskNotificationDrainer = new QueuedTaskNotificationDrainer(queue, transcriptStore);
        var appStateStore = new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                tempDir,
                StartupEnvironment.Capture(),
                settings,
                [],
                [],
                [
                    new DiscoveredPlugin(
                        "linear@builtin",
                        "linear",
                        Path.Combine(tempDir, ".clawsharp", "builtin", "linear"),
                        true,
                        true,
                        new PluginManifest(
                            "linear",
                            "Manage Linear issues and projects through the official MCP server.",
                            "1.0.0",
                            [],
                            [],
                            [],
                            [],
                            [],
                            [new PluginMcpServerDefinition("linear", new McpHttpServerConfig("https://mcp.linear.app/mcp", null, null, null))]),
                        [],
                        new Dictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>>()),
                ],
                [],
                [],
                []));
        var queryEngine = new QueryEngine(
            settings,
            eventSink,
            transcriptStore,
            queryTurnRunner,
            queuedTaskNotificationDrainer,
            toolRegistry: toolRegistry,
            appStateStore: appStateStore);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(
            session,
            "$linear create project \"HR\" description:\"Hiring for truckerpoints\"",
            [
                new ToolCallRequest("tooluse-note", "Read", "note.txt")
            ]);

        var result = await queryEngine.RunTurnAsync(session, request);

        Assert.NotNull(result.Request);
        Assert.Equal("user", result.Request!.Messages[0].Role);
        Assert.Contains("Plugin $linear", result.Request.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Contains("Prefer this plugin's capabilities when they fit the task.", result.Request.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Contains("mcp__linear__", result.Request.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Equal("create project \"HR\" description:\"Hiring for truckerpoints\"", result.Request.Messages[1].Content[0].Text);
    }

    [Fact]
    public async Task QueryEngine_Expands_Prompt_Skill_References_Into_User_Context()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-skill-context-tests", Guid.NewGuid().ToString("N"));
        var skillDirectory = Path.Combine(tempDir, ".clawsharp", "skills", "review-changes");
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(Path.Combine(tempDir, "note.txt"), "hello");
        var skillFilePath = Path.Combine(skillDirectory, "SKILL.md");
        await File.WriteAllTextAsync(
            skillFilePath,
            """
            ---
            name: review-changes
            ---
            # Review Changes

            Focus on bugs and missing tests.
            """);

        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(orchestrator);
        var queue = new InMemoryQueuedCommandQueue();
        var queuedTaskNotificationDrainer = new QueuedTaskNotificationDrainer(queue, transcriptStore);
        var appStateStore = new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                tempDir,
                StartupEnvironment.Capture(),
                settings,
                [],
                [],
                [],
                [new DiscoveredSkill("review-changes", skillFilePath, skillDirectory, "builtin")],
                [],
                []));
        var queryEngine = new QueryEngine(
            settings,
            eventSink,
            transcriptStore,
            queryTurnRunner,
            queuedTaskNotificationDrainer,
            toolRegistry: toolRegistry,
            appStateStore: appStateStore);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(
            session,
            "Use $review-changes before reading note.txt",
            [
                new ToolCallRequest("tooluse-note", "Read", "note.txt")
            ]);

        var result = await queryEngine.RunTurnAsync(session, request);

        Assert.NotNull(result.Request);
        Assert.Equal("user", result.Request!.Messages[0].Role);
        Assert.Contains("Skill $review-changes", result.Request.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Contains("Base directory for this skill:", result.Request.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Contains("Focus on bugs and missing tests.", result.Request.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("name: review-changes", result.Request.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Equal("Use $review-changes before reading note.txt", result.Request.Messages[1].Content[0].Text);
    }

    [Fact]
    public async Task QueryEngine_Resolves_Unique_Suffix_Matches_For_Prompt_Skill_References()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-skill-suffix-tests", Guid.NewGuid().ToString("N"));
        var skillDirectory = Path.Combine(tempDir, ".clawsharp", "plugins", "vercel", "agent-browser");
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(Path.Combine(tempDir, "note.txt"), "hello");
        var skillFilePath = Path.Combine(skillDirectory, "SKILL.md");
        await File.WriteAllTextAsync(skillFilePath, "# Agent Browser\n\nVerify the app in a browser.");

        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(orchestrator);
        var queue = new InMemoryQueuedCommandQueue();
        var queuedTaskNotificationDrainer = new QueuedTaskNotificationDrainer(queue, transcriptStore);
        var appStateStore = new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                tempDir,
                StartupEnvironment.Capture(),
                settings,
                [],
                [],
                [],
                [new DiscoveredSkill("vercel:agent-browser", skillFilePath, skillDirectory, "plugin:vercel")],
                [],
                []));
        var queryEngine = new QueryEngine(
            settings,
            eventSink,
            transcriptStore,
            queryTurnRunner,
            queuedTaskNotificationDrainer,
            toolRegistry: toolRegistry,
            appStateStore: appStateStore);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(
            session,
            "Run $agent-browser before reading note.txt",
            [
                new ToolCallRequest("tooluse-note", "Read", "note.txt")
            ]);

        var result = await queryEngine.RunTurnAsync(session, request);

        Assert.NotNull(result.Request);
        Assert.Contains("Skill $vercel:agent-browser", result.Request!.Messages[0].Content[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryEngine_Expands_Prompt_File_References_Into_User_Context()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-file-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var attachedFilePath = Path.Combine(tempDir, "src", "Program.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(attachedFilePath)!);
        await File.WriteAllTextAsync(attachedFilePath, "Console.WriteLine(\"hello\");");
        await File.WriteAllTextAsync(Path.Combine(tempDir, "note.txt"), "hello");

        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(orchestrator);
        var queue = new InMemoryQueuedCommandQueue();
        var queuedTaskNotificationDrainer = new QueuedTaskNotificationDrainer(queue, transcriptStore);
        var appStateStore = new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                tempDir,
                StartupEnvironment.Capture(),
                settings,
                [],
                [],
                [],
                [],
                [],
                []));
        var queryEngine = new QueryEngine(
            settings,
            eventSink,
            transcriptStore,
            queryTurnRunner,
            queuedTaskNotificationDrainer,
            toolRegistry: toolRegistry,
            appStateStore: appStateStore);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(
            session,
            "Review @src/Program.cs before reading note.txt",
            [
                new ToolCallRequest("tooluse-note", "Read", "note.txt")
            ]);

        var result = await queryEngine.RunTurnAsync(session, request);

        Assert.NotNull(result.Request);
        Assert.Equal("user", result.Request!.Messages[0].Role);
        Assert.Contains("Attached file @src/Program.cs", result.Request.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Contains("Console.WriteLine(\"hello\");", result.Request.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Equal("Review @src/Program.cs before reading note.txt", result.Request.Messages[1].Content[0].Text);
    }

    [Fact]
    public void BuildFromMessages_Uses_Prompt_Attachments_For_The_Final_User_Message()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-image-attachment-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var taskRegistry = new TaskRegistry();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(
            session,
            "Describe this\n\n<clawsharp-attachment>{\"kind\":\"image\",\"path\":\"/tmp/cat.png\",\"name\":\"cat.png\"}</clawsharp-attachment>") with
        {
            ResolvedUserInput = "Describe this",
            PromptAttachments =
            [
                new QueryPromptAttachment(
                    QueryPromptAttachmentKind.Image,
                    "/tmp/cat.png",
                    "cat.png",
                    "image/png",
                    "YWJjMTIz")
            ]
        };
        var stateMessages = new[]
        {
            ChatMessageFactory.CreateText(MessageRole.User, request.UserInput)
        };

        var builder = new QueryRequestBuilder();
        var modelRequest = builder.BuildFromMessages(
            request,
            stateMessages,
            new ClawSharpSettings(),
            toolRegistry.All,
            new QueryRequestBuildOptions(SystemPrompt: ["system body"]));

        Assert.Single(modelRequest.Messages);
        Assert.Equal("Describe this", modelRequest.Messages[0].Content[0].Text);
        Assert.Equal("image", modelRequest.Messages[0].Content[1].Type);
        Assert.Equal("image/png", modelRequest.Messages[0].Content[1].ImageSource?.MediaType);
        Assert.Equal("YWJjMTIz", modelRequest.Messages[0].Content[1].ImageSource?.Data);
    }

    [Fact]
    public void BuildFromMessages_Does_Not_Override_Tool_Result_User_Message_With_Resolved_User_Input()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-tool-result-override-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(
            session,
            "$linear create project \"HR\" description:\"Hiring for truckerpoints\"") with
        {
            ResolvedUserInput = "create project \"HR\" description:\"Hiring for truckerpoints\""
        };
        var messages = new[]
        {
            ChatMessageFactory.CreateToolResult(
                "call_123",
                "mcp__linear__list_teams",
                """{"teams":[{"name":"TruckerPoints"}]}""",
                success: true)
        };

        var builder = new QueryRequestBuilder();
        var modelRequest = builder.BuildFromMessages(
            request,
            messages,
            new ClawSharpSettings(),
            [],
            new QueryRequestBuildOptions(SystemPrompt: ["system body"]));

        Assert.Single(modelRequest.Messages);
        Assert.Single(modelRequest.Messages[0].Content);
        Assert.Equal("tool_result", modelRequest.Messages[0].Content[0].Type);
        Assert.Equal("""{"teams":[{"name":"TruckerPoints"}]}""", modelRequest.Messages[0].Content[0].Text);
        Assert.Equal("call_123", modelRequest.Messages[0].Content[0].ToolUseId);
    }

    [Fact]
    public void QueryModelIterationRequestBuilder_Repairs_Missing_MultiTool_Result_From_Previous_Response_Items()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-multitool-repair-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(session, "Create a Linear issue");
        var state = QueryLoopStateFactory.CreateInitial(
            [
                new ChatMessage(
                    Guid.NewGuid().ToString("N"),
                    MessageRole.User,
                    [
                        new MessageContentBlock(
                            MessageContentKind.ToolResult,
                            "Found project HR",
                            "mcp__linear__list_projects",
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["toolUseId"] = "call_list"
                            }),
                        new MessageContentBlock(MessageContentKind.Text, "Continue.")
                    ],
                    DateTimeOffset.UtcNow)
            ],
            previousResponseItems:
            [
                """{"type":"function_call","id":"fc_list","call_id":"call_list","name":"mcp__linear__list_projects","arguments":"{}"}""",
                """{"type":"function_call","id":"fc_save","call_id":"call_save","name":"mcp__linear__save_issue","arguments":"{\"title\":\"Candidate\"}"}"""
            ]);

        var builder = new QueryModelIterationRequestBuilder();
        var streamingRequest = builder.Build(request, state, new ClawSharpSettings());

        Assert.Single(streamingRequest.Request.Messages);
        var repairedUserMessage = streamingRequest.Request.Messages[0];
        Assert.Equal("user", repairedUserMessage.Role);
        Assert.Equal("tool_result", repairedUserMessage.Content[0].Type);
        Assert.Equal(QueryRequestToolResultPairingRepair.SyntheticToolResultPlaceholder, repairedUserMessage.Content[0].Text);
        Assert.Equal("call_save", repairedUserMessage.Content[0].ToolUseId);
        Assert.Equal("tool_result", repairedUserMessage.Content[1].Type);
        Assert.Equal("call_list", repairedUserMessage.Content[1].ToolUseId);
        Assert.Equal("text", repairedUserMessage.Content[2].Type);
        Assert.Equal("Continue.", repairedUserMessage.Content[2].Text);
    }

    [Fact]
    public void QueryModelIterationRequestBuilder_Preserves_Leading_Tool_Result_When_Previous_Response_Items_Match()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-leading-tool-result-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(session, "$linear create issue");
        var state = QueryLoopStateFactory.CreateInitial(
            [
                ChatMessageFactory.CreateToolResult("call_list", "mcp__linear__list_projects", "Found project HR")
            ],
            previousResponseItems:
            [
                """{"type":"function_call","id":"fc_list","call_id":"call_list","name":"mcp__linear__list_projects","arguments":"{}"}"""
            ]);

        var builder = new QueryModelIterationRequestBuilder();
        var streamingRequest = builder.Build(request, state, new ClawSharpSettings());

        Assert.Single(streamingRequest.Request.Messages);
        Assert.Single(streamingRequest.Request.Messages[0].Content);
        Assert.Equal("tool_result", streamingRequest.Request.Messages[0].Content[0].Type);
        Assert.Equal("Found project HR", streamingRequest.Request.Messages[0].Content[0].Text);
        Assert.Equal("call_list", streamingRequest.Request.Messages[0].Content[0].ToolUseId);
    }

    [Fact]
    public void QueryModelIterationRequestBuilder_Dedupes_Duplicate_Tool_Uses_And_Tool_Results()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-duplicate-tool-pair-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(session, "Search");
        var state = QueryLoopStateFactory.CreateInitial(
            [
                new ChatMessage(
                    Guid.NewGuid().ToString("N"),
                    MessageRole.Assistant,
                    [
                        new MessageContentBlock(
                            MessageContentKind.ToolUse,
                            """{"path":"."}""",
                            "Glob",
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["toolUseId"] = "call_dup"
                            }),
                        new MessageContentBlock(
                            MessageContentKind.ToolUse,
                            """{"path":"."}""",
                            "Glob",
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["toolUseId"] = "call_dup"
                            })
                    ],
                    DateTimeOffset.UtcNow),
                new ChatMessage(
                    Guid.NewGuid().ToString("N"),
                    MessageRole.User,
                    [
                        new MessageContentBlock(
                            MessageContentKind.ToolResult,
                            "match one",
                            "Glob",
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["toolUseId"] = "call_dup"
                            }),
                        new MessageContentBlock(
                            MessageContentKind.ToolResult,
                            "match two",
                            "Glob",
                            new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["toolUseId"] = "call_dup"
                            })
                    ],
                    DateTimeOffset.UtcNow)
            ]);

        var builder = new QueryModelIterationRequestBuilder();
        var streamingRequest = builder.Build(request, state, new ClawSharpSettings());

        Assert.Equal(2, streamingRequest.Request.Messages.Count);
        Assert.Single(streamingRequest.Request.Messages[0].Content);
        Assert.Equal("tool_use", streamingRequest.Request.Messages[0].Content[0].Type);
        Assert.Single(streamingRequest.Request.Messages[1].Content);
        Assert.Equal("tool_result", streamingRequest.Request.Messages[1].Content[0].Type);
        Assert.Equal("match one", streamingRequest.Request.Messages[1].Content[0].Text);
    }

    [Fact]
    public async Task QueryEngine_Extracts_Prompt_Attachment_Markers_And_Adds_File_Context()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-picked-file-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var attachedFilePath = Path.Combine(tempDir, "spec.md");
        await File.WriteAllTextAsync(attachedFilePath, "# Spec");
        await File.WriteAllTextAsync(Path.Combine(tempDir, "note.txt"), "hello");

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
            $"Review the picked file.\n\n<clawsharp-attachment>{{\"kind\":\"file\",\"path\":\"{attachedFilePath.Replace("\\", "\\\\")}\",\"name\":\"spec.md\"}}</clawsharp-attachment>",
            [
                new ToolCallRequest("tooluse-note", "Read", "note.txt")
            ]);

        var result = await queryEngine.RunTurnAsync(session, request);

        Assert.NotNull(result.Request);
        Assert.Contains("Attached file spec.md", result.Request!.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Contains("# Spec", result.Request.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Equal("Review the picked file.", result.Request.Messages[1].Content[0].Text);
    }

    [Fact]
    public async Task QueryEngine_Extracts_Pdf_Prompt_Attachment_Markers_And_Adds_Read_Guidance()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-request-picked-pdf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var attachedFilePath = Path.Combine(tempDir, "resume.pdf");
        var pdfBytes = new byte[]
        {
            (byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'4', (byte)'\n',
            0x00, 0x9C, 0xFF, 0x10,
            (byte)'\n',
            (byte)'%', (byte)'%', (byte)'E', (byte)'O', (byte)'F'
        };
        await File.WriteAllBytesAsync(attachedFilePath, pdfBytes);
        await File.WriteAllTextAsync(Path.Combine(tempDir, "note.txt"), "hello");

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
            $"Extract the candidate name from the attached CV.\n\n<clawsharp-attachment>{{\"kind\":\"file\",\"path\":\"{attachedFilePath.Replace("\\", "\\\\")}\",\"name\":\"resume.pdf\"}}</clawsharp-attachment>",
            [
                new ToolCallRequest("tooluse-note", "Read", "note.txt")
            ]);

        var result = await queryEngine.RunTurnAsync(session, request);

        Assert.NotNull(result.Request);
        Assert.Contains("Attached file resume.pdf", result.Request!.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Contains("Type: PDF document", result.Request.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Contains(attachedFilePath.Replace('\\', '/'), result.Request.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Contains("Use the Read tool with the exact absolute path above", result.Request.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Contains("pages parameter", result.Request.Messages[0].Content[0].Text, StringComparison.Ordinal);
        Assert.Equal("Extract the candidate name from the attached CV.", result.Request.Messages[1].Content[0].Text);
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
