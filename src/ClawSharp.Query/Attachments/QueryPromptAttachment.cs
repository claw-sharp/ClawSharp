namespace ClawSharp.Query.Attachments;

public enum QueryPromptAttachmentKind
{
    File,
    Image
}

public sealed record QueryPromptAttachment(
    QueryPromptAttachmentKind Kind,
    string Path,
    string Name,
    string? MediaType = null,
    string? Base64Data = null);
