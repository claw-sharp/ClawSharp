// TS origin: ./components/SessionBackgroundHint.tsx, ./hooks/useDoublePress.ts
namespace ClawSharp.Ui.Terminal;

public interface ISessionBackgroundKeyMonitor
{
    bool CanMonitor(TextReader input);

    Task<bool> WaitForBackgroundRequestAsync(
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken = default);
}

public sealed class ConsoleSessionBackgroundKeyMonitor : ISessionBackgroundKeyMonitor
{
    private static readonly TimeSpan DoublePressTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    public bool CanMonitor(TextReader input)
    {
        return ReferenceEquals(input, Console.In) && !Console.IsInputRedirected;
    }

    public async Task<bool> WaitForBackgroundRequestAsync(
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        if (!CanMonitor(input))
        {
            return false;
        }

        DateTimeOffset? firstPressAt = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            while (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                if ((key.Modifiers & ConsoleModifiers.Control) == 0 || key.Key != ConsoleKey.B)
                {
                    firstPressAt = null;
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                if (firstPressAt is not null && now - firstPressAt <= DoublePressTimeout)
                {
                    return true;
                }

                firstPressAt = now;
                await output.WriteLineAsync("Press Ctrl+B again within 800ms to background the current session.");
            }

            if (firstPressAt is not null && DateTimeOffset.UtcNow - firstPressAt > DoublePressTimeout)
            {
                firstPressAt = null;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        return false;
    }
}
