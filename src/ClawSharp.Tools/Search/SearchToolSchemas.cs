using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

internal static class SearchToolSchemas
{
    public static JsonObject GlobInputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("pattern", ToolJsonSchemaFactory.String("The glob pattern to match files against")),
                ("path", ToolJsonSchemaFactory.String(
                    "The directory to search in. If omitted, the current working directory is used. Must be a valid directory path if provided."))
            ],
            required: ["pattern"]);

    public static JsonObject GlobOutputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("durationMs", ToolJsonSchemaFactory.Number("Time taken to execute the search in milliseconds")),
                ("numFiles", ToolJsonSchemaFactory.Number("Total number of files found")),
                ("filenames", ToolJsonSchemaFactory.Array(
                    ToolJsonSchemaFactory.String(),
                    "Array of file paths that match the pattern")),
                ("truncated", ToolJsonSchemaFactory.Boolean("Whether results were truncated"))
            ],
            required: ["durationMs", "numFiles", "filenames", "truncated"]);

    public static JsonObject GrepInputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("pattern", ToolJsonSchemaFactory.String("The regular expression pattern to search for in file contents")),
                ("path", ToolJsonSchemaFactory.String("File or directory to search in. Defaults to current working directory.")),
                ("glob", ToolJsonSchemaFactory.String("Glob pattern to filter files (for example \"*.js\" or \"*.{ts,tsx}\")")),
                ("output_mode", ToolJsonSchemaFactory.StringEnum(
                    ["content", "files_with_matches", "count"],
                    "Output mode. Defaults to files_with_matches.")),
                ("-B", ToolJsonSchemaFactory.Integer("Number of lines to show before each match", minimum: 0)),
                ("-A", ToolJsonSchemaFactory.Integer("Number of lines to show after each match", minimum: 0)),
                ("-C", ToolJsonSchemaFactory.Integer("Alias for context", minimum: 0)),
                ("context", ToolJsonSchemaFactory.Integer("Number of lines to show before and after each match", minimum: 0)),
                ("-n", ToolJsonSchemaFactory.Boolean("Show line numbers in output", defaultValue: true)),
                ("-i", ToolJsonSchemaFactory.Boolean("Case insensitive search")),
                ("type", ToolJsonSchemaFactory.String("File type to search")),
                ("head_limit", ToolJsonSchemaFactory.Integer("Limit output to the first N lines or entries", minimum: 0)),
                ("offset", ToolJsonSchemaFactory.Integer("Skip the first N lines or entries before applying head_limit", minimum: 0)),
                ("multiline", ToolJsonSchemaFactory.Boolean("Enable multiline mode"))
            ],
            required: ["pattern"]);

    public static JsonObject GrepOutputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("mode", ToolJsonSchemaFactory.StringEnum(["content", "files_with_matches", "count"])),
                ("numFiles", ToolJsonSchemaFactory.Number()),
                ("filenames", ToolJsonSchemaFactory.Array(ToolJsonSchemaFactory.String())),
                ("content", ToolJsonSchemaFactory.String()),
                ("numLines", ToolJsonSchemaFactory.Number()),
                ("numMatches", ToolJsonSchemaFactory.Number()),
                ("appliedLimit", ToolJsonSchemaFactory.Number()),
                ("appliedOffset", ToolJsonSchemaFactory.Number())
            ],
            required: ["numFiles", "filenames"]);
}
