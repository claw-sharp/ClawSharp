// TS origin: ./tools/BashTool/BashTool.tsx, ./tools/PowerShellTool/PowerShellTool.tsx, ./utils/toolResultStorage.ts
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClawSharp.Core;
using ClawSharp.Tasks;

namespace ClawSharp.Tools;

internal sealed record ShellToolOutput(
    [property: JsonPropertyName("stdout")] string Stdout,
    [property: JsonPropertyName("stderr")] string Stderr,
    [property: JsonPropertyName("interrupted")] bool Interrupted,
    [property: JsonPropertyName("backgroundTaskId")] string? BackgroundTaskId = null,
    [property: JsonPropertyName("backgroundedByUser")] bool? BackgroundedByUser = null,
    [property: JsonPropertyName("assistantAutoBackgrounded")] bool? AssistantAutoBackgrounded = null,
    [property: JsonPropertyName("dangerouslyDisableSandbox")] bool? DangerouslyDisableSandbox = null,
    [property: JsonPropertyName("returnCodeInterpretation")] string? ReturnCodeInterpretation = null,
    [property: JsonPropertyName("persistedOutputPath")] string? PersistedOutputPath = null,
    [property: JsonPropertyName("persistedOutputSize")] long? PersistedOutputSize = null,
    [property: JsonPropertyName("isImage")] bool? IsImage = null,
    [property: JsonPropertyName("noOutputExpected")] bool? NoOutputExpected = null);

internal static class ShellToolExecutionSupport
{
    public const int DefaultTimeoutMs = 300_000;
    public const int ProgressThresholdMs = 2_000;
    public const int AssistantBlockingBudgetMs = 15_000;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static bool AreBackgroundTasksDisabled()
    {
        return IsEnvironmentTruthy("CLAUDE_CODE_DISABLE_BACKGROUND_TASKS");
    }

    public static bool ShouldAssistantAutoBackground(
        ToolExecutionContext context,
        ShellToolKind kind,
        string command)
    {
        return !AreBackgroundTasksDisabled() &&
               string.Equals(context.QuerySource, "repl_main_thread", StringComparison.Ordinal) &&
               ShellCommandSemantics.IsAutoBackgroundingAllowed(kind, command);
    }

    public static Action<TaskOutputProgressUpdate> CreateProgressCallback(
        ToolExecutionContext context,
        string toolName,
        int timeoutMs,
        string taskId,
        Stopwatch stopwatch)
    {
        return progress =>
        {
            if (stopwatch.ElapsedMilliseconds < ProgressThresholdMs)
            {
                return;
            }

            var payload = new JsonObject
            {
                ["type"] = $"{toolName.ToLowerInvariant()}_progress",
                ["output"] = progress.LastLines,
                ["fullOutput"] = progress.AllLines,
                ["elapsedTimeSeconds"] = (int)Math.Floor(stopwatch.Elapsed.TotalSeconds),
                ["totalLines"] = progress.TotalLines,
                ["totalBytes"] = progress.IsIncomplete ? progress.TotalBytes : 0,
                ["timeoutMs"] = timeoutMs,
                ["taskId"] = taskId
            };
            context.ReportProgress(toolName, payload);
        };
    }

    public static string BuildToolResultContent(ShellToolOutput output, string? backgroundOutputPath = null)
    {
        var processedStdout = output.Stdout;
        if (!string.IsNullOrWhiteSpace(output.PersistedOutputPath) && output.PersistedOutputSize is not null)
        {
            var previewSource = TrimStdout(output.Stdout);
            processedStdout = ShellToolResultStorage.BuildLargeToolResultMessage(
                previewSource,
                new PersistedShellOutput(output.PersistedOutputPath!, output.PersistedOutputSize.Value));
        }
        else if (!string.IsNullOrWhiteSpace(processedStdout))
        {
            processedStdout = TrimStdout(processedStdout);
        }

        var stderr = (output.Stderr ?? string.Empty).Trim();
        if (output.Interrupted)
        {
            stderr = string.IsNullOrWhiteSpace(stderr)
                ? "<error>Command was aborted before completion</error>"
                : stderr + "\n<error>Command was aborted before completion</error>";
        }

        var backgroundInfo = BuildBackgroundInfo(output, backgroundOutputPath);
        return string.Join(
            "\n",
            new[] { processedStdout, stderr, backgroundInfo }
                .Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    public static JsonObject CreateStructuredOutput(ShellToolOutput output)
    {
        return (JsonObject)JsonSerializer.SerializeToNode(output, SerializerOptions)!;
    }

    public static string? ResolveOwningAgentId(ToolExecutionContext context)
    {
        return context.Tasks.TryGet(context.Session.Id, out var currentTask) && currentTask is LocalAgentTask
            ? context.Session.Id
            : null;
    }

    private static string BuildBackgroundInfo(ShellToolOutput output, string? backgroundOutputPath)
    {
        if (string.IsNullOrWhiteSpace(output.BackgroundTaskId))
        {
            return string.Empty;
        }

        var outputPath = backgroundOutputPath ?? string.Empty;
        if (output.AssistantAutoBackgrounded == true)
        {
            return
                $"Command exceeded the assistant-mode blocking budget ({AssistantBlockingBudgetMs / 1000}s) and was moved to the background with ID: {output.BackgroundTaskId}. " +
                $"It is still running - you will be notified when it completes. Output is being written to: {outputPath}. " +
                "In assistant mode, delegate long-running work to a subagent or use run_in_background to keep this conversation responsive.";
        }

        if (output.BackgroundedByUser == true)
        {
            return $"Command was manually backgrounded by user with ID: {output.BackgroundTaskId}. Output is being written to: {outputPath}";
        }

        return $"Command running in background with ID: {output.BackgroundTaskId}. Output is being written to: {outputPath}";
    }

    private static string TrimStdout(string stdout)
    {
        return stdout
            .TrimStart('\r', '\n')
            .TrimEnd();
    }

    private static bool IsEnvironmentTruthy(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
