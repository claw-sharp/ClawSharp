// TS origin: ./tools/SendMessageTool/SendMessageTool.ts, ./tools/SendMessageTool/constants.ts
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClawSharp.Core;
using ClawSharp.Tasks;

namespace ClawSharp.Tools;

internal sealed class SendMessageTool : BaseTool
{
    private const string TeamLeadName = "team-lead";
    private readonly AgentResumePreflightService _resumePreflightService;

    public SendMessageTool()
        : this(new AgentResumePreflightService())
    {
    }

    internal SendMessageTool(AgentResumePreflightService resumePreflightService)
        : base(
            new ToolDescriptor(
                "SendMessage",
                "Send messages to agent teammates",
                SearchHint: "send messages to agent teammates (swarm protocol)",
                ShouldDefer: true,
                InputSchema: SendMessageToolSchemas.InputSchema,
                OutputSchema: SendMessageToolSchemas.OutputSchema,
                Strict: true))
    {
        _resumePreflightService = resumePreflightService;
    }

    public override bool IsReadOnly(string arguments)
    {
        return TryParseArguments(arguments, out var request, out _) &&
               request is not null &&
               request.Message is TextMessagePayload;
    }

    public override Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var request, out var errorMessage) || request is null)
        {
            return Task.FromResult(ToolValidationResult.Invalid(errorMessage ?? "SendMessage arguments must be a JSON object."));
        }

        if (string.IsNullOrWhiteSpace(request.To))
        {
            return Task.FromResult(ToolValidationResult.Invalid("'to' must not be empty."));
        }

        if (request.To.Contains('@', StringComparison.Ordinal))
        {
            return Task.FromResult(ToolValidationResult.Invalid("to must be a bare teammate name or \"*\"."));
        }

        if (request.Message is TextMessagePayload && string.IsNullOrWhiteSpace(request.Summary))
        {
            return Task.FromResult(ToolValidationResult.Invalid("'summary' is required when message is a string."));
        }

        if (request.To == "*" && request.Message is not TextMessagePayload)
        {
            return Task.FromResult(ToolValidationResult.Invalid("structured messages cannot be broadcast (to: \"*\")."));
        }

        if (request.Message is ShutdownResponseMessagePayload shutdownResponse &&
            !shutdownResponse.Approve &&
            string.IsNullOrWhiteSpace(shutdownResponse.Reason))
        {
            return Task.FromResult(ToolValidationResult.Invalid("'reason' is required when rejecting a shutdown request."));
        }

        if (request.Message is ShutdownResponseMessagePayload && !string.Equals(request.To, TeamLeadName, StringComparison.Ordinal))
        {
            return Task.FromResult(ToolValidationResult.Invalid($"shutdown_response must be sent to \"{TeamLeadName}\"."));
        }

        return Task.FromResult(ToolValidationResult.Valid());
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var request, out var errorMessage) || request is null)
        {
            return Failure(errorMessage ?? "SendMessage arguments must be a JSON object.");
        }

        if (request.Message is TextMessagePayload && request.To != "*")
        {
            var resolvedTarget = ResolveAgentTarget(context.AppState, request.To!);
            if (resolvedTarget.TeammateTask is not null)
            {
                var teammateStatus = resolvedTarget.TeammateTask.Status.ToString().ToLowerInvariant();
                return Failure(
                    $"SendMessage resolved \"{request.To}\" to teammate \"{resolvedTarget.TeammateTask.Identity.AgentId}\" ({teammateStatus}), but teammate mailbox delivery is not implemented in ClawSharp yet. The TypeScript path queues this message for mailbox or pendingUserMessages delivery.",
                    CreateStructuredFailureOutput("Teammate mailbox delivery is not implemented in ClawSharp yet."));
            }

            if (resolvedTarget.Task is not null && resolvedTarget.Task.Status == ClawSharp.Tasks.TaskStatus.Running)
            {
                return Failure(
                    "SendMessage delivery to running agents is not implemented in ClawSharp yet. The TypeScript path queues the message for the next tool round and depends on pending running-agent mailbox delivery that ClawSharp has not rebuilt yet.",
                    CreateStructuredFailureOutput("SendMessage delivery to running agents is not implemented in ClawSharp yet."));
            }

            if (resolvedTarget.AgentId is not null)
            {
                try
                {
                    var resumeState = await _resumePreflightService.PrepareAsync(
                        context.Session,
                        context.AgentDefinitions,
                        resolvedTarget.AgentId,
                        cancellationToken);
                    var stoppedStatus = resolvedTarget.Task?.Status.ToString().ToLowerInvariant() ?? "stopped";
                    var worktreeNote = resumeState.MissingWorktreePath
                        ? " Stored worktree path no longer exists; the TypeScript runtime would fall back to the parent cwd."
                        : string.Empty;

                    return Failure(
                        $"Agent \"{request.To}\" was stopped ({stoppedStatus}); ClawSharp found resumable transcript metadata for agent type \"{resumeState.SelectedAgent.AgentType}\" with output file {resumeState.OutputFile}, but cannot resume it yet because stopped-agent resume execution is not implemented.{worktreeNote}",
                        CreateStructuredFailureOutput($"Agent \"{request.To}\" cannot be resumed yet because stopped-agent resume execution is not implemented."));
                }
                catch (InvalidOperationException) when (resolvedTarget.ResolvedFromName || LooksLikeLocalAgentId(resolvedTarget.AgentId))
                {
                    return Failure(
                        $"Agent \"{request.To}\" is registered but has no transcript to resume. It may have been cleaned up.",
                        CreateStructuredFailureOutput($"Agent \"{request.To}\" has no transcript to resume."));
                }
            }
        }

        if (request.Message is not TextMessagePayload && request.To != "*")
        {
            var resolvedTarget = ResolveAgentTarget(context.AppState, request.To!);

            switch (request.Message)
            {
                case ShutdownRequestMessagePayload:
                    if (resolvedTarget.TeammateTask is not null)
                    {
                        return Failure(
                            $"SendMessage resolved shutdown_request for teammate \"{resolvedTarget.TeammateTask.Identity.AgentId}\", but teammate mailbox delivery is not implemented in ClawSharp yet. The TypeScript path writes a shutdown mailbox message to the target teammate.",
                            CreateStructuredFailureOutput("Teammate shutdown-request delivery is not implemented in ClawSharp yet."));
                    }

                    break;

                case PlanApprovalResponseMessagePayload planApproval:
                    if (resolvedTarget.TeammateTask is not null)
                    {
                        var approvalAction = planApproval.Approve ? "approval" : "rejection";
                        return Failure(
                            $"SendMessage resolved plan_approval_response for teammate \"{resolvedTarget.TeammateTask.Identity.AgentId}\" ({approvalAction}), but teammate mailbox delivery is not implemented in ClawSharp yet. The TypeScript path writes the plan response into the teammate mailbox.",
                            CreateStructuredFailureOutput("Teammate plan-approval delivery is not implemented in ClawSharp yet."));
                    }

                    break;

                case ShutdownResponseMessagePayload shutdownResponse
                    when string.Equals(request.To, TeamLeadName, StringComparison.Ordinal):
                    var responseAction = shutdownResponse.Approve ? "approval" : "rejection";
                    return Failure(
                        $"SendMessage resolved shutdown_response {responseAction} for \"{TeamLeadName}\", but team-lead mailbox delivery is not implemented in ClawSharp yet. The TypeScript path writes this response to the team-lead mailbox and may abort the teammate when approval is granted.",
                        CreateStructuredFailureOutput("Team-lead shutdown-response delivery is not implemented in ClawSharp yet."));
            }
        }

        return Failure(
            "Teammate mailbox and multi-agent coordination are not implemented in ClawSharp yet. The TypeScript SendMessage path depends on the missing swarm runtime and mailbox delivery.",
            CreateStructuredFailureOutput("Teammate mailbox and multi-agent coordination are not implemented in ClawSharp yet."));
    }

    private static ResolvedAgentTarget ResolveAgentTarget(ClawSharpAppState appState, string target)
    {
        if (appState.AgentNameRegistry.TryGetValue(target, out var mappedTaskId))
        {
            var mappedTeammate = InProcessTeammateTasks.FindByAgentId(mappedTaskId, appState.Tasks);
            if (mappedTeammate is not null)
            {
                return new ResolvedAgentTarget(
                    mappedTaskId,
                    null,
                    mappedTeammate,
                    ResolvedFromName: true);
            }

            return new ResolvedAgentTarget(
                mappedTaskId,
                appState.Tasks.TryGetValue(mappedTaskId, out var namedTask) ? namedTask as LocalAgentTask : null,
                null,
                ResolvedFromName: true);
        }

        if (appState.Tasks.TryGetValue(target, out var task) && task is LocalAgentTask localAgentTask)
        {
            return new ResolvedAgentTarget(target, localAgentTask, null, ResolvedFromName: false);
        }

        var teammateTask = InProcessTeammateTasks.FindByAgentId(target, appState.Tasks);
        if (teammateTask is not null)
        {
            return new ResolvedAgentTarget(target, null, teammateTask, ResolvedFromName: false);
        }

        return LooksLikeLocalAgentId(target)
            ? new ResolvedAgentTarget(target, null, null, ResolvedFromName: false)
            : new ResolvedAgentTarget(null, null, null, ResolvedFromName: false);
    }

    private static bool LooksLikeLocalAgentId(string value)
    {
        if (value.Length != 9 || value[0] != 'a')
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            if (!char.IsAsciiLetterOrDigit(value[index]) || char.IsUpper(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static JsonObject CreateStructuredFailureOutput(string message)
    {
        return new JsonObject
        {
            ["success"] = false,
            ["message"] = message
        };
    }

    private static bool TryParseArguments(string arguments, out SendMessageToolRequest? request, out string? errorMessage)
    {
        request = null;
        errorMessage = null;

        try
        {
            using var document = JsonDocument.Parse(arguments);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "SendMessage arguments must be a JSON object.";
                return false;
            }

            var root = document.RootElement;
            var to = root.TryGetProperty("to", out var toElement) && toElement.ValueKind == JsonValueKind.String
                ? toElement.GetString()
                : null;
            var summary = root.TryGetProperty("summary", out var summaryElement) && summaryElement.ValueKind == JsonValueKind.String
                ? summaryElement.GetString()
                : null;

            if (!root.TryGetProperty("message", out var messageElement))
            {
                errorMessage = "SendMessage requires 'message'.";
                return false;
            }

            if (!TryParseMessage(messageElement, out var message, out errorMessage) || message is null)
            {
                return false;
            }

            request = new SendMessageToolRequest(to, summary, message);
            return true;
        }
        catch (JsonException)
        {
            errorMessage = "SendMessage arguments must be a JSON object.";
            return false;
        }
    }

    private static bool TryParseMessage(JsonElement messageElement, out MessagePayload? payload, out string? errorMessage)
    {
        payload = null;
        errorMessage = null;

        if (messageElement.ValueKind == JsonValueKind.String)
        {
            payload = new TextMessagePayload(messageElement.GetString() ?? string.Empty);
            return true;
        }

        if (messageElement.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "'message' must be a string or structured object.";
            return false;
        }

        if (!messageElement.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            errorMessage = "structured 'message' requires a string 'type'.";
            return false;
        }

        var type = typeElement.GetString();
        switch (type)
        {
            case "shutdown_request":
                payload = new ShutdownRequestMessagePayload(
                    messageElement.TryGetProperty("reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String
                        ? reasonElement.GetString()
                        : null);
                return true;
            case "shutdown_response":
                if (!messageElement.TryGetProperty("request_id", out var requestIdElement) || requestIdElement.ValueKind != JsonValueKind.String ||
                    !messageElement.TryGetProperty("approve", out var approveElement) || (approveElement.ValueKind != JsonValueKind.True && approveElement.ValueKind != JsonValueKind.False))
                {
                    errorMessage = "shutdown_response requires 'request_id' and boolean 'approve'.";
                    return false;
                }

                payload = new ShutdownResponseMessagePayload(
                    requestIdElement.GetString() ?? string.Empty,
                    approveElement.GetBoolean(),
                    messageElement.TryGetProperty("reason", out var shutdownReasonElement) && shutdownReasonElement.ValueKind == JsonValueKind.String
                        ? shutdownReasonElement.GetString()
                        : null);
                return true;
            case "plan_approval_response":
                if (!messageElement.TryGetProperty("request_id", out var planRequestIdElement) || planRequestIdElement.ValueKind != JsonValueKind.String ||
                    !messageElement.TryGetProperty("approve", out var planApproveElement) || (planApproveElement.ValueKind != JsonValueKind.True && planApproveElement.ValueKind != JsonValueKind.False))
                {
                    errorMessage = "plan_approval_response requires 'request_id' and boolean 'approve'.";
                    return false;
                }

                payload = new PlanApprovalResponseMessagePayload(
                    planRequestIdElement.GetString() ?? string.Empty,
                    planApproveElement.GetBoolean(),
                    messageElement.TryGetProperty("feedback", out var feedbackElement) && feedbackElement.ValueKind == JsonValueKind.String
                        ? feedbackElement.GetString()
                        : null);
                return true;
            default:
                errorMessage = $"Unknown structured message type '{type}'.";
                return false;
        }
    }

    private sealed record SendMessageToolRequest(
        string? To,
        string? Summary,
        MessagePayload Message);

    private abstract record MessagePayload;

    private sealed record TextMessagePayload(string Value) : MessagePayload;

    private sealed record ShutdownRequestMessagePayload(string? Reason) : MessagePayload;

    private sealed record ShutdownResponseMessagePayload(
        string RequestId,
        bool Approve,
        string? Reason) : MessagePayload;

    private sealed record PlanApprovalResponseMessagePayload(
        string RequestId,
        bool Approve,
        string? Feedback) : MessagePayload;

    private sealed record ResolvedAgentTarget(
        string? AgentId,
        LocalAgentTask? Task,
        InProcessTeammateTask? TeammateTask,
        bool ResolvedFromName);
}
