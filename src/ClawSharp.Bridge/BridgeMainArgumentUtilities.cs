// TS origin: ./bridge/bridgeMain.ts
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Bridge;

public sealed record BridgeMainParserOptions(
    bool KairosEnabled = false);

public sealed record BridgeParsedArgs(
    bool Verbose,
    bool Sandbox,
    string? DebugFile,
    double? SessionTimeoutMs,
    string? PermissionMode,
    string? Name,
    SpawnMode? SpawnMode,
    int? Capacity,
    bool? CreateSessionInDir,
    string? SessionId,
    bool ContinueSession,
    bool Help,
    string? Error = null);

public static class BridgeMainArgumentUtilities
{
    private static readonly HashSet<string> ConnectionErrorCodes =
    [
        "ECONNREFUSED",
        "ECONNRESET",
        "ETIMEDOUT",
        "ENETUNREACH",
        "EHOSTUNREACH"
    ];

    public static bool IsConnectionError(object? error)
    {
        var code = TryGetCode(error);
        return code is not null && ConnectionErrorCodes.Contains(code);
    }

    public static bool IsServerError(object? error)
    {
        return string.Equals(TryGetCode(error), "ERR_BAD_RESPONSE", StringComparison.Ordinal);
    }

    public static BridgeParsedArgs ParseArgs(
        IReadOnlyList<string> args,
        BridgeMainParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        options ??= new BridgeMainParserOptions();

        var verbose = false;
        var sandbox = false;
        string? debugFile = null;
        double? sessionTimeoutMs = null;
        string? permissionMode = null;
        string? name = null;
        var help = false;
        SpawnMode? spawnMode = null;
        int? capacity = null;
        bool? createSessionInDir = null;
        string? sessionId = null;
        var continueSession = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is "--help" or "-h")
            {
                help = true;
            }
            else if (arg is "--verbose" or "-v")
            {
                verbose = true;
            }
            else if (arg == "--sandbox")
            {
                sandbox = true;
            }
            else if (arg == "--no-sandbox")
            {
                sandbox = false;
            }
            else if (arg == "--debug-file" && i + 1 < args.Count)
            {
                debugFile = Path.GetFullPath(args[++i]);
            }
            else if (arg.StartsWith("--debug-file=", StringComparison.Ordinal))
            {
                debugFile = Path.GetFullPath(arg["--debug-file=".Length..]);
            }
            else if (arg == "--session-timeout" && i + 1 < args.Count)
            {
                sessionTimeoutMs = ParseJavaScriptInt(args[++i]) * 1000;
            }
            else if (arg.StartsWith("--session-timeout=", StringComparison.Ordinal))
            {
                sessionTimeoutMs = ParseJavaScriptInt(arg["--session-timeout=".Length..]) * 1000;
            }
            else if (arg == "--permission-mode" && i + 1 < args.Count)
            {
                permissionMode = args[++i];
            }
            else if (arg.StartsWith("--permission-mode=", StringComparison.Ordinal))
            {
                permissionMode = arg["--permission-mode=".Length..];
            }
            else if (arg == "--name" && i + 1 < args.Count)
            {
                name = args[++i];
            }
            else if (arg.StartsWith("--name=", StringComparison.Ordinal))
            {
                name = arg["--name=".Length..];
            }
            else if (options.KairosEnabled && arg == "--session-id" && i + 1 < args.Count)
            {
                sessionId = args[++i];
                if (string.IsNullOrEmpty(sessionId))
                {
                    return MakeError("--session-id requires a value");
                }
            }
            else if (options.KairosEnabled && arg.StartsWith("--session-id=", StringComparison.Ordinal))
            {
                sessionId = arg["--session-id=".Length..];
                if (string.IsNullOrEmpty(sessionId))
                {
                    return MakeError("--session-id requires a value");
                }
            }
            else if (options.KairosEnabled && arg is "--continue" or "-c")
            {
                continueSession = true;
            }
            else if (arg == "--spawn" || arg.StartsWith("--spawn=", StringComparison.Ordinal))
            {
                if (spawnMode is not null)
                {
                    return MakeError("--spawn may only be specified once");
                }

                var raw = arg.StartsWith("--spawn=", StringComparison.Ordinal)
                    ? arg["--spawn=".Length..]
                    : i + 1 < args.Count
                        ? args[++i]
                        : null;
                var parsedSpawnMode = ParseSpawnValue(raw);
                if (parsedSpawnMode.Error is not null)
                {
                    return MakeError(parsedSpawnMode.Error);
                }

                spawnMode = parsedSpawnMode.Value;
            }
            else if (arg == "--capacity" || arg.StartsWith("--capacity=", StringComparison.Ordinal))
            {
                if (capacity is not null)
                {
                    return MakeError("--capacity may only be specified once");
                }

                var raw = arg.StartsWith("--capacity=", StringComparison.Ordinal)
                    ? arg["--capacity=".Length..]
                    : i + 1 < args.Count
                        ? args[++i]
                        : null;
                var parsedCapacity = ParseCapacityValue(raw);
                if (parsedCapacity.Error is not null)
                {
                    return MakeError(parsedCapacity.Error);
                }

                capacity = parsedCapacity.Value;
            }
            else if (arg == "--create-session-in-dir")
            {
                createSessionInDir = true;
            }
            else if (arg == "--no-create-session-in-dir")
            {
                createSessionInDir = false;
            }
            else
            {
                return MakeError($"Unknown argument: {arg}\nRun '{AppMetadata.RemoteControlCommand} --help' for usage.");
            }
        }

        if (spawnMode == Bridge.SpawnMode.SingleSession && capacity is not null)
        {
            return MakeError("--capacity cannot be used with --spawn=session (single-session mode has fixed capacity 1).");
        }

        if ((sessionId is not null || continueSession) &&
            (spawnMode is not null || capacity is not null || createSessionInDir is not null))
        {
            return MakeError("--session-id and --continue cannot be used with --spawn, --capacity, or --create-session-in-dir.");
        }

        if (sessionId is not null && continueSession)
        {
            return MakeError("--session-id and --continue cannot be used together.");
        }

        return BuildResult();

        BridgeParsedArgs MakeError(string error)
        {
            return BuildResult(error);
        }

        BridgeParsedArgs BuildResult(string? error = null)
        {
            return new BridgeParsedArgs(
                verbose,
                sandbox,
                debugFile,
                sessionTimeoutMs,
                permissionMode,
                name,
                spawnMode,
                capacity,
                createSessionInDir,
                sessionId,
                continueSession,
                help,
                error);
        }
    }

    private static (SpawnMode? Value, string? Error) ParseSpawnValue(string? raw)
    {
        return raw switch
        {
            "session" => (Bridge.SpawnMode.SingleSession, null),
            "same-dir" => (Bridge.SpawnMode.SameDir, null),
            "worktree" => (Bridge.SpawnMode.Worktree, null),
            _ => (null, $"--spawn requires one of: session, same-dir, worktree (got: {raw ?? "<missing>"})")
        };
    }

    private static (int? Value, string? Error) ParseCapacityValue(string? raw)
    {
        var parsed = ParseJavaScriptInt(raw);
        if (double.IsNaN(parsed) || parsed < 1)
        {
            return (null, $"--capacity requires a positive integer (got: {raw ?? "<missing>"})");
        }

        return ((int)parsed, null);
    }

    private static double ParseJavaScriptInt(string? raw)
    {
        if (raw is null)
        {
            return double.NaN;
        }

        var trimmed = raw.TrimStart();
        if (trimmed.Length == 0)
        {
            return double.NaN;
        }

        var sign = 1;
        var start = 0;
        if (trimmed[0] is '+' or '-')
        {
            sign = trimmed[0] == '-' ? -1 : 1;
            start = 1;
        }

        var index = start;
        while (index < trimmed.Length && char.IsAsciiDigit(trimmed[index]))
        {
            index++;
        }

        if (index == start)
        {
            return double.NaN;
        }

        var digits = trimmed[start..index];
        if (!double.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return double.NaN;
        }

        return parsed * sign;
    }

    private static string? TryGetCode(object? error)
    {
        switch (error)
        {
            case null:
                return null;
            case JsonObject jsonObject when jsonObject["code"] is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var jsonCode):
                return jsonCode;
            case IReadOnlyDictionary<string, object?> readOnlyDictionary when readOnlyDictionary.TryGetValue("code", out var readOnlyValue):
                return readOnlyValue as string;
            case IDictionary<string, object?> dictionary when dictionary.TryGetValue("code", out var value):
                return value as string;
        }

        var property = error.GetType().GetProperty("Code", BindingFlags.Instance | BindingFlags.Public);
        return property?.PropertyType == typeof(string)
            ? property.GetValue(error) as string
            : null;
    }
}
