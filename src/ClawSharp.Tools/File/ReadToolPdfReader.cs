namespace ClawSharp.Tools;

internal static class ReadToolPdfReader
{
    public static PdfReadResult Read(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var originalSize = fileInfo.Length;
        if (originalSize == 0)
        {
            throw new InvalidOperationException($"PDF file is empty: {filePath}");
        }

        if (originalSize > ReadToolPolicies.PdfTargetRawSizeBytes)
        {
            throw new InvalidOperationException(
                $"PDF file exceeds maximum allowed size of {FormatFileSize(ReadToolPolicies.PdfTargetRawSizeBytes)}.");
        }

        var fileBytes = File.ReadAllBytes(filePath);
        if (fileBytes.Length < 5 ||
            fileBytes[0] != (byte)'%' ||
            fileBytes[1] != (byte)'P' ||
            fileBytes[2] != (byte)'D' ||
            fileBytes[3] != (byte)'F' ||
            fileBytes[4] != (byte)'-')
        {
            throw new InvalidOperationException($"File is not a valid PDF (missing %PDF- header): {filePath}");
        }

        return new PdfReadResult(Convert.ToBase64String(fileBytes), originalSize);
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
}

internal sealed record PdfReadResult(string Base64, long OriginalSize);
