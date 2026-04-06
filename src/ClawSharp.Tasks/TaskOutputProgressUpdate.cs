// TS origin: ./utils/task/TaskOutput.ts
namespace ClawSharp.Tasks;

public sealed record TaskOutputProgressUpdate(
    string LastLines,
    string AllLines,
    int TotalLines,
    long TotalBytes,
    bool IsIncomplete);
