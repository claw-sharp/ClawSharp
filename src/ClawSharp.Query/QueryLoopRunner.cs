// TS origin: ./query.ts
// TS parity status: ports the outer TypeScript query-loop coordinator structure around per-iteration execution; full 1:1 behavior still depends on model-backed continuation-producing iterations.
using System.Threading.Channels;
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class QueryLoopRunner : IQueryTurnRunner
{
    private readonly IQueryIterationRunner _iterationRunner;

    public QueryLoopRunner(IQueryIterationRunner iterationRunner)
    {
        _iterationRunner = iterationRunner;
    }

    public async IAsyncEnumerable<QueryRuntimeEvent> RunAsync(
        QueryTurnRequest request,
        ConversationSession session,
        ClawSharpSettings settings,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<QueryRuntimeEvent>();
        var producerTask = ProduceRuntimeEventsAsync(request, session, settings, channel.Writer, cancellationToken);

        await foreach (var runtimeEvent in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return runtimeEvent;
        }

        await producerTask;
    }

    private async Task ProduceRuntimeEventsAsync(
        QueryTurnRequest request,
        ConversationSession session,
        ClawSharpSettings settings,
        ChannelWriter<QueryRuntimeEvent> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentRequest = request;
            var currentState = QueryLoopStateFactory.CreateInitial(
                session.Messages.ToArray(),
                request.InitialToolUseContext);

            while (true)
            {
                await writer.WriteAsync(new QueryRequestStartRuntimeEvent(), cancellationToken);

                var iterationResult = await _iterationRunner.RunAsync(
                    currentRequest,
                    currentState,
                    session,
                    settings,
                    (runtimeEvent, token) => writer.WriteAsync(runtimeEvent, token).AsTask(),
                    cancellationToken);

                switch (iterationResult)
                {
                    case QueryContinueIterationResult continueResult:
                        var maxTurnsNotification = QueryMaxTurnsPolicy.Evaluate(
                            currentRequest.MaxTurns,
                            continueResult.State.TurnCount);
                        if (maxTurnsNotification is not null)
                        {
                            await writer.WriteAsync(
                                new QueryMessageRuntimeEvent(
                                    ChatMessageFactory.CreateMaxTurnsReachedAttachmentMessage(
                                        maxTurnsNotification.MaxTurns,
                                        maxTurnsNotification.TurnCount)),
                                cancellationToken);
                            await writer.WriteAsync(
                            new QueryLoopTerminalRuntimeEvent(
                                    new QueryLoopTerminal(
                                        QueryTerminalReason.MaxTurns,
                                        TurnCount: maxTurnsNotification.TurnCount),
                                    continueResult.State),
                                cancellationToken);
                            writer.TryComplete();
                            return;
                        }

                        currentState = continueResult.State;
                        currentRequest = continueResult.NextRequest;
                        await writer.WriteAsync(
                            new QueryLoopTransitionRuntimeEvent(continueResult.Transition, continueResult.State),
                            cancellationToken);
                        continue;
                    case QueryTerminalIterationResult terminalResult:
                        await writer.WriteAsync(
                            new QueryLoopTerminalRuntimeEvent(terminalResult.Terminal, terminalResult.State),
                            cancellationToken);
                        writer.TryComplete();
                        return;
                    default:
                        throw new InvalidOperationException($"Unsupported iteration result '{iterationResult.GetType().Name}'.");
                }
            }
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
        }
    }
}
