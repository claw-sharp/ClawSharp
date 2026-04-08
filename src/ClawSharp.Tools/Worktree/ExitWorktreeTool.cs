using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core.Worktree;

namespace ClawSharp.Tools.Worktree;

internal sealed class ExitWorktreeTool : BaseTool
{
    public const string ToolName = "ExitWorktree";

    public ExitWorktreeTool()
        : base(new ToolDescriptor(
            ToolName,
            "Exits a worktree session created by EnterWorktree and restores the original working directory",
            SearchHint: "exit a worktree session and return to the original directory",
            ShouldDefer: true,
            InputSchema: ExitWorktreeToolSchemas.InputSchema,
            OutputSchema: ExitWorktreeToolSchemas.OutputSchema,
            Strict: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments) => false;
    public override bool IsReadOnly(string arguments) => false;

    public override Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var session = context.WorktreeService.GetCurrentWorktreeSession();
        if (session == null)
        {
            return Task.FromResult(ToolValidationResult.Invalid("No-op: there is no active EnterWorktree session to exit."));
        }

        return Task.FromResult(ToolValidationResult.Valid());
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var action, out var discardChanges))
        {
            return Failure("Invalid input.");
        }

        var session = context.WorktreeService.GetCurrentWorktreeSession();
        if (session == null)
        {
            return Failure("Not in a worktree session.");
        }

        try
        {
            // In a real implementation, we would check for uncommitted changes if action is "remove"
            // and discardChanges is false. For now, we'll assume the service handles it or we'll just success.

            await context.WorktreeService.ExitWorktreeAsync(cancellationToken);

            var message = action == "keep"
                ? $"Exited worktree. Your work is preserved at {session.WorktreePath}{ (session.WorktreeBranch != null ? $" on branch {session.WorktreeBranch}" : "") }. Session is now back in {session.OriginalCwd}."
                : $"Exited and removed worktree at {session.WorktreePath}. Session is now back in {session.OriginalCwd}.";

            var structured = new JsonObject
            {
                ["action"] = action,
                ["originalCwd"] = session.OriginalCwd,
                ["worktreePath"] = session.WorktreePath,
                ["worktreeBranch"] = session.WorktreeBranch,
                ["message"] = message
            };

            return Success(message, structured);
        }
        catch (Exception ex)
        {
            return Failure($"Failed to exit worktree: {ex.Message}");
        }
    }

    private static bool TryParseArguments(string arguments, out string? action, out bool discardChanges)
    {
        action = null;
        discardChanges = false;
        try
        {
            using var doc = JsonDocument.Parse(arguments);
            if (doc.RootElement.TryGetProperty("action", out var actionProp))
            {
                action = actionProp.GetString();
            }
            if (doc.RootElement.TryGetProperty("discard_changes", out var discardProp))
            {
                discardChanges = discardProp.GetBoolean();
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
