// TS parity status: ports the current tool schema projection that ClawSharp can represent from the existing C# tool descriptor surface; deferred-loading and search metadata are carried through without inventing provider-specific runtime fields.
using System.Text.Json.Nodes;

namespace ClawSharp.Query;

public sealed record QueryRequestTool(
    string Name,
    string Description,
    JsonObject? InputSchema = null,
    bool Strict = false,
    bool IsLongRunningCapable = false,
    string? SearchHint = null,
    bool DeferLoading = false,
    bool AlwaysLoad = false);
