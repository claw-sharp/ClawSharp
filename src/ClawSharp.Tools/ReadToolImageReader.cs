namespace ClawSharp.Tools;

internal static class ReadToolImageReader
{
    public static ImageReadResult Read(string filePath)
    {
        var imageBytes = File.ReadAllBytes(filePath);
        var originalSize = imageBytes.LongLength;
        if (originalSize == 0)
        {
            throw new InvalidOperationException($"Image file is empty: {filePath}");
        }

        if (originalSize > ReadToolPolicies.ImageTargetRawSizeBytes)
        {
            throw new InvalidOperationException(
                $"Image file exceeds maximum currently supported size of {FormatFileSize(ReadToolPolicies.ImageTargetRawSizeBytes)}. " +
                "ClawSharp does not implement the TypeScript resize/compression image-read path yet.");
        }

        var mediaType = DetectImageFormatFromBuffer(imageBytes);
        return new ImageReadResult(Convert.ToBase64String(imageBytes), mediaType, originalSize);
    }

    public static bool IsSupportedImageExtension(string filePath)
    {
        return SupportedImageExtensions.Contains(Path.GetExtension(filePath));
    }

    private static string DetectImageFormatFromBuffer(byte[] buffer)
    {
        if (buffer.Length < 4)
        {
            return "image/png";
        }

        if (buffer[0] == 0x89 &&
            buffer[1] == 0x50 &&
            buffer[2] == 0x4E &&
            buffer[3] == 0x47)
        {
            return "image/png";
        }

        if (buffer[0] == 0xFF &&
            buffer[1] == 0xD8 &&
            buffer[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (buffer[0] == 0x47 &&
            buffer[1] == 0x49 &&
            buffer[2] == 0x46)
        {
            return "image/gif";
        }

        if (buffer.Length >= 12 &&
            buffer[0] == 0x52 &&
            buffer[1] == 0x49 &&
            buffer[2] == 0x46 &&
            buffer[3] == 0x46 &&
            buffer[8] == 0x57 &&
            buffer[9] == 0x45 &&
            buffer[10] == 0x42 &&
            buffer[11] == 0x50)
        {
            return "image/webp";
        }

        return "image/png";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kilobytes = bytes / 1024d;
        if (kilobytes < 1024)
        {
            return $"{kilobytes:0.#} KB";
        }

        return $"{kilobytes / 1024d:0.#} MB";
    }

    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".webp"
    };
}

internal sealed record ImageReadResult(string Base64, string MediaType, long OriginalSize);
