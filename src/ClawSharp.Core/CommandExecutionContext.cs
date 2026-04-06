namespace ClawSharp.Core;

public sealed class CommandExecutionContext
{
    public required IClawSharpAppStateStore AppStateStore { get; init; }
    public required ConversationSession Session { get; init; }
    public required ISessionFactory SessionFactory { get; init; }
    public required ITranscriptStore TranscriptStore { get; init; }
    public required ClawSharpSettings Settings { get; init; }
    public FileStateCache? ReadFileState { get; init; }
    public IInteractionService? InteractionService { get; init; }
}
