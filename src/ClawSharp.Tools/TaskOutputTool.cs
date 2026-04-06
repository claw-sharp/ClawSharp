// TS origin: ./tools/TaskOutputTool/TaskOutputTool.tsx
// TS parity status: task output schemas, app-state-backed task lookup, disk-backed output lookup, live local-shell taskOutput precedence, queued task notifications, waiting progress events, block/non-block polling semantics, and current task-type-specific output shaping are ported; full 1:1 parity still depends on the absent shell executor/runtime loop and task-specific UI integrations.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClawSharp.Tasks;

namespace ClawSharp.Tools;

internal sealed class TaskOutputTool : BaseTool
{
    private const int DefaultTimeoutMs = 30_000;
    private const int TaskMaxOutputUpperLimit = 160_000;
    private const int TaskMaxOutputDefault = 32_000;
    private static readonly JsonObject InputSchema = CreateInputSchema();
    private static readonly JsonObject OutputSchema = CreateOutputSchema();

    public TaskOutputTool()
        : base(
            new ToolDescriptor(
                "TaskOutput",
                "Inspect background task output",
                Parameters:
                [
                    new ToolParameter("taskId", "Task id to inspect")
                ],
                Aliases:
                [
                    "AgentOutputTool",
                    "BashOutputTool"
                ],
                SearchHint: "read output/logs from a background task",
                ShouldDefer: true,
                InputSchema: InputSchema,
                OutputSchema: OutputSchema,
                Strict: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments)
    {
        return true;
    }

    public override bool IsReadOnly(string arguments)
    {
        return true;
    }

    public override string? RenderToolUseProgressMessage(IReadOnlyList<ToolProgressUpdate> progressMessages)
    {
        if (progressMessages.Count == 0)
        {
            return null;
        }

        var lastProgress = progressMessages[^1];
        var lines = new List<string>(2);
        var taskDescription = lastProgress.Data["taskDescription"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(taskDescription))
        {
            lines.Add($"  {taskDescription}");
        }

        lines.Add("     Waiting for task (esc to give additional instructions)");
        return string.Join(Environment.NewLine, lines);
    }

    public override string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages)
    {
        if (structuredOutput is not JsonObject result)
        {
            return null;
        }

        var retrievalStatus = result["retrieval_status"]?.GetValue<string>();
        var task = result["task"] as JsonObject;
        if (task is null)
        {
            return "No task output available";
        }

        var taskType = task["task_type"]?.GetValue<string>();
        return taskType switch
        {
            "local_bash" => RenderLocalBashTaskResult(task),
            "local_agent" => RenderLocalAgentTaskResult(retrievalStatus, task),
            "remote_agent" => RenderTaskSummary(task, includeOutput: true),
            _ => RenderTaskSummary(task, includeOutput: true)
        };
    }

    public override Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var request, out var errorMessage) || request is null)
        {
            return Task.FromResult(ToolValidationResult.Invalid(errorMessage ?? "Task ID is required"));
        }

        var state = context.TaskAppState.GetAppState();
        if (!state.Tasks.TryGetValue(request.TaskId, out var task) || task is null)
        {
            return Task.FromResult(ToolValidationResult.Invalid($"No task found with ID: {request.TaskId}"));
        }

        return Task.FromResult(ToolValidationResult.Valid());
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var request, out var errorMessage) || request is null)
        {
            return Failure(errorMessage ?? "Task ID is required");
        }

        var shouldEvictAfterRead = !context.Arguments.TrimStart().StartsWith("{", StringComparison.Ordinal);

        var state = context.TaskAppState.GetAppState();
        if (!state.Tasks.TryGetValue(request.TaskId, out var task) || task is null)
        {
            return Failure($"No task found with ID: {request.TaskId}");
        }

        if (!request.Block)
        {
            if (task.Status.IsTerminal())
            {
                MarkTaskNotified(task.Id, context.TaskAppState);
                var result = await BuildResponseAsync("success", task, context, cancellationToken);
                if (shouldEvictAfterRead)
                {
                    context.Tasks.SweepEvictableTerminalTasks();
                }

                return result;
            }

            return await BuildResponseAsync("not_ready", task, context, cancellationToken);
        }

        context.ReportProgress(
            $"task-output-waiting-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            new JsonObject
            {
                ["type"] = "waiting_for_task",
                ["taskDescription"] = task.Description,
                ["taskType"] = task.Type.ToSerializedName()
            });

        var completedTask = await WaitForTaskCompletionAsync(request.TaskId, request.TimeoutMs, context.TaskAppState, cancellationToken);
        if (completedTask is null)
        {
            return Success(BuildToolResultContent("timeout", null), BuildStructuredOutput("timeout", null));
        }

        if (!completedTask.Status.IsTerminal())
        {
            return await BuildResponseAsync("timeout", completedTask, context, cancellationToken);
        }

        MarkTaskNotified(completedTask.Id, context.TaskAppState);
        var successResult = await BuildResponseAsync("success", completedTask, context, cancellationToken);
        if (shouldEvictAfterRead)
        {
            context.Tasks.SweepEvictableTerminalTasks();
        }

        return successResult;
    }

    private static async Task<ToolExecutionResult> BuildResponseAsync(
        string retrievalStatus,
        ClawSharpTask task,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var taskOutput = await CreateTaskOutputAsync(task, context, cancellationToken);
        return Success(
            BuildToolResultContent(retrievalStatus, taskOutput),
            BuildStructuredOutput(retrievalStatus, taskOutput));
    }

    private static async Task<SerializedTaskOutput> CreateTaskOutputAsync(
        ClawSharpTask task,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        return task switch
        {
            LocalAgentTask localAgentTask => CreateLocalAgentTaskOutput(
                localAgentTask,
                await context.Tasks.GetOutputAsync(task.Id, cancellationToken)),
            RemoteAgentTask remoteAgentTask => CreateRemoteAgentTaskOutput(
                remoteAgentTask,
                await context.Tasks.GetOutputAsync(task.Id, cancellationToken)),
            LocalBashTask localBashTask => CreateLocalBashTaskOutput(
                localBashTask,
                await GetLocalBashOutputAsync(localBashTask, context, cancellationToken)),
            _ => CreateGenericTaskOutput(
                task,
                await context.Tasks.GetOutputAsync(task.Id, cancellationToken))
        };
    }

    private static async Task<string> GetLocalBashOutputAsync(
        LocalBashTask task,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var taskOutput = task.ShellCommand?.TaskOutput;
        if (taskOutput is null)
        {
            return await context.Tasks.GetOutputAsync(task.Id, cancellationToken);
        }

        var stdout = await taskOutput.GetStdoutAsync(cancellationToken);
        var stderr = taskOutput.GetStderr();
        return JoinTruthyParts(stdout, stderr);
    }

    private static string JoinTruthyParts(string first, string second)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(first))
        {
            parts.Add(first);
        }

        if (!string.IsNullOrEmpty(second))
        {
            parts.Add(second);
        }

        return string.Join('\n', parts);
    }

    private static string RenderLocalBashTaskResult(JsonObject task)
    {
        var output = task["output"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(output))
        {
            return output.TrimEnd();
        }

        var error = task["error"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(error))
        {
            return error;
        }

        return "(No output)";
    }

    private static string RenderLocalAgentTaskResult(string? retrievalStatus, JsonObject task)
    {
        if (string.Equals(retrievalStatus, "success", StringComparison.Ordinal))
        {
            var result = task["result"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(result))
            {
                return result.TrimEnd();
            }

            var output = task["output"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(output))
            {
                return output.TrimEnd();
            }
        }

        var status = task["status"]?.GetValue<string>();
        if (string.Equals(retrievalStatus, "timeout", StringComparison.Ordinal) ||
            string.Equals(retrievalStatus, "not_ready", StringComparison.Ordinal) ||
            string.Equals(status, "running", StringComparison.Ordinal))
        {
            return "Task is still running...";
        }

        return "Task not ready";
    }

    private static string RenderTaskSummary(JsonObject task, bool includeOutput)
    {
        var description = task["description"]?.GetValue<string>() ?? "Task";
        var status = task["status"]?.GetValue<string>() ?? "unknown";
        var lines = new List<string>
        {
            $"  {description} [{status}]"
        };

        if (includeOutput)
        {
            var output = task["output"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(output))
            {
                lines.Add(output.TrimEnd());
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static SerializedTaskOutput CreateLocalBashTaskOutput(LocalBashTask task, string output)
    {
        return new SerializedTaskOutput(
            TaskId: task.Id,
            TaskType: task.Type.ToSerializedName(),
            Status: task.Status.ToSerializedName(),
            Description: task.Description,
            Output: output,
            OutputFile: task.OutputFile,
            ExitCode: task.ExitCode,
            Error: task.Error,
            Prompt: null,
            Result: null);
    }

    private static SerializedTaskOutput CreateLocalAgentTaskOutput(LocalAgentTask task, string output)
    {
        var result = string.IsNullOrEmpty(task.Result) ? output : task.Result;
        return new SerializedTaskOutput(
            TaskId: task.Id,
            TaskType: task.Type.ToSerializedName(),
            Status: task.Status.ToSerializedName(),
            Description: task.Description,
            Output: result,
            OutputFile: task.OutputFile,
            ExitCode: null,
            Error: task.Error,
            Prompt: task.Prompt,
            Result: result);
    }

    private static SerializedTaskOutput CreateRemoteAgentTaskOutput(RemoteAgentTask task, string output)
    {
        return new SerializedTaskOutput(
            TaskId: task.Id,
            TaskType: task.Type.ToSerializedName(),
            Status: task.Status.ToSerializedName(),
            Description: task.Description,
            Output: output,
            OutputFile: task.OutputFile,
            ExitCode: null,
            Error: task.Error,
            Prompt: task.Command,
            Result: null);
    }

    private static SerializedTaskOutput CreateGenericTaskOutput(ClawSharpTask task, string output)
    {
        return new SerializedTaskOutput(
            TaskId: task.Id,
            TaskType: task.Type.ToSerializedName(),
            Status: task.Status.ToSerializedName(),
            Description: task.Description,
            Output: output,
            OutputFile: task.OutputFile,
            ExitCode: task.ExitCode,
            Error: task.Error,
            Prompt: task.Prompt,
            Result: task.Result);
    }

    private static async Task<ClawSharpTask?> WaitForTaskCompletionAsync(
        string taskId,
        int timeoutMs,
        ITaskAppStateStore taskAppStateStore,
        CancellationToken cancellationToken)
    {
        var startedAt = Environment.TickCount64;
        while (Environment.TickCount64 - startedAt < timeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = taskAppStateStore.GetAppState();
            if (!state.Tasks.TryGetValue(taskId, out var task) || task is null)
            {
                return null;
            }

            if (task.Status.IsTerminal())
            {
                return task;
            }

            await Task.Delay(100, cancellationToken);
        }

        var finalState = taskAppStateStore.GetAppState();
        return finalState.Tasks.TryGetValue(taskId, out var currentTask)
            ? currentTask
            : null;
    }

    private static void MarkTaskNotified(string taskId, ITaskAppStateStore taskAppStateStore)
    {
        taskAppStateStore.SetAppState(
            previousState =>
            {
                if (!previousState.Tasks.TryGetValue(taskId, out var task) || task.Notified)
                {
                    return previousState;
                }

                var updatedTasks = new Dictionary<string, ClawSharpTask>(previousState.Tasks, StringComparer.Ordinal)
                {
                    [taskId] = task with { Notified = true }
                };

                return new TaskAppState(updatedTasks);
            });
    }

    private static string BuildToolResultContent(string retrievalStatus, SerializedTaskOutput? task)
    {
        var builder = new StringBuilder();
        builder.Append("<retrieval_status>")
            .Append(retrievalStatus)
            .Append("</retrieval_status>");

        if (task is null)
        {
            return builder.ToString();
        }

        builder.AppendLine()
            .AppendLine()
            .Append("<task_id>")
            .Append(task.TaskId)
            .Append("</task_id>")
            .AppendLine()
            .AppendLine()
            .Append("<task_type>")
            .Append(task.TaskType)
            .Append("</task_type>")
            .AppendLine()
            .AppendLine()
            .Append("<status>")
            .Append(task.Status)
            .Append("</status>");

        if (task.ExitCode is not null)
        {
            builder.AppendLine()
                .AppendLine()
                .Append("<exit_code>")
                .Append(task.ExitCode.Value)
                .Append("</exit_code>");
        }

        if (!string.IsNullOrWhiteSpace(task.Output))
        {
            var formattedOutput = FormatTaskOutput(task.Output, task.OutputFile);
            builder.AppendLine()
                .AppendLine()
                .Append("<output>")
                .AppendLine()
                .Append(formattedOutput.TrimEnd())
                .AppendLine()
                .Append("</output>");
        }

        if (!string.IsNullOrWhiteSpace(task.Error))
        {
            builder.AppendLine()
                .AppendLine()
                .Append("<error>")
                .Append(task.Error)
                .Append("</error>");
        }

        return builder.ToString();
    }

    private static JsonObject BuildStructuredOutput(string retrievalStatus, SerializedTaskOutput? task)
    {
        var result = new JsonObject
        {
            ["retrieval_status"] = retrievalStatus
        };

        result["task"] = task is null
            ? null
            : JsonSerializer.SerializeToNode(task, SerializerOptions);

        return result;
    }

    private static string FormatTaskOutput(string output, string outputFile)
    {
        var maxLength = GetMaxTaskOutputLength();
        if (output.Length <= maxLength)
        {
            return output;
        }

        var header = $"[Truncated. Full output: {outputFile}]\n\n";
        var availableSpace = maxLength - header.Length;
        var truncated = SliceLikeJavaScript(output, -availableSpace);
        return header + truncated;
    }

    private static int GetMaxTaskOutputLength()
    {
        var configuredValue = Environment.GetEnvironmentVariable("TASK_MAX_OUTPUT_LENGTH");
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return TaskMaxOutputDefault;
        }

        if (!int.TryParse(configuredValue, out var parsedValue) || parsedValue <= 0)
        {
            return TaskMaxOutputDefault;
        }

        return Math.Min(parsedValue, TaskMaxOutputUpperLimit);
    }

    private static string SliceLikeJavaScript(string value, int start)
    {
        var normalizedStart = start >= 0
            ? Math.Min(start, value.Length)
            : Math.Max(value.Length + start, 0);

        return value[normalizedStart..];
    }

    private static JsonObject CreateInputSchema()
    {
        return ToolJsonSchemaFactory.StrictObject(
            [
                ("task_id", ToolJsonSchemaFactory.String("The task ID to get output from")),
                ("block", ToolJsonSchemaFactory.Boolean("Whether to wait for completion", defaultValue: true)),
                ("timeout", ToolJsonSchemaFactory.Number("Max wait time in ms", minimum: 0, maximum: 600000))
            ],
            required:
            [
                "task_id"
            ]);
    }

    private static JsonObject CreateOutputSchema()
    {
        return ToolJsonSchemaFactory.StrictObject(
            [
                ("retrieval_status", ToolJsonSchemaFactory.StringEnum(
                    [
                        "success",
                        "timeout",
                        "not_ready"
                    ])),
                ("task", ToolJsonSchemaFactory.Nullable(
                    ToolJsonSchemaFactory.StrictObject(
                        [
                            ("task_id", ToolJsonSchemaFactory.String()),
                            ("task_type", ToolJsonSchemaFactory.String()),
                            ("status", ToolJsonSchemaFactory.String()),
                            ("description", ToolJsonSchemaFactory.String()),
                            ("output", ToolJsonSchemaFactory.String()),
                            ("exitCode", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.Number())),
                            ("error", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.String())),
                            ("prompt", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.String())),
                            ("result", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.String()))
                        ],
                        required:
                        [
                            "task_id",
                            "task_type",
                            "status",
                            "description",
                            "output"
                        ])))
            ],
            required:
            [
                "retrieval_status",
                "task"
            ]);
    }

    private static bool TryParseArguments(
        string arguments,
        out TaskOutputRequest? request,
        out string? errorMessage)
    {
        request = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(arguments))
        {
            errorMessage = "Task ID is required";
            return false;
        }

        var trimmed = arguments.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            request = new TaskOutputRequest(trimmed, Block: true, TimeoutMs: DefaultTimeoutMs);
            return true;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<SerializedTaskOutputRequest>(trimmed, SerializerOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.TaskId))
            {
                errorMessage = "Task ID is required";
                return false;
            }

            request = new TaskOutputRequest(
                parsed.TaskId,
                parsed.Block ?? true,
                parsed.Timeout ?? DefaultTimeoutMs);
            return true;
        }
        catch (JsonException)
        {
            errorMessage = "TaskOutput arguments must be a task id or a JSON object with task_id.";
            return false;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed record TaskOutputRequest(string TaskId, bool Block, int TimeoutMs);

    private sealed record SerializedTaskOutput(
        [property: JsonPropertyName("task_id")] string TaskId,
        [property: JsonPropertyName("task_type")] string TaskType,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("output")] string Output,
        [property: JsonIgnore] string OutputFile,
        [property: JsonPropertyName("exitCode")] int? ExitCode,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("prompt")] string? Prompt,
        [property: JsonPropertyName("result")] string? Result);

    private sealed record SerializedTaskOutputRequest(
        [property: JsonPropertyName("task_id")] string? TaskId,
        [property: JsonPropertyName("block")] bool? Block,
        [property: JsonPropertyName("timeout")] int? Timeout);
}
