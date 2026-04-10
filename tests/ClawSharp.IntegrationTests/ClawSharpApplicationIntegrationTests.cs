using System.Text;
using ClawSharp.Infrastructure;
using ClawSharp.Core;
using ClawSharp.Query;
using ClawSharp.Tools;
using ClawSharp.Tasks;

namespace ClawSharp.IntegrationTests;

[Collection("SessionStorage")]
public class ClawSharpApplicationIntegrationTests
{
    [Fact]
    public async Task Factory_Creates_Default_Application()
    {
        var app = await ClawSharpApplicationFactory.CreateDefaultAsync();

        Assert.NotNull(app.AppState);
        Assert.NotNull(app.AppStateStore);
        Assert.NotNull(app.QueryEngine);
        Assert.NotNull(app.TerminalShell);
        Assert.NotNull(app.Tasks);
        Assert.NotNull(app.Commands);
        Assert.NotNull(app.Settings);
        Assert.Same(app.AppState, app.AppStateStore.GetState());
    }

    [Fact]
    public async Task TerminalShell_Prints_Query_Runtime_Gap_For_Plain_Prompts()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var originalApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var originalUseOpenAi = Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_OPENAI");
        var originalOpenAiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        var originalOpenAiBaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL");
        var originalOpenAiApiBase = Environment.GetEnvironmentVariable("OPENAI_API_BASE");
        var originalCodexApiKey = Environment.GetEnvironmentVariable("CODEX_API_KEY");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", string.Empty);
        Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_OPENAI", null);
        Environment.SetEnvironmentVariable("OPENAI_MODEL", null);
        Environment.SetEnvironmentVariable("OPENAI_BASE_URL", null);
        Environment.SetEnvironmentVariable("OPENAI_API_BASE", null);
        Environment.SetEnvironmentVariable("CODEX_API_KEY", null);

        try
        {
            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
            using var input = new StringReader("hello\n/exit\n");
            using var output = new StringWriter();

            var exitCode = await app.TerminalShell.RunReplAsync(input, output);
            var transcript = output.ToString();
            var projectDir = SessionStoragePaths.GetProjectDir(Directory.GetCurrentDirectory());
            var transcriptFiles = Directory.Exists(projectDir)
                ? Directory.GetFiles(projectDir, "*.jsonl")
                : [];

            Assert.Equal(0, exitCode);
            Assert.Contains("Query model request failed with API response (401)", transcript);
            Assert.Contains("ClawSharp", transcript);
            Assert.Single(transcriptFiles);
            var transcriptLines = await File.ReadAllLinesAsync(transcriptFiles[0]);
            Assert.True(transcriptLines.Length >= 2);
            Assert.Contains(transcriptLines, line => line.Contains("\"type\":\"file-history-snapshot\"", StringComparison.Ordinal));
            Assert.Contains(transcriptLines, line => line.Contains("\"type\":\"user\"", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_OPENAI", originalUseOpenAi);
            Environment.SetEnvironmentVariable("OPENAI_MODEL", originalOpenAiModel);
            Environment.SetEnvironmentVariable("OPENAI_BASE_URL", originalOpenAiBaseUrl);
            Environment.SetEnvironmentVariable("OPENAI_API_BASE", originalOpenAiApiBase);
            Environment.SetEnvironmentVariable("CODEX_API_KEY", originalCodexApiKey);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }
        }
    }

    [Fact]
    public async Task TerminalShell_Joins_Backslash_Continued_Input_Into_Single_Turn()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
            using var input = new StringReader("hello\\\nworld\n/exit\n");
            using var output = new StringWriter();

            var exitCode = await app.TerminalShell.RunReplAsync(input, output);
            var projectDir = SessionStoragePaths.GetProjectDir(Directory.GetCurrentDirectory());
            var transcriptFiles = Directory.Exists(projectDir)
                ? Directory.GetFiles(projectDir, "*.jsonl")
                : [];

            Assert.Equal(0, exitCode);
            Assert.Single(transcriptFiles);
            var transcriptLines = await File.ReadAllLinesAsync(transcriptFiles[0]);
            var userTranscriptLine = Assert.Single(
                transcriptLines,
                line => line.Contains("\"type\":\"user\"", StringComparison.Ordinal));
            Assert.Contains("hello", userTranscriptLine, StringComparison.Ordinal);
            Assert.Contains("world", userTranscriptLine, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }
        }
    }

    [Fact]
    public async Task TerminalShell_Does_Not_Treat_Bash_Mode_Input_As_Slash_Command()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
            using var input = new StringReader("!/exit\n/exit\n");
            using var output = new StringWriter();

            var exitCode = await app.TerminalShell.RunReplAsync(input, output);
            var transcript = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains("Bash input mode is not implemented yet.", transcript, StringComparison.Ordinal);
            Assert.DoesNotContain("Unknown command: exit", transcript, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }
        }
    }

    [Fact]
    public async Task TerminalShell_Drains_Queued_Task_Notifications_Into_Output_And_Transcript()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
            app.QueuedCommandQueue.EnqueuePendingNotification(new QueuedCommand(
                "<task-notification>\n<task-id>task-123</task-id>\n<status>completed</status>\n<summary>Background command completed</summary>\n</task-notification>",
                PromptInputMode.TaskNotification));

            using var input = new StringReader("/exit\n");
            using var output = new StringWriter();

            var exitCode = await app.TerminalShell.RunReplAsync(input, output);
            var transcript = output.ToString();
            var projectDir = SessionStoragePaths.GetProjectDir(Directory.GetCurrentDirectory());
            var transcriptFiles = Directory.Exists(projectDir)
                ? Directory.GetFiles(projectDir, "*.jsonl")
                : [];

            Assert.Equal(0, exitCode);
            Assert.Contains("\u25CF Background command completed", transcript, StringComparison.Ordinal);
            Assert.Single(transcriptFiles);
            var transcriptLines = await File.ReadAllLinesAsync(transcriptFiles[0]);
            Assert.Single(transcriptLines);
            Assert.Contains("\"type\":\"user\"", transcriptLines[0], StringComparison.Ordinal);
            Assert.Contains("task-notification", transcriptLines[0], StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }
        }
    }

    [Fact]
    public async Task TerminalShell_Replays_Resumed_Session_History_At_Startup()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
            var seededSession = app.SessionFactory.Create();
            seededSession.Add(ChatMessageFactory.CreateText(MessageRole.User, "previous prompt"));
            seededSession.Add(ChatMessageFactory.CreateText(MessageRole.Assistant, "previous answer"));
            await app.TranscriptStore.RecordTranscriptAsync(seededSession, seededSession.Messages);

            var resumedSession = await app.SessionFactory.ResumeAsync(seededSession.Id);
            Assert.NotNull(resumedSession);

            using var input = new StringReader("/exit\n");
            using var output = new StringWriter();

            var exitCode = await app.TerminalShell.RunReplAsync(input, output, resumedSession);
            var transcript = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains("> previous prompt", transcript, StringComparison.Ordinal);
            Assert.Contains("previous answer", transcript, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }
        }
    }

    [Fact]
    public async Task TerminalShell_Resume_Command_Switches_Session_And_Replays_History()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
            var seededSession = app.SessionFactory.Create();
            seededSession.Add(ChatMessageFactory.CreateText(MessageRole.User, "restored prompt"));
            seededSession.Add(ChatMessageFactory.CreateText(MessageRole.Assistant, "restored answer"));
            await app.TranscriptStore.RecordTranscriptAsync(seededSession, seededSession.Messages);

            using var input = new StringReader($"/resume {seededSession.Id}\n/exit\n");
            using var output = new StringWriter();

            var exitCode = await app.TerminalShell.RunReplAsync(input, output);
            var transcript = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains($"Resumed session {seededSession.Id}.", transcript, StringComparison.Ordinal);
            Assert.Contains("> restored prompt", transcript, StringComparison.Ordinal);
            Assert.Contains("restored answer", transcript, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }
        }
    }

    [Fact]
    public async Task TerminalShell_Replay_Groups_Consecutive_Assistant_Messages()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
            var seededSession = app.SessionFactory.Create();
            seededSession.Add(ChatMessageFactory.CreateText(MessageRole.User, "previous prompt"));
            seededSession.Add(ChatMessageFactory.CreateText(MessageRole.Assistant, "first answer"));
            seededSession.Add(ChatMessageFactory.CreateText(MessageRole.Assistant, "follow-up answer"));
            await app.TranscriptStore.RecordTranscriptAsync(seededSession, seededSession.Messages);

            var resumedSession = await app.SessionFactory.ResumeAsync(seededSession.Id);
            Assert.NotNull(resumedSession);

            using var input = new StringReader("/exit\n");
            using var output = new StringWriter();

            var exitCode = await app.TerminalShell.RunReplAsync(input, output, resumedSession);
            var transcript = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains("* first answer", transcript, StringComparison.Ordinal);
            Assert.Contains("  follow-up answer", transcript, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }
        }
    }

    [Fact]
    public async Task TerminalShell_Prints_Status_Footer_For_Active_Session_Model_And_Mode()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
            var session = app.SessionFactory.Create();
            session.SetCustomTitle("Incident Review");
            app.AppStateStore.SetState(
                state => ClawSharpAppStateMutations.WithStatusLineText(
                    ClawSharpAppStateMutations.WithToolPermissionMode(
                        ClawSharpAppStateMutations.WithMainLoopModel(state, "opus"),
                        PermissionMode.Plan),
                    "ready"));

            using var input = new StringReader("/exit\n");
            using var output = new StringWriter();

            var exitCode = await app.TerminalShell.RunReplAsync(input, output, session);
            var transcript = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains("ready", transcript, StringComparison.Ordinal);
            Assert.Contains("plan mode on · session Incident Review · model Opus", transcript, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }
        }
    }

    [Fact]
    public async Task TerminalShell_Prints_Approved_DeepLink_Draft_Note_At_Startup()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
            using var input = new StringReader("/exit\n");
            using var output = new StringWriter();

            var exitCode = await app.TerminalShell.RunReplAsync(
                input,
                output,
                deepLinkRuntimeContext: new DeepLinkRuntimeContext(true, "review the current workspace", "owner/repo"));
            var transcript = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains("This session was opened by an external deep link", transcript, StringComparison.Ordinal);
            Assert.Contains("Resolved owner/repo from local clones.", transcript, StringComparison.Ordinal);
            Assert.Contains("cannot prefill editable input without submitting it", transcript, StringComparison.Ordinal);
            Assert.Contains("review the current workspace", transcript, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }
        }
    }

    [Fact]
    public async Task TerminalShell_Prints_Background_Task_Summary_In_Footer()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
            var session = app.SessionFactory.Create();
            session.SetCustomTitle("Incident Review");
            app.AppStateStore.SetState(
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

            using var input = new StringReader("/exit\n");
            using var output = new StringWriter();

            var exitCode = await app.TerminalShell.RunReplAsync(input, output, session);
            var transcript = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains("1 shell", transcript, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }
        }
    }

    [Fact]
    public async Task TerminalShell_Tasks_Command_Prints_Background_Task_List()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
            app.AppStateStore.SetState(
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
                            "dotnet build"),
                        ["task-2"] = new LocalAgentTask(
                            "task-2",
                            "Review diff",
                            ClawSharp.Tasks.TaskStatus.Running,
                            DateTimeOffset.UtcNow.AddMinutes(-1),
                            "agent.log",
                            "Review the diff",
                            "reviewer")
                    }));

            using var input = new StringReader("/tasks\n/exit\n");
            using var output = new StringWriter();

            var exitCode = await app.TerminalShell.RunReplAsync(input, output);
            var transcript = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains("Background tasks", transcript, StringComparison.Ordinal);
            Assert.Contains("task-1 [running] dotnet build", transcript, StringComparison.Ordinal);
            Assert.Contains("task-2 [running] Review diff", transcript, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }
        }
    }

    [Fact]
    public async Task TerminalShell_Tasks_Command_Prints_Task_Detail_For_Id()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
            Directory.CreateDirectory(tempConfigDir);
            var outputFile = Path.Combine(tempConfigDir, "task.log");
            await File.WriteAllTextAsync(outputFile, "Build succeeded\n");
            app.AppStateStore.SetState(
                state => ClawSharpAppStateMutations.WithTasks(
                    state,
                    new Dictionary<string, ClawSharpTask>(StringComparer.Ordinal)
                    {
                        ["task-1"] = new LocalBashTask(
                            "task-1",
                            "run build",
                            ClawSharp.Tasks.TaskStatus.Completed,
                            DateTimeOffset.UtcNow,
                            outputFile,
                            "dotnet build",
                            ExitCode: 0)
                    }));

            using var input = new StringReader("/tasks task-1\n/exit\n");
            using var output = new StringWriter();

            var exitCode = await app.TerminalShell.RunReplAsync(input, output);
            var transcript = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains("task-1 [completed] local_bash", transcript, StringComparison.Ordinal);
            Assert.Contains("Command: dotnet build", transcript, StringComparison.Ordinal);
            Assert.Contains("Build succeeded", transcript, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }
        }
    }

    [Fact]
    public async Task Bootstrapper_Name_Persists_Custom_Title_On_First_Transcript_Write()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
            var (success, session, error) = await app.ReplSessionBootstrapper.ResolveAsync(["--name", "Named Session"]);

            Assert.True(success, error);
            Assert.NotNull(session);

            using var input = new StringReader("hello\n/exit\n");
            using var output = new StringWriter();

            var exitCode = await app.TerminalShell.RunReplAsync(input, output, session);
            Assert.Equal(0, exitCode);

            var transcriptLines = await File.ReadAllLinesAsync(session!.TranscriptPath);
            Assert.True(transcriptLines.Length >= 2);
            Assert.Contains("\"type\":\"custom-title\"", transcriptLines[0], StringComparison.Ordinal);
            Assert.Contains("\"customTitle\":\"Named Session\"", transcriptLines[0], StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }
        }
    }

    [Fact]
    public async Task TerminalShell_Resume_Command_Resolves_By_Exact_Custom_Title()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
            var seededSession = app.SessionFactory.Create();
            seededSession.SetCustomTitle("Daily Sync");
            seededSession.Add(ChatMessageFactory.CreateText(MessageRole.User, "restored prompt"));
            seededSession.Add(ChatMessageFactory.CreateText(MessageRole.Assistant, "restored answer"));
            await app.TranscriptStore.RecordTranscriptAsync(seededSession, seededSession.Messages);

            using var input = new StringReader("/resume Daily Sync\n/exit\n");
            using var output = new StringWriter();

            var exitCode = await app.TerminalShell.RunReplAsync(input, output);
            var transcript = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains($"Resumed session {seededSession.Id}.", transcript, StringComparison.Ordinal);
            Assert.Contains("> restored prompt", transcript, StringComparison.Ordinal);
            Assert.Contains("restored answer", transcript, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }
        }
    }

    [Fact]
    public async Task TerminalShell_Rename_Command_Persists_Title_And_Resume_Uses_It()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
            using var input = new StringReader("/rename Incident Review\nhello\n/exit\n");
            using var output = new StringWriter();

            var exitCode = await app.TerminalShell.RunReplAsync(input, output);
            Assert.Equal(0, exitCode);

            var projectDir = SessionStoragePaths.GetProjectDir(Directory.GetCurrentDirectory());
            var transcriptFile = Directory.GetFiles(projectDir, "*.jsonl").Single();
            var transcriptLines = await File.ReadAllLinesAsync(transcriptFile);
            Assert.Contains(transcriptLines, line => line.Contains("\"type\":\"custom-title\"", StringComparison.Ordinal));
            Assert.Contains(transcriptLines, line => line.Contains("\"customTitle\":\"Incident Review\"", StringComparison.Ordinal));

            using var resumeInput = new StringReader("/resume Incident Review\n/exit\n");
            using var resumeOutput = new StringWriter();

            var resumeExitCode = await app.TerminalShell.RunReplAsync(resumeInput, resumeOutput);
            var replay = resumeOutput.ToString();

            Assert.Equal(0, resumeExitCode);
            Assert.Contains("Resumed session", replay, StringComparison.Ordinal);
            Assert.Contains("> hello", replay, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }
        }
    }

    [Fact]
    public async Task TerminalShell_Synchronizes_Active_Session_State_On_Rename()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();
            using var input = new StringReader("/rename Incident Review\n/exit\n");
            using var output = new StringWriter();

            var exitCode = await app.TerminalShell.RunReplAsync(input, output);

            Assert.Equal(0, exitCode);
            Assert.NotNull(app.AppState.ActiveSessionId);
            Assert.Equal("Incident Review", app.AppState.ActiveSessionTitle);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }
        }
    }

    [Fact]
    public async Task QueryEngine_Snapshot_Emits_File_Updated_Notification_For_Changed_Tracked_File()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-file-history-notify", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var session = new DefaultSessionFactory(workspaceRoot, transcriptStore).Create();
            var settings = new ClawSharpSettings();
            var eventSink = new InMemoryEventSink();
            var queuedTaskNotificationDrainer = new QueuedTaskNotificationDrainer(new InMemoryQueuedCommandQueue(), transcriptStore);
            var fileUpdateNotifier = new EventSinkFileUpdateNotifier(eventSink);
            var toolRegistry = new ToolRegistry(workspaceRoot, new TaskRegistry(), fileUpdateNotifier: fileUpdateNotifier);
            var queryEngine = new QueryEngine(
                settings,
                eventSink,
                transcriptStore,
                new ExplicitToolTurnRunner(new ToolOrchestrator(toolRegistry, eventSink)),
                queuedTaskNotificationDrainer,
                fileUpdateNotifier);

            var filePath = Path.Combine(workspaceRoot, "tracked.txt");
            await File.WriteAllTextAsync(filePath, "alpha");
            var timestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();
            toolRegistry.ReadFileState.Set(filePath, new FileState("alpha", timestamp, null, null));
            session.Add(ChatMessageFactory.CreateToolUse([("tooluse-write", "Write", """{"file_path":"tracked.txt","content":"beta"}""")]));
            var writeResult = await toolRegistry.ExecuteAsync(
                "Write",
                """{"file_path":"tracked.txt","content":"beta"}""",
                session,
                settings);
            Assert.True(writeResult.Success, writeResult.Output);
            session.Add(ChatMessageFactory.CreateToolResult("tooluse-write", "Write", writeResult.Output, writeResult.StructuredOutput));
            await FileHistoryService.MakeSnapshotAsync(session, settings, "seed-user-message", fileUpdateNotifier);
            var baselineEventCount = eventSink.Events.Count;

            await File.WriteAllTextAsync(filePath, "gamma");

            await Assert.ThrowsAsync<QueryExecutionNotImplementedException>(
                async () => await queryEngine.RunTurnAsync(session, "hello"));

            var newEvents = eventSink.Events.Skip(baselineEventCount).ToArray();
            var notification = Assert.Single(
                newEvents,
                appEvent => appEvent.Type == AppEventType.NotificationRaised &&
                            appEvent.Metadata?["notificationType"] == "file-updated");
            Assert.Equal(filePath, notification.Metadata!["filePath"]);
            Assert.Equal("beta", notification.Metadata["oldContent"]);
            Assert.Equal("gamma", notification.Metadata["newContent"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                DeleteDirectoryWithRetry(tempConfigDir);
            }

            if (Directory.Exists(workspaceRoot))
            {
                DeleteDirectoryWithRetry(workspaceRoot);
            }
        }
    }

    [Fact]
    public async Task Factory_Loads_Settings_From_Claude_Config_And_Project_Paths()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-integration-config", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-settings-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempConfigDir);
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(Path.Combine(workspaceRoot, ".clawsharp"));
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        await File.WriteAllTextAsync(
            Path.Combine(tempConfigDir, "settings.json"),
            """
            {
              "runtime": {
                "model": "user-model"
              },
              "terminal": {
                "useColor": false
              }
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(workspaceRoot, ".clawsharp", "settings.json"),
            """
            {
              "runtime": {
                "permissionMode": "plan"
              },
              "terminal": {
                "showTimestamps": true
              }
            }
            """);

        var originalDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(workspaceRoot);

            var app = await ClawSharpApplicationFactory.CreateDefaultAsync();

            Assert.Equal("user-model", app.Settings.Runtime.Model);
            Assert.Equal(PermissionMode.Plan, app.Settings.Runtime.PermissionMode);
            Assert.True(app.Settings.Terminal.ShowTimestamps);
            Assert.False(app.Settings.Terminal.UseColor);
            Assert.Equal(tempConfigDir.Normalize(NormalizationForm.FormC), app.AppState.Environment.ClaudeConfigHomeDir);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);

            DeleteDirectoryWithRetry(tempConfigDir);
            DeleteDirectoryWithRetry(workspaceRoot);
        }
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        IOException? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException exception)
            {
                lastError = exception;
                Thread.Sleep(100);
            }
        }

        if (lastError is not null)
        {
            throw lastError;
        }
    }
}
