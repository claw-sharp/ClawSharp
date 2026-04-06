using ClawSharp.Core;
using ClawSharp.Tasks;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.ParityTests;

public sealed class GoldenOutputTests
{
    [Fact]
    public void Transcript_Rendering_Matches_Golden_Output()
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

        var actual = RenderTranscript(renderer, messages);
        Assert.Equal(LoadFixture("transcript-rendering.golden.txt"), actual);
    }

    [Fact]
    public void Terminal_Footer_Rendering_Matches_Golden_Output()
    {
        var renderer = new TerminalFooterRenderer();
        var settings = new ClawSharpSettings
        {
            Runtime = new RuntimeSettings
            {
                Model = "opus"
            }
        };
        var appState = ClawSharpAppState.CreateDefault(
            Environment.CurrentDirectory,
            new StartupEnvironment(SessionStoragePaths.GetClaudeConfigHomeDir()),
            settings,
            [],
            [],
            [],
            [],
            [],
            [],
            ToolPermissionContexts.CreateEmpty(PermissionMode.Plan));
        appState = ClawSharpAppStateMutations.WithStatusLineText(appState, "ready");
        var session = new ConversationSession("session-1", Environment.CurrentDirectory);
        session.SetCustomTitle("Incident Review");
        appState = ClawSharpAppStateMutations.WithActiveSession(appState, session);
        appState = ClawSharpAppStateMutations.WithTasks(
            appState,
            new Dictionary<string, ClawSharpTask>(StringComparer.Ordinal)
            {
                ["task-1"] = new LocalBashTask("task-1", "run build", ClawSharp.Tasks.TaskStatus.Running, DateTimeOffset.UtcNow, "build.log", "dotnet build")
            });

        var actual = NormalizeNewlines(string.Join(Environment.NewLine, renderer.Render(appState)));
        Assert.Equal(LoadFixture("terminal-footer.golden.txt"), actual);
    }

    [Fact]
    public void Background_Task_List_Rendering_Matches_Golden_Output()
    {
        var renderer = new BackgroundTasksCommandRenderer();
        var now = new DateTimeOffset(2026, 4, 5, 18, 0, 0, TimeSpan.Zero);
        var tasks = new Dictionary<string, ClawSharpTask>(StringComparer.Ordinal)
        {
            ["task-1"] = new LocalBashTask("task-1", "run build", ClawSharp.Tasks.TaskStatus.Running, now, "build.log", "dotnet build"),
            ["task-2"] = new RemoteAgentTask("task-2", "remote review", ClawSharp.Tasks.TaskStatus.Pending, now.AddSeconds(-10), "remote.log", "remote-session", "review", "Remote Review"),
            ["task-3"] = new LocalAgentTask("task-3", "Review diff", ClawSharp.Tasks.TaskStatus.Running, now.AddSeconds(-20), "agent.log", "Review the diff", "reviewer")
        };

        var actual = NormalizeNewlines(renderer.RenderList(tasks));
        Assert.Equal(LoadFixture("background-tasks-list.golden.txt"), actual);
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

        return NormalizeNewlines(string.Join(Environment.NewLine, lines));
    }

    private static string LoadFixture(string fileName)
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "Fixtures",
            fileName);
        return NormalizeNewlines(File.ReadAllText(fixturePath));
    }

    private static string NormalizeNewlines(string value)
    {
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\n', '\r');
    }
}
