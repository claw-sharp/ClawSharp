using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core.Worktree;

namespace ClawSharp.Tools.Worktree;

internal sealed class EnterWorktreeTool : BaseTool
{
    public const string ToolName = "EnterWorktree";

    public EnterWorktreeTool()
        : base(new ToolDescriptor(
            ToolName,
            "Creates an isolated worktree (via git or configured hooks) and switches the session into it",
            SearchHint: "create an isolated git worktree and switch into it",
            ShouldDefer: true,
            InputSchema: EnterWorktreeToolSchemas.InputSchema,
            OutputSchema: EnterWorktreeToolSchemas.OutputSchema,
            Strict: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments) => false;
    public override bool IsReadOnly(string arguments) => false;

    public override Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (context.WorktreeService.GetCurrentWorktreeSession() != null)
        {
            return Task.FromResult(ToolValidationResult.Invalid("Already in a worktree session."));
        }

        if (!TryParseArguments(context.Arguments, out var name))
        {
            return Task.FromResult(ToolValidationResult.Invalid("Invalid input schema."));
        }

        if (name != null)
        {
            try
            {
                ValidateWorktreeSlug(name);
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolValidationResult.Invalid(ex.Message));
            }
        }

        return Task.FromResult(ToolValidationResult.Valid());
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var name))
        {
            return Failure("Invalid input.");
        }

        var slug = name ?? "auto-worktree"; // Real implementation would use getPlanSlug()

        try
        {
            var session = await context.WorktreeService.CreateWorktreeForSessionAsync(context.Session.Id, slug, cancellationToken);
            
            var branchInfo = session.WorktreeBranch != null 
                ? $" on branch {session.WorktreeBranch}" 
                : "";

            var message = $"Created worktree at {session.WorktreePath}{branchInfo}. The session is now working in the worktree. Use ExitWorktree to leave mid-session, or exit the session to be prompted.";

            var structured = new JsonObject
            {
                ["worktreePath"] = session.WorktreePath,
                ["worktreeBranch"] = session.WorktreeBranch,
                ["message"] = message
            };

            return Success(message, structured);
        }
        catch (NotSupportedException ex)
        {
            return Failure(ex.Message);
        }
        catch (Exception ex)
        {
            return Failure($"Failed to create worktree: {ex.Message}");
        }
    }

    private static bool TryParseArguments(string arguments, out string? name)
    {
        name = null;
        try
        {
            using var doc = JsonDocument.Parse(arguments);
            if (doc.RootElement.TryGetProperty("name", out var nameProp))
            {
                name = nameProp.GetString();
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ValidateWorktreeSlug(string slug)
    {
        const int MaxLength = 64;
        if (slug.Length > MaxLength)
        {
            throw new Exception($"Invalid worktree name: must be {MaxLength} characters or fewer.");
        }

        foreach (var segment in slug.Split('/'))
        {
            if (segment == "." || segment == "..")
            {
                throw new Exception($"Invalid worktree name \"{slug}\": must not contain \".\" or \"..\" segments.");
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(segment, "^[a-zA-Z0-9._-]+$"))
            {
                throw new Exception($"Invalid worktree name \"{slug}\": each segment must be non-empty and contains only letters, digits, dots, underscores, and dashes.");
            }
        }
    }
}
