namespace ClawSharp.Tasks;

public enum BoardTaskStatus
{
    Pending,
    InProgress,
    Completed
}

public sealed record BoardTask(
    string Id,
    string Subject,
    string Description,
    BoardTaskStatus Status,
    string? ActiveForm = null,
    string? Owner = null,
    IReadOnlyList<string>? Blocks = null,
    IReadOnlyList<string>? BlockedBy = null,
    IReadOnlyDictionary<string, object>? Metadata = null);
