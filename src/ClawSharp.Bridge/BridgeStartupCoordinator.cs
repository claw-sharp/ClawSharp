// TS origin: ./bridge/bridgeMain.ts
using ClawSharp.Core;

namespace ClawSharp.Bridge;

public sealed record BridgeStartupRequest(
    BridgeConfig Config,
    bool PreCreateSession,
    string? Title = null,
    string? PermissionMode = null,
    string? ResumeSessionId = null,
    string? ResumePointerDir = null);

public sealed record BridgeStartupResult(
    string EnvironmentId,
    string EnvironmentSecret,
    string? InitialSessionId,
    string? EffectiveResumeSessionId,
    string? WarningMessage = null);

public sealed record BridgeStartupDependencies(
    IBridgeApiClient Api,
    Func<string, CancellationToken, Task<BridgeSessionSummary?>> GetBridgeSessionAsync,
    Func<string, string?, CancellationToken, Task<string?>> CreateSessionAsync,
    Func<string, string, string, CancellationToken, Task> WriteBridgePointerAsync,
    Func<string, CancellationToken, Task>? ClearBridgePointerAsync = null,
    Action<string>? OnDebug = null);

public sealed class BridgeStartupCoordinator
{
    private readonly BridgeStartupDependencies _dependencies;

    public BridgeStartupCoordinator(BridgeStartupDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    public async Task<BridgeStartupResult> StartAsync(
        BridgeStartupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Config);

        string? reuseEnvironmentId = null;
        if (!string.IsNullOrEmpty(request.ResumeSessionId))
        {
            reuseEnvironmentId = await ResolveResumeEnvironmentIdAsync(
                request.ResumeSessionId,
                request.ResumePointerDir,
                cancellationToken);
            _dependencies.OnDebug?.Invoke(
                $"[bridge:init] Resuming session {request.ResumeSessionId} on environment {reuseEnvironmentId}");
        }

        var config = request.Config with { ReuseEnvironmentId = reuseEnvironmentId };
        BridgeRegisterEnvironmentResponse registration;
        try
        {
            registration = await _dependencies.Api.RegisterBridgeEnvironmentAsync(config, cancellationToken);
        }
        catch (BridgeFatalError error) when (error.Status == 404)
        {
            throw new InvalidOperationException("Remote Control environments are not available for your account.", error);
        }

        string? effectiveResumeSessionId = null;
        string? warningMessage = null;
        if (!string.IsNullOrEmpty(request.ResumeSessionId))
        {
            if (!string.IsNullOrEmpty(reuseEnvironmentId) &&
                !string.Equals(registration.EnvironmentId, reuseEnvironmentId, StringComparison.Ordinal))
            {
                warningMessage =
                    $"Warning: Could not resume session {request.ResumeSessionId} — its environment has expired. Creating a fresh session instead.";
            }
            else
            {
                effectiveResumeSessionId = await ReconnectResumedSessionAsync(
                    registration.EnvironmentId,
                    request.ResumeSessionId,
                    request.ResumePointerDir,
                    cancellationToken);
            }
        }

        var initialSessionId = effectiveResumeSessionId;
        if (initialSessionId is null && request.PreCreateSession)
        {
            initialSessionId = await _dependencies.CreateSessionAsync(
                registration.EnvironmentId,
                request.Title,
                cancellationToken);
            if (!string.IsNullOrEmpty(initialSessionId) &&
                config.SpawnMode == SpawnMode.SingleSession)
            {
                await _dependencies.WriteBridgePointerAsync(
                    config.Dir,
                    initialSessionId,
                    registration.EnvironmentId,
                    cancellationToken);
            }
        }
        else if (!string.IsNullOrEmpty(initialSessionId) && config.SpawnMode == SpawnMode.SingleSession)
        {
            await _dependencies.WriteBridgePointerAsync(
                config.Dir,
                initialSessionId,
                registration.EnvironmentId,
                cancellationToken);
        }

        _dependencies.OnDebug?.Invoke(
            $"[bridge:init] Registered, server environmentId={registration.EnvironmentId}");

        return new BridgeStartupResult(
            registration.EnvironmentId,
            registration.EnvironmentSecret,
            initialSessionId,
            effectiveResumeSessionId,
            warningMessage);
    }

    private async Task<string> ResolveResumeEnvironmentIdAsync(
        string resumeSessionId,
        string? resumePointerDir,
        CancellationToken cancellationToken)
    {
        BridgeApiErrorUtilities.ValidateBridgeId(resumeSessionId, "sessionId");

        var session = await _dependencies.GetBridgeSessionAsync(resumeSessionId, cancellationToken);
        if (session is null)
        {
            if (!string.IsNullOrEmpty(resumePointerDir) && _dependencies.ClearBridgePointerAsync is not null)
            {
                await _dependencies.ClearBridgePointerAsync(resumePointerDir, cancellationToken);
            }

            throw new InvalidOperationException(
                $"Session {resumeSessionId} not found. It may have been archived or expired, or your login may have lapsed (run `{AppMetadata.CommandName}` and use `/login`).");
        }

        if (string.IsNullOrEmpty(session.EnvironmentId))
        {
            if (!string.IsNullOrEmpty(resumePointerDir) && _dependencies.ClearBridgePointerAsync is not null)
            {
                await _dependencies.ClearBridgePointerAsync(resumePointerDir, cancellationToken);
            }

            throw new InvalidOperationException(
                $"Session {resumeSessionId} has no environment_id. It may never have been attached to a bridge.");
        }

        return session.EnvironmentId;
    }

    private async Task<string> ReconnectResumedSessionAsync(
        string environmentId,
        string resumeSessionId,
        string? resumePointerDir,
        CancellationToken cancellationToken)
    {
        var infraResumeId = BridgeSessionIdCompat.ToInfraSessionId(resumeSessionId);
        var reconnectCandidates = string.Equals(infraResumeId, resumeSessionId, StringComparison.Ordinal)
            ? new[] { resumeSessionId }
            : new[] { resumeSessionId, infraResumeId };

        Exception? lastReconnectError = null;
        foreach (var candidateId in reconnectCandidates)
        {
            try
            {
                await _dependencies.Api.ReconnectSessionAsync(environmentId, candidateId, cancellationToken);
                _dependencies.OnDebug?.Invoke(
                    $"[bridge:init] Session {candidateId} re-queued via bridge/reconnect");
                return resumeSessionId;
            }
            catch (Exception error)
            {
                lastReconnectError = error;
                _dependencies.OnDebug?.Invoke(
                    $"[bridge:init] reconnectSession({candidateId}) failed: {error.Message}");
            }
        }

        if (!string.IsNullOrEmpty(resumePointerDir) &&
            lastReconnectError is BridgeFatalError &&
            _dependencies.ClearBridgePointerAsync is not null)
        {
            await _dependencies.ClearBridgePointerAsync(resumePointerDir, cancellationToken);
        }

        if (lastReconnectError is BridgeFatalError fatalError)
        {
            throw fatalError;
        }

        throw new InvalidOperationException(
            $"Failed to reconnect session {resumeSessionId}: {lastReconnectError?.Message}\nThe session may still be resumable — try running the same command again.");
    }
}
