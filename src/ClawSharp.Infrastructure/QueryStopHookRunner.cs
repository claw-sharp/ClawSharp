// TS origin: ./query/stopHooks.ts, ./utils/hooks.ts
// TS parity status: ports the Stop-hook execution branch that runs after tool results, emits streamed hook messages and summaries, and returns prevent-continuation versus blocking-hook outcomes to the query loop; the broader stop-hook blocking continuation still depends on the missing model-backed recursive iteration path.
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Query;
using ClawSharp.Tools;

namespace ClawSharp.Infrastructure;

public sealed class QueryStopHookRunner : IQueryStopHookRunner
{
    private readonly ToolRegistry _toolRegistry;
    private readonly HookRegistry _hookRegistry;
    private readonly HookExecutor _hookExecutor;

    public QueryStopHookRunner(
        ToolRegistry toolRegistry,
        HookRegistry? hookRegistry = null,
        HookExecutor? hookExecutor = null)
    {
        _toolRegistry = toolRegistry;
        _hookRegistry = hookRegistry ?? new HookRegistry();
        _hookExecutor = hookExecutor ?? new HookExecutor();
    }

    public async Task<QueryStopHookExecutionResult> RunAsync(
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken = default)
    {
        var appState = _toolRegistry.AppStateStore.GetState();
        var hooks = _hookRegistry.GetHooksForEvent(appState.Hooks, HookEvent.Stop);
        if (hooks.Count == 0)
        {
            return QueryStopHookExecutionResult.Empty;
        }

        var toolUseId = Guid.NewGuid().ToString("N");
        var hookRequest = new HookExecutionRequest(
            HookEvent.Stop,
            session.ProjectDirectory,
            CreateStopHookInput(
                session,
                appState.ToolPermissionContext.Mode,
                state.StopHookActive ?? false,
                TryGetLastAssistantText(state.Messages)).ToJsonString(),
            Settings: settings,
            HookName: "Stop",
            ToolUseId: toolUseId);

        var hookCount = 0;
        var preventedContinuation = false;
        string? stopReason = null;
        var hasOutput = false;
        var hookErrors = new List<string>();
        var hookInfos = new List<StopHookInfo>();
        var blockingMessages = new List<ChatMessage>();

        await foreach (var update in _hookExecutor.StreamAsync(hooks, hookRequest, cancellationToken))
        {
            if (update.Message is not null)
            {
                await emitEvent(new QueryMessageRuntimeEvent(update.Message), cancellationToken);
                TrackStopHookMessage(
                    update.Message,
                    hookInfos,
                    hookErrors,
                    ref hookCount,
                    ref hasOutput);
            }

            if (update.BlockingError is not null)
            {
                var userMessage = ChatMessageFactory.CreateUserMessage(
                    GetStopHookMessage(update.BlockingError),
                    isMeta: true);
                blockingMessages.Add(userMessage);
                await emitEvent(new QueryMessageRuntimeEvent(userMessage), cancellationToken);
                hookErrors.Add(update.BlockingError.BlockingError);
                hasOutput = true;
            }

            if (update.PreventContinuation)
            {
                preventedContinuation = true;
                stopReason = update.StopReason ?? "Stop hook prevented continuation";
                await emitEvent(
                    new QueryMessageRuntimeEvent(
                        ChatMessageFactory.CreateHookStoppedContinuationAttachmentMessage(
                            stopReason,
                            "Stop",
                            toolUseId,
                            HookEvent.Stop)),
                    cancellationToken);
            }
        }

        if (hookCount > 0)
        {
            await emitEvent(
                new QueryMessageRuntimeEvent(
                    ChatMessageFactory.CreateStopHookSummaryMessage(
                        hookCount,
                        hookInfos,
                        hookErrors,
                        preventedContinuation,
                        stopReason,
                        hasOutput,
                        "suggestion",
                        toolUseId)),
                cancellationToken);
        }

        return new QueryStopHookExecutionResult(preventedContinuation, blockingMessages);
    }

    private static string GetStopHookMessage(HookBlockingError blockingError)
    {
        return $"Stop hook feedback:{Environment.NewLine}{blockingError.BlockingError}";
    }

    private static JsonObject CreateStopHookInput(
        ConversationSession session,
        PermissionMode permissionMode,
        bool stopHookActive,
        string? lastAssistantMessage)
    {
        var input = new JsonObject
        {
            ["session_id"] = session.Id,
            ["transcript_path"] = session.TranscriptPath,
            ["cwd"] = session.ProjectDirectory,
            ["permission_mode"] = permissionMode.ToString(),
            ["hook_event_name"] = "Stop",
            ["stop_hook_active"] = stopHookActive
        };

        if (!string.IsNullOrWhiteSpace(lastAssistantMessage))
        {
            input["last_assistant_message"] = lastAssistantMessage;
        }

        return input;
    }

    private static string? TryGetLastAssistantText(IReadOnlyList<ChatMessage> messages)
    {
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            var message = messages[index];
            if (message.Role != MessageRole.Assistant)
            {
                continue;
            }

            var text = string.Join(
                "\n",
                message.ContentBlocks
                    .Where(block => block.Kind == MessageContentKind.Text)
                    .Select(block => block.Value)
                    .Where(content => !string.IsNullOrWhiteSpace(content)))
                .Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static void TrackStopHookMessage(
        ChatMessage message,
        ICollection<StopHookInfo> hookInfos,
        ICollection<string> hookErrors,
        ref int hookCount,
        ref bool hasOutput)
    {
        if (message.ContentBlocks.Count != 1)
        {
            return;
        }

        var block = message.ContentBlocks[0];
        if (block.Kind == MessageContentKind.Progress)
        {
            hookCount++;
            if (JsonNode.Parse(block.Value) is JsonObject progressData)
            {
                var command = progressData["command"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(command))
                {
                    hookInfos.Add(
                        new StopHookInfo(
                            command,
                            progressData["promptText"]?.GetValue<string>()));
                }
            }

            return;
        }

        if (QueryAttachmentHelpers.TryGetHookNonBlockingErrorAttachment(message, out var nonBlockingError) &&
            nonBlockingError is not null &&
            nonBlockingError.HookEvent == HookEvent.Stop)
        {
            hookErrors.Add(nonBlockingError.Stderr);
            hasOutput = true;
            return;
        }

        if (QueryAttachmentHelpers.TryGetHookErrorDuringExecutionAttachment(message, out var executionError) &&
            executionError is not null &&
            executionError.HookEvent == HookEvent.Stop)
        {
            hookErrors.Add(executionError.Content);
            hasOutput = true;
            return;
        }

        if (QueryAttachmentHelpers.TryGetHookSuccessAttachment(message, out var successAttachment) &&
            successAttachment is not null &&
            successAttachment.HookEvent == HookEvent.Stop)
        {
            if (!string.IsNullOrWhiteSpace(successAttachment.Stdout) ||
                !string.IsNullOrWhiteSpace(successAttachment.Stderr))
            {
                hasOutput = true;
            }

            return;
        }

        if (QueryAttachmentHelpers.TryGetHookBlockingErrorAttachment(message, out var blockingAttachment) &&
            blockingAttachment is not null &&
            blockingAttachment.HookEvent == HookEvent.Stop)
        {
            hookErrors.Add(blockingAttachment.BlockingError.BlockingError);
            hasOutput = true;
        }
    }
}
