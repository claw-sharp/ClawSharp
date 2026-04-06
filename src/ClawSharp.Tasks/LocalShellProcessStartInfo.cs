// TS origin: ./utils/Shell.ts, ./utils/ShellCommand.ts
namespace ClawSharp.Tasks;

public sealed record LocalShellProcessStartInfo(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    TaskOutput TaskOutput,
    int TimeoutMs,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null);
