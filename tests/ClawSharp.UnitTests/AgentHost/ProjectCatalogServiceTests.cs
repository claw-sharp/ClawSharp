using ClawSharp.AgentHost.Projects;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class ProjectCatalogServiceTests
{
    [Fact]
    public async Task RecordOpenAsync_Stores_And_Sorts_Recent_Projects()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-agenthost-project-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var store = new RecentProjectStore(Path.Combine(tempRoot, "recent-projects.json"));

        try
        {
            await store.RecordOpenAsync(
                new ClawSharp.AgentHost.Contracts.ProjectSummaryDto(
                    "p1",
                    "repo-one",
                    Path.Combine(tempRoot, "repo-one"),
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    null,
                    0,
                    null));
            await store.RecordOpenAsync(
                new ClawSharp.AgentHost.Contracts.ProjectSummaryDto(
                    "p2",
                    "repo-two",
                    Path.Combine(tempRoot, "repo-two"),
                    DateTimeOffset.UtcNow,
                    null,
                    0,
                    null));

            Directory.CreateDirectory(Path.Combine(tempRoot, "repo-one"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "repo-two"));

            var projects = await store.ListAsync();

            Assert.Equal(2, projects.Count);
            Assert.Equal("p2", projects[0].ProjectId);
            Assert.Equal("p1", projects[1].ProjectId);
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
