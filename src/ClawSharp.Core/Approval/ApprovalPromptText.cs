namespace ClawSharp.Core;

public static class ApprovalPromptText
{
    public static string CreateToolPermissionRequestMessage(string toolName)
    {
        return $"ClawSharp to use {toolName}, but you haven't granted it yet.";
    }

    public static string CreateReadPermissionRequestMessage(string path)
    {
        return $"ClawSharp to read from {path}, but you haven't granted it yet.";
    }

    public static string CreateWritePermissionRequestMessage(string path)
    {
        return $"ClawSharp to write to {path}, but you haven't granted it yet.";
    }

    public static bool ShouldShowAlwaysAllowOptions(bool allowManagedPermissionRulesOnly)
    {
        return !allowManagedPermissionRulesOnly;
    }
}
