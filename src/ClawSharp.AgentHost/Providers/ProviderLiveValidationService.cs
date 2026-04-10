using System.Net.Http;
using ClawSharp.AgentHost.Contracts;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;

namespace ClawSharp.AgentHost.Providers;

public interface IProviderLiveValidationService
{
    Task<ProviderValidationDto> ValidateAsync(
        string provider,
        string model,
        ClawSharpSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed class ProviderLiveValidationService : IProviderLiveValidationService
{
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(20);
    private readonly EnvironmentQueryModelHttpClientConfigProvider _configProvider;
    private readonly IQueryModelHttpStreamingClient _streamingClient;

    public ProviderLiveValidationService(
        IMcpSecureStorage? secureStorage = null,
        IQueryModelHttpStreamingClient? streamingClient = null)
    {
        _configProvider = new EnvironmentQueryModelHttpClientConfigProvider(
            secureStorage ?? McpSecureStorageFactory.CreateDefault());
        _streamingClient = streamingClient ?? new QueryModelSseStreamingClient();
    }

    public async Task<ProviderValidationDto> ValidateAsync(
        string provider,
        string model,
        ClawSharpSettings settings,
        CancellationToken cancellationToken = default)
    {
        var runtime = ProviderRuntimeResolver.Resolve(settings, model);
        var session = new ConversationSession(
            $"provider-validation-{Guid.NewGuid():N}",
            Directory.GetCurrentDirectory());
        var request = QueryTurnRequest.Create(session, "Validate provider credentials.");
        var loopState = new QueryLoopState(
            [],
            0,
            QueryToolUseContextState.Empty with { MainLoopModel = model });
        var config = _configProvider.GetConfig(request, loopState, session, settings);
        var streamingRequest = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                session.Id,
                runtime.ResolvedModel,
                [new QuerySystemPromptBlock("Return a minimal response.")],
                [
                    new QueryRequestMessage(
                        "user",
                        [new QueryRequestContentBlock("text", Text: "ping")])
                ],
                [],
                new QueryRequestOutputConfig(),
                [],
                MaxTokens: 16),
            "desktop_provider_validation");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ValidationTimeout);

        try
        {
            await using var enumerator = _streamingClient
                .StreamAsync(config, streamingRequest, timeoutCts.Token)
                .GetAsyncEnumerator(timeoutCts.Token);

            await enumerator.MoveNextAsync();

            return new ProviderValidationDto(
                provider,
                true,
                [],
                []);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new ProviderValidationDto(
                provider,
                false,
                [$"Timed out while validating provider '{provider}' against the live API."],
                []);
        }
        catch (QueryModelApiException exception)
        {
            return new ProviderValidationDto(
                provider,
                false,
                [exception.Message],
                []);
        }
        catch (HttpRequestException exception)
        {
            return new ProviderValidationDto(
                provider,
                false,
                [$"Provider '{provider}' could not be reached: {exception.Message}"],
                []);
        }
    }
}
