using ClawSharp.Core;

namespace ClawSharp.Tools;

internal sealed record ShellToolPermissionResolution(
    bool Allowed,
    string? Message)
{
    public static ShellToolPermissionResolution Denied(string message)
    {
        return new ShellToolPermissionResolution(false, message);
    }

    public static ShellToolPermissionResolution AllowedResult()
    {
        return new ShellToolPermissionResolution(true, null);
    }
}

internal static class ShellToolPermissionRuntime
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> SessionAllowedCommands =
        new(StringComparer.Ordinal);

    internal static void ResetSessionCache()
    {
        SessionAllowedCommands.Clear();
    }

    public static async Task<ShellToolPermissionResolution> ResolveAsync(
        string toolName,
        string command,
        ToolExecutionContext context,
        bool dangerouslyDisableSandbox,
        bool caseInsensitive,
        CancellationToken cancellationToken = default)
    {
        var permissionContext = context.ToolPermissionContext;
        var cacheKey = CreateCacheKey(toolName, dangerouslyDisableSandbox);
        if (SessionAllowedCommands.ContainsKey(cacheKey))
        {
            return ShellToolPermissionResolution.AllowedResult();
        }

        var decision = ShellPermissionEvaluator.Evaluate(
            toolName,
            command,
            permissionContext,
            dangerouslyDisableSandbox,
            caseInsensitive);

        if (decision.Behavior == FileToolPermissionBehavior.Allow)
        {
            return ShellToolPermissionResolution.AllowedResult();
        }

        if (decision.Behavior == FileToolPermissionBehavior.Deny ||
            permissionContext.ShouldAvoidPermissionPrompts == true)
        {
            return ShellToolPermissionResolution.Denied(
                decision.Message ?? $"Permission to use {toolName} has been denied.");
        }

        var promptDecision = await context.PermissionPrompter.PromptAsync(
            decision.Message ?? ApprovalPromptText.CreateToolPermissionRequestMessage(toolName),
            cancellationToken).ConfigureAwait(false);
        if (promptDecision is PromptPermissionDecision.Allow or PromptPermissionDecision.AlwaysAllow)
        {
            if (promptDecision == PromptPermissionDecision.AlwaysAllow)
            {
                SessionAllowedCommands[cacheKey] = true;
                if (!dangerouslyDisableSandbox)
                {
                    RememberToolAllowedForSession(context, toolName);
                }
            }

            return ShellToolPermissionResolution.AllowedResult();
        }

        return ShellToolPermissionResolution.Denied(
            decision.Message ?? $"Permission to use {toolName} has been denied.");
    }

    private static void RememberToolAllowedForSession(ToolExecutionContext context, string toolName)
    {
        var update = new AddPermissionRulesUpdate(
            PermissionUpdateDestination.Session,
            [new PermissionRuleValue(toolName)],
            PermissionBehavior.Allow);

        context.AppStateStore.SetState(
            state => state with
            {
                ToolPermissionContext = PermissionUpdateApplier.ApplyPermissionUpdate(
                    state.ToolPermissionContext,
                    update)
            });
    }

    private static string CreateCacheKey(string toolName, bool dangerouslyDisableSandbox)
    {
        return dangerouslyDisableSandbox
            ? $"{toolName}\u001funsandboxed"
            : $"{toolName}\u001ftool";
    }
}
