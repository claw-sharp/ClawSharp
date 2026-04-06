// TS origin: ./bridge/sessionRunner.ts
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class SessionRunnerUtilitiesTests
{
    [Fact]
    public void SafeFilenameId_Replaces_Unsafe_Characters_With_Underscores()
    {
        Assert.Equal("session____evil__name", SessionRunnerUtilities.SafeFilenameId("session/../evil::name"));
    }

    [Fact]
    public void ToolSummary_Uses_Ts_Verb_And_Target_Priority()
    {
        var summary = SessionRunnerUtilities.ToolSummary(
            "Bash",
            new Dictionary<string, object?> { ["command"] = new string('x', 80) });

        Assert.Equal($"Running {new string('x', 60)}", summary);
    }

    [Fact]
    public void ExtractActivities_Parses_Tool_Use_Text_And_Result_Lines()
    {
        List<string> debug = [];
        var assistantLine = """
            {"type":"assistant","message":{"content":[{"type":"tool_use","name":"Read","input":{"file_path":"app.ts"}},{"type":"text","text":"Hello world from the bridge runtime"}]}}
            """;
        var resultLine = """
            {"type":"result","subtype":"error","errors":["boom"]}
            """;

        var assistantActivities = SessionRunnerUtilities.ExtractActivities(assistantLine, "session-1", debug.Add);
        var resultActivities = SessionRunnerUtilities.ExtractActivities(resultLine, "session-1", debug.Add);

        Assert.Equal(2, assistantActivities.Count);
        Assert.Equal(SessionActivityType.ToolStart, assistantActivities[0].Type);
        Assert.Equal("Reading app.ts", assistantActivities[0].Summary);
        Assert.Equal(SessionActivityType.Text, assistantActivities[1].Type);
        Assert.Equal("Hello world from the bridge runtime", assistantActivities[1].Summary);
        Assert.Single(resultActivities);
        Assert.Equal(SessionActivityType.Error, resultActivities[0].Type);
        Assert.Equal("boom", resultActivities[0].Summary);
        Assert.Contains(debug, line => line.Contains("tool_use name=Read", StringComparison.Ordinal));
        Assert.Contains(debug, line => line.Contains("result subtype=error", StringComparison.Ordinal));
    }

    [Fact]
    public void ExtractActivities_Ignores_Invalid_Lines_And_Handles_Success_Result()
    {
        var invalid = SessionRunnerUtilities.ExtractActivities("not json", "session-1");
        var success = SessionRunnerUtilities.ExtractActivities("""{"type":"result","subtype":"success"}""", "session-1");

        Assert.Empty(invalid);
        Assert.Single(success);
        Assert.Equal(SessionActivityType.Result, success[0].Type);
        Assert.Equal("Session completed", success[0].Summary);
    }

    [Fact]
    public void ExtractUserMessageText_Skips_Synthetic_And_Finds_First_Text_Block()
    {
        var synthetic = SessionRunnerUtilities.ExtractUserMessageText(new Dictionary<string, object?>
        {
            ["isSynthetic"] = true,
            ["message"] = new Dictionary<string, object?> { ["content"] = "ignored" }
        });

        var real = SessionRunnerUtilities.ExtractUserMessageText(new Dictionary<string, object?>
        {
            ["message"] = new Dictionary<string, object?>
            {
                ["content"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["type"] = "image", ["text"] = "ignored" },
                    new Dictionary<string, object?> { ["type"] = "text", ["text"] = "  hello bridge  " }
                }
            }
        });

        Assert.Null(synthetic);
        Assert.Equal("hello bridge", real);
    }

    [Fact]
    public void InputPreview_Uses_First_Three_String_Fields_And_Truncates_To_100()
    {
        var preview = SessionRunnerUtilities.InputPreview(new Dictionary<string, object?>
        {
            ["a"] = new string('a', 120),
            ["b"] = "beta",
            ["c"] = "gamma",
            ["d"] = "delta"
        });

        Assert.Equal($"a=\"{new string('a', 100)}\" b=\"beta\" c=\"gamma\"", preview);
    }
}
