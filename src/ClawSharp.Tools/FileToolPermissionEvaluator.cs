using System.Text;
using ClawSharp.Core;

namespace ClawSharp.Tools;

internal static class FileToolPermissionEvaluator
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> SessionAllowedPaths = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<FileToolPermissionResolution> ResolveForReadAsync(
        string inputPath,
        string workspaceRoot,
        ToolPermissionContext permissionContext,
        IPermissionPrompter permissionPrompter,
        CancellationToken cancellationToken = default)
    {
        var validation = FileToolPathValidation.Validate(inputPath, workspaceRoot, FileToolOperationType.Read);
        if (!validation.Allowed || validation.ResolvedPath is null)
        {
            return FileToolPermissionResolution.Invalid(validation.Message ?? "Read permission denied.");
        }

        var pathsToCheck = FileToolPathResolution.GetPathsForPermissionCheck(validation.ResolvedPath);
        if (SessionAllowedPaths.ContainsKey(validation.ResolvedPath))
        {
            return new FileToolPermissionResolution(true, validation.CleanPath, validation.ResolvedPath, null);
        }

        var permissionDecision = EvaluateRead(
            validation.ResolvedPath,
            workspaceRoot,
            permissionContext,
            pathsToCheck);
            
        bool allowed = permissionDecision.Behavior == FileToolPermissionBehavior.Allow;
        if (permissionDecision.Behavior == FileToolPermissionBehavior.Ask)
        {
            var decision = await permissionPrompter.PromptAsync(
                permissionDecision.Message ?? $"Permission to read {validation.ResolvedPath} is required.", 
                cancellationToken);
                
            allowed = decision is PromptPermissionDecision.Allow or PromptPermissionDecision.AlwaysAllow;
            
            if (decision == PromptPermissionDecision.AlwaysAllow)
            {
                SessionAllowedPaths[validation.ResolvedPath] = true;
            }
        }
            
        if (!allowed)
        {
            return FileToolPermissionResolution.Invalid(permissionDecision.Message ?? "Read permission denied.");
        }

        return new FileToolPermissionResolution(true, validation.CleanPath, validation.ResolvedPath, null);
    }

    public static async Task<FileToolPermissionResolution> ResolveForWriteAsync(
        string inputPath,
        string workspaceRoot,
        ToolPermissionContext permissionContext,
        IPermissionPrompter permissionPrompter,
        bool allowCreate,
        CancellationToken cancellationToken = default)
    {
        var validation = FileToolPathValidation.Validate(
            inputPath,
            workspaceRoot,
            allowCreate ? FileToolOperationType.Create : FileToolOperationType.Write);
        if (!validation.Allowed || validation.ResolvedPath is null)
        {
            return FileToolPermissionResolution.Invalid(validation.Message ?? "Write permission denied.");
        }

        var pathsToCheck = FileToolPathResolution.GetPathsForPermissionCheck(validation.ResolvedPath);
        if (SessionAllowedPaths.ContainsKey(validation.ResolvedPath))
        {
            return new FileToolPermissionResolution(true, validation.CleanPath, validation.ResolvedPath, null);
        }

        var permissionDecision = EvaluateWrite(
            validation.ResolvedPath,
            workspaceRoot,
            permissionContext,
            pathsToCheck);
            
        bool allowed = permissionDecision.Behavior == FileToolPermissionBehavior.Allow;
        if (permissionDecision.Behavior == FileToolPermissionBehavior.Ask)
        {
            var decision = await permissionPrompter.PromptAsync(
                permissionDecision.Message ?? $"Permission to write {validation.ResolvedPath} is required.", 
                cancellationToken);
                
            allowed = decision is PromptPermissionDecision.Allow or PromptPermissionDecision.AlwaysAllow;
            
            if (decision == PromptPermissionDecision.AlwaysAllow)
            {
                SessionAllowedPaths[validation.ResolvedPath] = true;
            }
        }
            
        if (!allowed)
        {
            return FileToolPermissionResolution.Invalid(permissionDecision.Message ?? "Write permission denied.");
        }

        return new FileToolPermissionResolution(true, validation.CleanPath, validation.ResolvedPath, null);
    }

    public static FileToolPermissionDecision EvaluateRead(
        string path,
        string workspaceRoot,
        ToolPermissionContext permissionContext,
        IReadOnlyList<string>? precomputedPathsToCheck = null)
    {
        var pathsToCheck = precomputedPathsToCheck ?? FileToolPathResolution.GetPathsForPermissionCheck(path);
        if (ShouldBypassPermissions(permissionContext))
        {
            return FileToolPermissionDecision.Allow();
        }

        if (MatchesAnyRule(pathsToCheck, workspaceRoot, permissionContext, permissionContext.AlwaysDenyRules))
        {
            return FileToolPermissionDecision.Deny($"Permission to read {path} has been denied.");
        }

        if (MatchesAnyRule(pathsToCheck, workspaceRoot, permissionContext, permissionContext.AlwaysAskRules))
        {
            return FileToolPermissionDecision.Ask(ApprovalPromptText.CreateReadPermissionRequestMessage(path));
        }

        var writeDecision = EvaluateWrite(path, workspaceRoot, permissionContext, pathsToCheck);
        if (writeDecision.Behavior == FileToolPermissionBehavior.Allow)
        {
            return writeDecision;
        }

        if (IsInAllowedWorkingDirectory(pathsToCheck, workspaceRoot, permissionContext))
        {
            return FileToolPermissionDecision.Allow();
        }

        if (MatchesAnyRule(pathsToCheck, workspaceRoot, permissionContext, permissionContext.AlwaysAllowRules))
        {
            return FileToolPermissionDecision.Allow();
        }

        return FileToolPermissionDecision.Ask(
            ApprovalPromptText.CreateReadPermissionRequestMessage(path));
    }

    public static FileToolPermissionDecision EvaluateWrite(
        string path,
        string workspaceRoot,
        ToolPermissionContext permissionContext,
        IReadOnlyList<string>? precomputedPathsToCheck = null)
    {
        var pathsToCheck = precomputedPathsToCheck ?? FileToolPathResolution.GetPathsForPermissionCheck(path);
        if (MatchesAnyRule(pathsToCheck, workspaceRoot, permissionContext, permissionContext.AlwaysDenyRules))
        {
            return FileToolPermissionDecision.Deny($"Permission to edit {path} has been denied.");
        }

        var safetyCheck = FileToolAutoEditSafety.Check(path, workspaceRoot, pathsToCheck);
        if (!safetyCheck.Safe)
        {
            return FileToolPermissionDecision.Ask(
                safetyCheck.Message ?? $"Claude requested permissions to write to {path}, but you haven't granted it yet.");
        }

        if (MatchesAnyRule(pathsToCheck, workspaceRoot, permissionContext, permissionContext.AlwaysAskRules))
        {
            return FileToolPermissionDecision.Ask(ApprovalPromptText.CreateWritePermissionRequestMessage(path));
        }

        if (ShouldBypassPermissions(permissionContext))
        {
            return FileToolPermissionDecision.Allow();
        }

        if (IsInAllowedWorkingDirectory(pathsToCheck, workspaceRoot, permissionContext) &&
            AllowsWorkingDirectoryWritesInCurrentRuntime(permissionContext))
        {
            return FileToolPermissionDecision.Allow();
        }

        if (MatchesAnyRule(pathsToCheck, workspaceRoot, permissionContext, permissionContext.AlwaysAllowRules))
        {
            return FileToolPermissionDecision.Allow();
        }

        return FileToolPermissionDecision.Ask(
            ApprovalPromptText.CreateWritePermissionRequestMessage(path));
    }

    public static string ResolvePath(string workspaceRoot, string inputPath)
    {
        var expanded = ExpandPath(inputPath);
        return Path.GetFullPath(
            Path.IsPathRooted(expanded)
                ? expanded
                : Path.Combine(workspaceRoot, expanded));
    }

    private static bool ShouldBypassPermissions(ToolPermissionContext permissionContext)
    {
        return permissionContext.Mode == PermissionMode.BypassPermissions ||
               (permissionContext.Mode == PermissionMode.Plan && permissionContext.IsBypassPermissionsModeAvailable);
    }

    private static bool AllowsWorkingDirectoryWritesInCurrentRuntime(ToolPermissionContext permissionContext)
    {
        return permissionContext.Mode is PermissionMode.Default or
               PermissionMode.AcceptEdits or
               PermissionMode.DontAsk or
               PermissionMode.Plan;
    }

    private static bool IsInAllowedWorkingDirectory(
        IReadOnlyList<string> pathsToCheck,
        string workspaceRoot,
        ToolPermissionContext permissionContext)
    {
        foreach (var absolutePath in pathsToCheck.Select(NormalizeAbsolutePath))
        {
            var isWithinAllowedRoot = false;
            foreach (var root in GetAllowedRoots(workspaceRoot, permissionContext))
            {
                if (IsWithinRoot(absolutePath, NormalizeAbsolutePath(root)))
                {
                    isWithinAllowedRoot = true;
                    break;
                }
            }

            if (!isWithinAllowedRoot)
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> GetAllowedRoots(string workspaceRoot, ToolPermissionContext permissionContext)
    {
        foreach (var rootForm in FileToolPathResolution.GetPathsForPermissionCheck(Path.GetFullPath(workspaceRoot)))
        {
            yield return rootForm;
        }

        foreach (var additionalWorkingDirectory in permissionContext.AdditionalWorkingDirectories.Values)
        {
            foreach (var rootForm in FileToolPathResolution.GetPathsForPermissionCheck(Path.GetFullPath(ExpandPath(additionalWorkingDirectory.Path))))
            {
                yield return rootForm;
            }
        }
    }

    private static bool MatchesAnyRule(
        IReadOnlyList<string> pathsToCheck,
        string workspaceRoot,
        ToolPermissionContext permissionContext,
        IReadOnlyDictionary<PermissionRuleSource, IReadOnlyList<string>> ruleSets)
    {
        foreach (var source in Enum.GetValues<PermissionRuleSource>())
        {
            if (!ruleSets.TryGetValue(source, out var rules))
            {
                continue;
            }

            foreach (var rule in rules)
            {
                foreach (var pathToCheck in pathsToCheck)
                {
                    if (PathMatchesRule(pathToCheck, workspaceRoot, permissionContext, rule))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool PathMatchesRule(
        string path,
        string workspaceRoot,
        ToolPermissionContext permissionContext,
        string rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return false;
        }

        var absolutePath = NormalizeAbsolutePath(path);
        var normalizedRule = Path.IsPathRooted(ExpandPath(rule)) || rule.StartsWith("~", StringComparison.Ordinal)
            ? NormalizeAbsolutePath(ExpandPath(rule))
            : NormalizeRelativePath(rule);

        if (WildcardMatch(normalizedRule, absolutePath))
        {
            return true;
        }

        foreach (var root in GetAllowedRoots(workspaceRoot, permissionContext))
        {
            var normalizedRoot = NormalizeAbsolutePath(root);
            var relativePath = GetRelativePathIfWithinRoot(absolutePath, normalizedRoot);
            if (relativePath is null)
            {
                continue;
            }

            if (WildcardMatch(normalizedRule, relativePath))
            {
                return true;
            }

            var rootedRule = NormalizeAbsolutePath(Path.Combine(root, rule));
            if (WildcardMatch(rootedRule, absolutePath))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetRelativePathIfWithinRoot(string absolutePath, string normalizedRoot)
    {
        if (!IsWithinRoot(absolutePath, normalizedRoot))
        {
            return null;
        }

        var relative = Path.GetRelativePath(normalizedRoot, absolutePath);
        return NormalizeRelativePath(relative);
    }

    private static bool IsWithinRoot(string absolutePath, string root)
    {
        absolutePath = NormalizeMacOsSystemAliases(absolutePath);
        root = NormalizeMacOsSystemAliases(root);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return absolutePath.Equals(root, comparison) ||
               absolutePath.StartsWith(root.TrimEnd('/') + "/", comparison);
    }

    private static string NormalizeAbsolutePath(string path)
    {
        return Path.GetFullPath(path)
            .Replace('\\', '/')
            .TrimEnd('/');
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/').TrimEnd('/');
    }

    private static string ExpandPath(string path)
    {
        return PathUtilities.NormalizePathInputForCurrentPlatform(path);
    }

    private static string NormalizeMacOsSystemAliases(string path)
    {
        return path
            .Replace("/private/var/", "/var/", StringComparison.Ordinal)
            .Replace("/private/tmp/", "/tmp/", StringComparison.Ordinal);
    }

    private static bool WildcardMatch(string pattern, string value)
    {
        var regexPattern = new StringBuilder();
        regexPattern.Append('^');
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            if (character == '*')
            {
                regexPattern.Append(".*");
                continue;
            }

            if ("\\.^$+?()[]{}|".Contains(character))
            {
                regexPattern.Append('\\');
            }

            regexPattern.Append(character);
        }

        regexPattern.Append('$');
        var comparison = OperatingSystem.IsWindows()
            ? System.Text.RegularExpressions.RegexOptions.IgnoreCase
            : System.Text.RegularExpressions.RegexOptions.None;
        return System.Text.RegularExpressions.Regex.IsMatch(value, regexPattern.ToString(), comparison);
    }
}

internal sealed record FileToolPermissionResolution(
    bool Allowed,
    string InputPath,
    string? ResolvedPath,
    string? Message)
{
    public static FileToolPermissionResolution Invalid(string message)
    {
        return new FileToolPermissionResolution(false, string.Empty, null, message);
    }
}

internal enum FileToolPermissionBehavior
{
    Allow,
    Ask,
    Deny
}

internal sealed record FileToolPermissionDecision(
    FileToolPermissionBehavior Behavior,
    string? Message)
{
    public static FileToolPermissionDecision Allow()
    {
        return new FileToolPermissionDecision(FileToolPermissionBehavior.Allow, null);
    }

    public static FileToolPermissionDecision Ask(string message)
    {
        return new FileToolPermissionDecision(FileToolPermissionBehavior.Ask, message);
    }

    public static FileToolPermissionDecision Deny(string message)
    {
        return new FileToolPermissionDecision(FileToolPermissionBehavior.Deny, message);
    }
}
