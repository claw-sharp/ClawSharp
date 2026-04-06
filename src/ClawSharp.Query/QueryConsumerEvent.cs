// TS origin: ./QueryEngine.ts
// TS parity status: this ports the current SDK-facing query emission boundary for the C# tool-only runtime; full 1:1 parity still depends on the model-backed query loop and the broader SDK message union.
using ClawSharp.Core;

namespace ClawSharp.Query;

public abstract record QueryConsumerEvent;

public sealed record QueryRequestStartConsumerEvent : QueryConsumerEvent;

public sealed record QueryMessageConsumerEvent(ChatMessage Message) : QueryConsumerEvent;

public sealed record QueryTombstoneConsumerEvent(ChatMessage Message) : QueryConsumerEvent;

public sealed record QueryToolUseSummaryConsumerEvent(QueryToolUseSummaryMessage Message) : QueryConsumerEvent;

public sealed record QueryStreamEventConsumerEvent(QueryStreamEvent Event) : QueryConsumerEvent;

public sealed record QueryStreamDeltaConsumerEvent(string Delta) : QueryConsumerEvent;

public sealed record QueryLoopTransitionConsumerEvent(QueryLoopTransition Transition) : QueryConsumerEvent;

public sealed record QueryLoopTerminalConsumerEvent(QueryLoopTerminal Terminal) : QueryConsumerEvent;

public sealed record QueryResultConsumerEvent(QueryResult Result) : QueryConsumerEvent;
