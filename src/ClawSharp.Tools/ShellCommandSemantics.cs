// TS origin: ./tools/BashTool/commandSemantics.ts, ./tools/PowerShellTool/commandSemantics.ts, ./tools/BashTool/BashTool.tsx, ./tools/PowerShellTool/PowerShellTool.tsx
using System.Text.RegularExpressions;

namespace ClawSharp.Tools;

internal enum ShellToolKind
{
    Bash,
    PowerShell
}

internal sealed record ShellCommandInterpretation(
    bool IsError,
    string? Message = null);

internal static partial class ShellCommandSemantics
{
    private static readonly HashSet<string> BashSilentCommands =
    [
        "mv",
        "cp",
        "rm",
        "mkdir",
        "rmdir",
        "chmod",
        "chown",
        "chgrp",
        "touch",
        "ln",
        "cd",
        "export",
        "unset",
        "wait"
    ];

    private static readonly HashSet<string> BashSemanticNeutralCommands =
    [
        "echo",
        "printf",
        "true",
        "false",
        ":"
    ];

    public static ShellCommandInterpretation Interpret(
        ShellToolKind kind,
        string command,
        int exitCode,
        string stdout,
        string stderr)
    {
        var baseCommand = kind == ShellToolKind.PowerShell
            ? ExtractPowerShellBaseCommand(command)
            : ExtractBashBaseCommand(command);

        return kind switch
        {
            ShellToolKind.PowerShell => InterpretPowerShell(baseCommand, exitCode),
            _ => InterpretBash(baseCommand, exitCode)
        };
    }

    public static bool IsAutoBackgroundingAllowed(ShellToolKind kind, string command)
    {
        var baseCommand = kind == ShellToolKind.PowerShell
            ? ExtractPowerShellBaseCommand(command)
            : ExtractBashBaseCommand(command);

        return kind switch
        {
            ShellToolKind.PowerShell => !string.Equals(baseCommand, "start-sleep", StringComparison.OrdinalIgnoreCase) &&
                                        !string.Equals(baseCommand, "sleep", StringComparison.OrdinalIgnoreCase),
            _ => !string.Equals(baseCommand, "sleep", StringComparison.Ordinal)
        };
    }

    public static bool IsSilentBashCommand(string command)
    {
        var parts = SplitCompoundCommand(command, splitPipes: false);
        if (parts.Count == 0)
        {
            return false;
        }

        var hasNonFallbackCommand = false;
        var lastOperator = string.Empty;
        foreach (var token in parts)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (IsOperator(token))
            {
                lastOperator = token;
                continue;
            }

            var baseCommand = ExtractFirstWord(token);
            if (string.IsNullOrWhiteSpace(baseCommand))
            {
                continue;
            }

            if (string.Equals(lastOperator, "||", StringComparison.Ordinal) &&
                BashSemanticNeutralCommands.Contains(baseCommand))
            {
                continue;
            }

            hasNonFallbackCommand = true;
            if (!BashSilentCommands.Contains(baseCommand))
            {
                return false;
            }
        }

        return hasNonFallbackCommand;
    }

    private static ShellCommandInterpretation InterpretBash(string baseCommand, int exitCode)
    {
        return baseCommand switch
        {
            "grep" or "rg" => new ShellCommandInterpretation(exitCode >= 2, exitCode == 1 ? "No matches found" : null),
            "find" => new ShellCommandInterpretation(exitCode >= 2, exitCode == 1 ? "Some directories were inaccessible" : null),
            "diff" => new ShellCommandInterpretation(exitCode >= 2, exitCode == 1 ? "Files differ" : null),
            "test" or "[" => new ShellCommandInterpretation(exitCode >= 2, exitCode == 1 ? "Condition is false" : null),
            _ => new ShellCommandInterpretation(exitCode != 0, exitCode != 0 ? $"Command failed with exit code {exitCode}" : null)
        };
    }

    private static ShellCommandInterpretation InterpretPowerShell(string baseCommand, int exitCode)
    {
        return baseCommand switch
        {
            "grep" or "rg" or "findstr" => new ShellCommandInterpretation(exitCode >= 2, exitCode == 1 ? "No matches found" : null),
            "robocopy" => new ShellCommandInterpretation(
                exitCode >= 8,
                exitCode switch
                {
                    0 => "No files copied (already in sync)",
                    >= 1 and < 8 when (exitCode & 1) == 1 => "Files copied successfully",
                    >= 1 and < 8 => "Robocopy completed (no errors)",
                    _ => null
                }),
            _ => new ShellCommandInterpretation(exitCode != 0, exitCode != 0 ? $"Command failed with exit code {exitCode}" : null)
        };
    }

    private static string ExtractBashBaseCommand(string command)
    {
        var segments = SplitCompoundCommand(command, splitPipes: true);
        return segments.Count == 0 ? string.Empty : ExtractFirstWord(segments[^1]);
    }

    private static string ExtractPowerShellBaseCommand(string command)
    {
        var segments = command
            .Split([';', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return string.Empty;
        }

        var stripped = Regex.Replace(segments[^1], "^[&.]\\s+", string.Empty);
        var firstToken = ExtractFirstWord(stripped)
            .Trim('"', '\'');
        var fileName = Path.GetFileName(firstToken);
        if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName[..^4];
        }

        return fileName.ToLowerInvariant();
    }

    private static List<string> SplitCompoundCommand(string command, bool splitPipes)
    {
        var parts = new List<string>();
        var buffer = new System.Text.StringBuilder(command.Length);
        var quote = '\0';
        for (var index = 0; index < command.Length; index++)
        {
            var current = command[index];
            if (quote != '\0')
            {
                buffer.Append(current);
                if (current == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                buffer.Append(current);
                continue;
            }

            if (current == '\\' && index + 1 < command.Length)
            {
                buffer.Append(current);
                buffer.Append(command[++index]);
                continue;
            }

            if (current == '|' && index + 1 < command.Length && command[index + 1] == '|')
            {
                FlushBuffer(parts, buffer);
                parts.Add("||");
                index++;
                continue;
            }

            if (current == '&' && index + 1 < command.Length && command[index + 1] == '&')
            {
                FlushBuffer(parts, buffer);
                parts.Add("&&");
                index++;
                continue;
            }

            if (current == ';' || (splitPipes && current == '|'))
            {
                FlushBuffer(parts, buffer);
                parts.Add(current.ToString());
                continue;
            }

            buffer.Append(current);
        }

        FlushBuffer(parts, buffer);
        return parts;
    }

    private static void FlushBuffer(List<string> parts, System.Text.StringBuilder buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        var value = buffer.ToString().Trim();
        if (value.Length > 0)
        {
            parts.Add(value);
        }

        buffer.Clear();
    }

    private static bool IsOperator(string token)
    {
        return token is "||" or "&&" or "|" or ";";
    }

    private static string ExtractFirstWord(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var match = FirstWordRegex().Match(command.Trim());
        return match.Success ? match.Value : string.Empty;
    }

    [GeneratedRegex(@"^\S+", RegexOptions.CultureInvariant)]
    private static partial Regex FirstWordRegex();
}
