using ClawSharp.Core;

namespace ClawSharp.Query;

public abstract record QueryRuntimeEvent;

public sealed record QueryRequestStartRuntimeEvent : QueryRuntimeEvent;

public sealed record QueryMessageRuntimeEvent(ChatMessage Message) : QueryRuntimeEvent;

public sealed record QueryTombstoneRuntimeEvent(ChatMessage Message) : QueryRuntimeEvent;

public sealed record QueryToolUseSummaryRuntimeEvent(QueryToolUseSummaryMessage Message) : QueryRuntimeEvent;

public sealed record QueryStreamEventRuntimeEvent(QueryStreamEvent Event) : QueryRuntimeEvent;

public sealed record QueryStreamDeltaRuntimeEvent(string Delta) : QueryRuntimeEvent;

public sealed record QueryLoopTransitionRuntimeEvent(
    QueryLoopTransition Transition,
    QueryLoopState State) : QueryRuntimeEvent;

public sealed record QueryLoopTerminalRuntimeEvent(
    QueryLoopTerminal Terminal,
    QueryLoopState State) : QueryRuntimeEvent;
