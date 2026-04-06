// TS origin: ./services/mcp/vscodeSdkMcp.ts, ./services/diagnosticTracking.ts, ./services/lsp/LSPDiagnosticRegistry.ts
namespace ClawSharp.Core;

public interface IFileUpdateNotifier
{
    Task HandleQueryStartAsync(CancellationToken cancellationToken = default);

    Task BeforeFileEditedAsync(string filePath, CancellationToken cancellationToken = default);

    Task ClearDiagnosticsForFileAsync(string filePath, CancellationToken cancellationToken = default);

    Task NotifyFileUpdatedAsync(
        string filePath,
        string? oldContent,
        string? newContent,
        CancellationToken cancellationToken = default);
}
