using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgePointerStoreTests
{
    [Fact]
    public async Task ReadBridgePointerAsync_Returns_Pointer_With_Age_When_Fresh()
    {
        var root = CreateTempDirectory();
        try
        {
            var projectsDirectory = Path.Combine(root, "projects");
            var dependencies = new BridgePointerStoreDependencies(
                projectsDirectory,
                GetNow: () => DateTimeOffset.UtcNow);

            await BridgePointerStore.WriteBridgePointerAsync(
                dependencies,
                "D:\\repo",
                new BridgePointer("session_123", "env_123", "repl"));

            var pointer = await BridgePointerStore.ReadBridgePointerAsync(dependencies, "D:\\repo");

            Assert.NotNull(pointer);
            Assert.Equal("session_123", pointer!.SessionId);
            Assert.Equal("env_123", pointer.EnvironmentId);
            Assert.Equal("repl", pointer.Source);
            Assert.InRange(pointer.AgeMs, 0, 10_000);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadBridgePointerAsync_Clears_Stale_And_Invalid_Pointers()
    {
        var root = CreateTempDirectory();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var debug = new List<string>();
            var dependencies = new BridgePointerStoreDependencies(
                Path.Combine(root, "projects"),
                debug.Add,
                () => now);
            var staleDir = "D:\\stale";
            var invalidDir = "D:\\invalid";
            var stalePath = BridgePointerStore.GetBridgePointerPath(dependencies, staleDir);
            var invalidPath = BridgePointerStore.GetBridgePointerPath(dependencies, invalidDir);

            Directory.CreateDirectory(Path.GetDirectoryName(stalePath)!);
            await File.WriteAllTextAsync(stalePath, """{"sessionId":"session_1","environmentId":"env_1","source":"standalone"}""");
            File.SetLastWriteTimeUtc(stalePath, now.AddHours(-5).UtcDateTime);

            Directory.CreateDirectory(Path.GetDirectoryName(invalidPath)!);
            await File.WriteAllTextAsync(invalidPath, """{"sessionId":123}""");

            var stale = await BridgePointerStore.ReadBridgePointerAsync(dependencies, staleDir);
            var invalid = await BridgePointerStore.ReadBridgePointerAsync(dependencies, invalidDir);

            Assert.Null(stale);
            Assert.Null(invalid);
            Assert.False(File.Exists(stalePath));
            Assert.False(File.Exists(invalidPath));
            Assert.Contains(debug, line => line.Contains("stale (>4h mtime)", StringComparison.Ordinal));
            Assert.Contains(debug, line => line.Contains("invalid schema", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadBridgePointerAcrossWorktreesAsync_Picks_Freshest_Worktree_Pointer()
    {
        var root = CreateTempDirectory();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var debug = new List<string>();
            var dependencies = new BridgePointerStoreDependencies(
                Path.Combine(root, "projects"),
                debug.Add,
                () => now,
                (dir, _) => Task.FromResult<IReadOnlyList<string>>(["D:\\repo", "D:\\repo-worktree-a", "D:\\repo-worktree-b"]));

            await BridgePointerStore.WriteBridgePointerAsync(
                dependencies,
                "D:\\repo-worktree-a",
                new BridgePointer("session_a", "env_a", "repl"));
            await BridgePointerStore.WriteBridgePointerAsync(
                dependencies,
                "D:\\repo-worktree-b",
                new BridgePointer("session_b", "env_b", "standalone"));

            File.SetLastWriteTimeUtc(
                BridgePointerStore.GetBridgePointerPath(dependencies, "D:\\repo-worktree-a"),
                now.AddMinutes(-8).UtcDateTime);
            File.SetLastWriteTimeUtc(
                BridgePointerStore.GetBridgePointerPath(dependencies, "D:\\repo-worktree-b"),
                now.AddMinutes(-2).UtcDateTime);

            var result = await BridgePointerStore.ReadBridgePointerAcrossWorktreesAsync(dependencies, "D:\\repo");

            Assert.NotNull(result);
            Assert.Equal("D:\\repo-worktree-b", result!.Dir);
            Assert.Equal("session_b", result.Pointer.SessionId);
            Assert.Contains(debug, line => line.Contains("fanout found pointer in worktree D:\\repo-worktree-b", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadBridgePointerAcrossWorktreesAsync_Stops_When_Fanout_Exceeds_Cap()
    {
        var root = CreateTempDirectory();
        try
        {
            var debug = new List<string>();
            var worktrees = Enumerable.Range(0, BridgePointerStore.MaxWorktreeFanout + 1)
                .Select(index => $"D:\\repo-{index}")
                .ToArray();
            var dependencies = new BridgePointerStoreDependencies(
                Path.Combine(root, "projects"),
                debug.Add,
                GetWorktreePathsAsync: (_, _) => Task.FromResult<IReadOnlyList<string>>(worktrees));

            var result = await BridgePointerStore.ReadBridgePointerAcrossWorktreesAsync(dependencies, "D:\\repo");

            Assert.Null(result);
            Assert.Contains(debug, line => line.Contains("exceeds fanout cap", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-bridge-pointer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
