// TS parity status: ports the per-iteration model request snapshot shaping from the current C# loop state and carried model-turn context; current C# reuses the existing request builder foundation and the currently available tool catalog only.
using ClawSharp.Core;
using ClawSharp.Tools;
using System.Text.Json.Nodes;

namespace ClawSharp.Query;

public sealed class QueryModelIterationRequestBuilder : IQueryModelIterationRequestBuilder
{
    private readonly QueryRequestBuilder _queryRequestBuilder;
    private readonly IReadOnlyList<ToolDescriptor> _availableTools;
    private readonly Func<IReadOnlyList<ToolDescriptor>>? _availableToolsProvider;

    public QueryModelIterationRequestBuilder(
        QueryRequestBuilder? queryRequestBuilder = null,
        IReadOnlyList<ToolDescriptor>? availableTools = null,
        Func<IReadOnlyList<ToolDescriptor>>? availableToolsProvider = null)
    {
        _queryRequestBuilder = queryRequestBuilder ?? new QueryRequestBuilder();
        _availableTools = availableTools ?? [];
        _availableToolsProvider = availableToolsProvider;
    }

    public QueryModelHttpStreamingRequest Build(
        QueryTurnRequest request,
        QueryLoopState state,
        ClawSharpSettings settings)
    {
        var modelTurnContext = request.ModelTurnContext ?? QueryModelTurnContext.ReplMainThread;
        var effectiveMessages = state.Messages;
        var userContext = modelTurnContext.UserContext;
        var hasPreviousResponseState =
            !string.IsNullOrWhiteSpace(state.PreviousResponseId) ||
            state.PreviousResponseItems?.Count > 0;
        if (hasPreviousResponseState &&
            state.PreviousResponseMessageCount is int previousResponseMessageCount &&
            previousResponseMessageCount >= 0 &&
            previousResponseMessageCount <= state.Messages.Count)
        {
            effectiveMessages = state.Messages.Skip(previousResponseMessageCount).ToArray();
            userContext = null;
        }

        var modelRequest = _queryRequestBuilder.BuildFromMessages(
            request,
            effectiveMessages,
            settings,
            _availableToolsProvider?.Invoke() ?? _availableTools,
            new QueryRequestBuildOptions(
                SystemPrompt: modelTurnContext.SystemPrompt,
                SystemContext: modelTurnContext.SystemContext,
                UserContext: userContext,
                TaskBudget: request.TaskBudget,
                ShouldIncludeFirstPartyOnlyBetas: request.TaskBudget is not null));

        if (!string.IsNullOrWhiteSpace(state.PreviousResponseId))
        {
            modelRequest = modelRequest with
            {
                PreviousResponseId = state.PreviousResponseId
            };
        }

        if (state.PreviousResponseItems?.Count > 0)
        {
            modelRequest = modelRequest with
            {
                PreviousResponseItems = state.PreviousResponseItems
            };
        }

        if (hasPreviousResponseState ||
            modelRequest.Messages.Any(
                static message =>
                    string.Equals(message.Role, "user", StringComparison.Ordinal) &&
                    message.Content.Any(static block => string.Equals(block.Type, "tool_result", StringComparison.Ordinal))))
        {
            var expectedToolUseIds = ExtractExpectedToolUseIds(modelRequest.PreviousResponseItems);
            modelRequest = modelRequest with
            {
                Messages = QueryRequestToolResultPairingRepair.EnsureToolResultPairing(
                    modelRequest.Messages,
                    expectedToolUseIds)
            };
        }

        if (!string.IsNullOrWhiteSpace(state.ToolUseContext.MainLoopModel) &&
            !string.Equals(modelRequest.Model, state.ToolUseContext.MainLoopModel, StringComparison.Ordinal))
        {
            modelRequest = modelRequest with
            {
                Model = state.ToolUseContext.MainLoopModel,
                MaxTokens = QueryMaxOutputTokensResolver.GetMaxOutputTokensForModel(state.ToolUseContext.MainLoopModel)
            };
        }

        var providerRuntimeConfig = ProviderRuntimeResolver.Resolve(settings, modelRequest.Model);
        if (!string.Equals(modelRequest.Model, providerRuntimeConfig.ResolvedModel, StringComparison.Ordinal))
        {
            modelRequest = modelRequest with
            {
                Model = providerRuntimeConfig.ResolvedModel,
                MaxTokens = QueryMaxOutputTokensResolver.GetMaxOutputTokensForModel(providerRuntimeConfig.ResolvedModel)
            };
        }

        if (state.MaxOutputTokensOverride is not null &&
            modelRequest.MaxTokens != state.MaxOutputTokensOverride)
        {
            modelRequest = modelRequest with { MaxTokens = state.MaxOutputTokensOverride.Value };
        }

        return new QueryModelHttpStreamingRequest(modelRequest, modelTurnContext.QuerySource);
    }

    private static IReadOnlyList<string> ExtractExpectedToolUseIds(IReadOnlyList<string>? previousResponseItems)
    {
        if (previousResponseItems is null || previousResponseItems.Count == 0)
        {
            return [];
        }

        List<string> result = [];
        foreach (var rawItem in previousResponseItems)
        {
            if (string.IsNullOrWhiteSpace(rawItem))
            {
                continue;
            }

            JsonObject? parsed;
            try
            {
                parsed = JsonNode.Parse(rawItem)?.AsObject();
            }
            catch
            {
                continue;
            }

            if (!string.Equals(parsed?["type"]?.GetValue<string>(), "function_call", StringComparison.Ordinal))
            {
                continue;
            }

            var callId = parsed?["call_id"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(callId))
            {
                result.Add(callId);
            }
        }

        return result;
    }
}
