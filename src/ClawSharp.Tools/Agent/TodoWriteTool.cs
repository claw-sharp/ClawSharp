using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Tasks;

namespace ClawSharp.Tools;

internal sealed class TodoWriteTool : BaseTool
{
    public TodoWriteTool()
        : base(new ToolDescriptor(
            "TodoWrite",
            "Update the todo list for the current session",
            Parameters: [
                new ToolParameter("todos", "The updated todo list")
            ],
            InputSchema: TodoWriteToolSchemas.InputSchema,
            OutputSchema: TodoWriteToolSchemas.OutputSchema,
            Strict: true,
            ShouldDefer: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments) => true;

    public override string? RenderToolUseMessage(string arguments) => null;

    public override string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages)
    {
        // TS implementation returns nothing for rendering, handles it via UI
        return null;
    }

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var input = JsonNode.Parse(context.Arguments);
        var todosNode = input?["todos"]?.AsArray();
        if (todosNode == null)
        {
            return Task.FromResult(Failure("Missing todos array."));
        }

        var newTodoItems = new List<TodoItem>();
        foreach (var node in todosNode)
        {
            var content = node?["content"]?.GetValue<string>() ?? "";
            var statusStr = node?["status"]?.GetValue<string>() ?? "pending";
            var activeForm = node?["activeForm"]?.GetValue<string>() ?? "";

            var status = statusStr switch
            {
                "in_progress" => TodoStatus.InProgress,
                "completed" => TodoStatus.Completed,
                _ => TodoStatus.Pending
            };

            newTodoItems.Add(new TodoItem(content, status, activeForm));
        }

        var appState = context.TaskAppState.GetAppState();
        // In ClawSharp, we use the session ID as the key for the main thread todos.
        // If it's a subagent, we use the agent ID.
        var todoKey = context.AgentId ?? context.Session.Id;
        var oldTodos = appState.Todos.TryGetValue(todoKey, out var existing) ? existing : [];

        var allDone = newTodoItems.All(t => t.Status == TodoStatus.Completed);
        var finalTodos = allDone ? new List<TodoItem>() : newTodoItems;

        context.TaskAppState.SetAppState(prev =>
        {
            var updatedTodos = new Dictionary<string, IReadOnlyList<TodoItem>>(prev.Todos, StringComparer.Ordinal)
            {
                [todoKey] = finalTodos.AsReadOnly()
            };
            return prev with { Todos = updatedTodos };
        });

        // Verification nudge logic (placeholder - depends on feature flags)
        bool verificationNudgeNeeded = false;
        // In the TS source, it checks feature('VERIFICATION_AGENT') && tengu_hive_evidence ...
        // We'll skip the nudge for now or implement it as a simple check if we want parity.

        var structuredOutput = new JsonObject
        {
            ["oldTodos"] = JsonSerializer.SerializeToNode(oldTodos),
            ["newTodos"] = JsonSerializer.SerializeToNode(newTodoItems),
            ["verificationNudgeNeeded"] = verificationNudgeNeeded
        };

        var resultMessage = "Todos have been modified successfully. Ensure that you continue to use the todo list to track your progress. Please proceed with the current tasks if applicable";

        return Task.FromResult(Success(resultMessage, structuredOutput));
    }
}
