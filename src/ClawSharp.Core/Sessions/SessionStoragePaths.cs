using System.Globalization;
using System.Text;

namespace ClawSharp.Core;

public static class SessionStoragePaths
{
    public const int MaxSanitizedLength = 200;

    public static string GetClaudeConfigHomeDir()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var configHome = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".clawsharp")
            : configured;

        return configHome.Normalize(NormalizationForm.FormC);
    }

    public static string GetProjectsDir()
    {
        return Path.Combine(GetClaudeConfigHomeDir(), "projects");
    }

    public static string GetProjectDir(string projectDirectory)
    {
        return Path.Combine(GetProjectsDir(), SanitizePath(projectDirectory));
    }

    public static string GetTranscriptPath(string projectDirectory, string sessionId)
    {
        return Path.Combine(GetProjectDir(projectDirectory), $"{sessionId}.jsonl");
    }

    public static string GetSessionLogMetadataPath(string projectDirectory, string sessionId)
    {
        return Path.Combine(GetProjectDir(projectDirectory), $"{sessionId}.session.json");
    }

    public static string GetMemoryDir(string projectDirectory)
    {
        return Path.Combine(GetProjectDir(projectDirectory), "memory");
    }

    public static string GetFileHistorySessionDir(string sessionId)
    {
        return Path.Combine(GetClaudeConfigHomeDir(), "file-history", sessionId);
    }

    public static string GetFileHistoryBackupPath(string sessionId, string backupFileName)
    {
        return Path.Combine(GetFileHistorySessionDir(sessionId), backupFileName);
    }

    public static string SanitizePath(string name)
    {
        var sanitizedBuilder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            sanitizedBuilder.Append(IsAsciiAlphaNumeric(character) ? character : '-');
        }

        var sanitized = sanitizedBuilder.ToString();
        if (sanitized.Length <= MaxSanitizedLength)
        {
            return sanitized;
        }

        return $"{sanitized[..MaxSanitizedLength]}-{SimpleHash(name)}";
    }

    private static string SimpleHash(string value)
    {
        var hash = Djb2Hash(value);
        return ToBase36(Math.Abs(hash));
    }

    private static int Djb2Hash(string value)
    {
        var hash = 5381;
        foreach (var character in value)
        {
            unchecked
            {
                hash = ((hash << 5) + hash) + character;
            }
        }

        return hash;
    }

    private static bool IsAsciiAlphaNumeric(char value)
    {
        return
            (value >= 'a' && value <= 'z') ||
            (value >= 'A' && value <= 'Z') ||
            (value >= '0' && value <= '9');
    }

    private static string ToBase36(int value)
    {
        if (value == 0)
        {
            return "0";
        }

        const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
        var remaining = value;
        var buffer = new StringBuilder();

        while (remaining > 0)
        {
            buffer.Insert(0, alphabet[remaining % 36]);
            remaining /= 36;
        }

        return buffer.ToString();
    }
}
