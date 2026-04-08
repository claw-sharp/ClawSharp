namespace ClawSharp.Tasks;

public enum TodoStatus
{
    Pending,
    InProgress,
    Completed
}

public sealed record TodoItem(
    string Content,
    TodoStatus Status,
    string ActiveForm);
