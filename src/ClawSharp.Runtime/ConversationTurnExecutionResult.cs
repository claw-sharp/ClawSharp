using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.Runtime;

public sealed record ConversationTurnExecutionResult(
    QueryResult QueryResult,
    ChatMessage? FinalAssistantMessage);
