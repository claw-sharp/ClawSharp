// TS origin: ./services/diagnosticTracking.ts
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class DiagnosticTrackingService
{
    private readonly McpLifecycleManager _lifecycleManager;
    private readonly IdeMcpServerConfigResolver _ideServerConfigResolver;
    private readonly Dictionary<string, IReadOnlyList<IdeDiagnostic>> _baseline;
    private readonly Dictionary<string, IReadOnlyList<IdeDiagnostic>> _rightFileDiagnosticsState;
    private readonly Dictionary<string, long> _lastProcessedTimestamps;

    private bool _initialized;
    private ConnectedMcpServerConnection? _ideConnection;

    public DiagnosticTrackingService(
        McpLifecycleManager lifecycleManager,
        IdeMcpServerConfigResolver ideServerConfigResolver)
    {
        _lifecycleManager = lifecycleManager;
        _ideServerConfigResolver = ideServerConfigResolver;
        _baseline = new Dictionary<string, IReadOnlyList<IdeDiagnostic>>(GetPathComparer());
        _rightFileDiagnosticsState = new Dictionary<string, IReadOnlyList<IdeDiagnostic>>(GetPathComparer());
        _lastProcessedTimestamps = new Dictionary<string, long>(GetPathComparer());
    }

    public async Task HandleQueryStartAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            var ideServer = await _ideServerConfigResolver.TryResolveAsync(cancellationToken).ConfigureAwait(false);
            if (ideServer is null)
            {
                return;
            }

            var connection = await _lifecycleManager.ConnectToServerAsync("ide", ideServer, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (connection is ConnectedMcpServerConnection connected)
            {
                _ideConnection = connected;
                _initialized = true;
            }

            return;
        }

        Reset();
    }

    public async Task BeforeFileEditedAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!_initialized || _ideConnection is null)
        {
            return;
        }

        try
        {
            var result = await _ideConnection.Client.CallToolAsync(
                    "getDiagnostics",
                    new JsonObject
                    {
                        ["uri"] = $"file://{filePath}"
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var diagnosticFile = ParseDiagnosticFiles(result).FirstOrDefault();
            var normalizedPath = NormalizeFileUri(filePath);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (diagnosticFile is not null)
            {
                if (!PathsEqual(normalizedPath, NormalizeFileUri(diagnosticFile.Uri)))
                {
                    return;
                }

                _baseline[normalizedPath] = diagnosticFile.Diagnostics;
                _lastProcessedTimestamps[normalizedPath] = timestamp;
                return;
            }

            _baseline[normalizedPath] = Array.Empty<IdeDiagnostic>();
            _lastProcessedTimestamps[normalizedPath] = timestamp;
        }
        catch
        {
            // TS parity: ignore IDE diagnostics failures.
        }
    }

    public void Reset()
    {
        _baseline.Clear();
        _rightFileDiagnosticsState.Clear();
        _lastProcessedTimestamps.Clear();
    }

    private IReadOnlyList<IdeDiagnosticFile> ParseDiagnosticFiles(McpToolCallResult result)
    {
        if (result.StructuredContent is JsonArray structuredArray)
        {
            var parsedStructured = structuredArray.Deserialize(
                DiagnosticTrackingJsonContext.Default.ListIdeDiagnosticFile);
            if (parsedStructured is not null)
            {
                return parsedStructured;
            }
        }

        if (string.IsNullOrWhiteSpace(result.Content))
        {
            return Array.Empty<IdeDiagnosticFile>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(
                result.Content,
                DiagnosticTrackingJsonContext.Default.ListIdeDiagnosticFile);
            return parsed is null ? Array.Empty<IdeDiagnosticFile>() : parsed;
        }
        catch (JsonException)
        {
            return Array.Empty<IdeDiagnosticFile>();
        }
    }

    private static string NormalizeFileUri(string fileUri)
    {
        var normalized = fileUri;
        foreach (var prefix in new[] { "file://", "_claude_fs_right:", "_claude_fs_left:" })
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                normalized = normalized[prefix.Length..];
                break;
            }
        }

        if (OperatingSystem.IsWindows() &&
            normalized.Length >= 3 &&
            normalized[0] == '/' &&
            char.IsLetter(normalized[1]) &&
            normalized[2] == ':')
        {
            normalized = normalized[1..];
        }

        normalized = normalized
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch
        {
            // Preserve the best-effort normalized path.
        }

        return normalized;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(left, right, GetPathComparison());
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}

[JsonSerializable(typeof(List<IdeDiagnosticFile>))]
internal sealed partial class DiagnosticTrackingJsonContext : JsonSerializerContext
{
}

internal sealed record IdeDiagnostic(
    string Message,
    string Severity,
    IdeDiagnosticRange Range,
    string? Source = null,
    string? Code = null);

internal sealed record IdeDiagnosticRange(
    IdeDiagnosticPosition Start,
    IdeDiagnosticPosition End);

internal sealed record IdeDiagnosticPosition(
    int Line,
    int Character);

internal sealed record IdeDiagnosticFile(
    string Uri,
    IReadOnlyList<IdeDiagnostic> Diagnostics);
