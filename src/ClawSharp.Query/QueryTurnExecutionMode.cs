// TS parity status: ports the minimal execution-mode discriminator the C# query loop needs to route between explicit-tool iterations and model-backed iterations without inventing continuation control flow.
namespace ClawSharp.Query;

public enum QueryTurnExecutionMode
{
    Auto,
    ExplicitTool,
    ModelBacked
}
