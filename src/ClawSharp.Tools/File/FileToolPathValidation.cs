namespace ClawSharp.Tools;

internal static class FileToolPathValidation
{
    private static readonly char[] QuoteCharacters = ['"', '\''];

    public static FileToolPathValidationResult Validate(
        string inputPath,
        string workspaceRoot,
        FileToolOperationType operationType)
    {
        var cleanPath = CleanPath(inputPath);

        if (ContainsVulnerableUncPath(cleanPath))
        {
            return FileToolPathValidationResult.Blocked(
                cleanPath,
                "UNC network paths require manual approval");
        }

        if (cleanPath.StartsWith('~'))
        {
            return FileToolPathValidationResult.Blocked(
                cleanPath,
                "Tilde expansion variants (~user, ~+, ~-) in paths require manual approval");
        }

        if (cleanPath.Contains('$') ||
            cleanPath.Contains('%') ||
            cleanPath.StartsWith("=", StringComparison.Ordinal))
        {
            return FileToolPathValidationResult.Blocked(
                cleanPath,
                "Shell expansion syntax in paths requires manual approval");
        }

        if ((operationType == FileToolOperationType.Write || operationType == FileToolOperationType.Create) &&
            ContainsGlobPattern(cleanPath))
        {
            return FileToolPathValidationResult.Blocked(
                cleanPath,
                "Glob patterns are not allowed in write operations. Please specify an exact file path.");
        }

        var resolvedPath = ResolvePath(workspaceRoot, cleanPath);
        var pathsToCheck = FileToolPathResolution.GetPathsForPermissionCheck(resolvedPath);
        if ((operationType == FileToolOperationType.Write || operationType == FileToolOperationType.Create) &&
            FileToolAutoEditSafety.Check(cleanPath, workspaceRoot, pathsToCheck) is { Safe: false } safetyResult)
        {
            return FileToolPathValidationResult.Blocked(cleanPath, safetyResult.Message ?? "Write permission denied.");
        }

        return FileToolPathValidationResult.CreateAllowed(cleanPath, resolvedPath);
    }

    private static string CleanPath(string inputPath)
    {
        var trimmed = inputPath.Trim();
        if (trimmed.Length >= 2 &&
            QuoteCharacters.Contains(trimmed[0]) &&
            trimmed[^1] == trimmed[0])
        {
            trimmed = trimmed[1..^1];
        }

        return FileToolPathResolution.ExpandLeadingHomePath(trimmed);
    }

    private static bool ContainsGlobPattern(string path)
    {
        foreach (var character in path)
        {
            if (character is '*' or '?' or '[' or ']' or '{' or '}')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsVulnerableUncPath(string path)
    {
        return path.StartsWith("\\\\", StringComparison.Ordinal) ||
               path.StartsWith("//", StringComparison.Ordinal);
    }

    private static string ResolvePath(string workspaceRoot, string cleanPath)
    {
        return Path.GetFullPath(
            Path.IsPathRooted(cleanPath)
                ? cleanPath
                : Path.Combine(workspaceRoot, cleanPath));
    }
}

internal enum FileToolOperationType
{
    Read,
    Write,
    Create
}

internal sealed record FileToolPathValidationResult(
    bool Allowed,
    string CleanPath,
    string? ResolvedPath,
    string? Message)
{
    public static FileToolPathValidationResult CreateAllowed(string cleanPath, string resolvedPath)
    {
        return new FileToolPathValidationResult(true, cleanPath, resolvedPath, null);
    }

    public static FileToolPathValidationResult Blocked(string cleanPath, string message)
    {
        return new FileToolPathValidationResult(false, cleanPath, null, message);
    }
}
