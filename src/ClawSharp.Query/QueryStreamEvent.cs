// TS parity status: ports the TypeScript stream_event contract as a focused C# query-runtime payload; live model streaming production of raw events remains unported.
using System.Text.Json.Nodes;

namespace ClawSharp.Query;

public sealed record QueryStreamEvent(JsonNode Event);
