using System.Text.Json.Nodes;
using ClawSharp.Tools.Registry;

namespace ClawSharp.Tools.Worktree;

internal static class ExitWorktreeToolSchemas
{
    public static readonly JsonObject InputSchema = ToolJsonSchemaFactory.StrictObject(
        new[]
        {
            ("action", ToolJsonSchemaFactory.Enum(new[] { "keep", "remove" }, "\"keep\" leaves the worktree and branch on disk; \"remove\" deletes both.")),
            ("discard_changes", ToolJsonSchemaFactory.Boolean("Required true when action is \"remove\" and the worktree has uncommitted files or unmerged commits. The tool will refuse and list them otherwise."))
        },
        required: new[] { "action" });

    public static readonly JsonObject OutputSchema = ToolJsonSchemaFactory.StrictObject(
        new[]
        {
            ("action", ToolJsonSchemaFactory.String()),
            ("originalCwd", ToolJsonSchemaFactory.String()),
            ("worktreePath", ToolJsonSchemaFactory.String()),
            ("worktreeBranch", ToolJsonSchemaFactory.String()),
            ("tmuxSessionName", ToolJsonSchemaFactory.String()),
            ("discardedFiles", ToolJsonSchemaFactory.Number()),
            ("discardedCommits", ToolJsonSchemaFactory.Number()),
            ("message", ToolJsonSchemaFactory.String())
        });
}
