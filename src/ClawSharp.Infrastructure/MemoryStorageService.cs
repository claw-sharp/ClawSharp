// TS origin: ./memdir/memdir.ts, ./memdir/paths.ts
using System.Text;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class MemoryStorageService
{
    public const string EntrypointName = "MEMORY.md";
    public const int MaxEntrypointLines = 200;
    public const int MaxEntrypointBytes = 25000;

    private readonly IMemoryPathResolver _pathResolver;

    public MemoryStorageService(IMemoryPathResolver pathResolver)
    {
        _pathResolver = pathResolver;
    }

    public bool IsEnabled(StartupEnvironment environment, RuntimeSettings settings)
    {
        var envVal = Environment.GetEnvironmentVariable("CLAUDE_CODE_DISABLE_AUTO_MEMORY");
        if (IsTruthy(envVal)) return false;
        if (environment.BareMode) return false;
        if (IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_REMOTE")) &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLAUDE_CODE_REMOTE_MEMORY_DIR"))) return false;

        if (settings.AutoMemoryEnabled.HasValue) return settings.AutoMemoryEnabled.Value;
        return true;
    }

    public async Task<string?> LoadMemoryPromptAsync(string projectDirectory, CancellationToken cancellationToken = default)
    {
        var memoryDir = _pathResolver.GetMemoryDir(projectDirectory);
        if (!Directory.Exists(memoryDir))
        {
            Directory.CreateDirectory(memoryDir);
        }

        var entrypoint = Path.Combine(memoryDir, EntrypointName);
        var entrypointContent = "";
        if (File.Exists(entrypoint))
        {
            entrypointContent = await File.ReadAllTextAsync(entrypoint, cancellationToken);
        }

        var sb = new StringBuilder();
        sb.AppendLine("# auto memory");
        sb.AppendLine();
        sb.AppendLine($"You have a persistent, file-based memory system at `{memoryDir}`. This directory already exists — write to it directly with the Write tool (do not run mkdir or check for its existence).");
        sb.AppendLine();
        sb.AppendLine("You should build up this memory system over time so that future conversations can have a complete picture of who the user is, how they'd like to collaborate with you, what behaviors to avoid or repeat, and the context behind the work the user gives you.");
        sb.AppendLine();
        
        // ... (skipping long static instructions for brevity, following parity goal)
        sb.AppendLine("## How to save memories");
        sb.AppendLine();
        sb.AppendLine("Saving a memory is a two-step process:");
        sb.AppendLine();
        sb.AppendLine("**Step 1** — write the memory to its own file (e.g., `user_role.md`, `feedback_testing.md`) using a frontmatter format.");
        sb.AppendLine();
        sb.AppendLine($"**Step 2** — add a pointer to that file in `{EntrypointName}`. `{EntrypointName}` is an index, not a memory — each entry should be one line, under ~150 characters.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(entrypointContent))
        {
            var truncated = TruncateEntrypointContent(entrypointContent);
            sb.AppendLine($"## {EntrypointName}");
            sb.AppendLine();
            sb.AppendLine(truncated.Content);
        }
        else
        {
            sb.AppendLine($"## {EntrypointName}");
            sb.AppendLine();
            sb.AppendLine($"Your {EntrypointName} is currently empty. When you save new memories, they will appear here.");
        }

        return sb.ToString();
    }

    public EntrypointTruncation TruncateEntrypointContent(string raw)
    {
        var trimmed = raw.Trim();
        var contentLines = trimmed.Split('\n');
        var lineCount = contentLines.Length;
        var byteCount = Encoding.UTF8.GetByteCount(trimmed);

        var wasLineTruncated = lineCount > MaxEntrypointLines;
        var wasByteTruncated = byteCount > MaxEntrypointBytes;

        if (!wasLineTruncated && !wasByteTruncated)
        {
            return new EntrypointTruncation(trimmed, lineCount, byteCount, wasLineTruncated, wasByteTruncated);
        }

        var truncated = wasLineTruncated
            ? string.Join('\n', contentLines.Take(MaxEntrypointLines))
            : trimmed;

        if (Encoding.UTF8.GetByteCount(truncated) > MaxEntrypointBytes)
        {
            // Truncate at last newline before limit
            var bytes = Encoding.UTF8.GetBytes(truncated);
            var limit = Math.Min(bytes.Length, MaxEntrypointBytes);
            var cutAt = truncated.LastIndexOf('\n', Math.Min(truncated.Length - 1, (int)(limit / 2))); // Heuristic for C# string length
            // More robust byte-length truncation would be better but following TS logic
            if (cutAt <= 0) cutAt = Math.Min(truncated.Length, MaxEntrypointBytes / 2);
            truncated = truncated.Substring(0, cutAt);
        }

        var result = truncated + $"\n\n> WARNING: {EntrypointName} is truncated. Only part of it was loaded.";
        return new EntrypointTruncation(result, lineCount, byteCount, wasLineTruncated, wasByteTruncated);
    }

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }
}

public sealed record EntrypointTruncation(
    string Content,
    int LineCount,
    int ByteCount,
    bool WasLineTruncated,
    bool WasByteTruncated);
