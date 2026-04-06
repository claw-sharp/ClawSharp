namespace ClawSharp.Core;

public static class ApprovalPromptText
{
    public static string CreateToolPermissionRequestMessage(string toolName)
    {
        return $"Claude requested permissions to use {toolName}, but you haven't granted it yet.";
    }

    public static string CreateReadPermissionRequestMessage(string path)
    {
        return $"Claude requested permissions to read from {path}, but you haven't granted it yet.";
    }

    public static string CreateWritePermissionRequestMessage(string path)
    {
        return $"Claude requested permissions to write to {path}, but you haven't granted it yet.";
    }

    public static bool ShouldShowAlwaysAllowOptions(bool allowManagedPermissionRulesOnly)
    {
        return !allowManagedPermissionRulesOnly;
    }
}
