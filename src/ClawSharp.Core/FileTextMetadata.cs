// TS origin: ./utils/fileRead.ts
using System.Text;

namespace ClawSharp.Core;

public sealed record FileTextMetadata(
    string Content,
    Encoding Encoding,
    FileLineEnding LineEndings);
