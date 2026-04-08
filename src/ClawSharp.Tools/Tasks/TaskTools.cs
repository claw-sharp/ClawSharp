using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Tasks;

namespace ClawSharp.Tools.Tasks;

internal static class TaskToolSchemas
{
    public static JsonObject CreateInputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("subject", ToolJsonSchemaFactory.String("A short title for the task")),
                ("description", ToolJsonSchemaFactory.String("What needs to be done")),
                ("status", ToolJsonSchemaFactory.StringEnum(["pending", "in_progress", "completed"], defaultValue: "pending")),
                ("activeForm", ToolJsonSchemaFactory.String("Present continuous form for spinner (e.g., \"Running tests\")", Required: false)),
                ("blocks", ToolJsonSchemaFactory.Array(ToolJsonSchemaFactory.String(), "IDs this task blocks", Required: false)),
                ("blockedBy", ToolJsonSchemaFactory.Array(ToolJsonSchemaFactory.String(), "IDs that block this task", Required: false)),
                ("metadata", ToolJsonSchemaFactory.Object(Required: false, description: "Arbitrary metadata to attach to the task"))
            ],
            required: ["subject", "description"]);

    public static JsonObject UpdateInputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("id", ToolJsonSchemaFactory.String("Task ID")),
                ("subject", ToolJsonSchemaFactory.String("Update title", Required: false)),
                ("description", ToolJsonSchemaFactory.String("Update description", Required: false)),
                ("status", ToolJsonSchemaFactory.StringEnum(["pending", "in_progress", "completed"], Required: false)),
                ("activeForm", ToolJsonSchemaFactory.String("Update spinner text", Required: false)),
                ("owner", ToolJsonSchemaFactory.String("Agent ID who owns it", Required: false)),
                ("blocks", ToolJsonSchemaFactory.Array(ToolJsonSchemaFactory.String(), Required: false)),
                ("blockedBy", ToolJsonSchemaFactory.Array(ToolJsonSchemaFactory.String(), Required: false)),
                ("metadata", ToolJsonSchemaFactory.Object(Required: false))
            ],
            required: ["id"]);
            
    public static JsonObject TaskInfoSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("id", ToolJsonSchemaFactory.String()),
                ("subject", ToolJsonSchemaFactory.String()),
                ("description", ToolJsonSchemaFactory.String()),
                ("status", ToolJsonSchemaFactory.String()),
                ("activeForm", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.String())),
                ("owner", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.String())),
                ("blocks", ToolJsonSchemaFactory.Array(ToolJsonSchemaFactory.String())),
                ("blockedBy", ToolJsonSchemaFactory.Array(ToolJsonSchemaFactory.String())),
                ("metadata", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.Object(Required: false)))
            ],
            required: ["id", "subject", "description", "status", "blocks", "blockedBy"]);
}

internal sealed class TaskCreateTool : BaseTool
{
    public TaskCreateTool()
        : base(
            new ToolDescriptor(
                "TaskCreate",
                "Create a new task on the board",
                Parameters:
                [
                    new ToolParameter("subject", "Task title"),
                    new ToolParameter("description", "Task detail")
                ],
                InputSchema: TaskToolSchemas.CreateInputSchema,
                OutputSchema: ToolJsonSchemaFactory.StrictObject([("task", TaskToolSchemas.TaskInfoSchema)], ["task"]),
                Strict: true))
    {
    }

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var input = JsonNode.Parse(context.Arguments);
        var subject = input?["subject"]?.GetValue<string>() ?? string.Empty;
        var description = input?["description"]?.GetValue<string>() ?? string.Empty;
        var statusStr = input?["status"]?.GetValue<string>() ?? "pending";
        var activeForm = input?["activeForm"]?.GetValue<string>();
        
        var status = statusStr switch {
            "in_progress" => BoardTaskStatus.InProgress,
            "completed" => BoardTaskStatus.Completed,
            _ => BoardTaskStatus.Pending
        };

        var taskListId = context.WorkspaceRoot; // Use workspace as task list ID by default
        
        var createdTask = (BoardTask?)null;

        context.TaskAppState.SetAppState(prev =>
        {
            var boardTasks = prev.BoardTasks ?? new Dictionary<string, IReadOnlyDictionary<string, BoardTask>>();
            var currentList = boardTasks.TryGetValue(taskListId, out var existing) 
                ? new Dictionary<string, BoardTask>(existing, StringComparer.Ordinal) 
                : new Dictionary<string, BoardTask>(StringComparer.Ordinal);
            
            var highestId = 0;
            foreach (var idStr in currentList.Keys) {
                if (int.TryParse(idStr, out var idVal) && idVal > highestId) highestId = idVal;
            }
            
            var newId = (highestId + 1).ToString();
            createdTask = new BoardTask(
                newId,
                subject,
                description,
                status,
                activeForm,
                Blocks: input?["blocks"]?.AsArray().Select(x => x!.GetValue<string>()).ToList() ?? [],
                BlockedBy: input?["blockedBy"]?.AsArray().Select(x => x!.GetValue<string>()).ToList() ?? [],
                Metadata: input?["metadata"]?.Deserialize<Dictionary<string, object>>()
            );
            
            currentList[newId] = createdTask;
            
            var updatedBoard = new Dictionary<string, IReadOnlyDictionary<string, BoardTask>>(boardTasks, StringComparer.Ordinal) {
                [taskListId] = currentList
            };
            
            return prev with { BoardTasks = updatedBoard };
        });

        if (createdTask == null) return Task.FromResult(Failure("Failed to create task"));

        return Task.FromResult(Success("Task created.", new JsonObject { ["task"] = JsonSerializer.SerializeToNode(createdTask) }));
    }
}

internal sealed class TaskGetTool : BaseTool
{
    public TaskGetTool()
        : base(
            new ToolDescriptor(
                "TaskGet",
                "Get details for a specific task",
                Parameters: [new ToolParameter("id", "Task ID")],
                InputSchema: ToolJsonSchemaFactory.StrictObject([("id", ToolJsonSchemaFactory.String())], ["id"]),
                OutputSchema: ToolJsonSchemaFactory.StrictObject([("task", TaskToolSchemas.TaskInfoSchema)], ["task"]),
                Strict: true))
    {
    }

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var input = JsonNode.Parse(context.Arguments);
        var id = input?["id"]?.GetValue<string>() ?? string.Empty;
        var taskListId = context.WorkspaceRoot;

        var state = context.TaskAppState.GetAppState();
        if (state.BoardTasks?.TryGetValue(taskListId, out var currentList) == true &&
            currentList.TryGetValue(id, out var task))
        {
            return Task.FromResult(Success("Task found.", new JsonObject { ["task"] = JsonSerializer.SerializeToNode(task) }));
        }

        return Task.FromResult(Failure($"Task {id} not found in {taskListId}."));
    }
}

internal sealed class TaskUpdateTool : BaseTool
{
    public TaskUpdateTool()
        : base(
            new ToolDescriptor(
                "TaskUpdate",
                "Update an existing task",
                Parameters: [new ToolParameter("id", "Task ID to update")],
                InputSchema: TaskToolSchemas.UpdateInputSchema,
                OutputSchema: ToolJsonSchemaFactory.StrictObject([("task", TaskToolSchemas.TaskInfoSchema)], ["task"]),
                Strict: true))
    {
    }

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var input = JsonNode.Parse(context.Arguments);
        var id = (string?)input?["id"] ?? string.Empty;
        var taskListId = context.WorkspaceRoot;

        var updatedTask = (BoardTask?)null;
        var found = false;
        var updatedFields = new List<string>();

        context.TaskAppState.SetAppState(prev =>
        {
            if (prev.BoardTasks?.TryGetValue(taskListId, out var currentList) != true ||
                !currentList.TryGetValue(id, out var existing))
            {
                return prev;
            }

            found = true;
            
            var newStatusStr = (string?)input?["status"];
            BoardTaskStatus? parsedStatus = newStatusStr switch {
                "in_progress" => BoardTaskStatus.InProgress,
                "completed" => BoardTaskStatus.Completed,
                "pending" => BoardTaskStatus.Pending,
                _ => null
            };

            var newSubject = (string?)input?["subject"];
            var newDescription = (string?)input?["description"];
            var newActiveForm = (string?)input?["activeForm"];
            var newOwner = (string?)input?["owner"];
            var addBlocks = input?["blocks"]?.AsArray().Select(x => x!.GetValue<string>()).ToList();
            var addBlockedBy = input?["blockedBy"]?.AsArray().Select(x => x!.GetValue<string>()).ToList();
            var metadataUpdates = input?["metadata"]?.Deserialize<Dictionary<string, object>>();

            updatedTask = existing with
            {
                Subject = newSubject ?? existing.Subject,
                Description = newDescription ?? existing.Description,
                ActiveForm = newActiveForm ?? existing.ActiveForm,
                Status = parsedStatus ?? existing.Status,
                Owner = newOwner ?? existing.Owner,
                Blocks = addBlocks != null 
                    ? [.. existing.Blocks ?? [], .. addBlocks] 
                    : existing.Blocks,
                BlockedBy = addBlockedBy != null 
                    ? [.. existing.BlockedBy ?? [], .. addBlockedBy] 
                    : existing.BlockedBy,
                Metadata = metadataUpdates != null 
                    ? MergeMetadata(existing.Metadata, metadataUpdates) 
                    : existing.Metadata
            };
            
            if (newSubject != null) updatedFields.Add("subject");
            if (newDescription != null) updatedFields.Add("description");
            if (newActiveForm != null) updatedFields.Add("activeForm");
            if (parsedStatus.HasValue) updatedFields.Add("status");
            if (newOwner != null) updatedFields.Add("owner");
            if (addBlocks != null && addBlocks.Count > 0) updatedFields.Add("blocks");
            if (addBlockedBy != null && addBlockedBy.Count > 0) updatedFields.Add("blockedBy");
            if (metadataUpdates != null) updatedFields.Add("metadata");

            var newList = new Dictionary<string, BoardTask>(currentList, StringComparer.Ordinal) { [id] = updatedTask };
            var updatedBoard = new Dictionary<string, IReadOnlyDictionary<string, BoardTask>>(prev.BoardTasks, StringComparer.Ordinal) {
                [taskListId] = newList
            };

            return prev with { BoardTasks = updatedBoard };
        });

        if (!found) return Task.FromResult(Failure($"Task {id} not found."));
        
        return Task.FromResult(Success($"Updated task #{id} {string.Join(", ", updatedFields)}", new JsonObject
        {
            ["success"] = true,
            ["taskId"] = id,
            ["updatedFields"] = JsonSerializer.SerializeToNode(updatedFields)
        }));
    }

    private static IReadOnlyDictionary<string, object>? MergeMetadata(
        IReadOnlyDictionary<string, object>? existing, 
        Dictionary<string, object> updates)
    {
        var result = existing != null 
            ? new Dictionary<string, object>(existing, StringComparer.Ordinal) 
            : new Dictionary<string, object>(StringComparer.Ordinal);
            
        foreach (var kvp in updates)
        {
            if (kvp.Value == null)
            {
                result.Remove(kvp.Key);
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        
        return result.Count > 0 ? result : null;
    }
}

internal sealed class TaskListTool : BaseTool
{
    public TaskListTool()
        : base(
            new ToolDescriptor(
                "TaskList",
                "List all tasks on the board",
                Parameters: [],
                InputSchema: ToolJsonSchemaFactory.StrictObject(Array.Empty<(string Name, JsonNode Schema)>(), []),
                OutputSchema: ToolJsonSchemaFactory.StrictObject(
                    [("tasks", ToolJsonSchemaFactory.Array(TaskToolSchemas.TaskInfoSchema))], 
                    ["tasks"]),
                Strict: true))
    {
    }

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var taskListId = context.WorkspaceRoot;
        var state = context.TaskAppState.GetAppState();
        
        var tasks = (state.BoardTasks?.TryGetValue(taskListId, out var currentList) == true)
            ? currentList.Values.ToList()
            : new List<BoardTask>();

        var result = new JsonObject {
            ["tasks"] = new JsonArray(tasks.Select(t => JsonSerializer.SerializeToNode(t)!).ToArray())
        };

        return Task.FromResult(Success($"Found {tasks.Count} tasks.", result));
    }
}
