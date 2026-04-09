using ClawSharp.Core;
using ClawSharp.Tools;

namespace ClawSharp.Query;

public static class MainThreadTurnRequestBuilder
{
    public static async Task<QueryTurnRequest> BuildAsync(
        ConversationSession session,
        string userInput,
        ToolRegistry? toolRegistry = null,
        IQueryModelTurnContextProvider? modelTurnContextProvider = null,
        CancellationToken cancellationToken = default)
    {
        var request = QueryTurnRequest.Create(session, userInput) with
        {
            AbortReason = QueryAbortReason.Interrupt
        };

        if (toolRegistry is not null)
        {
            request = request with
            {
                InitialToolUseContext = QueryToolUseContextStateFactory.CreateFromToolRegistry(toolRegistry)
            };
        }

        if (modelTurnContextProvider is not null)
        {
            request = request with
            {
                ModelTurnContext = await modelTurnContextProvider.GetReplMainThreadContextAsync(cancellationToken)
            };
        }

        return request;
    }
}
