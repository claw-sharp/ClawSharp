using ClawSharp.Core;
using System.Text;
using System.Text.Json;

namespace ClawSharp.Infrastructure;

public sealed record BridgeClientContextDependencies(
    string? GlobalConfigPath = null,
    Func<string>? GetCurrentDirectory = null,
    Func<bool>? GetSessionTrustAccepted = null,
    Func<string, string, CancellationToken, Task<ProcessExecutionResult>>? ExecuteAsync = null,
    Func<string, string?>? GetEnvironmentVariable = null);

public sealed class BridgeClientContextUtilities
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _globalConfigPath;
    private readonly Func<string> _getCurrentDirectory;
    private readonly Func<bool> _getSessionTrustAccepted;
    private readonly Func<string, string, CancellationToken, Task<ProcessExecutionResult>> _executeAsync;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private bool _trustAccepted;

    public BridgeClientContextUtilities(BridgeClientContextDependencies? dependencies = null)
    {
        _globalConfigPath = dependencies?.GlobalConfigPath ?? ClaudeConfigPaths.GetGlobalClaudeFilePath();
        _getCurrentDirectory = dependencies?.GetCurrentDirectory ?? Directory.GetCurrentDirectory;
        _getSessionTrustAccepted = dependencies?.GetSessionTrustAccepted ?? (() => false);
        _executeAsync = dependencies?.ExecuteAsync ?? DefaultExecuteAsync;
        _getEnvironmentVariable = dependencies?.GetEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    public async Task<bool> CheckHasTrustDialogAcceptedAsync(CancellationToken cancellationToken = default)
    {
        if (_trustAccepted)
        {
            return true;
        }

        _trustAccepted = await ComputeTrustDialogAcceptedAsync(cancellationToken).ConfigureAwait(false);
        return _trustAccepted;
    }

    public async Task<bool> IsPathTrustedAsync(string dir, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dir);

        var config = LoadGlobalConfig();
        var currentPath = NormalizePathForConfigKey(dir);
        while (true)
        {
            if (config.Projects is not null &&
                config.Projects.TryGetValue(currentPath, out var projectConfig) &&
                projectConfig?.HasTrustDialogAccepted == true)
            {
                return true;
            }

            var parentPath = NormalizePathForConfigKey(Path.GetFullPath(Path.Combine(currentPath, "..")));
            if (string.Equals(parentPath, currentPath, StringComparison.Ordinal))
            {
                return false;
            }

            currentPath = parentPath;
        }
    }

    public string? GetCachedOrganizationUuid()
    {
        var envOrganizationUuid = _getEnvironmentVariable("CLAUDE_CODE_ORGANIZATION_UUID");
        if (!string.IsNullOrWhiteSpace(envOrganizationUuid))
        {
            return envOrganizationUuid;
        }

        return LoadGlobalConfig().OAuthAccount?.OrganizationUuid;
    }

    private async Task<bool> ComputeTrustDialogAcceptedAsync(CancellationToken cancellationToken)
    {
        if (_getSessionTrustAccepted())
        {
            return true;
        }

        var config = LoadGlobalConfig();
        var projectPath = await GetProjectPathForConfigAsync(cancellationToken).ConfigureAwait(false);
        if (config.Projects is not null &&
            config.Projects.TryGetValue(projectPath, out var projectConfig) &&
            projectConfig?.HasTrustDialogAccepted == true)
        {
            return true;
        }

        var currentPath = NormalizePathForConfigKey(_getCurrentDirectory());
        while (true)
        {
            if (config.Projects is not null &&
                config.Projects.TryGetValue(currentPath, out var pathConfig) &&
                pathConfig?.HasTrustDialogAccepted == true)
            {
                return true;
            }

            var parentPath = NormalizePathForConfigKey(Path.GetFullPath(Path.Combine(currentPath, "..")));
            if (string.Equals(parentPath, currentPath, StringComparison.Ordinal))
            {
                break;
            }

            currentPath = parentPath;
        }

        return false;
    }

    private async Task<string> GetProjectPathForConfigAsync(CancellationToken cancellationToken)
    {
        var originalCwd = Path.GetFullPath(_getCurrentDirectory());
        var gitRoot = await FindCanonicalGitRootAsync(originalCwd, cancellationToken).ConfigureAwait(false);
        return NormalizePathForConfigKey(gitRoot ?? originalCwd);
    }

    private async Task<string?> FindCanonicalGitRootAsync(string dir, CancellationToken cancellationToken)
    {
        var result = await _executeAsync("git", $"rev-parse --show-toplevel", cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
        {
            return null;
        }

        return Path.GetFullPath(result.Stdout.Trim()).Normalize(NormalizationForm.FormC);
    }

    private BridgeGlobalConfig LoadGlobalConfig()
    {
        if (!File.Exists(_globalConfigPath))
        {
            return new BridgeGlobalConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<BridgeGlobalConfig>(
                       File.ReadAllText(_globalConfigPath),
                       SerializerOptions) ??
                   new BridgeGlobalConfig();
        }
        catch
        {
            return new BridgeGlobalConfig();
        }
    }

    private static string NormalizePathForConfigKey(string path)
    {
        return PathUtilities.NormalizePathForConfigKey(path);
    }

    private static Task<ProcessExecutionResult> DefaultExecuteAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        return ProcessExecutionUtilities.ExecuteAsync(
            fileName,
            ProcessExecutionUtilities.ParseCommandString(arguments),
            cancellationToken: cancellationToken);
    }

    private sealed record BridgeGlobalConfig(
        Dictionary<string, BridgeProjectConfig?>? Projects = null,
        BridgeOauthAccount? OAuthAccount = null);

    private sealed record BridgeProjectConfig(
        bool HasTrustDialogAccepted = false);

    private sealed record BridgeOauthAccount(
        string? OrganizationUuid = null);
}
