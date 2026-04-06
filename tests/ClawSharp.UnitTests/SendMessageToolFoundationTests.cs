using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Tasks;
using ClawSharp.Tools;
using TaskStatus = ClawSharp.Tasks.TaskStatus;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class SendMessageToolFoundationTests
{
    [Fact]
    public async Task SendMessageTool_Requires_Summary_For_String_Messages()
    {
        var appStateStore = CreateAppStateStore();
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry(), appStateStore: appStateStore);
        var session = new DefaultSessionFactory(Environment.CurrentDirectory).Create();

        var result = await registry.ExecuteAsync(
            "SendMessage",
            """{"to":"worker-1","message":"continue"}""",
            session,
            new ClawSharpSettings());

        Assert.False(result.Success);
        Assert.Contains("'summary' is required", result.Output);
    }

    [Fact]
    public async Task SendMessageTool_Resolves_Running_Local_Agent_By_Registered_Name_And_Stops_At_Runtime_Boundary()
    {
        var tempDir = CreateTempDirectory();
        var appStateStore = CreateAppStateStore(tempDir);
        var tasks = new TaskRegistry(tempDir, appStateStore: appStateStore);
        var registry = new ToolRegistry(tempDir, tasks, appStateStore: appStateStore);
        var session = new DefaultSessionFactory(tempDir).Create();
        var agentTask = await tasks.CreateLocalAgentForSessionAsync(
            session.Id,
            "worker task",
            "do work",
            "general-purpose",
            TaskStatus.Running);

        appStateStore.SetState(state => ClawSharpAppStateMutations.WithAgentNameRegistration(state, "worker-1", agentTask.Id));

        var result = await registry.ExecuteAsync(
            "SendMessage",
            """{"to":"worker-1","summary":"continue work","message":"continue"}""",
            session,
            new ClawSharpSettings());

        Assert.False(result.Success);
        Assert.Contains("running agents", result.Output);
        Assert.Contains("next tool round", result.Output);
        Assert.False(result.StructuredOutput?["success"]?.GetValue<bool>() ?? true);
    }

    [Fact]
    public async Task SendMessageTool_Resolves_Stopped_Local_Agent_By_Task_Id_And_Stops_At_Resume_Boundary()
    {
        var tempDir = CreateTempDirectory();
        var appStateStore = CreateAppStateStore(tempDir);
        var tasks = new TaskRegistry(tempDir, appStateStore: appStateStore);
        var registry = new ToolRegistry(tempDir, tasks, appStateStore: appStateStore);
        var session = new DefaultSessionFactory(tempDir).Create();
        var agentTask = await tasks.CreateLocalAgentForSessionAsync(
            session.Id,
            "worker task",
            "do work",
            "general-purpose",
            TaskStatus.Failed);
        var persistence = new AgentPersistenceService(new JsonlTranscriptStore());
        await persistence.RecordSidechainTranscriptAsync(
            session,
            agentTask.Id,
            [ChatMessageFactory.CreateText(MessageRole.Assistant, "prior output")]);
        await persistence.WriteAgentMetadataAsync(
            session,
            agentTask.Id,
            new AgentMetadata("Explore", Description: "worker task"));

        var result = await registry.ExecuteAsync(
            "SendMessage",
            $$"""{"to":"{{agentTask.Id}}","summary":"resume work","message":"continue"}""",
            session,
            new ClawSharpSettings());

        Assert.False(result.Success);
        Assert.Contains("resumable transcript metadata", result.Output);
        Assert.Contains("Explore", result.Output);
        Assert.Contains("resume execution is not implemented", result.Output);
    }

    [Fact]
    public async Task SendMessageTool_Returns_NoTranscript_Message_For_Missing_Stopped_Agent_Transcript()
    {
        var tempDir = CreateTempDirectory();
        var appStateStore = CreateAppStateStore(tempDir);
        var tasks = new TaskRegistry(tempDir, appStateStore: appStateStore);
        var registry = new ToolRegistry(tempDir, tasks, appStateStore: appStateStore);
        var session = new DefaultSessionFactory(tempDir).Create();
        var agentTask = await tasks.CreateLocalAgentForSessionAsync(
            session.Id,
            "worker task",
            "do work",
            "general-purpose",
            TaskStatus.Killed);

        var result = await registry.ExecuteAsync(
            "SendMessage",
            $$"""{"to":"{{agentTask.Id}}","summary":"resume work","message":"continue"}""",
            session,
            new ClawSharpSettings());

        Assert.False(result.Success);
        Assert.Contains("has no transcript to resume", result.Output);
    }

    [Fact]
    public async Task SendMessageTool_Resolves_InProcessTeammate_By_Registered_Name_And_Stops_At_Mailbox_Boundary()
    {
        var tempDir = CreateTempDirectory();
        var appStateStore = CreateAppStateStore(tempDir);
        var tasks = new TaskRegistry(tempDir, appStateStore: appStateStore);
        var registry = new ToolRegistry(tempDir, tasks, appStateStore: appStateStore);
        var session = new DefaultSessionFactory(tempDir).Create();
        var teammate = await tasks.CreateInProcessTeammateForSessionAsync(
            session.Id,
            "researcher: inspect repo",
            new TeammateIdentity("researcher@alpha", "researcher", "alpha", PlanModeRequired: false, ParentSessionId: session.Id),
            "inspect repo");

        appStateStore.SetState(state => ClawSharpAppStateMutations.WithAgentNameRegistration(state, "researcher", teammate.Identity.AgentId));

        var result = await registry.ExecuteAsync(
            "SendMessage",
            """{"to":"researcher","summary":"continue work","message":"continue"}""",
            session,
            new ClawSharpSettings());

        Assert.False(result.Success);
        Assert.Contains("teammate mailbox delivery", result.Output);
        Assert.Contains(teammate.Identity.AgentId, result.Output);
    }

    [Fact]
    public async Task SendMessageTool_Resolves_Shutdown_Request_For_InProcessTeammate_And_Stops_At_Mailbox_Boundary()
    {
        var tempDir = CreateTempDirectory();
        var appStateStore = CreateAppStateStore(tempDir);
        var tasks = new TaskRegistry(tempDir, appStateStore: appStateStore);
        var registry = new ToolRegistry(tempDir, tasks, appStateStore: appStateStore);
        var session = new DefaultSessionFactory(tempDir).Create();
        var teammate = await tasks.CreateInProcessTeammateForSessionAsync(
            session.Id,
            "researcher: inspect repo",
            new TeammateIdentity("researcher@alpha", "researcher", "alpha", PlanModeRequired: false, ParentSessionId: session.Id),
            "inspect repo");
        appStateStore.SetState(state => ClawSharpAppStateMutations.WithAgentNameRegistration(state, "researcher", teammate.Identity.AgentId));

        var result = await registry.ExecuteAsync(
            "SendMessage",
            """{"to":"researcher","message":{"type":"shutdown_request","reason":"done"}}""",
            session,
            new ClawSharpSettings());

        Assert.False(result.Success);
        Assert.Contains("shutdown_request", result.Output);
        Assert.Contains(teammate.Identity.AgentId, result.Output);
    }

    [Fact]
    public async Task SendMessageTool_Resolves_Plan_Approval_Response_For_InProcessTeammate_And_Stops_At_Mailbox_Boundary()
    {
        var tempDir = CreateTempDirectory();
        var appStateStore = CreateAppStateStore(tempDir);
        var tasks = new TaskRegistry(tempDir, appStateStore: appStateStore);
        var registry = new ToolRegistry(tempDir, tasks, appStateStore: appStateStore);
        var session = new DefaultSessionFactory(tempDir).Create();
        var teammate = await tasks.CreateInProcessTeammateForSessionAsync(
            session.Id,
            "planner: make plan",
            new TeammateIdentity("planner@alpha", "planner", "alpha", PlanModeRequired: true, ParentSessionId: session.Id),
            "make plan");
        appStateStore.SetState(state => ClawSharpAppStateMutations.WithAgentNameRegistration(state, "planner", teammate.Identity.AgentId));

        var result = await registry.ExecuteAsync(
            "SendMessage",
            """{"to":"planner","message":{"type":"plan_approval_response","request_id":"req-1","approve":false,"feedback":"add rollback plan"}}""",
            session,
            new ClawSharpSettings());

        Assert.False(result.Success);
        Assert.Contains("plan_approval_response", result.Output);
        Assert.Contains(teammate.Identity.AgentId, result.Output);
    }

    [Fact]
    public async Task SendMessageTool_Resolves_Shutdown_Response_For_Team_Lead_And_Stops_At_Mailbox_Boundary()
    {
        var appStateStore = CreateAppStateStore();
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry(), appStateStore: appStateStore);
        var session = new DefaultSessionFactory(Environment.CurrentDirectory).Create();

        var result = await registry.ExecuteAsync(
            "SendMessage",
            """{"to":"team-lead","message":{"type":"shutdown_response","request_id":"req-1","approve":true}}""",
            session,
            new ClawSharpSettings());

        Assert.False(result.Success);
        Assert.Contains("shutdown_response approval", result.Output);
        Assert.Contains("team-lead mailbox delivery", result.Output);
    }

    private static ClawSharpAppStateStore CreateAppStateStore(string? workspaceRoot = null)
    {
        var root = workspaceRoot ?? Environment.CurrentDirectory;
        return new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                root,
                StartupEnvironment.Capture(),
                new ClawSharpSettings(),
                [],
                [],
                [],
                [],
                [],
                []));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-send-message-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
