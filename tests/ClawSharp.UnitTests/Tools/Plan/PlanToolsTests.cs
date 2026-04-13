using ClawSharp.Core;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class PlanToolsTests
{
    [Fact]
    public async Task EnterPlanMode_Omits_AskUserQuestion_Guidance_When_Tool_Is_Not_Available()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-plan-tools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var appStateStore = new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                tempDir,
                StartupEnvironment.Capture(),
                new ClawSharpSettings(),
                [],
                [],
                [],
                [],
                [],
                []));
        var registry = new ToolRegistry(
            tempDir,
            new TaskRegistry(tempDir),
            appStateStore: appStateStore,
            excludedToolNames: new HashSet<string>(["AskUserQuestion"], StringComparer.OrdinalIgnoreCase));
        var session = new DefaultSessionFactory(tempDir).Create();

        var result = await registry.ExecuteAsync(
            "EnterPlanMode",
            "{}",
            session,
            new ClawSharpSettings());

        Assert.True(result.Success);
        Assert.DoesNotContain("Use AskUserQuestion", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "record the open question in your plan instead of calling AskUserQuestion",
            result.Output,
            StringComparison.Ordinal);
    }
}
