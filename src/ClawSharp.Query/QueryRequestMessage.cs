// TS parity status: ports the current MessageParam request envelope used for user and assistant turns; system-side progress events remain excluded like the TS API normalization path.
namespace ClawSharp.Query;

public sealed record QueryRequestMessage(
    string Role,
    IReadOnlyList<QueryRequestContentBlock> Content);
