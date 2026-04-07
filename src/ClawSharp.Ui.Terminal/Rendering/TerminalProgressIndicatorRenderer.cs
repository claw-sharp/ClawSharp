using System.Globalization;
using System.Text.Json.Nodes;

namespace ClawSharp.Ui.Terminal;

public sealed class TerminalProgressIndicatorRenderer
{
    public string? TryRender(JsonObject progressData)
    {
        var type = progressData["type"]?.GetValue<string>();
        return type switch
        {
            "mcp_progress" => RenderMcpProgress(progressData),
            "waiting_for_task" => RenderWaitingForTask(progressData),
            _ => null
        };
    }

    private static string? RenderMcpProgress(JsonObject progressData)
    {
        var status = progressData["status"]?.GetValue<string>();
        if (string.Equals(status, "completed", StringComparison.Ordinal) ||
            string.Equals(status, "failed", StringComparison.Ordinal))
        {
            return null;
        }

        var serverName = progressData["serverName"]?.GetValue<string>();
        var toolName = progressData["toolName"]?.GetValue<string>();
        var progressMessage = progressData["progressMessage"]?.GetValue<string>();
        var progress = TryGetNumber(progressData["progress"]);
        var total = TryGetNumber(progressData["total"]);

        var headline = !string.IsNullOrWhiteSpace(progressMessage)
            ? progressMessage
            : BuildDefaultMcpHeadline(serverName, toolName);
        if (string.IsNullOrWhiteSpace(headline))
        {
            return null;
        }

        var suffix = BuildProgressSuffix(progress, total);
        return string.IsNullOrWhiteSpace(suffix)
            ? $"  {headline}"
            : $"  {headline} {suffix}";
    }

    private static string? RenderWaitingForTask(JsonObject progressData)
    {
        var taskDescription = progressData["taskDescription"]?.GetValue<string>();
        var lines = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(taskDescription))
        {
            lines.Add($"  {taskDescription}");
        }

        lines.Add("     Waiting for task (esc to give additional instructions)");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildDefaultMcpHeadline(string? serverName, string? toolName)
    {
        if (!string.IsNullOrWhiteSpace(toolName) && !string.IsNullOrWhiteSpace(serverName))
        {
            return $"Running {toolName} via {serverName}...";
        }

        if (!string.IsNullOrWhiteSpace(toolName))
        {
            return $"Running {toolName}...";
        }

        if (!string.IsNullOrWhiteSpace(serverName))
        {
            return $"Running MCP request via {serverName}...";
        }

        return "Running MCP request...";
    }

    private static string? BuildProgressSuffix(double? progress, double? total)
    {
        if (progress is null && total is null)
        {
            return null;
        }

        if (progress is not null && total is not null)
        {
            return $"({FormatNumber(progress.Value)}/{FormatNumber(total.Value)})";
        }

        if (progress is not null)
        {
            return $"({FormatNumber(progress.Value)})";
        }

        return $"(/ {FormatNumber(total!.Value)})";
    }

    private static string FormatNumber(double value)
    {
        return Math.Abs(value % 1) < double.Epsilon
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static double? TryGetNumber(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (value.TryGetValue<long>(out var longValue))
        {
            return longValue;
        }

        if (value.TryGetValue<decimal>(out var decimalValue))
        {
            return (double)decimalValue;
        }

        return null;
    }
}
