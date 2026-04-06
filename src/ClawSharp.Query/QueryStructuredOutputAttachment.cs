// TS parity status: ports the focused structured_output attachment payload consumed by the TypeScript query engine; live structured-output result wiring remains blocked on the missing model-backed query loop.
using System.Text.Json.Nodes;

namespace ClawSharp.Query;

public sealed record QueryStructuredOutputAttachment(JsonNode Data);
