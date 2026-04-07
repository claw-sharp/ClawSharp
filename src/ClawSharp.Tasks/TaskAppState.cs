namespace ClawSharp.Tasks;

public sealed record TaskAppState(
    IReadOnlyDictionary<string, ClawSharpTask> Tasks,
    IReadOnlyDictionary<string, IReadOnlyList<TodoItem>> Todos);
