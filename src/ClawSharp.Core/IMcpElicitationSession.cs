// TS origin: ./services/mcp/elicitationHandler.ts, ./services/mcp/client.ts
namespace ClawSharp.Core;

public interface IMcpElicitationSession
{
    void SetElicitationRequestHandler(Func<McpElicitationRequestContext, Task<McpElicitResult>> handler);

    void SetElicitationCompletionHandler(Action<string> handler);
}
