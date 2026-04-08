using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools.Plan;

internal sealed class EnterPlanModeTool : BaseTool
{
    public EnterPlanModeTool()
        : base(
            new ToolDescriptor(
                "EnterPlanMode",
                "Requests permission to enter plan mode for complex tasks requiring exploration and design",
                Parameters: [],
                InputSchema: PlanToolSchemas.EnterInputSchema,
                OutputSchema: PlanToolSchemas.EnterOutputSchema,
                SearchHint: "switch to plan mode to design an approach before coding",
                ShouldDefer: true,
                Strict: true))
    {
    }

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (context.AgentId != null)
        {
            return Task.FromResult(Failure("EnterPlanMode tool cannot be used in agent contexts"));
        }

        context.AppStateStore.SetState(prev =>
        {
            var updatedContext = prev.ToolPermissionContext with
            {
                Mode = PermissionMode.Plan,
                PrePlanMode = prev.ToolPermissionContext.Mode
            };
            return prev with { ToolPermissionContext = updatedContext };
        });

        var message = "Entered plan mode. You should now focus on exploring the codebase and designing an implementation approach.\n\n" +
                      "In plan mode, you should:\n" +
                      "1. Thoroughly explore the codebase to understand existing patterns\n" +
                      "2. Identify similar features and architectural approaches\n" +
                      "3. Consider multiple approaches and their trade-offs\n" +
                      "4. Use AskUserQuestion if you need to clarify the approach\n" +
                      "5. Design a concrete implementation strategy\n" +
                      "6. When ready, use ExitPlanMode to present your plan for approval\n\n" +
                      "Remember: DO NOT write or edit any files yet. This is a read-only exploration and planning phase.";

        return Task.FromResult(Success(message, new JsonObject { ["message"] = message }));
    }
}

internal sealed class ExitPlanModeTool : BaseTool
{
    public ExitPlanModeTool()
        : base(
            new ToolDescriptor(
                "ExitPlanMode",
                "Exit plan mode and present a proposed plan for user approval",
                Parameters:
                [
                    new ToolParameter("plan", "The proposed plan or summary of work to be done")
                ],
                InputSchema: PlanToolSchemas.ExitInputSchema,
                OutputSchema: PlanToolSchemas.ExitOutputSchema,
                SearchHint: "submit your plan for approval and exit plan mode",
                ShouldDefer: true,
                Strict: true))
    {
    }

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var input = JsonNode.Parse(context.Arguments);
        var plan = input?["plan"]?.GetValue<string>() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(plan))
        {
            return Task.FromResult(Failure("A plan is required to exit plan mode."));
        }

        context.AppStateStore.SetState(prev =>
        {
            var targetMode = prev.ToolPermissionContext.PrePlanMode ?? PermissionMode.Default;
            var updatedContext = prev.ToolPermissionContext with
            {
                Mode = targetMode,
                PrePlanMode = null
            };
            return prev with { ToolPermissionContext = updatedContext };
        });

        // In a real implementation, this might trigger a specific UI approval workflow.
        // For now, we follow the logic of confirming the exit.

        var message = "Plan submitted successfully. Exiting plan mode and returning to previous session mode.";
        return Task.FromResult(Success(message, new JsonObject { ["message"] = message }));
    }
}
