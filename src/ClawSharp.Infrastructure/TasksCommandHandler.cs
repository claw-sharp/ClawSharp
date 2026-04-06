// TS origin: ./commands/tasks/index.ts, ./commands/tasks/tasks.tsx, ./components/tasks/BackgroundTasksDialog.tsx
using ClawSharp.Core;
using ClawSharp.Tasks;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.Infrastructure;

public sealed class TasksCommandHandler : ICommandHandler
{
    private readonly BackgroundTasksCommandRenderer _renderer;

    public TasksCommandHandler(BackgroundTasksCommandRenderer? renderer = null)
    {
        _renderer = renderer ?? new BackgroundTasksCommandRenderer();
    }

    public CommandDescriptor Descriptor { get; } =
        new(
            "tasks",
            "List and inspect background tasks",
            "/tasks [task-id]",
            IsInteractive: true,
            Aliases: ["bashes"]);

    public async Task<CommandResult> ExecuteAsync(
        string input,
        CommandExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var state = context.AppStateStore.GetState();
        var argument = ParseArgument(input);
        if (string.IsNullOrWhiteSpace(argument))
        {
            return new CommandResult(true, _renderer.RenderList(state.Tasks));
        }

        if (!state.Tasks.TryGetValue(argument, out var task))
        {
            return new CommandResult(true, $"Background task {argument} was not found.");
        }

        var output = await ReadTaskOutputAsync(task, cancellationToken);
        return new CommandResult(true, _renderer.RenderDetail(task, output));
    }

    private static string? ParseArgument(string input)
    {
        var trimmed = input.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace < 0 || firstSpace == trimmed.Length - 1)
        {
            return null;
        }

        return trimmed[(firstSpace + 1)..].Trim();
    }

    private static async Task<string?> ReadTaskOutputAsync(ClawSharpTask task, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(task.OutputFile) || !File.Exists(task.OutputFile))
        {
            return null;
        }

        return await File.ReadAllTextAsync(task.OutputFile, cancellationToken);
    }
}
