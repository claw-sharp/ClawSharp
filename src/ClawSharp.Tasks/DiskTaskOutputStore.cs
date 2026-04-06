using System.Collections.Concurrent;
using System.Text;

namespace ClawSharp.Tasks;

public sealed class DiskTaskOutputStore
{
    private const int DefaultBufferSize = 4096;
    private static readonly byte[] TruncationNoticeBytes =
        Encoding.UTF8.GetBytes($"\n[output truncated: exceeded {MaxTaskOutputBytesDisplay} disk cap]\n");

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _cappedPaths = new(StringComparer.Ordinal);

    public const int DefaultMaxReadBytes = 8 * 1024 * 1024;
    public const long MaxTaskOutputBytes = 5L * 1024 * 1024 * 1024;
    public const string MaxTaskOutputBytesDisplay = "5GB";

    public async Task InitTaskOutputAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await using var stream = new FileStream(
            outputPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.ReadWrite,
            DefaultBufferSize,
            useAsync: true);
        await stream.FlushAsync(cancellationToken);
    }

    public async Task InitTaskOutputAsSymlinkAsync(
        string outputPath,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        try
        {
            File.CreateSymbolicLink(outputPath, targetPath);
            return;
        }
        catch
        {
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                File.CreateSymbolicLink(outputPath, targetPath);
                return;
            }
            catch
            {
                await InitTaskOutputAsync(outputPath, cancellationToken);
            }
        }
    }

    public async Task AppendAsync(string outputPath, string content, CancellationToken cancellationToken = default)
    {
        var fileLock = GetLock(outputPath);
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            if (_cappedPaths.ContainsKey(outputPath))
            {
                return;
            }

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var contentBytes = Encoding.UTF8.GetBytes(content);
            var existingLength = File.Exists(outputPath)
                ? new FileInfo(outputPath).Length
                : 0;

            await using var stream = new FileStream(
                outputPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                DefaultBufferSize,
                useAsync: true);

            if (existingLength + contentBytes.Length > MaxTaskOutputBytes)
            {
                _cappedPaths[outputPath] = true;
                await stream.WriteAsync(TruncationNoticeBytes, cancellationToken);
            }
            else
            {
                await stream.WriteAsync(contentBytes, cancellationToken);
            }

            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task<string> GetOutputAsync(
        string outputPath,
        int maxBytes = DefaultMaxReadBytes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(
                outputPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                DefaultBufferSize,
                useAsync: true);
            var length = stream.Length;
            var bytesToRead = (int)Math.Min(length, maxBytes);
            if (bytesToRead == 0)
            {
                return string.Empty;
            }

            var startOffset = Math.Max(0, length - bytesToRead);
            stream.Seek(startOffset, SeekOrigin.Begin);
            var buffer = new byte[bytesToRead];
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            var content = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            if (length > bytesRead)
            {
                var omittedKilobytes = Math.Round((length - bytesRead) / 1024d);
                return $"[{omittedKilobytes:0}KB of earlier output omitted]\n{content}";
            }

            return content;
        }
        catch (FileNotFoundException)
        {
            return string.Empty;
        }
        catch (DirectoryNotFoundException)
        {
            return string.Empty;
        }
    }

    public async Task<(string Content, long NewOffset)> GetOutputDeltaAsync(
        string outputPath,
        long fromOffset,
        int maxBytes = DefaultMaxReadBytes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(
                outputPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                DefaultBufferSize,
                useAsync: true);
            if (stream.Length <= fromOffset)
            {
                return (string.Empty, fromOffset);
            }

            stream.Seek(fromOffset, SeekOrigin.Begin);
            var bytesToRead = (int)Math.Min(maxBytes, stream.Length - fromOffset);
            var buffer = new byte[bytesToRead];
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            return (Encoding.UTF8.GetString(buffer, 0, bytesRead), fromOffset + bytesRead);
        }
        catch (FileNotFoundException)
        {
            return (string.Empty, fromOffset);
        }
        catch (DirectoryNotFoundException)
        {
            return (string.Empty, fromOffset);
        }
    }

    public Task<long> GetOutputSizeAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0L);
        }
        catch
        {
            return Task.FromResult(0L);
        }
    }

    public void EvictOutputState(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        _cappedPaths.TryRemove(outputPath, out _);
        _locks.TryRemove(outputPath, out _);
    }

    private SemaphoreSlim GetLock(string outputPath)
    {
        return _locks.GetOrAdd(outputPath, _ => new SemaphoreSlim(1, 1));
    }
}
