using System.Text.Json.Nodes;
using ClawSharp.Tools.Registry;

namespace ClawSharp.Tools.Worktree;

internal static class EnterWorktreeToolSchemas
{
    public static readonly JsonObject InputSchema = ToolJsonSchemaFactory.StrictObject(
        new[]
        {
            ("name", ToolJsonSchemaFactory.String("Optional name for the worktree. Each \"/\"-separated segment may contain only letters, digits, dots, underscores, and dashes; max 64 chars total. A random name is generated if not provided."))
        });

    public static readonly JsonObject OutputSchema = ToolJsonSchemaFactory.StrictObject(
        new[]
        {
            ("worktreePath", ToolJsonSchemaFactory.String("The absolute path to the newly created worktree directory.")),
            ("worktreeBranch", ToolJsonSchemaFactory.String("The name of the git branch created for this worktree.")),
            ("message", ToolJsonSchemaFactory.String("Status message describing the result of the worktree creation."))
        });
}
