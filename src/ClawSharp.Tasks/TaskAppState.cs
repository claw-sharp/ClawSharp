namespace ClawSharp.Tasks;

public sealed partial record TaskAppState(
    IReadOnlyDictionary<string, ClawSharpTask> Tasks,
    IReadOnlyDictionary<string, IReadOnlyList<TodoItem>> Todos,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, BoardTask>> BoardTasks);

public sealed partial record TaskAppState
{
    public TaskAppState(IReadOnlyDictionary<string, ClawSharpTask> Tasks)
        : this(
            Tasks,
            new Dictionary<string, IReadOnlyList<TodoItem>>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyDictionary<string, BoardTask>>(StringComparer.Ordinal))
    {
    }
}
