// TS parity status: ports the shared compact.ts pre-compact and post-compact hook execution outside the REPL by reusing the current hook registry and executor, including the visible TypeScript userDisplayMessage aggregation for success and failure lines; the live reactive-compact post-summary invocation still depends on the missing raw-summary handoff from the compaction model-call path.
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Query;
using ClawSharp.Tools;

namespace ClawSharp.Infrastructure;

public sealed class QueryReactiveCompactHookRunner : IQueryReactiveCompactHookRunner
{
    private readonly ToolRegistry _toolRegistry;
    private readonly HookRegistry _hookRegistry;
    private readonly HookExecutor _hookExecutor;

    public QueryReactiveCompactHookRunner(
        ToolRegistry toolRegistry,
        HookRegistry? hookRegistry = null,
        HookExecutor? hookExecutor = null)
    {
        _toolRegistry = toolRegistry;
        _hookRegistry = hookRegistry ?? new HookRegistry();
        _hookExecutor = hookExecutor ?? new HookExecutor();
    }

    public async Task<QueryReactiveCompactHookRunResult> RunPreCompactAsync(
        QueryReactiveCompactExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var hooks = GetHooks(HookEvent.PreCompact, GetTrigger(context));
        if (hooks.Count == 0)
        {
            return QueryReactiveCompactHookRunResult.Empty;
        }

        var batch = await _hookExecutor.ExecuteAsync(
            hooks,
            new HookExecutionRequest(
                HookEvent.PreCompact,
                context.Session.ProjectDirectory,
                CreatePreCompactHookInput(context).ToJsonString(),
                MatcherValue: GetTrigger(context),
                Settings: context.Settings),
            cancellationToken);

        var successfulOutputs = batch.Traces
            .Where(trace => trace.Succeeded)
            .Select(GetTraceOutput)
            .Where(static output => !string.IsNullOrWhiteSpace(output))
            .ToArray();

        return new QueryReactiveCompactHookRunResult(
            CustomInstructions: successfulOutputs.Length > 0
                ? string.Join("\n\n", successfulOutputs)
                : null,
            UserDisplayMessage: BuildUserDisplayMessage("PreCompact", batch.Traces));
    }

    public async Task<QueryReactiveCompactPostCompactHookRunResult> RunPostCompactAsync(
        QueryReactiveCompactExecutionContext context,
        string compactSummary,
        CancellationToken cancellationToken = default)
    {
        var hooks = GetHooks(HookEvent.PostCompact, GetTrigger(context));
        if (hooks.Count == 0)
        {
            return QueryReactiveCompactPostCompactHookRunResult.Empty;
        }

        var batch = await _hookExecutor.ExecuteAsync(
            hooks,
            new HookExecutionRequest(
                HookEvent.PostCompact,
                context.Session.ProjectDirectory,
                CreatePostCompactHookInput(context, compactSummary).ToJsonString(),
                MatcherValue: GetTrigger(context),
                Settings: context.Settings),
            cancellationToken);

        return new QueryReactiveCompactPostCompactHookRunResult(
            BuildUserDisplayMessage("PostCompact", batch.Traces));
    }

    private IReadOnlyList<HookDefinition> GetHooks(HookEvent hookEvent, string trigger)
    {
        var appState = _toolRegistry.AppStateStore.GetState();
        return _hookRegistry.GetHooksForEvent(appState.Hooks, hookEvent, trigger);
    }

    private static string GetTrigger(QueryReactiveCompactExecutionContext context)
    {
        return context.Trigger;
    }

    private static JsonObject CreatePreCompactHookInput(QueryReactiveCompactExecutionContext context)
    {
        var input = CreateBaseCompactHookInput(context, HookEvent.PreCompact);
        input["trigger"] = GetTrigger(context);

        var customInstructions = context.Request.ModelTurnContext?.SystemPrompt is { Count: > 0 } systemPrompt
            ? string.Join("\n", systemPrompt)
            : null;
        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            input["custom_instructions"] = customInstructions;
        }

        return input;
    }

    private static JsonObject CreatePostCompactHookInput(
        QueryReactiveCompactExecutionContext context,
        string compactSummary)
    {
        var input = CreateBaseCompactHookInput(context, HookEvent.PostCompact);
        input["trigger"] = GetTrigger(context);
        input["compact_summary"] = compactSummary;
        return input;
    }

    private static JsonObject CreateBaseCompactHookInput(
        QueryReactiveCompactExecutionContext context,
        HookEvent hookEvent)
    {
        return new JsonObject
        {
            ["session_id"] = context.Session.Id,
            ["transcript_path"] = context.Session.TranscriptPath,
            ["cwd"] = context.Session.ProjectDirectory,
            ["hook_event_name"] = hookEvent.ToString()
        };
    }

    private static string? BuildUserDisplayMessage(
        string hookName,
        IReadOnlyList<HookExecutionTrace> traces)
    {
        if (traces.Count == 0)
        {
            return null;
        }

        var messages = traces
            .Select(
                trace =>
                {
                    var outcome = trace.Succeeded ? "completed successfully" : "failed";
                    var output = GetTraceOutput(trace);
                    return string.IsNullOrWhiteSpace(output)
                        ? $"{hookName} [{GetHookDisplayText(trace.Hook.Command)}] {outcome}"
                        : $"{hookName} [{GetHookDisplayText(trace.Hook.Command)}] {outcome}: {output}";
                })
            .ToArray();

        return messages.Length == 0 ? null : string.Join("\n", messages);
    }

    private static string GetTraceOutput(HookExecutionTrace trace)
    {
        return $"{trace.Stdout}{trace.Stderr}".Trim();
    }

    private static string GetHookDisplayText(HookCommandDefinition hook)
    {
        if (!string.IsNullOrWhiteSpace(hook.StatusMessage))
        {
            return hook.StatusMessage;
        }

        return hook.Type switch
        {
            HookKind.Command => hook.Command ?? "command",
            HookKind.Prompt => hook.Prompt ?? "prompt",
            HookKind.Agent => hook.Prompt ?? "agent",
            HookKind.Http => hook.Url ?? "http",
            _ => hook.Type.ToString()
        };
    }
}
