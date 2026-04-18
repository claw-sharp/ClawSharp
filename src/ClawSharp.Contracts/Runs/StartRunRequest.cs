namespace ClawSharp.Contracts.Runs;

public sealed record StartRunRequest(
    string ProjectId,
    string ThreadId,
    string Prompt);
