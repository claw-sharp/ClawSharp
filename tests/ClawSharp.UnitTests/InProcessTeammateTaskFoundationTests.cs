// TS origin: ./tasks/InProcessTeammateTask/types.ts, ./tasks/InProcessTeammateTask/InProcessTeammateTask.tsx, ./utils/swarm/spawnInProcess.ts
using ClawSharp.Core;
using ClawSharp.Tasks;
using TaskStatus = ClawSharp.Tasks.TaskStatus;

namespace ClawSharp.UnitTests;

public sealed class InProcessTeammateTaskFoundationTests
{
    [Fact]
    public async Task TaskRegistry_Creates_InProcessTeammate_Task_State()
    {
        var tempDir = CreateTempDirectory();
        var session = new DefaultSessionFactory(tempDir).Create();
        var registry = new TaskRegistry(tempDir);

        var task = await registry.CreateInProcessTeammateForSessionAsync(
            session.Id,
            "researcher: inspect repo",
            new TeammateIdentity("researcher@alpha", "researcher", "alpha", PlanModeRequired: true, ParentSessionId: session.Id),
            "inspect repo");

        Assert.Equal(TaskType.InProcessTeammate, task.Type);
        Assert.Equal(PermissionMode.Plan, task.PermissionMode);
        Assert.Empty(task.PendingUserMessages!);
        Assert.Empty(task.Messages!);
        Assert.False(task.ShutdownRequested);
    }

    [Fact]
    public async Task TaskRegistry_Appends_And_Caps_InProcessTeammate_Messages()
    {
        var tempDir = CreateTempDirectory();
        var session = new DefaultSessionFactory(tempDir).Create();
        var registry = new TaskRegistry(tempDir);
        var task = await registry.CreateInProcessTeammateForSessionAsync(
            session.Id,
            "researcher: inspect repo",
            new TeammateIdentity("researcher@alpha", "researcher", "alpha", PlanModeRequired: false, ParentSessionId: session.Id),
            "inspect repo");

        for (var index = 0; index < InProcessTeammateTasks.MessagesUiCap + 5; index++)
        {
            Assert.True(registry.TryAppendInProcessTeammateMessage(
                task.Id,
                ChatMessageFactory.CreateText(MessageRole.Assistant, $"message-{index}")));
        }

        Assert.True(registry.TryGet(task.Id, out var updated));
        var teammateTask = Assert.IsType<InProcessTeammateTask>(updated);
        Assert.Equal(InProcessTeammateTasks.MessagesUiCap, teammateTask.Messages!.Count);
        Assert.Equal("message-5", teammateTask.Messages[0].Content);
        Assert.Equal($"message-{InProcessTeammateTasks.MessagesUiCap + 4}", teammateTask.Messages[^1].Content);
    }

    [Fact]
    public async Task TaskRegistry_Injects_User_Message_Into_Running_Or_Idle_InProcessTeammate()
    {
        var tempDir = CreateTempDirectory();
        var session = new DefaultSessionFactory(tempDir).Create();
        var registry = new TaskRegistry(tempDir);
        var task = await registry.CreateInProcessTeammateForSessionAsync(
            session.Id,
            "researcher: inspect repo",
            new TeammateIdentity("researcher@alpha", "researcher", "alpha", PlanModeRequired: false, ParentSessionId: session.Id),
            "inspect repo");

        Assert.True(registry.TryUpdate(task.Id, current => ((InProcessTeammateTask)current) with { IsIdle = true }));
        Assert.True(registry.TryInjectUserMessageToInProcessTeammate(task.Id, "continue searching"));
        Assert.True(registry.TryGet(task.Id, out var updated));
        var teammateTask = Assert.IsType<InProcessTeammateTask>(updated);
        Assert.Equal(["continue searching"], teammateTask.PendingUserMessages);
        var injected = Assert.Single(teammateTask.Messages!);
        Assert.Equal(MessageRole.User, injected.Role);
        Assert.Equal("continue searching", injected.Content);
    }

    [Fact]
    public async Task InProcessTeammateTasks_FindByAgentId_Prefers_Running_Task()
    {
        var tempDir = CreateTempDirectory();
        var session = new DefaultSessionFactory(tempDir).Create();
        var registry = new TaskRegistry(tempDir);

        var stopped = await registry.CreateInProcessTeammateForSessionAsync(
            session.Id,
            "researcher old",
            new TeammateIdentity("researcher@alpha", "researcher", "alpha", PlanModeRequired: false, ParentSessionId: session.Id),
            "inspect repo",
            TaskStatus.Killed);
        var running = await registry.CreateInProcessTeammateForSessionAsync(
            session.Id,
            "researcher current",
            new TeammateIdentity("researcher@alpha", "researcher", "alpha", PlanModeRequired: false, ParentSessionId: session.Id),
            "inspect repo",
            TaskStatus.Running);

        var found = InProcessTeammateTasks.FindByAgentId("researcher@alpha", registry.GetAppState().Tasks);

        Assert.NotNull(found);
        Assert.Equal(running.Id, found!.Id);
        Assert.NotEqual(stopped.Id, found.Id);
    }

    [Fact]
    public async Task InProcessTeammateTasks_GetRunningSorted_Orders_By_Agent_Name()
    {
        var tempDir = CreateTempDirectory();
        var session = new DefaultSessionFactory(tempDir).Create();
        var registry = new TaskRegistry(tempDir);

        await registry.CreateInProcessTeammateForSessionAsync(
            session.Id,
            "zebra: inspect repo",
            new TeammateIdentity("zebra@alpha", "zebra", "alpha", PlanModeRequired: false, ParentSessionId: session.Id),
            "inspect repo");
        await registry.CreateInProcessTeammateForSessionAsync(
            session.Id,
            "alpha: inspect repo",
            new TeammateIdentity("alpha@alpha", "alpha", "alpha", PlanModeRequired: false, ParentSessionId: session.Id),
            "inspect repo");
        await registry.CreateInProcessTeammateForSessionAsync(
            session.Id,
            "middle: inspect repo",
            new TeammateIdentity("middle@alpha", "middle", "alpha", PlanModeRequired: false, ParentSessionId: session.Id),
            "inspect repo",
            TaskStatus.Killed);

        var running = InProcessTeammateTasks.GetRunningSorted(registry.GetAppState().Tasks);

        Assert.Equal(["alpha", "zebra"], running.Select(static task => task.Identity.AgentName).ToArray());
    }

    [Fact]
    public async Task TaskRegistry_Requests_InProcessTeammate_Shutdown_Once()
    {
        var tempDir = CreateTempDirectory();
        var session = new DefaultSessionFactory(tempDir).Create();
        var registry = new TaskRegistry(tempDir);
        var task = await registry.CreateInProcessTeammateForSessionAsync(
            session.Id,
            "researcher: inspect repo",
            new TeammateIdentity("researcher@alpha", "researcher", "alpha", PlanModeRequired: false, ParentSessionId: session.Id),
            "inspect repo");

        Assert.True(registry.TryRequestInProcessTeammateShutdown(task.Id));
        Assert.False(registry.TryRequestInProcessTeammateShutdown(task.Id));
        Assert.True(registry.TryGet(task.Id, out var updated));
        Assert.True(Assert.IsType<InProcessTeammateTask>(updated).ShutdownRequested);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-in-process-teammate-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
