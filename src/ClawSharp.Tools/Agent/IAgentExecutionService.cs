using ClawSharp.Core;

namespace ClawSharp.Tools;

public interface IAgentExecutionService
{
    Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionContext context,
        AgentDefinition selectedAgent,
        AgentExecutionRequest request,
        CancellationToken cancellationToken = default);
}
