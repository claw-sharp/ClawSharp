namespace ClawSharp.Tools;

internal sealed class UnavailableTool : BaseTool
{
    private readonly string _reason;

    public UnavailableTool(ToolDescriptor descriptor, string reason)
        : base(descriptor)
    {
        _reason = reason;
    }

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Failure(_reason));
    }
}
