namespace ClawSharp.Tools;

using System.Text.Json.Nodes;

public interface IClawSharpTool
{
    ToolDescriptor Descriptor { get; }

    bool IsEnabled();

    bool IsConcurrencySafe(string arguments);

    bool IsReadOnly(string arguments);

    string? RenderToolUseMessage(string arguments)
    {
        return null;
    }

    string? RenderToolUseProgressMessage(IReadOnlyList<ToolProgressUpdate> progressMessages);

    string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages);

    Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default);

    Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default);
}
