using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

internal static class AgentToolSchemas
{
    public static readonly JsonObject InputSchema = ToolJsonSchemaFactory.StrictObject(
        [
            ("description", ToolJsonSchemaFactory.String("A short (3-5 word) description of the task")),
            ("prompt", ToolJsonSchemaFactory.String("The task for the agent to perform")),
            ("subagent_type", ToolJsonSchemaFactory.String("The type of specialized agent to use for this task")),
            ("model", ToolJsonSchemaFactory.StringEnum(["sonnet", "opus", "haiku"], "Optional model override for this agent. Takes precedence over the agent definition's model frontmatter. If omitted, uses the agent definition's model, or inherits from the parent.")),
            ("run_in_background", ToolJsonSchemaFactory.Boolean("Set to true to run this agent in the background. You will be notified when it completes.")),
            ("name", ToolJsonSchemaFactory.String("Name for the spawned agent. Makes it addressable via SendMessage({to: name}) while running.")),
            ("team_name", ToolJsonSchemaFactory.String("Team name for spawning. Uses current team context if omitted.")),
            ("mode", ToolJsonSchemaFactory.StringEnum(["default", "acceptEdits", "bypassPermissions", "dontAsk", "plan", "auto", "bubble"], "Permission mode for spawned teammate (e.g., \"plan\" to require plan approval).")),
            ("isolation", ToolJsonSchemaFactory.StringEnum(["worktree"], "Isolation mode. \"worktree\" creates a temporary git worktree so the agent works on an isolated copy of the repo.")),
            ("cwd", ToolJsonSchemaFactory.String("Absolute path to run the agent in. Overrides the working directory for all filesystem and shell operations within this agent. Mutually exclusive with isolation: \"worktree\"."))
        ],
        required:
        [
            "description",
            "prompt"
        ]);

    public static readonly JsonObject OutputSchema = ToolJsonSchemaFactory.OneOf(
        ToolJsonSchemaFactory.StrictObject(
            [
                ("status", ToolJsonSchemaFactory.StringEnum(["completed"])),
                ("prompt", ToolJsonSchemaFactory.String()),
                ("result", ToolJsonSchemaFactory.String("The agent's final result")),
                ("usage", ToolJsonSchemaFactory.Nullable(
                    ToolJsonSchemaFactory.StrictObject(
                        [
                            ("input_tokens", ToolJsonSchemaFactory.Integer()),
                            ("output_tokens", ToolJsonSchemaFactory.Integer())
                        ])))
            ],
            required:
            [
                "status",
                "prompt",
                "result"
            ]),
        ToolJsonSchemaFactory.StrictObject(
            [
                ("status", ToolJsonSchemaFactory.StringEnum(["async_launched"])),
                ("agentId", ToolJsonSchemaFactory.String("The ID of the async agent")),
                ("description", ToolJsonSchemaFactory.String("The description of the task")),
                ("prompt", ToolJsonSchemaFactory.String("The prompt for the agent")),
                ("outputFile", ToolJsonSchemaFactory.String("Path to the output file for checking agent progress")),
                ("canReadOutputFile", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.Boolean("Whether the calling agent has Read/Bash tools to check progress")))
            ],
            required:
            [
                "status",
                "agentId",
                "description",
                "prompt",
                "outputFile"
            ]));
}
