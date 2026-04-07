using ClawSharp.Core;
using ClawSharp.Tasks;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.UnitTests;

public sealed class TerminalFooterRendererTests
{
    private readonly TerminalFooterRenderer _renderer = new();

    [Fact]
    public void Render_Includes_StatusLine_Session_Model_And_Active_Mode()
    {
        var state = ClawSharpAppState.CreateDefault(
            workspaceRoot: "d:\\workspace",
            environment: new StartupEnvironment("d:\\config", BareMode: false, DisablePolicySkills: false),
            settings: new ClawSharpSettings
            {
                Runtime = new RuntimeSettings
                {
                    Model = "sonnet",
                    PermissionMode = PermissionMode.Default
                }
            },
            settingsIssues: [],
            pluginInstallations: [],
            plugins: [],
            skills: [],
            agentDefinitions: [],
            hooks: []);

        state = ClawSharpAppStateMutations.WithStatusLineText(state, "ready");
        state = ClawSharpAppStateMutations.WithMainLoopModel(state, "opus");
        state = ClawSharpAppStateMutations.WithToolPermissionMode(state, PermissionMode.Plan);
        state = state with
        {
            ActiveSessionId = "session-123",
            ActiveSessionTitle = "Incident Review"
        };

        var rendered = _renderer.Render(state);

        Assert.Equal(
            [
                "ready",
                "plan mode on · session Incident Review · model Opus"
            ],
            rendered);
    }

    [Fact]
    public void Render_Includes_Background_Task_Summary_Before_Session_Metadata()
    {
        var state = ClawSharpAppState.CreateDefault(
            workspaceRoot: "d:\\workspace",
            environment: new StartupEnvironment("d:\\config", BareMode: false, DisablePolicySkills: false),
            settings: new ClawSharpSettings
            {
                Runtime = new RuntimeSettings
                {
                    Model = "sonnet",
                    PermissionMode = PermissionMode.Default
                }
            },
            settingsIssues: [],
            pluginInstallations: [],
            plugins: [],
            skills: [],
            agentDefinitions: [],
            hooks: []);

        state = ClawSharpAppStateMutations.WithTasks(
            state,
            new Dictionary<string, ClawSharpTask>(StringComparer.Ordinal)
            {
                ["task-1"] = new LocalBashTask(
                    "task-1",
                    "shell",
                    ClawSharp.Tasks.TaskStatus.Running,
                    DateTimeOffset.UtcNow,
                    "shell.log",
                    "echo hi"),
                ["task-2"] = new LocalBashTask(
                    "task-2",
                    "monitor",
                    ClawSharp.Tasks.TaskStatus.Running,
                    DateTimeOffset.UtcNow,
                    "monitor.log",
                    "tail",
                    Kind: BashTaskKind.Monitor)
            });
        state = state with { ActiveSessionId = "session-123" };

        var rendered = _renderer.Render(state);

        Assert.Equal(["1 shell, 1 monitor · session session-123 · model Sonnet"], rendered);
    }

    [Fact]
    public void Render_Hides_Default_Mode_Indicator()
    {
        var state = ClawSharpAppState.CreateDefault(
            workspaceRoot: "d:\\workspace",
            environment: new StartupEnvironment("d:\\config", BareMode: false, DisablePolicySkills: false),
            settings: new ClawSharpSettings
            {
                Runtime = new RuntimeSettings
                {
                    Model = "sonnet",
                    PermissionMode = PermissionMode.Default
                }
            },
            settingsIssues: [],
            pluginInstallations: [],
            plugins: [],
            skills: [],
            agentDefinitions: [],
            hooks: []);

        state = state with { ActiveSessionId = "session-123" };

        var rendered = _renderer.Render(state);

        Assert.Equal(["session session-123 · model Sonnet"], rendered);
    }

    [Fact]
    public void Render_Replaces_Placeholder_Model_With_Ts_Default_Fallback()
    {
        var state = ClawSharpAppState.CreateDefault(
            workspaceRoot: "d:\\workspace",
            environment: new StartupEnvironment("d:\\config", BareMode: false, DisablePolicySkills: false),
            settings: new ClawSharpSettings(),
            settingsIssues: [],
            pluginInstallations: [],
            plugins: [],
            skills: [],
            agentDefinitions: [],
            hooks: []);

        state = state with { ActiveSessionId = "session-123" };

        var rendered = _renderer.Render(state);

        Assert.Equal(["session session-123 · model Haiku 4.5"], rendered);
    }
}
