// TS origin: ./state/AppState.ts, ./utils/task/framework.ts
namespace ClawSharp.Tasks;

public sealed record TaskAppState(
    IReadOnlyDictionary<string, ClawSharpTask> Tasks);
