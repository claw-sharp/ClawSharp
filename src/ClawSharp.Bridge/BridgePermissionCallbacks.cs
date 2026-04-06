using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Bridge;

public sealed record BridgePermissionResponse(
    string Behavior,
    IReadOnlyDictionary<string, object?>? UpdatedInput = null,
    IReadOnlyList<PermissionUpdate>? UpdatedPermissions = null,
    string? Message = null);

public interface IBridgePermissionCallbacks
{
    void SendRequest(
        string requestId,
        string toolName,
        IReadOnlyDictionary<string, object?> input,
        string toolUseId,
        string description,
        IReadOnlyList<PermissionUpdate>? permissionSuggestions = null,
        string? blockedPath = null);

    void SendResponse(string requestId, BridgePermissionResponse response);

    void CancelRequest(string requestId);

    Action OnResponse(string requestId, Action<BridgePermissionResponse> handler);
}

public static class BridgePermissionCallbackUtilities
{
    public static bool IsBridgePermissionResponse(JsonNode? value)
    {
        if (value is not JsonObject obj)
        {
            return false;
        }

        var behavior = obj["behavior"]?.GetValue<string>();
        return behavior is "allow" or "deny";
    }
}
