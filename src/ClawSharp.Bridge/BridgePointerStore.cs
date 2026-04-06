using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClawSharp.Bridge;

public sealed record BridgePointer(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("environmentId")] string EnvironmentId,
    [property: JsonPropertyName("source")] string Source);

public sealed record BridgePointerWithAge(
    string SessionId,
    string EnvironmentId,
    string Source,
    double AgeMs);

public sealed record BridgePointerAcrossWorktreesResult(
    BridgePointerWithAge Pointer,
    string Dir);

public sealed record BridgePointerStoreDependencies(
    string ProjectsDirectory,
    Action<string>? OnDebug = null,
    Func<DateTimeOffset>? GetNow = null,
    Func<string, CancellationToken, Task<IReadOnlyList<string>>>? GetWorktreePathsAsync = null);

public static class BridgePointerStore
{
    public const double BridgePointerTtlMs = 4 * 60 * 60 * 1000;
    public const int MaxWorktreeFanout = 50;
    public const int MaxSanitizedLength = 200;

    public static string GetBridgePointerPath(
        BridgePointerStoreDependencies dependencies,
        string dir)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dir);

        return Path.Combine(
            dependencies.ProjectsDirectory,
            SanitizePath(dir),
            "bridge-pointer.json");
    }

    public static async Task WriteBridgePointerAsync(
        BridgePointerStoreDependencies dependencies,
        string dir,
        BridgePointer pointer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dir);
        ArgumentNullException.ThrowIfNull(pointer);

        var path = GetBridgePointerPath(dependencies, dir);
        try
        {
            var parentDirectory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            var json = JsonSerializer.Serialize(pointer);
            await File.WriteAllTextAsync(path, json, cancellationToken);
            dependencies.OnDebug?.Invoke($"[bridge:pointer] wrote {path}");
        }
        catch (Exception error)
        {
            dependencies.OnDebug?.Invoke($"[bridge:pointer] write failed: {error}");
        }
    }

    public static async Task<BridgePointerWithAge?> ReadBridgePointerAsync(
        BridgePointerStoreDependencies dependencies,
        string dir,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dir);

        var path = GetBridgePointerPath(dependencies, dir);
        string raw;
        DateTimeOffset lastWriteTimeUtc;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            lastWriteTimeUtc = info.LastWriteTimeUtc;
            raw = await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch
        {
            return null;
        }

        if (!TryParseBridgePointer(raw, out var pointer))
        {
            dependencies.OnDebug?.Invoke($"[bridge:pointer] invalid schema, clearing: {path}");
            await ClearBridgePointerAsync(dependencies, dir, cancellationToken);
            return null;
        }

        var now = dependencies.GetNow?.Invoke() ?? DateTimeOffset.UtcNow;
        var ageMs = Math.Max(0d, (now - lastWriteTimeUtc).TotalMilliseconds);
        if (ageMs > BridgePointerTtlMs)
        {
            dependencies.OnDebug?.Invoke($"[bridge:pointer] stale (>4h mtime), clearing: {path}");
            await ClearBridgePointerAsync(dependencies, dir, cancellationToken);
            return null;
        }

        var validatedPointer = pointer!;
        return new BridgePointerWithAge(
            validatedPointer.SessionId,
            validatedPointer.EnvironmentId,
            validatedPointer.Source,
            ageMs);
    }

    public static async Task<BridgePointerAcrossWorktreesResult?> ReadBridgePointerAcrossWorktreesAsync(
        BridgePointerStoreDependencies dependencies,
        string dir,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dir);

        var here = await ReadBridgePointerAsync(dependencies, dir, cancellationToken);
        if (here is not null)
        {
            return new BridgePointerAcrossWorktreesResult(here, dir);
        }

        var getWorktreePathsAsync = dependencies.GetWorktreePathsAsync;
        if (getWorktreePathsAsync is null)
        {
            return null;
        }

        var worktrees = await getWorktreePathsAsync(dir, cancellationToken);
        if (worktrees is null || worktrees.Count <= 1)
        {
            return null;
        }

        if (worktrees.Count > MaxWorktreeFanout)
        {
            dependencies.OnDebug?.Invoke(
                $"[bridge:pointer] {worktrees.Count} worktrees exceeds fanout cap {MaxWorktreeFanout}, skipping");
            return null;
        }

        var dirKey = SanitizePath(dir);
        var candidates = worktrees
            .Where(worktree => !string.Equals(SanitizePath(worktree), dirKey, StringComparison.Ordinal))
            .ToArray();

        var results = await Task.WhenAll(
            candidates.Select(async worktree =>
            {
                var pointer = await ReadBridgePointerAsync(dependencies, worktree, cancellationToken);
                return pointer is null ? null : new BridgePointerAcrossWorktreesResult(pointer, worktree);
            }));

        BridgePointerAcrossWorktreesResult? freshest = null;
        foreach (var result in results)
        {
            if (result is not null &&
                (freshest is null || result.Pointer.AgeMs < freshest.Pointer.AgeMs))
            {
                freshest = result;
            }
        }

        if (freshest is not null)
        {
            dependencies.OnDebug?.Invoke(
                $"[bridge:pointer] fanout found pointer in worktree {freshest.Dir} (ageMs={freshest.Pointer.AgeMs})");
        }

        return freshest;
    }

    public static Task ClearBridgePointerAsync(
        BridgePointerStoreDependencies dependencies,
        string dir,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dir);

        var path = GetBridgePointerPath(dependencies, dir);
        try
        {
            File.Delete(path);
            dependencies.OnDebug?.Invoke($"[bridge:pointer] cleared {path}");
        }
        catch (Exception error) when (error is not FileNotFoundException && error is not DirectoryNotFoundException)
        {
            dependencies.OnDebug?.Invoke($"[bridge:pointer] clear failed: {error}");
        }

        return Task.CompletedTask;
    }

    public static string SanitizePath(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Span<char> buffer = stackalloc char[name.Length];
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            buffer[index] = char.IsAsciiLetterOrDigit(character) ? character : '-';
        }

        var sanitized = buffer.ToString();
        if (sanitized.Length <= MaxSanitizedLength)
        {
            return sanitized;
        }

        return $"{sanitized[..MaxSanitizedLength]}-{SimpleHash(name)}";
    }

    private static bool TryParseBridgePointer(string raw, out BridgePointer? pointer)
    {
        pointer = null;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("sessionId", out var sessionIdElement) ||
                !root.TryGetProperty("environmentId", out var environmentIdElement) ||
                !root.TryGetProperty("source", out var sourceElement))
            {
                return false;
            }

            var sessionId = sessionIdElement.GetString();
            var environmentId = environmentIdElement.GetString();
            var source = sourceElement.GetString();
            if (string.IsNullOrEmpty(sessionId) ||
                string.IsNullOrEmpty(environmentId) ||
                source is not ("standalone" or "repl"))
            {
                return false;
            }

            pointer = new BridgePointer(sessionId, environmentId, source);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string SimpleHash(string value)
    {
        var hash = 0;
        foreach (var character in value)
        {
            hash = ((hash << 5) - hash + character) | 0;
        }

        return Math.Abs(hash).ToString("x", System.Globalization.CultureInfo.InvariantCulture);
    }
}
