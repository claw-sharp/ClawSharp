using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class ClawSharpApplicationTests
{
    [Fact]
    public async Task EnsureRuntimeAsync_InitializesRuntimeOnlyOnce()
    {
        var initializationCount = 0;
        var runtime = new ClawSharpApplicationRuntime(null!, null!, null!, null!, null!);
        var app = CreateApplication(
            _ =>
            {
                Interlocked.Increment(ref initializationCount);
                return Task.FromResult(runtime);
            });

        Assert.False(app.IsRuntimeInitialized);

        var first = app.EnsureRuntimeAsync();
        var second = app.EnsureRuntimeAsync();
        var results = await Task.WhenAll(first, second);

        Assert.True(app.IsRuntimeInitialized);
        Assert.Equal(1, Volatile.Read(ref initializationCount));
        Assert.Same(runtime, results[0]);
        Assert.Same(runtime, results[1]);
    }

    [Fact]
    public async Task EnsureRuntimeAsync_AllowsRetryAfterInitializationFailure()
    {
        var initializationCount = 0;
        var runtime = new ClawSharpApplicationRuntime(null!, null!, null!, null!, null!);
        var app = CreateApplication(
            _ =>
            {
                if (Interlocked.Increment(ref initializationCount) == 1)
                {
                    throw new InvalidOperationException("boom");
                }

                return Task.FromResult(runtime);
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.EnsureRuntimeAsync());

        var resolved = await app.EnsureRuntimeAsync();

        Assert.Equal(2, Volatile.Read(ref initializationCount));
        Assert.Same(runtime, resolved);
    }

    private static ClawSharpApplication CreateApplication(
        Func<CancellationToken, Task<ClawSharpApplicationRuntime>> runtimeFactory)
    {
        return new ClawSharpApplication(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            new NullClawSharpAppStateStore(Environment.CurrentDirectory),
            null!,
            null!,
            null!,
            runtimeFactory: runtimeFactory);
    }
}
