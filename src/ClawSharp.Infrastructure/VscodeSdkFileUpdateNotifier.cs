using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class VscodeSdkFileUpdateNotifier : IFileUpdateNotifier
{
    private readonly McpConfigService _mcpConfigService;
    private readonly McpLifecycleManager _mcpLifecycleManager;
    private readonly Func<string?> _userTypeProvider;

    private ConnectedMcpServerConnection? _vscodeConnection;
    private ScopedMcpServerConfig? _vscodeConfig;
    private bool _attemptedResolve;

    public VscodeSdkFileUpdateNotifier(
        McpConfigService mcpConfigService,
        McpLifecycleManager mcpLifecycleManager,
        Func<string?>? userTypeProvider = null)
    {
        _mcpConfigService = mcpConfigService;
        _mcpLifecycleManager = mcpLifecycleManager;
        _userTypeProvider = userTypeProvider ?? (() => Environment.GetEnvironmentVariable("USER_TYPE"));
    }

    public Task HandleQueryStartAsync(CancellationToken cancellationToken = default)
    {
        return EnsureConnectedAsync(cancellationToken);
    }

    public Task BeforeFileEditedAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task ClearDiagnosticsForFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task NotifyFileUpdatedAsync(
        string filePath,
        string? oldContent,
        string? newContent,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(_userTypeProvider(), "ant", StringComparison.Ordinal) ||
            !await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false) ||
            _vscodeConnection is null)
        {
            return;
        }

        try
        {
            await _vscodeConnection.Client.SendNotificationAsync(
                    "file_updated",
                    new JsonObject
                    {
                        ["filePath"] = filePath,
                        ["oldContent"] = oldContent is null ? null : JsonValue.Create(oldContent),
                        ["newContent"] = newContent is null ? null : JsonValue.Create(newContent)
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // TS parity: swallow VSCode notification failures.
        }
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (!string.Equals(_userTypeProvider(), "ant", StringComparison.Ordinal))
        {
            return false;
        }

        if (_vscodeConnection is not null)
        {
            return true;
        }

        if (!_attemptedResolve)
        {
            _vscodeConfig = _mcpConfigService.GetConfigByName("claude-vscode");
            _attemptedResolve = true;
        }

        if (_vscodeConfig is null)
        {
            return false;
        }

        var connection = await _mcpLifecycleManager.ConnectToServerAsync(
                "claude-vscode",
                _vscodeConfig,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (connection is not ConnectedMcpServerConnection connected)
        {
            return false;
        }

        _vscodeConnection = connected;
        return true;
    }
}
