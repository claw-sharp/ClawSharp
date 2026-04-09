using System.Text.Json;
using ClawSharp.AgentHost.Contracts;
using ClawSharp.Core;

namespace ClawSharp.AgentHost.Ipc;

public sealed class AgentHostStdioServer
{
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly AgentHostCommandRouter _commandRouter;
    private readonly AgentHostEventDispatcher _eventDispatcher;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Lock _requestLock = new();
    private readonly HashSet<Task> _inflightRequests = [];

    public AgentHostStdioServer(
        TextReader input,
        TextWriter output,
        AgentHostCommandRouter commandRouter,
        AgentHostEventDispatcher eventDispatcher)
    {
        _input = input;
        _output = output;
        _commandRouter = commandRouter;
        _eventDispatcher = eventDispatcher;
        _eventDispatcher.SetPublisher(WriteEventAsync);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await WriteEventAsync(
            new AgentHostEventEnvelope(
                "hostReady",
                DateTimeOffset.UtcNow,
                new HostReadyEvent(
                    $"{AppMetadata.Name}.AgentHost",
                    AppMetadata.Version,
                    AgentHostProtocol.ProtocolVersion)),
            cancellationToken);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await _input.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                Task[] inflight;
                lock (_requestLock)
                {
                    inflight = _inflightRequests.ToArray();
                }

                await Task.WhenAll(inflight);
                return;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            AgentHostRequestEnvelope? request;
            try
            {
                request = JsonSerializer.Deserialize<AgentHostRequestEnvelope>(line, AgentHostProtocol.JsonOptions);
            }
            catch (JsonException error)
            {
                await WriteResponseAsync(
                    new AgentHostResponseEnvelope(
                        RequestId: string.Empty,
                        Command: "unknown",
                        Success: false,
                        Timestamp: DateTimeOffset.UtcNow,
                        Error: new AgentHostErrorDto("invalid_json", "Failed to parse AgentHost request.", error.Message)),
                    cancellationToken);
                continue;
            }

            if (request is null || string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.Command))
            {
                await WriteResponseAsync(
                    new AgentHostResponseEnvelope(
                        RequestId: request?.RequestId ?? string.Empty,
                        Command: request?.Command ?? "unknown",
                        Success: false,
                        Timestamp: DateTimeOffset.UtcNow,
                        Error: new AgentHostErrorDto("invalid_request", "AgentHost requests must include requestId and command.")),
                    cancellationToken);
                continue;
            }

            Task requestTask;
            lock (_requestLock)
            {
                requestTask = HandleRequestAsync(request, cancellationToken);
                _inflightRequests.Add(requestTask);
            }

            _ = requestTask.ContinueWith(
                _ =>
                {
                    lock (_requestLock)
                    {
                        _inflightRequests.Remove(requestTask);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task HandleRequestAsync(AgentHostRequestEnvelope request, CancellationToken cancellationToken)
    {
        AgentHostLog.Debug(
            "ipc:request",
            $"requestId={request.RequestId} command={request.Command} payload={request.Payload?.GetRawText() ?? "null"}");
        try
        {
            var payload = await _commandRouter.ExecuteAsync(request, cancellationToken);
            AgentHostLog.Debug(
                "ipc:response-success",
                $"requestId={request.RequestId} command={request.Command}");
            await WriteResponseAsync(
                new AgentHostResponseEnvelope(
                    request.RequestId,
                    request.Command,
                    true,
                    DateTimeOffset.UtcNow,
                    Payload: payload),
                cancellationToken);
        }
        catch (AgentHostException error)
        {
            AgentHostLog.Warn(
                "ipc:response-agenthost-error",
                $"requestId={request.RequestId} command={request.Command} code={error.Code} message={error.Message}");
            await WriteResponseAsync(
                new AgentHostResponseEnvelope(
                    request.RequestId,
                    request.Command,
                    false,
                    DateTimeOffset.UtcNow,
                    Error: new AgentHostErrorDto(error.Code, error.Message, error.Details)),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            AgentHostLog.Warn(
                "ipc:response-cancelled",
                $"requestId={request.RequestId} command={request.Command}");
            await WriteResponseAsync(
                new AgentHostResponseEnvelope(
                    request.RequestId,
                    request.Command,
                    false,
                    DateTimeOffset.UtcNow,
                    Error: new AgentHostErrorDto("cancelled", "AgentHost request was cancelled.")),
                CancellationToken.None);
        }
        catch (Exception error)
        {
            AgentHostLog.Error(
                "ipc:response-internal-error",
                $"requestId={request.RequestId} command={request.Command} error={error.GetType().Name}: {error.Message}");
            await WriteResponseAsync(
                new AgentHostResponseEnvelope(
                    request.RequestId,
                    request.Command,
                    false,
                    DateTimeOffset.UtcNow,
                    Error: new AgentHostErrorDto("internal_error", error.Message, error.ToString())),
                CancellationToken.None);
        }
    }

    private async Task WriteResponseAsync(AgentHostResponseEnvelope response, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(response, AgentHostProtocol.JsonOptions);
        await WriteLineAsync(line, cancellationToken);
    }

    private async Task WriteEventAsync(AgentHostEventEnvelope agentEvent, CancellationToken cancellationToken)
    {
        AgentHostLog.Debug(
            "ipc:event",
            $"event={agentEvent.Event} payload={JsonSerializer.Serialize(agentEvent.Payload, AgentHostProtocol.JsonOptions)}");
        var line = JsonSerializer.Serialize(agentEvent, AgentHostProtocol.JsonOptions);
        await WriteLineAsync(line, cancellationToken);
    }

    private async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _output.WriteLineAsync(line.AsMemory(), cancellationToken);
            await _output.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
