// TS origin: ./Tool.ts
using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

internal abstract class BaseTool : IClawSharpTool
{
    protected BaseTool(ToolDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public ToolDescriptor Descriptor { get; }

    public virtual bool IsEnabled()
    {
        return true;
    }

    public virtual bool IsConcurrencySafe(string arguments)
    {
        return false;
    }

    public virtual bool IsReadOnly(string arguments)
    {
        return false;
    }

    public virtual string? RenderToolUseProgressMessage(IReadOnlyList<ToolProgressUpdate> progressMessages)
    {
        return null;
    }

    public virtual string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages)
    {
        return null;
    }

    public virtual Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ToolValidationResult.Valid());
    }

    public abstract Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default);

    protected static ToolExecutionResult Failure(string output, JsonNode? structuredOutput = null)
    {
        return new ToolExecutionResult(false, output, structuredOutput);
    }

    protected static ToolExecutionResult Success(string output, JsonNode? structuredOutput = null)
    {
        return new ToolExecutionResult(true, output, structuredOutput);
    }
}
