// TS parity status: tool batching, execution lifecycle events, and tool progress event emission are ported; full 1:1 parity still depends on the real streaming tool executor and model-backed query loop.
using System.Text.Json.Nodes;
using System.Threading.Channels;
using ClawSharp.Core;
using ClawSharp.Tools;

namespace ClawSharp.Query;

public sealed class ToolOrchestrator
{
    private readonly ToolRegistry _toolRegistry;
    private readonly IEventSink _eventSink;

    public ToolOrchestrator(ToolRegistry toolRegistry, IEventSink eventSink)
    {
        _toolRegistry = toolRegistry;
        _eventSink = eventSink;
    }

    public ToolRegistry ToolRegistry => _toolRegistry;

    public async Task<IReadOnlyList<ToolExecutionRecord>> RunAsync(
        IReadOnlyList<ToolCallRequest> toolCalls,
        ConversationSession session,
        ClawSharpSettings settings,
        Action<ToolCallRequest, ToolProgressUpdate>? onProgress = null,
        string? querySource = null,
        IReadOnlyList<string>? currentSystemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ToolExecutionRecord>();

        await foreach (var update in StreamAsync(
                           toolCalls,
                           session,
                           settings,
                           onProgress,
                           querySource,
                           currentSystemPrompt,
                           cancellationToken))
        {
            if (update.Result is not null)
            {
                results.Add(update.Result);
            }
        }

        return results;
    }

    public async IAsyncEnumerable<ToolExecutionUpdate> StreamAsync(
        IReadOnlyList<ToolCallRequest> toolCalls,
        ConversationSession session,
        ClawSharpSettings settings,
        Action<ToolCallRequest, ToolProgressUpdate>? onProgress = null,
        string? querySource = null,
        IReadOnlyList<string>? currentSystemPrompt = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<ToolExecutionUpdate>();
        var producerTask = ProduceUpdatesAsync(
            toolCalls,
            session,
            settings,
            channel.Writer,
            onProgress,
            querySource,
            currentSystemPrompt,
            cancellationToken);

        await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return update;
        }

        await producerTask;
    }

    private IReadOnlyList<ToolBatch> PartitionToolCalls(IReadOnlyList<ToolCallRequest> toolCalls)
    {
        var batches = new List<ToolBatch>();

        foreach (var toolCall in toolCalls)
        {
            var isConcurrencySafe =
                _toolRegistry.TryResolve(toolCall.ToolName, out var tool) &&
                tool is not null &&
                tool.IsConcurrencySafe(toolCall.Arguments);

            if (isConcurrencySafe && batches.Count > 0 && batches[^1].IsConcurrencySafe)
            {
                batches[^1].ToolCalls.Add(toolCall);
                continue;
            }

            batches.Add(new ToolBatch(isConcurrencySafe, [toolCall]));
        }

        return batches;
    }

    private async Task ProduceUpdatesAsync(
        IReadOnlyList<ToolCallRequest> toolCalls,
        ConversationSession session,
        ClawSharpSettings settings,
        ChannelWriter<ToolExecutionUpdate> writer,
        Action<ToolCallRequest, ToolProgressUpdate>? onProgress,
        string? querySource,
        IReadOnlyList<string>? currentSystemPrompt,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var batch in PartitionToolCalls(toolCalls))
            {
                if (batch.IsConcurrencySafe)
                {
                    await Task.WhenAll(
                        batch.ToolCalls.Select(
                            toolCall => ExecuteSingleAsync(
                                toolCall,
                                session,
                                settings,
                                writer,
                                onProgress,
                                querySource,
                                currentSystemPrompt,
                                cancellationToken)));
                }
                else
                {
                    foreach (var toolCall in batch.ToolCalls)
                    {
                        await ExecuteSingleAsync(
                            toolCall,
                            session,
                            settings,
                            writer,
                            onProgress,
                            querySource,
                            currentSystemPrompt,
                            cancellationToken);
                    }
                }
            }

            writer.TryComplete();
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
        }
    }

    private async Task<ToolExecutionRecord> ExecuteSingleAsync(
        ToolCallRequest toolCall,
        ConversationSession session,
        ClawSharpSettings settings,
        ChannelWriter<ToolExecutionUpdate> writer,
        Action<ToolCallRequest, ToolProgressUpdate>? onProgress,
        string? querySource,
        IReadOnlyList<string>? currentSystemPrompt,
        CancellationToken cancellationToken)
    {
        using var toolSpan = ClawSharpTelemetry.StartToolSpan(
            toolCall.ToolName,
            new Dictionary<string, object?>
            {
                ["tool_use_id"] = toolCall.ToolUseId
            });
        _eventSink.Publish(
            new AppEvent(
                AppEventType.ToolExecutionStarted,
                $"Tool execution started: {toolCall.ToolName}",
                DateTimeOffset.UtcNow,
                new Dictionary<string, string>
                {
                    ["sessionId"] = session.Id,
                    ["toolName"] = toolCall.ToolName,
                    ["toolUseId"] = toolCall.ToolUseId
                }));

        var result = await _toolRegistry.ExecuteAsync(
            toolCall.ToolName,
            toolCall.Arguments,
            session,
            settings,
            progressUpdate =>
            {
                onProgress?.Invoke(toolCall, progressUpdate);
                toolSpan.AddEvent(
                    "tool.progress",
                    new Dictionary<string, object?>
                    {
                        ["progress_tool_use_id"] = progressUpdate.ToolUseId,
                        ["field_count"] = progressUpdate.Data.Count
                    });
                PublishToolExecutionProgress(session.Id, toolCall, progressUpdate);
            },
            message => writer.WriteAsync(new ToolExecutionUpdate(Message: message), cancellationToken).AsTask().GetAwaiter().GetResult(),
            querySource,
            null,
            currentSystemPrompt,
            _toolRegistry.All,
            cancellationToken);

        _eventSink.Publish(
            new AppEvent(
                AppEventType.ToolExecutionCompleted,
                $"Tool execution completed: {toolCall.ToolName}",
                DateTimeOffset.UtcNow,
                new Dictionary<string, string>
                {
                    ["sessionId"] = session.Id,
                    ["toolName"] = toolCall.ToolName,
                    ["toolUseId"] = toolCall.ToolUseId,
                    ["success"] = result.Success.ToString()
                }));
        toolSpan.SetAttribute("success", result.Success);
        ClawSharpTelemetry.RecordMetric(
            "tool.execution.count",
            1,
            new Dictionary<string, object?>
            {
                ["tool_name"] = toolCall.ToolName,
                ["success"] = result.Success
            });
        ClawSharpTelemetry.LogEvent(
            "tengu_tool_execution",
            new Dictionary<string, object?>
            {
                ["tool_name"] = toolCall.ToolName,
                ["success"] = result.Success,
                ["output_length"] = result.Output.Length
            });

        var record = new ToolExecutionRecord(
            toolCall,
            result.Success,
            result.Output,
            result.StructuredOutput,
            result.InjectedMessages);
        await writer.WriteAsync(
            new ToolExecutionUpdate(
                Result: record,
                UpdatedToolUseContext: QueryToolUseContextStateFactory.CreateFromToolRegistry(_toolRegistry)),
            cancellationToken);
        return record;
    }

    private void PublishToolExecutionProgress(
        string sessionId,
        ToolCallRequest toolCall,
        ToolProgressUpdate progressUpdate)
    {
        var metadata = new Dictionary<string, string>
        {
            ["sessionId"] = sessionId,
            ["toolName"] = toolCall.ToolName,
            ["toolUseId"] = toolCall.ToolUseId,
            ["progressToolUseId"] = progressUpdate.ToolUseId
        };

        foreach (var property in progressUpdate.Data)
        {
            if (property.Value is null)
            {
                continue;
            }

            metadata[property.Key] = JsonNodeToMetadataValue(property.Value);
        }

        _eventSink.Publish(
            new AppEvent(
                AppEventType.ToolExecutionProgress,
                $"Tool execution progress: {toolCall.ToolName}",
                DateTimeOffset.UtcNow,
                metadata));
    }

    private static string JsonNodeToMetadataValue(JsonNode node)
    {
        return node switch
        {
            JsonValue value => value.ToJsonString().Trim('"'),
            _ => node.ToJsonString()
        };
    }

    private sealed record ToolBatch(
        bool IsConcurrencySafe,
        List<ToolCallRequest> ToolCalls);
}
