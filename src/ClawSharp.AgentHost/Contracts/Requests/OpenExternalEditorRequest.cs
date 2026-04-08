namespace ClawSharp.AgentHost.Contracts;

public sealed class OpenExternalEditorRequest
{
    public string Kind { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public string? Path { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public string? LeftPath { get; init; }
    public string? RightPath { get; init; }
    public string? EditorCommand { get; init; }
}
