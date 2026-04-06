// TS origin: ./utils/secureStorage/macOsKeychainStorage.ts, ./utils/secureStorage/macOsKeychainHelpers.ts
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed record MacOsKeychainProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? Stdin = null);

public sealed record MacOsKeychainMcpSecureStorageDependencies(
    Func<MacOsKeychainProcessRequest, ProcessExecutionResult>? Execute = null,
    Func<MacOsKeychainProcessRequest, CancellationToken, Task<ProcessExecutionResult>>? ExecuteAsync = null,
    Func<string>? GetClaudeConfigHomeDir = null,
    Func<string, string?>? GetEnvironmentVariable = null,
    Func<string>? GetUsername = null,
    Func<DateTimeOffset>? GetNow = null);

public sealed class MacOsKeychainMcpSecureStorage : IMcpSecureStorage
{
    private const int SecurityStdinLineLimit = 4096 - 64;
    private const int KeychainCacheTtlMs = 30_000;
    private const string CredentialsServiceSuffix = "-credentials";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _syncRoot = new();
    private readonly Func<MacOsKeychainProcessRequest, ProcessExecutionResult> _execute;
    private readonly Func<MacOsKeychainProcessRequest, CancellationToken, Task<ProcessExecutionResult>> _executeAsync;
    private readonly Func<string> _getClaudeConfigHomeDir;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<string> _getUsername;
    private readonly Func<DateTimeOffset> _getNow;

    private McpSecureStorageData? _cachedData;
    private DateTimeOffset _cachedAt;
    private Task<McpSecureStorageData?>? _readInFlight;
    private long _generation;

    public MacOsKeychainMcpSecureStorage(MacOsKeychainMcpSecureStorageDependencies? dependencies = null)
    {
        _execute = dependencies?.Execute ?? ExecuteSynchronously;
        _executeAsync = dependencies?.ExecuteAsync ?? ExecuteAsynchronously;
        _getClaudeConfigHomeDir = dependencies?.GetClaudeConfigHomeDir ?? SessionStoragePaths.GetClaudeConfigHomeDir;
        _getEnvironmentVariable = dependencies?.GetEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        _getUsername = dependencies?.GetUsername ?? GetUsername;
        _getNow = dependencies?.GetNow ?? (() => DateTimeOffset.UtcNow);
    }

    public McpSecureStorageData? Read()
    {
        var previous = GetCachedValueIfFresh();
        if (previous.IsFresh)
        {
            return previous.Data;
        }

        var readResult = TryReadWithSecurity();
        if (readResult is not null)
        {
            SetCache(readResult);
            return readResult;
        }

        if (previous.Data is not null)
        {
            SetCache(previous.Data);
            return previous.Data;
        }

        SetCache(null);
        return null;
    }

    public async Task<McpSecureStorageData?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var previous = GetCachedValueIfFresh();
        if (previous.IsFresh)
        {
            return previous.Data;
        }

        Task<McpSecureStorageData?>? inFlight;
        long generation;
        lock (_syncRoot)
        {
            if (IsCacheFreshNoLock())
            {
                return _cachedData;
            }

            if (_readInFlight is not null)
            {
                inFlight = _readInFlight;
                goto AwaitInFlight;
            }

            generation = _generation;
            _readInFlight = DoReadAsync(previous.Data, generation, cancellationToken);
            inFlight = _readInFlight;
        }

AwaitInFlight:
        return await inFlight.ConfigureAwait(false);
    }

    public void Update(McpSecureStorageData data)
    {
        ClearCache();

        var request = BuildUpdateRequest(data);
        var result = _execute(request);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Stderr)
                ? "Failed to write macOS keychain entry."
                : result.Stderr.Trim());
        }

        SetCache(data);
    }

    public bool Delete()
    {
        ClearCache();

        var result = _execute(
            new MacOsKeychainProcessRequest(
                "security",
                [
                    "delete-generic-password",
                    "-a",
                    _getUsername(),
                    "-s",
                    GetStorageServiceName()
                ]));

        return result.ExitCode == 0;
    }

    private async Task<McpSecureStorageData?> DoReadAsync(
        McpSecureStorageData? previousData,
        long generation,
        CancellationToken cancellationToken)
    {
        var result = await TryReadWithSecurityAsync(cancellationToken).ConfigureAwait(false);
        lock (_syncRoot)
        {
            try
            {
                if (generation != _generation)
                {
                    return result;
                }

                var next = result ?? previousData;
                _cachedData = next;
                _cachedAt = _getNow();
                return next;
            }
            finally
            {
                _readInFlight = null;
            }
        }
    }

    private McpSecureStorageData? TryReadWithSecurity()
    {
        var result = _execute(
            new MacOsKeychainProcessRequest(
                "security",
                [
                    "find-generic-password",
                    "-a",
                    _getUsername(),
                    "-w",
                    "-s",
                    GetStorageServiceName()
                ]));

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
        {
            return null;
        }

        return TryDeserialize(result.Stdout.Trim());
    }

    private async Task<McpSecureStorageData?> TryReadWithSecurityAsync(CancellationToken cancellationToken)
    {
        var result = await _executeAsync(
            new MacOsKeychainProcessRequest(
                "security",
                [
                    "find-generic-password",
                    "-a",
                    _getUsername(),
                    "-w",
                    "-s",
                    GetStorageServiceName()
                ]),
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
        {
            return null;
        }

        return TryDeserialize(result.Stdout.Trim());
    }

    private MacOsKeychainProcessRequest BuildUpdateRequest(McpSecureStorageData data)
    {
        var storageServiceName = GetStorageServiceName();
        var username = _getUsername();
        var json = JsonSerializer.Serialize(data, SerializerOptions);
        var hexValue = Convert.ToHexString(Encoding.UTF8.GetBytes(json)).ToLowerInvariant();
        var interactiveCommand =
            $"add-generic-password -U -a \"{username}\" -s \"{storageServiceName}\" -X \"{hexValue}\"\n";

        if (interactiveCommand.Length <= SecurityStdinLineLimit)
        {
            return new MacOsKeychainProcessRequest(
                "security",
                ["-i"],
                interactiveCommand);
        }

        return new MacOsKeychainProcessRequest(
            "security",
            [
                "add-generic-password",
                "-U",
                "-a",
                username,
                "-s",
                storageServiceName,
                "-X",
                hexValue
            ]);
    }

    private string GetStorageServiceName()
    {
        var configDir = _getClaudeConfigHomeDir().Normalize(NormalizationForm.FormC);
        var isDefaultDir = string.IsNullOrWhiteSpace(_getEnvironmentVariable("CLAUDE_CONFIG_DIR"));
        var dirHash = isDefaultDir
            ? string.Empty
            : "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configDir))).ToLowerInvariant()[..8];

        return $"Claude Code{GetOauthFileSuffix()}{CredentialsServiceSuffix}{dirHash}";
    }

    private string GetOauthFileSuffix()
    {
        if (!string.IsNullOrWhiteSpace(_getEnvironmentVariable("CLAUDE_CODE_CUSTOM_OAUTH_URL")))
        {
            return "-custom-oauth";
        }

        if (!string.Equals(_getEnvironmentVariable("USER_TYPE"), "ant", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (IsEnvTruthy(_getEnvironmentVariable("USE_LOCAL_OAUTH")))
        {
            return "-local-oauth";
        }

        if (IsEnvTruthy(_getEnvironmentVariable("USE_STAGING_OAUTH")))
        {
            return "-staging-oauth";
        }

        return string.Empty;
    }

    private static bool IsEnvTruthy(string? value)
    {
        return value is not null &&
               !string.Equals(value, "0", StringComparison.Ordinal) &&
               !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUsername()
    {
        return Environment.GetEnvironmentVariable("USER") ??
               Environment.UserName ??
               "claude-code-user";
    }

    private (bool IsFresh, McpSecureStorageData? Data) GetCachedValueIfFresh()
    {
        lock (_syncRoot)
        {
            return (IsCacheFreshNoLock(), _cachedData);
        }
    }

    private bool IsCacheFreshNoLock()
    {
        return (_getNow() - _cachedAt).TotalMilliseconds < KeychainCacheTtlMs;
    }

    private void SetCache(McpSecureStorageData? data)
    {
        lock (_syncRoot)
        {
            _cachedData = data;
            _cachedAt = _getNow();
        }
    }

    private void ClearCache()
    {
        lock (_syncRoot)
        {
            _cachedData = null;
            _cachedAt = DateTimeOffset.MinValue;
            _generation++;
            _readInFlight = null;
        }
    }

    private static McpSecureStorageData? TryDeserialize(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<McpSecureStorageData>(value, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static ProcessExecutionResult ExecuteSynchronously(MacOsKeychainProcessRequest request)
    {
        try
        {
            using var process = CreateProcess(request);
            if (!process.Start())
            {
                return new ProcessExecutionResult(-1, string.Empty, $"Failed to start {request.FileName}.");
            }

            if (request.Stdin is not null)
            {
                process.StandardInput.Write(request.Stdin);
                process.StandardInput.Close();
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ProcessExecutionResult(process.ExitCode, stdout, stderr);
        }
        catch (Exception exception)
        {
            return new ProcessExecutionResult(-1, string.Empty, exception.Message);
        }
    }

    private static async Task<ProcessExecutionResult> ExecuteAsynchronously(
        MacOsKeychainProcessRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = CreateProcess(request);
            if (!process.Start())
            {
                return new ProcessExecutionResult(-1, string.Empty, $"Failed to start {request.FileName}.");
            }

            if (request.Stdin is not null)
            {
                await process.StandardInput.WriteAsync(request.Stdin.AsMemory(), cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return new ProcessExecutionResult(process.ExitCode, stdout, stderr);
        }
        catch (Exception exception)
        {
            return new ProcessExecutionResult(-1, string.Empty, exception.Message);
        }
    }

    private static Process CreateProcess(MacOsKeychainProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = request.Stdin is not null,
            CreateNoWindow = true
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return new Process
        {
            StartInfo = startInfo
        };
    }
}
