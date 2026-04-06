// TS origin: ./utils/ShellCommand.ts
namespace ClawSharp.Tasks;

public sealed record LocalShellExecutionResult(
    string Stdout,
    string Stderr,
    int Code,
    bool Interrupted,
    string? BackgroundTaskId = null,
    bool? BackgroundedByUser = null,
    bool? AssistantAutoBackgrounded = null,
    string? OutputFilePath = null,
    long? OutputFileSize = null,
    string? OutputTaskId = null,
    string? PreSpawnError = null);
