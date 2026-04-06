// TS origin: ./query.ts, ./services/api/claude.ts, ./utils/api.ts
// TS parity status: ports the per-iteration model request snapshot shaping from the current C# loop state and carried model-turn context; current C# reuses the existing request builder foundation and the currently available tool catalog only.
using ClawSharp.Core;
using ClawSharp.Tools;

namespace ClawSharp.Query;

public sealed class QueryModelIterationRequestBuilder : IQueryModelIterationRequestBuilder
{
    private readonly QueryRequestBuilder _queryRequestBuilder;
    private readonly IReadOnlyList<ToolDescriptor> _availableTools;

    public QueryModelIterationRequestBuilder(
        QueryRequestBuilder? queryRequestBuilder = null,
        IReadOnlyList<ToolDescriptor>? availableTools = null)
    {
        _queryRequestBuilder = queryRequestBuilder ?? new QueryRequestBuilder();
        _availableTools = availableTools ?? [];
    }

    public QueryModelHttpStreamingRequest Build(
        QueryTurnRequest request,
        QueryLoopState state,
        ClawSharpSettings settings)
    {
        var modelTurnContext = request.ModelTurnContext ?? QueryModelTurnContext.ReplMainThread;
        var modelRequest = _queryRequestBuilder.BuildFromMessages(
            request,
            state.Messages,
            settings,
            _availableTools,
            new QueryRequestBuildOptions(
                SystemPrompt: modelTurnContext.SystemPrompt,
                SystemContext: modelTurnContext.SystemContext,
                UserContext: modelTurnContext.UserContext,
                TaskBudget: request.TaskBudget,
                ShouldIncludeFirstPartyOnlyBetas: request.TaskBudget is not null));

        if (!string.IsNullOrWhiteSpace(state.ToolUseContext.MainLoopModel) &&
            !string.Equals(modelRequest.Model, state.ToolUseContext.MainLoopModel, StringComparison.Ordinal))
        {
            modelRequest = modelRequest with
            {
                Model = state.ToolUseContext.MainLoopModel,
                MaxTokens = QueryMaxOutputTokensResolver.GetMaxOutputTokensForModel(state.ToolUseContext.MainLoopModel)
            };
        }

        if (state.MaxOutputTokensOverride is not null &&
            modelRequest.MaxTokens != state.MaxOutputTokensOverride)
        {
            modelRequest = modelRequest with { MaxTokens = state.MaxOutputTokensOverride.Value };
        }

        return new QueryModelHttpStreamingRequest(modelRequest, modelTurnContext.QuerySource);
    }
}
