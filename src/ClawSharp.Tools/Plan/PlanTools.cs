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
            return prev with
            {
                ToolPermissionContext = updatedContext,
                PendingPlanVerification = null
            };
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
            return prev with
            {
                ToolPermissionContext = updatedContext,
                PendingPlanVerification = new PendingPlanVerification(
                    plan,
                    RequestedAt: DateTimeOffset.UtcNow)
            };
        });

        var message = "Plan submitted successfully. Exiting plan mode and marking the plan as pending verification.";
        return Task.FromResult(Success(message, new JsonObject { ["message"] = message }));
    }
}

internal sealed class VerifyPlanExecutionTool : BaseTool
{
    public VerifyPlanExecutionTool()
        : base(
            new ToolDescriptor(
                "VerifyPlanExecution",
                "Mark the most recently approved plan as under verification and record local completion state",
                InputSchema: PlanToolSchemas.VerifyInputSchema,
                OutputSchema: PlanToolSchemas.VerifyOutputSchema,
                SearchHint: "verify that a previously approved implementation plan was completed",
                ShouldDefer: true,
                Strict: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments)
    {
        return true;
    }

    public override bool IsReadOnly(string arguments)
    {
        return false;
    }

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        PendingPlanVerification? updatedVerification = null;

        context.AppStateStore.SetState(prev =>
        {
            if (prev.PendingPlanVerification is null)
            {
                return prev;
            }

            var startedAt = prev.PendingPlanVerification.StartedAt ?? DateTimeOffset.UtcNow;
            var completedAt = DateTimeOffset.UtcNow;
            updatedVerification = prev.PendingPlanVerification with
            {
                VerificationStarted = true,
                VerificationCompleted = true,
                StartedAt = startedAt,
                CompletedAt = completedAt
            };

            return prev with { PendingPlanVerification = updatedVerification };
        });

        if (updatedVerification is null)
        {
            return Task.FromResult(Failure("No pending plan verification is recorded for this session."));
        }

        var message = "Plan verification recorded for the current session.";
        return Task.FromResult(Success(
            message,
            new JsonObject
            {
                ["message"] = message,
                ["plan"] = updatedVerification.Plan,
                ["verificationStarted"] = updatedVerification.VerificationStarted,
                ["verificationCompleted"] = updatedVerification.VerificationCompleted
            }));
    }
}
