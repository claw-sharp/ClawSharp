// TS origin: ./state/store.ts, ./state/selectors.ts, ./state/onChangeAppState.ts
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Tasks;

namespace ClawSharp.UnitTests;

public sealed class AppStateStoreTests
{
    [Fact]
    public void SetState_Notifies_Listeners_And_OnChange_When_State_Instance_Changes()
    {
        var initialState = CreateState();
        var observedChanges = new List<(ClawSharpAppState OldState, ClawSharpAppState NewState)>();
        var notificationCount = 0;
        var store = new ClawSharpAppStateStore(
            initialState,
            (newState, oldState) => observedChanges.Add((oldState, newState)));
        using var subscription = store.Subscribe(() => notificationCount++);

        store.SetState(state => ClawSharpAppStateMutations.WithStatusLineText(state, "busy"));

        Assert.Equal(1, notificationCount);
        var change = Assert.Single(observedChanges);
        Assert.Same(initialState, change.OldState);
        Assert.Equal("busy", change.NewState.StatusLineText);
    }

    [Fact]
    public void SetState_Skips_Notifications_When_Updater_Returns_Same_Instance()
    {
        var initialState = CreateState();
        var notificationCount = 0;
        var onChangeCount = 0;
        var store = new ClawSharpAppStateStore(initialState, (_, _) => onChangeCount++);
        using var subscription = store.Subscribe(() => notificationCount++);

        store.SetState(state => state);

        Assert.Equal(0, notificationCount);
        Assert.Equal(0, onChangeCount);
        Assert.Same(initialState, store.GetState());
    }

    [Fact]
    public void Selectors_Return_Running_Tasks_And_Active_Session_Display_Name()
    {
        var state = CreateState() with
        {
            ActiveSessionId = "session-1",
            ActiveSessionTitle = "Daily Sync",
            Tasks = new Dictionary<string, ClawSharpTask>(StringComparer.Ordinal)
            {
                ["task-running"] = new ClawSharpTask(
                    "task-running",
                    TaskType.LocalBash,
                    "run",
                    ClawSharp.Tasks.TaskStatus.Running,
                    DateTimeOffset.UtcNow,
                    "output.txt"),
                ["task-done"] = new ClawSharpTask(
                    "task-done",
                    TaskType.LocalBash,
                    "done",
                    ClawSharp.Tasks.TaskStatus.Completed,
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    "output.txt")
            }
        };

        var runningTask = Assert.Single(ClawSharpAppStateSelectors.GetRunningTasks(state));
        Assert.Equal("task-running", runningTask.Id);
        Assert.Equal("Daily Sync", ClawSharpAppStateSelectors.GetActiveSessionDisplayName(state));
        Assert.Equal(runningTask, ClawSharpAppStateSelectors.GetTask(state, "task-running"));
    }

    [Fact]
    public void TaskRegistry_Synchronizes_Tasks_Into_Central_App_State_Store()
    {
        var store = new ClawSharpAppStateStore(CreateState());
        var registry = new TaskRegistry(appStateStore: store);

        var task = registry.Create("background work");

        Assert.True(store.GetState().Tasks.TryGetValue(task.Id, out var syncedTask));
        Assert.Equal(task, syncedTask);
    }

    private static ClawSharpAppState CreateState()
    {
        return ClawSharpAppState.CreateDefault(
            Environment.CurrentDirectory,
            StartupEnvironment.Capture(),
            new ClawSharpSettings(),
            [],
            [],
            [],
            [],
            [],
            []);
    }
}
