using ClawSharp.AgentHost.Approvals;
using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

public sealed class AgentHostApprovalPermissionPrompterTests
{
    [Fact]
    public async Task PromptAsync_QueuesApprovalEvent_AndReturnsAllowWhenApproved()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "clawsharp-agenthost-approval-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var store = new JsonApprovalRequestStore(Path.Combine(tempRoot, "approval-requests.json"));
        var dispatcher = new AgentHostEventDispatcher();
        AgentHostEventEnvelope? publishedEvent = null;
        dispatcher.SetPublisher((agentEvent, _) =>
        {
            publishedEvent = agentEvent;
            return Task.CompletedTask;
        });

        var prompter = new AgentHostApprovalPermissionPrompter(dispatcher, store);
        var promptTask = prompter.PromptAsync("Run this shell command?");

        var approval = await WaitForApprovalAsync(store);
        store.Save([approval with { Decision = ApprovalDecision.Approved }]);

        var decision = await promptTask;

        Assert.Equal(PromptPermissionDecision.Allow, decision);
        Assert.NotNull(publishedEvent);
        Assert.Equal("ApprovalRequested", publishedEvent!.Event);
        var payload = Assert.IsType<ApprovalRequestDto>(publishedEvent.Payload);
        Assert.Equal(approval.Id, payload.Id);
        Assert.Equal("Run this shell command?", payload.Action);
    }

    [Fact]
    public async Task PromptAsync_ReturnsDenyWhenRejected()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "clawsharp-agenthost-approval-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var store = new JsonApprovalRequestStore(Path.Combine(tempRoot, "approval-requests.json"));
        var dispatcher = new AgentHostEventDispatcher();
        dispatcher.SetPublisher((_, _) => Task.CompletedTask);

        var prompter = new AgentHostApprovalPermissionPrompter(dispatcher, store);
        var promptTask = prompter.PromptAsync("Open this file?");

        var approval = await WaitForApprovalAsync(store);
        store.Save([approval with { Decision = ApprovalDecision.Rejected }]);

        var decision = await promptTask;

        Assert.Equal(PromptPermissionDecision.Deny, decision);
    }

    private static async Task<ApprovalRequest> WaitForApprovalAsync(JsonApprovalRequestStore store)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            var approval = (await store.LoadAsync(timeout.Token)).SingleOrDefault();
            if (approval is not null)
            {
                return approval;
            }

            await Task.Delay(50, timeout.Token);
        }

        throw new TimeoutException("Timed out waiting for approval request.");
    }
}
