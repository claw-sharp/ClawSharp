using System.Text.Json;
using ClawSharp.Core;

namespace ClawSharp.Tools;

internal sealed class AgentTool : BaseTool
{
    private const string GeneralPurposeAgentType = "general-purpose";
    private readonly IAgentExecutionService _agentExecutionService;

    public AgentTool(
        IAgentExecutionService agentExecutionService)
        : base(
            new ToolDescriptor(
                "Agent",
                "Launch a new agent",
                IsLongRunningCapable: true,
                Aliases: ["Task"],
                SearchHint: "delegate work to a subagent",
                InputSchema: AgentToolSchemas.InputSchema,
                OutputSchema: AgentToolSchemas.OutputSchema,
                Strict: true))
    {
        _agentExecutionService = agentExecutionService;
    }

    public override Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var request, out var errorMessage) || request is null)
        {
            return Task.FromResult(ToolValidationResult.Invalid(errorMessage ?? "Agent arguments must be a JSON object."));
        }

        if (!string.IsNullOrWhiteSpace(request.Name) || !string.IsNullOrWhiteSpace(request.TeamName) || request.Mode is not null)
        {
            return Task.FromResult(
                ToolValidationResult.Invalid(
                    "Teammate and multi-agent coordination is not implemented in ClawSharp yet. The TypeScript path depends on the missing swarm mailbox and agent runtime."));
        }

        if (!string.IsNullOrWhiteSpace(request.Cwd) && !Path.IsPathRooted(request.Cwd))
        {
            return Task.FromResult(ToolValidationResult.Invalid("'cwd' must be an absolute path."));
        }

        if (!string.IsNullOrWhiteSpace(request.Cwd) &&
            string.Equals(request.Isolation, "worktree", StringComparison.Ordinal))
        {
            return Task.FromResult(ToolValidationResult.Invalid("'cwd' is mutually exclusive with isolation 'worktree'."));
        }

        if (!string.IsNullOrWhiteSpace(request.Cwd))
        {
            return Task.FromResult(
                ToolValidationResult.Invalid(
                    "Agent 'cwd' overrides are not implemented in ClawSharp yet. The TypeScript runtime changes both filesystem roots and shell working directory for the child agent; ClawSharp needs an approved C# equivalent before enabling that behavior."));
        }

        if (string.Equals(request.Isolation, "worktree", StringComparison.Ordinal))
        {
            return Task.FromResult(
                ToolValidationResult.Invalid(
                    "Agent isolation 'worktree' is not implemented in ClawSharp yet. The TypeScript runtime creates and manages a real git worktree for the child agent; ClawSharp is intentionally blocked at that decision point until the C# equivalent is approved."));
        }

        if (ShouldUseForkPath(request))
        {
            if (string.Equals(context.QuerySource, $"agent:builtin:{ForkSubagentFoundation.ForkSubagentType}", StringComparison.Ordinal) ||
                ForkSubagentFoundation.IsInForkChild(context.Session.Messages))
            {
                return Task.FromResult(
                    ToolValidationResult.Invalid(
                        "Fork is not available inside a forked worker. Complete your task directly using your tools."));
            }

            return Task.FromResult(ToolValidationResult.Valid());
        }

        var selectedAgentType = ResolveSelectedAgentType(request);

        var selectedAgent = context.AgentDefinitions.FirstOrDefault(
            agent => string.Equals(agent.AgentType, selectedAgentType, StringComparison.Ordinal));
        if (selectedAgent is null)
        {
            return Task.FromResult(ToolValidationResult.Invalid($"Unknown subagent_type '{selectedAgentType}'."));
        }

        return Task.FromResult(ToolValidationResult.Valid());
    }

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var request, out var errorMessage) || request is null)
        {
            return Task.FromResult(Failure(errorMessage ?? "Agent arguments must be a JSON object."));
        }

        if (ShouldUseForkPath(request))
        {
            if (string.Equals(context.QuerySource, $"agent:builtin:{ForkSubagentFoundation.ForkSubagentType}", StringComparison.Ordinal) ||
                ForkSubagentFoundation.IsInForkChild(context.Session.Messages))
            {
                return Task.FromResult(
                    Failure("Fork is not available inside a forked worker. Complete your task directly using your tools."));
            }

            return _agentExecutionService.ExecuteAsync(
                context,
                ForkSubagentFoundation.ForkAgentDefinition,
                request,
                cancellationToken);
        }

        var selectedAgentType = ResolveSelectedAgentType(request);
        var selectedAgent = context.AgentDefinitions.FirstOrDefault(
            agent => string.Equals(agent.AgentType, selectedAgentType, StringComparison.Ordinal));
        if (selectedAgent is null)
        {
            return Task.FromResult(Failure($"Unknown subagent_type '{selectedAgentType}'."));
        }

        return _agentExecutionService.ExecuteAsync(context, selectedAgent, request, cancellationToken);
    }

    private static bool TryParseArguments(string arguments, out AgentExecutionRequest? request, out string? errorMessage)
    {
        request = null;
        errorMessage = null;

        try
        {
            request = JsonSerializer.Deserialize<AgentExecutionRequest>(arguments);
            if (request is null)
            {
                errorMessage = "Agent arguments must be a JSON object.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.Description) || string.IsNullOrWhiteSpace(request.Prompt))
            {
                errorMessage = "Agent requires 'description' and 'prompt'.";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            errorMessage = "Agent arguments must be a JSON object.";
            return false;
        }
    }

    private static bool ShouldUseForkPath(AgentExecutionRequest request)
    {
        return string.IsNullOrWhiteSpace(request.SubagentType) &&
               ForkSubagentFoundation.IsForkSubagentEnabled();
    }

    private static string ResolveSelectedAgentType(AgentExecutionRequest request)
    {
        return string.IsNullOrWhiteSpace(request.SubagentType)
            ? GeneralPurposeAgentType
            : request.SubagentType.Trim();
    }
}
