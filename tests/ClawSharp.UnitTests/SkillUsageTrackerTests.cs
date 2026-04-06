using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class SkillUsageTrackerTests
{
    [Fact]
    public void RecordSkillUsage_Debounces_Repeated_Writes_And_Computes_Recency_Score()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-skill-usage-tests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(tempRoot, ".claude.json");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var tracker = new SkillUsageTracker(configPath);
            tracker.RecordSkillUsage("review", 1_000_000);
            tracker.RecordSkillUsage("review", 1_030_000);
            tracker.RecordSkillUsage("review", 1_070_000);

            var score = tracker.GetSkillUsageScore("review", 1_070_000);

            Assert.True(score >= 2d);
            var persisted = File.ReadAllText(configPath);
            Assert.Contains("\"usageCount\": 2", persisted, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
