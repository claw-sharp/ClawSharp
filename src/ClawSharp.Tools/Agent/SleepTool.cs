using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools;

internal sealed class SleepTool : BaseTool
{
    private const int DefaultSleepMs = 5000;

    public SleepTool()
        : base(new ToolDescriptor(
            "Sleep",
            "Wait for a specified duration",
            Parameters: [
                new ToolParameter("duration_ms", "Duration to sleep in milliseconds", Required: false),
                new ToolParameter("reason", "Reason for sleeping", Required: false)
            ],
            InputSchema: SleepToolSchemas.InputSchema,
            OutputSchema: SleepToolSchemas.OutputSchema,
            Strict: true,
            SearchHint: "wait/rest until the user interacts or a timer expires"))
    {
    }

    public override bool IsConcurrencySafe(string arguments) => true;
    public override bool IsReadOnly(string arguments) => true;

    public override string? RenderToolUseMessage(string arguments)
    {
        try
        {
            var node = JsonNode.Parse(arguments);
            var duration = node?["duration_ms"]?.GetValue<double>() ?? DefaultSleepMs;
            var reason = node?["reason"]?.GetValue<string>();
            var suffix = string.IsNullOrWhiteSpace(reason) ? "" : $" ({reason})";
            return $"Sleeping for {duration / 1000:0.#}s{suffix}";
        }
        catch { }
        return "Sleeping";
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var input = JsonNode.Parse(context.Arguments);
        var durationMs = (int)(input?["duration_ms"]?.GetValue<double>() ?? DefaultSleepMs);
        
        try
        {
            await Task.Delay(durationMs, cancellationToken);
            return Success("Sleep complete");
        }
        catch (OperationCanceledException)
        {
            return Success("Sleep interrupted");
        }
    }
}
