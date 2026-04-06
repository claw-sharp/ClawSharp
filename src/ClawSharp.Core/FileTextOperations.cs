// TS origin: ./utils/fileRead.ts, ./utils/file.ts
using System.Text;

namespace ClawSharp.Core;

public static class FileTextOperations
{
    public static FileTextMetadata ReadFileWithMetadata(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var encoding = DetectEncoding(bytes);

        var raw = encoding.GetString(bytes);
        if (raw.Length > 0 && raw[0] == '\uFEFF')
        {
            raw = raw[1..];
        }

        var lineEndings = DetectLineEndings(raw.AsSpan(0, Math.Min(raw.Length, 4096)));
        return new FileTextMetadata(
            raw.Replace("\r\n", "\n", StringComparison.Ordinal),
            encoding,
            lineEndings);
    }

    public static void WriteTextContent(
        string filePath,
        string content,
        Encoding encoding,
        FileLineEnding lineEndings)
    {
        var toWrite = content;
        if (lineEndings == FileLineEnding.CRLF)
        {
            toWrite = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        }

        File.WriteAllText(filePath, toWrite, encoding);
    }

    public static Encoding DetectEncoding(string filePath)
    {
        return DetectEncoding(File.ReadAllBytes(filePath));
    }

    public static FileLineEnding DetectLineEndings(string content)
    {
        return DetectLineEndings(content.AsSpan(0, Math.Min(content.Length, 4096)));
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return new UTF8Encoding(false);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(false);
        }

        return new UTF8Encoding(false);
    }

    private static FileLineEnding DetectLineEndings(ReadOnlySpan<char> content)
    {
        var crlfCount = 0;
        var lfCount = 0;

        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] != '\n')
            {
                continue;
            }

            if (index > 0 && content[index - 1] == '\r')
            {
                crlfCount++;
            }
            else
            {
                lfCount++;
            }
        }

        return crlfCount > lfCount ? FileLineEnding.CRLF : FileLineEnding.LF;
    }
}
