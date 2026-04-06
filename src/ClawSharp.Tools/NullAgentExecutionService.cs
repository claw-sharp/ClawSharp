using ClawSharp.Core;

namespace ClawSharp.Tools;

public sealed class NullAgentExecutionService : IAgentExecutionService
{
    public Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionContext context,
        AgentDefinition selectedAgent,
        AgentExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            new ToolExecutionResult(
                false,
                "Agent execution is not available in this ClawSharp runtime. The local agent query pipeline has not been wired into this ToolRegistry instance."));
    }
}
