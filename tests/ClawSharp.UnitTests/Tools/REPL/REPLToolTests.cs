using ClawSharp.Core;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class REPLToolTests
{
    [Fact]
    public async Task ExecuteAsync_Accepts_JsonEncoded_String_Input()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-repl-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);

        var registry = new ToolRegistry(workspaceRoot, new TaskRegistry());
        registry.Register(new EchoArgumentsTool());

        var session = new DefaultSessionFactory(workspaceRoot).Create();
        var result = await registry.ExecuteAsync(
            "REPL",
            """
            {
              "actions": [
                {
                  "tool": "EchoArguments",
                  "input": "{\"value\":123,\"label\":\"ok\"}"
                }
              ]
            }
            """,
            session,
            new ClawSharpSettings());

        Assert.True(result.Success);

        var entries = Assert.IsType<System.Text.Json.Nodes.JsonArray>(result.StructuredOutput);
        var first = Assert.IsType<System.Text.Json.Nodes.JsonObject>(Assert.Single(entries));
        Assert.Equal("EchoArguments", first["tool"]?.GetValue<string>());
        Assert.Equal("{\"value\":123,\"label\":\"ok\"}", first["output"]?.GetValue<string>());
        Assert.True(first["success"]?.GetValue<bool>());
    }

    private sealed class EchoArgumentsTool : IClawSharpTool
    {
        public ToolDescriptor Descriptor { get; } = new(
            "EchoArguments",
            "Returns the raw argument payload.");

        public bool IsEnabled() => true;

        public bool IsConcurrencySafe(string arguments) => true;

        public bool IsReadOnly(string arguments) => true;

        public string? RenderToolUseProgressMessage(IReadOnlyList<ToolProgressUpdate> progressMessages) => null;

        public string? RenderToolResultMessage(string content, System.Text.Json.Nodes.JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages) => content;

        public Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ToolValidationResult.Valid());
        }

        public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ToolExecutionResult(true, context.Arguments));
        }
    }
}
