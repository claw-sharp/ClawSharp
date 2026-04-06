// TS origin: ./utils/Shell.ts, ./utils/ShellCommand.ts
using System.Diagnostics;
using System.Text;

namespace ClawSharp.Tasks;

public sealed class LocalShellProcessRunner
{
    private const int SpawnFailureExitCode = 126;
    private const int PumpBufferSize = 4096;

    public Task<LocalShellCommand> StartAsync(
        LocalShellProcessStartInfo startInfo,
        CancellationToken cancellationToken = default)
    {
        var command = new LocalShellCommand(startInfo.TaskOutput, startInfo.TimeoutMs);
        _ = RunAsync(command, startInfo, cancellationToken);
        return Task.FromResult(command);
    }

    private static async Task RunAsync(
        LocalShellCommand command,
        LocalShellProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = CreateProcess(startInfo);
        using var timeoutCancellationSource = new CancellationTokenSource();
        using var linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellationSource.Token);

        if (startInfo.TaskOutput.StdoutToFile)
        {
            TaskOutput.StartPolling(startInfo.TaskOutput.TaskId);
        }

        try
        {
            if (!process.Start())
            {
                command.Complete(SpawnFailureExitCode, stderrOverride: "Failed to start shell process.");
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            command.Complete(SpawnFailureExitCode, stderrOverride: ex.Message);
            return;
        }

        command.BindKillAction(() => TryKillProcess(process));

        var stdoutTask = PumpStreamAsync(process.StandardOutput, startInfo.TaskOutput, isStderr: false, linkedCancellationSource.Token);
        var stderrTask = PumpStreamAsync(process.StandardError, startInfo.TaskOutput, isStderr: true, linkedCancellationSource.Token);
        var waitForExitTask = process.WaitForExitAsync(CancellationToken.None);
        var timeoutTask = Task.Delay(startInfo.TimeoutMs, timeoutCancellationSource.Token);

        try
        {
            var completedTask = await Task.WhenAny(waitForExitTask, timeoutTask, cancellationToken.AsTask());
            if (completedTask == timeoutTask)
            {
                if (!command.HandleTimeout())
                {
                    command.MarkTimedOut();
                    TryKillProcess(process);
                }

                await waitForExitTask;
            }
            else if (completedTask != waitForExitTask)
            {
                command.Kill();
                await waitForExitTask;
            }
        }
        catch (OperationCanceledException)
        {
            command.Kill();
            await waitForExitTask;
        }
        finally
        {
            timeoutCancellationSource.Cancel();
            try
            {
                await Task.WhenAll(stdoutTask, stderrTask);
            }
            catch (OperationCanceledException)
            {
            }

            if (startInfo.TaskOutput.StdoutToFile)
            {
                TaskOutput.StopPolling(startInfo.TaskOutput.TaskId);
            }
        }

        command.Complete(process.ExitCode);
    }

    private static Process CreateProcess(LocalShellProcessStartInfo startInfo)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = startInfo.FileName,
            WorkingDirectory = startInfo.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in startInfo.Arguments)
        {
            processStartInfo.ArgumentList.Add(argument);
        }

        if (startInfo.EnvironmentVariables is not null)
        {
            foreach (var pair in startInfo.EnvironmentVariables)
            {
                processStartInfo.Environment[pair.Key] = pair.Value;
            }
        }

        return new Process
        {
            StartInfo = processStartInfo,
            EnableRaisingEvents = true
        };
    }

    private static async Task PumpStreamAsync(
        StreamReader reader,
        TaskOutput taskOutput,
        bool isStderr,
        CancellationToken cancellationToken)
    {
        var buffer = new char[PumpBufferSize];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            var data = new string(buffer, 0, read);
            if (taskOutput.StdoutToFile)
            {
                taskOutput.WriteMergedOutput(data);
            }
            else if (isStderr)
            {
                taskOutput.WriteStderr(data);
            }
            else
            {
                taskOutput.WriteStdout(data);
            }
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}

internal static class CancellationTokenTaskExtensions
{
    public static Task AsTask(this CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            completionSource);
        return completionSource.Task;
    }
}
