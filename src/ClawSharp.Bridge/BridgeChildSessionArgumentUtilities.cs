// TS origin: ./bridge/sessionRunner.ts, ./entrypoints/cli.tsx
namespace ClawSharp.Bridge;

public sealed record BridgeChildSessionOptions(
    string SdkUrl,
    string SessionId,
    string InputFormat,
    string OutputFormat,
    bool ReplayUserMessages,
    bool Verbose = false,
    string? DebugFile = null,
    string? PermissionMode = null);

public sealed record BridgeChildSessionParseResult(
    bool IsBridgeChildMode,
    BridgeChildSessionOptions? Options = null,
    string? Error = null);

public static class BridgeChildSessionArgumentUtilities
{
    public static BridgeChildSessionParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!args.Any(static arg => string.Equals(arg, "--print", StringComparison.Ordinal)))
        {
            return new BridgeChildSessionParseResult(false);
        }

        string? sdkUrl = null;
        string? sessionId = null;
        string? inputFormat = null;
        string? outputFormat = null;
        var replayUserMessages = false;
        var verbose = false;
        string? debugFile = null;
        string? permissionMode = null;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (arg == "--print")
            {
                continue;
            }

            if (arg == "--sdk-url")
            {
                if (!TryReadNext(args, ref index, out sdkUrl))
                {
                    return MakeError("--sdk-url requires a value");
                }

                continue;
            }

            if (arg.StartsWith("--sdk-url=", StringComparison.Ordinal))
            {
                sdkUrl = arg["--sdk-url=".Length..];
                if (string.IsNullOrWhiteSpace(sdkUrl))
                {
                    return MakeError("--sdk-url requires a value");
                }

                continue;
            }

            if (arg == "--session-id")
            {
                if (!TryReadNext(args, ref index, out sessionId))
                {
                    return MakeError("--session-id requires a value");
                }

                continue;
            }

            if (arg.StartsWith("--session-id=", StringComparison.Ordinal))
            {
                sessionId = arg["--session-id=".Length..];
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    return MakeError("--session-id requires a value");
                }

                continue;
            }

            if (arg == "--input-format")
            {
                if (!TryReadNext(args, ref index, out inputFormat))
                {
                    return MakeError("--input-format requires a value");
                }

                continue;
            }

            if (arg.StartsWith("--input-format=", StringComparison.Ordinal))
            {
                inputFormat = arg["--input-format=".Length..];
                if (string.IsNullOrWhiteSpace(inputFormat))
                {
                    return MakeError("--input-format requires a value");
                }

                continue;
            }

            if (arg == "--output-format")
            {
                if (!TryReadNext(args, ref index, out outputFormat))
                {
                    return MakeError("--output-format requires a value");
                }

                continue;
            }

            if (arg.StartsWith("--output-format=", StringComparison.Ordinal))
            {
                outputFormat = arg["--output-format=".Length..];
                if (string.IsNullOrWhiteSpace(outputFormat))
                {
                    return MakeError("--output-format requires a value");
                }

                continue;
            }

            if (arg == "--replay-user-messages")
            {
                replayUserMessages = true;
                continue;
            }

            if (arg == "--verbose")
            {
                verbose = true;
                continue;
            }

            if (arg == "--debug-file")
            {
                if (!TryReadNext(args, ref index, out debugFile))
                {
                    return MakeError("--debug-file requires a value");
                }

                debugFile = Path.GetFullPath(debugFile);
                continue;
            }

            if (arg.StartsWith("--debug-file=", StringComparison.Ordinal))
            {
                debugFile = arg["--debug-file=".Length..];
                if (string.IsNullOrWhiteSpace(debugFile))
                {
                    return MakeError("--debug-file requires a value");
                }

                debugFile = Path.GetFullPath(debugFile);
                continue;
            }

            if (arg == "--permission-mode")
            {
                if (!TryReadNext(args, ref index, out permissionMode))
                {
                    return MakeError("--permission-mode requires a value");
                }

                continue;
            }

            if (arg.StartsWith("--permission-mode=", StringComparison.Ordinal))
            {
                permissionMode = arg["--permission-mode=".Length..];
                if (string.IsNullOrWhiteSpace(permissionMode))
                {
                    return MakeError("--permission-mode requires a value");
                }

                continue;
            }

            return MakeError($"Unknown bridge child argument: {arg}");
        }

        if (string.IsNullOrWhiteSpace(sdkUrl))
        {
            return MakeError("--sdk-url is required with --print");
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return MakeError("--session-id is required with --print");
        }

        if (!string.Equals(inputFormat, "stream-json", StringComparison.Ordinal))
        {
            return MakeError("--input-format must be stream-json");
        }

        if (!string.Equals(outputFormat, "stream-json", StringComparison.Ordinal))
        {
            return MakeError("--output-format must be stream-json");
        }

        return new BridgeChildSessionParseResult(
            true,
            new BridgeChildSessionOptions(
                sdkUrl,
                sessionId,
                inputFormat!,
                outputFormat!,
                replayUserMessages,
                verbose,
                debugFile,
                permissionMode));
    }

    private static bool TryReadNext(IReadOnlyList<string> args, ref int index, out string value)
    {
        if (index + 1 < args.Count && !string.IsNullOrWhiteSpace(args[index + 1]))
        {
            value = args[++index];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static BridgeChildSessionParseResult MakeError(string error)
    {
        return new BridgeChildSessionParseResult(true, Error: error);
    }
}
