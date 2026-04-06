using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClawSharp.Tasks;

namespace ClawSharp.Tools;

internal sealed class TaskStopTool : BaseTool
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TaskStopTool()
        : base(
            new ToolDescriptor(
                "TaskStop",
                "Stop a running background task",
                SearchHint: "kill a running background task",
                Aliases: ["KillShell"],
                InputSchema: CreateInputSchema(),
                OutputSchema: CreateOutputSchema(),
                ShouldDefer: true,
                Strict: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments)
    {
        return true;
    }

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var request, out var errorMessage) || request is null)
        {
            return Task.FromResult(Failure(errorMessage ?? "Missing required parameter: task_id"));
        }

        var taskId = request.TaskId ?? request.ShellId;
        if (!context.Tasks.TryStopTask(taskId!, out var errorCode, out var taskType, out var command))
        {
            return Task.FromResult(Failure(BuildFailureMessage(taskId!, errorCode, taskType)));
        }

        var output = new TaskStopOutput(
            $"Successfully stopped task: {taskId} ({command})",
            taskId!,
            taskType!,
            command);
        return Task.FromResult(Success(
            output.Message,
            (JsonObject)JsonSerializer.SerializeToNode(output, SerializerOptions)!));
    }

    public override Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var request, out var errorMessage) || request is null)
        {
            return Task.FromResult(ToolValidationResult.Invalid(errorMessage ?? "Missing required parameter: task_id"));
        }

        var taskId = request.TaskId ?? request.ShellId;
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return Task.FromResult(ToolValidationResult.Invalid("Missing required parameter: task_id"));
        }

        var taskState = context.TaskAppState.GetAppState();
        if (!taskState.Tasks.TryGetValue(taskId, out var task))
        {
            return Task.FromResult(ToolValidationResult.Invalid($"No task found with ID: {taskId}"));
        }

        if (task.Status != ClawSharp.Tasks.TaskStatus.Running)
        {
            return Task.FromResult(ToolValidationResult.Invalid(
                $"Task {taskId} is not running (status: {task.Status.ToSerializedName()})"));
        }

        return Task.FromResult(ToolValidationResult.Valid());
    }

    private static bool TryParseArguments(string arguments, out TaskStopRequest? request, out string? errorMessage)
    {
        request = null;
        errorMessage = null;

        try
        {
            request = JsonSerializer.Deserialize<TaskStopRequest>(arguments, SerializerOptions);
            if (request is null || (string.IsNullOrWhiteSpace(request.TaskId) && string.IsNullOrWhiteSpace(request.ShellId)))
            {
                errorMessage = "Missing required parameter: task_id";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            errorMessage = "TaskStop arguments must be a JSON object.";
            return false;
        }
    }

    private static string BuildFailureMessage(string taskId, string? errorCode, string? taskType)
    {
        return errorCode switch
        {
            "not_found" => $"No task found with ID: {taskId}",
            "not_running" => $"Task {taskId} is not running.",
            "unsupported_type" => $"Unsupported task type: {taskType}",
            _ => $"Unable to stop task {taskId}."
        };
    }

    private static JsonObject CreateInputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["task_id"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The ID of the background task to stop"
                },
                ["shell_id"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Deprecated: use task_id instead"
                }
            }
        };
    }

    private static JsonObject CreateOutputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("message", "task_id", "task_type"),
            ["properties"] = new JsonObject
            {
                ["message"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["task_id"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["task_type"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["command"] = new JsonObject
                {
                    ["type"] = "string"
                }
            }
        };
    }

    private sealed record TaskStopRequest(
        [property: JsonPropertyName("task_id")] string? TaskId,
        [property: JsonPropertyName("shell_id")] string? ShellId);

    private sealed record TaskStopOutput(
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("task_id")] string TaskId,
        [property: JsonPropertyName("task_type")] string TaskType,
        [property: JsonPropertyName("command")] string? Command);
}
