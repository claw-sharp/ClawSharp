using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Tasks;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public class CommandRegistryTests
{
    private static readonly IWorktreePathResolver SingleWorktreePathResolver =
        new FixedWorktreePathResolver([]);

    [Fact]
    public async Task HelpCommand_Lists_Registered_Commands()
    {
        var registry = new CommandRegistry();
        registry.Register(new VersionCommandHandler());
        registry.Register(new ResumeCommandHandler(new DiskSessionLogStore(), SingleWorktreePathResolver));
        registry.Register(new HelpCommandHandler(registry));

        Assert.True(registry.TryResolve("help", out var handler));
        Assert.NotNull(handler);

        var result = await handler!.ExecuteAsync(
            "/help",
            new CommandExecutionContext
            {
                AppStateStore = CreateAppStateStore(Environment.CurrentDirectory),
                Session = new DefaultSessionFactory(Environment.CurrentDirectory).Create(),
                SessionFactory = new DefaultSessionFactory(Environment.CurrentDirectory),
                TranscriptStore = new JsonlTranscriptStore(),
                Settings = new ClawSharpSettings()
            });

        Assert.True(result.Success);
        Assert.Contains("/version", result.Output);
    }

    [Fact]
    public async Task ResumeCommand_Resolves_Existing_Session_By_Id()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-resume-command-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var sessionFactory = new DefaultSessionFactory(workspaceRoot, transcriptStore);
            var seededSession = sessionFactory.Create();
            seededSession.Add(ChatMessageFactory.CreateText(MessageRole.User, "seed"));
            await transcriptStore.RecordTranscriptAsync(seededSession, seededSession.Messages);

            var handler = new ResumeCommandHandler(
                new DiskSessionLogStore(),
                new FixedWorktreePathResolver([workspaceRoot]));
            var result = await handler.ExecuteAsync(
                $"/resume {seededSession.Id}",
                new CommandExecutionContext
                {
                    AppStateStore = CreateAppStateStore(workspaceRoot),
                    Session = sessionFactory.Create(),
                    SessionFactory = sessionFactory,
                    TranscriptStore = transcriptStore,
                    Settings = new ClawSharpSettings()
                });

            Assert.True(result.Success);
            Assert.NotNull(result.SessionOverride);
            Assert.Equal(seededSession.Id, result.SessionOverride!.Id);
            Assert.Single(result.SessionOverride.Messages);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                Directory.Delete(tempConfigDir, recursive: true);
            }

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TasksCommand_Lists_Background_Tasks()
    {
        var registry = new CommandRegistry();
        registry.Register(new TasksCommandHandler());

        Assert.True(registry.TryResolve("tasks", out var handler));
        Assert.NotNull(handler);

        var appStateStore = CreateAppStateStore(Environment.CurrentDirectory);
        appStateStore.SetState(
            state => ClawSharpAppStateMutations.WithTasks(
                state,
                new Dictionary<string, ClawSharpTask>(StringComparer.Ordinal)
                {
                    ["task-1"] = new LocalBashTask(
                        "task-1",
                        "run build",
                        ClawSharp.Tasks.TaskStatus.Running,
                        DateTimeOffset.UtcNow,
                        "build.log",
                        "dotnet build")
                }));

        var result = await handler!.ExecuteAsync(
            "/tasks",
            new CommandExecutionContext
            {
                AppStateStore = appStateStore,
                Session = new DefaultSessionFactory(Environment.CurrentDirectory).Create(),
                SessionFactory = new DefaultSessionFactory(Environment.CurrentDirectory),
                TranscriptStore = new JsonlTranscriptStore(),
                Settings = new ClawSharpSettings()
            });

        Assert.True(result.Success);
        Assert.Contains("Background tasks", result.Output, StringComparison.Ordinal);
        Assert.Contains("task-1 [running] dotnet build", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_Command_Is_Hidden_And_Not_Resolvable()
    {
        var registry = new CommandRegistry();
        registry.Register(new DisabledCommandHandler());

        Assert.False(registry.TryResolve("disabled", out _));
        Assert.DoesNotContain(registry.GetAllDescriptors(), static descriptor => string.Equals(descriptor.Name, "disabled", StringComparison.Ordinal));
        Assert.DoesNotContain(registry.GetAvailableDescriptors(), static descriptor => string.Equals(descriptor.Name, "disabled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResumeCommand_Resolves_Existing_Session_By_Exact_Custom_Title()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-resume-title-command-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var sessionFactory = new DefaultSessionFactory(workspaceRoot, transcriptStore);
            var seededSession = sessionFactory.Create();
            seededSession.SetCustomTitle("Incident Review");
            seededSession.Add(ChatMessageFactory.CreateText(MessageRole.User, "seed"));
            await transcriptStore.RecordTranscriptAsync(seededSession, seededSession.Messages);

            var handler = new ResumeCommandHandler(
                new DiskSessionLogStore(),
                new FixedWorktreePathResolver([workspaceRoot]));
            var result = await handler.ExecuteAsync(
                "/resume Incident Review",
                new CommandExecutionContext
                {
                    AppStateStore = CreateAppStateStore(workspaceRoot),
                    Session = sessionFactory.Create(),
                    SessionFactory = sessionFactory,
                    TranscriptStore = transcriptStore,
                    Settings = new ClawSharpSettings()
                });

            Assert.True(result.Success);
            Assert.NotNull(result.SessionOverride);
            Assert.Equal(seededSession.Id, result.SessionOverride!.Id);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                Directory.Delete(tempConfigDir, recursive: true);
            }

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RenameCommand_Persists_Custom_Title_For_Existing_Session()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-rename-command-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var sessionFactory = new DefaultSessionFactory(workspaceRoot, transcriptStore);
            var session = sessionFactory.Create();
            session.Add(ChatMessageFactory.CreateText(MessageRole.User, "seed"));
            await transcriptStore.RecordTranscriptAsync(session, session.Messages);

            var handler = new RenameCommandHandler();
            var result = await handler.ExecuteAsync(
                "/rename Incident Review",
                new CommandExecutionContext
                {
                    AppStateStore = CreateAppStateStore(workspaceRoot),
                    Session = session,
                    SessionFactory = sessionFactory,
                    TranscriptStore = transcriptStore,
                    Settings = new ClawSharpSettings()
                });

            Assert.True(result.Success);
            Assert.Equal("Incident Review", session.CustomTitle);
            var transcriptLines = await File.ReadAllLinesAsync(session.TranscriptPath);
            Assert.Contains(transcriptLines, line => line.Contains("\"type\":\"custom-title\"", StringComparison.Ordinal));
            Assert.Contains(transcriptLines, line => line.Contains("\"customTitle\":\"Incident Review\"", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                Directory.Delete(tempConfigDir, recursive: true);
            }

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RenameCommand_Stores_Title_For_First_Future_Transcript_Write()
    {
        var transcriptStore = new JsonlTranscriptStore();
        var sessionFactory = new DefaultSessionFactory(Environment.CurrentDirectory, transcriptStore);
        var session = sessionFactory.Create();
        var handler = new RenameCommandHandler();

        var result = await handler.ExecuteAsync(
            "/rename Planned Session",
            new CommandExecutionContext
            {
                AppStateStore = CreateAppStateStore(Environment.CurrentDirectory),
                Session = session,
                SessionFactory = sessionFactory,
                TranscriptStore = transcriptStore,
                Settings = new ClawSharpSettings()
            });

        Assert.True(result.Success);
        Assert.Equal("Planned Session", session.CustomTitle);
        Assert.True(session.HasUnrecordedCustomTitle());
    }

    [Fact]
    public async Task AddClaudeKeyCommand_Stores_Key_In_App_State_And_Process_Environment()
    {
        var previousApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
            var handler = new AddClaudeKeyCommandHandler();
            var appStateStore = CreateAppStateStore(Environment.CurrentDirectory);

            var result = await handler.ExecuteAsync(
                "/add-claude-key claude_test_key",
                new CommandExecutionContext
                {
                    AppStateStore = appStateStore,
                    Session = new DefaultSessionFactory(Environment.CurrentDirectory).Create(),
                    SessionFactory = new DefaultSessionFactory(Environment.CurrentDirectory),
                    TranscriptStore = new JsonlTranscriptStore(),
                    Settings = new ClawSharpSettings()
                });

            Assert.True(result.Success);
            Assert.Equal("claude_test_key", Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
            Assert.Equal("claude_test_key", appStateStore.GetState().Settings.ClaudeApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previousApiKey);
        }
    }

    [Fact]
    public async Task ResumeCommand_Resolves_Same_Repo_Worktree_Session_By_Id()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-resume-same-repo-id-tests", Guid.NewGuid().ToString("N"));
        var currentWorkspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"), "repo");
        var siblingWorkspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"), "repo-worktree");
        Directory.CreateDirectory(currentWorkspaceRoot);
        Directory.CreateDirectory(siblingWorkspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var currentSessionFactory = new DefaultSessionFactory(currentWorkspaceRoot, transcriptStore);
            var siblingSessionFactory = new DefaultSessionFactory(siblingWorkspaceRoot, transcriptStore);
            var siblingSession = siblingSessionFactory.Create();
            siblingSession.Add(ChatMessageFactory.CreateText(MessageRole.User, "sibling prompt"));
            await transcriptStore.RecordTranscriptAsync(siblingSession, siblingSession.Messages);

            var handler = new ResumeCommandHandler(
                new DiskSessionLogStore(),
                new FixedWorktreePathResolver([currentWorkspaceRoot, siblingWorkspaceRoot]));
            var result = await handler.ExecuteAsync(
                $"/resume {siblingSession.Id}",
                new CommandExecutionContext
                {
                    AppStateStore = CreateAppStateStore(currentWorkspaceRoot),
                    Session = currentSessionFactory.Create(),
                    SessionFactory = currentSessionFactory,
                    TranscriptStore = transcriptStore,
                    Settings = new ClawSharpSettings()
                });

            Assert.True(result.Success);
            Assert.NotNull(result.SessionOverride);
            Assert.Equal(siblingSession.Id, result.SessionOverride!.Id);
            Assert.Equal(siblingWorkspaceRoot, result.SessionOverride.ProjectDirectory);
            Assert.Single(result.SessionOverride.Messages);
            Assert.Equal("sibling prompt", result.SessionOverride.Messages[0].Content);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                Directory.Delete(tempConfigDir, recursive: true);
            }

            if (Directory.Exists(Path.GetDirectoryName(currentWorkspaceRoot)!))
            {
                Directory.Delete(Path.GetDirectoryName(currentWorkspaceRoot)!, recursive: true);
            }

            if (Directory.Exists(Path.GetDirectoryName(siblingWorkspaceRoot)!))
            {
                Directory.Delete(Path.GetDirectoryName(siblingWorkspaceRoot)!, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ResumeCommand_Resolves_Same_Repo_Worktree_Session_By_Title()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-resume-same-repo-title-tests", Guid.NewGuid().ToString("N"));
        var currentWorkspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"), "repo");
        var siblingWorkspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"), "repo-worktree");
        Directory.CreateDirectory(currentWorkspaceRoot);
        Directory.CreateDirectory(siblingWorkspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var currentSessionFactory = new DefaultSessionFactory(currentWorkspaceRoot, transcriptStore);
            var siblingSessionFactory = new DefaultSessionFactory(siblingWorkspaceRoot, transcriptStore);
            var siblingSession = siblingSessionFactory.Create();
            siblingSession.SetCustomTitle("Sibling Review");
            siblingSession.Add(ChatMessageFactory.CreateText(MessageRole.User, "sibling prompt"));
            await transcriptStore.RecordTranscriptAsync(siblingSession, siblingSession.Messages);

            var handler = new ResumeCommandHandler(
                new DiskSessionLogStore(),
                new FixedWorktreePathResolver([currentWorkspaceRoot, siblingWorkspaceRoot]));
            var result = await handler.ExecuteAsync(
                "/resume Sibling Review",
                new CommandExecutionContext
                {
                    AppStateStore = CreateAppStateStore(currentWorkspaceRoot),
                    Session = currentSessionFactory.Create(),
                    SessionFactory = currentSessionFactory,
                    TranscriptStore = transcriptStore,
                    Settings = new ClawSharpSettings()
                });

            Assert.True(result.Success);
            Assert.NotNull(result.SessionOverride);
            Assert.Equal(siblingSession.Id, result.SessionOverride!.Id);
            Assert.Equal(siblingWorkspaceRoot, result.SessionOverride.ProjectDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                Directory.Delete(tempConfigDir, recursive: true);
            }

            if (Directory.Exists(Path.GetDirectoryName(currentWorkspaceRoot)!))
            {
                Directory.Delete(Path.GetDirectoryName(currentWorkspaceRoot)!, recursive: true);
            }

            if (Directory.Exists(Path.GetDirectoryName(siblingWorkspaceRoot)!))
            {
                Directory.Delete(Path.GetDirectoryName(siblingWorkspaceRoot)!, recursive: true);
            }
        }
    }

    private sealed class FixedWorktreePathResolver : IWorktreePathResolver
    {
        private readonly IReadOnlyList<string> _paths;

        public FixedWorktreePathResolver(IReadOnlyList<string> paths)
        {
            _paths = paths;
        }

        public Task<IReadOnlyList<string>> GetWorktreePathsAsync(
            string workspaceRoot,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_paths.Count == 0 ? (IReadOnlyList<string>)[workspaceRoot] : _paths);
        }
    }

    private sealed class DisabledCommandHandler : ICommandHandler
    {
        public CommandDescriptor Descriptor { get; } =
            new("disabled", "Disabled command", "/disabled", IsEnabled: false);

        public Task<CommandResult> ExecuteAsync(
            string input,
            CommandExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CommandResult(true, string.Empty));
        }
    }

    private static IClawSharpAppStateStore CreateAppStateStore(string workspaceRoot)
    {
        return new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                workspaceRoot,
                StartupEnvironment.Capture(),
                new ClawSharpSettings(),
                [],
                [],
                [],
                [],
                [],
                []));
    }
}
