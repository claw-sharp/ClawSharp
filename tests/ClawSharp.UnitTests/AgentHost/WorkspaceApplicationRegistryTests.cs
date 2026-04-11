using System.Threading;
using ClawSharp.AgentHost.Services;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class WorkspaceApplicationRegistryTests
{
    [Fact]
    public async Task GetOrCreateAsync_DoesNotRemoveInflightEntry_WhenWaiterCancels()
    {
        var workspaceRoot = CreateWorkspace();
        var createCount = 0;
        var creation = new TaskCompletionSource<ClawSharpApplication>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new WorkspaceApplicationRegistry(
            applicationFactory: (_, _, _) =>
            {
                Interlocked.Increment(ref createCount);
                return creation.Task;
            });

        using var cancellation = new CancellationTokenSource();
        var canceledWait = registry.GetOrCreateAsync(workspaceRoot, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceledWait);

        var app = (ClawSharpApplication)null!;
        creation.SetResult(app);

        var resolved = await registry.GetOrCreateAsync(workspaceRoot);

        Assert.Null(resolved);
        Assert.Equal(1, Volatile.Read(ref createCount));
    }

    [Fact]
    public async Task GetOrCreateAsync_ReusesSameEntry_ForDifferentPathCasing_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var workspaceRoot = CreateWorkspace();
        var alternateCasing = TogglePathCasing(workspaceRoot);
        var createCount = 0;
        var app = (ClawSharpApplication)null!;
        var registry = new WorkspaceApplicationRegistry(
            applicationFactory: (_, _, _) =>
            {
                Interlocked.Increment(ref createCount);
                return Task.FromResult(app);
            });

        var first = await registry.GetOrCreateAsync(workspaceRoot);
        var second = await registry.GetOrCreateAsync(alternateCasing);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, Volatile.Read(ref createCount));
    }

    private static string CreateWorkspace()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace-registry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        return workspaceRoot;
    }

    private static string TogglePathCasing(string path)
    {
        var buffer = path.ToCharArray();
        for (var index = 0; index < buffer.Length; index++)
        {
            var character = buffer[index];
            if (!char.IsLetter(character))
            {
                continue;
            }

            buffer[index] = char.IsUpper(character)
                ? char.ToLowerInvariant(character)
                : char.ToUpperInvariant(character);
        }

        return new string(buffer);
    }
}
