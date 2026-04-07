//
// Ports:
//   - isChannelPermissionRelayEnabled()  (GrowthBook gate — C# uses injected feature flag)
//   - ChannelPermissionResponse / ChannelPermissionCallbacks types
//   - PERMISSION_REPLY_RE constant
//   - shortRequestId()
//   - truncateForPreview()
//   - filterPermissionRelayClients()
//   - createChannelPermissionCallbacks()
//
// Design decisions:
// - GrowthBook runtime not yet ported; isChannelPermissionRelayEnabled uses an
//   injected IChannelPermissionGateProvider seam (approved fallback, default = false).
// - FNV-1a hash and 25-char alphabet are ported verbatim from TS.
// - ID_AVOID_SUBSTRINGS blocklist is ported verbatim.
// - filterPermissionRelayClients generic constraint maps to IMcpConnectionInfo.

using System.Text;
using System.Text.Json;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

/// <summary>
/// Gate provider for the <c>tengu_harbor_permissions</c> GrowthBook feature flag.
/// Injected so the GrowthBook seam is explicit and testable without the live runtime.
/// </summary>
public interface IChannelPermissionGateProvider
{
    bool IsEnabled { get; }
}

/// <summary>
/// Default implementation: always disabled (matches the GrowthBook default of false).
/// </summary>
public sealed class DefaultOffChannelPermissionGateProvider : IChannelPermissionGateProvider
{
    public bool IsEnabled => false;
}

/// <summary>
/// Response delivered from a channel server when the user approves or denies a permission prompt.
/// Mirrors <c>ChannelPermissionResponse</c> from <c>channelPermissions.ts</c>.
/// </summary>
public sealed record ChannelPermissionResponse(
    /// <summary>'allow' or 'deny'</summary>
    string Behavior,
    /// <summary>Which channel server the reply came from, e.g. "plugin:telegram:tg".</summary>
    string FromServer);

/// <summary>
/// Callbacks surface for permission-relay over channels.
/// Mirrors <c>ChannelPermissionCallbacks</c> from <c>channelPermissions.ts</c>.
/// </summary>
public interface IChannelPermissionCallbacks
{
    /// <summary>
    /// Register a resolver for a request ID. Returns an unsubscribe action.
    /// </summary>
    Action OnResponse(string requestId, Action<ChannelPermissionResponse> handler);

    /// <summary>
    /// Resolve a pending request from a structured channel event.
    /// Returns true if the ID was pending.
    /// </summary>
    bool Resolve(string requestId, string behavior, string fromServer);
}

/// <summary>
/// Minimal capability info needed to filter permission-relay MCP clients.
/// Maps to the subset of <c>ConnectedMCPServer</c> used in <c>filterPermissionRelayClients</c>.
/// </summary>
public interface IMcpPermissionRelayConnection
{
    string Type { get; }
    string Name { get; }
    IReadOnlyDictionary<string, object?> Capabilities { get; }
}

/// <summary>
/// Channel permission helpers. Direct port of <c>channelPermissions.ts</c>.
/// </summary>
public static class McpChannelPermissions
{
    // TS: PERMISSION_REPLY_RE
    // Reply format: /^\s*(y|yes|n|no)\s+([a-km-z]{5})\s*$/i
    // (5 lowercase letters, no 'l' — looks like 1/I)
    public static readonly string PermissionReplyPattern = @"^\s*(y|yes|n|no)\s+([a-km-z]{5})\s*$";

    // 25-letter alphabet: a-z minus 'l'
    private const string IdAlphabet = "abcdefghijkmnopqrstuvwxyz";

    // Substring blocklist — ported verbatim from TS.
    // prettier-ignore
    private static readonly string[] IdAvoidSubstrings =
    [
        "fuck", "shit", "cunt", "cock", "dick", "twat", "piss", "crap",
        "bitch", "whore", "ass", "tit", "cum", "fag", "dyke", "nig",
        "kike", "rape", "nazi", "damn", "poo", "pee", "wank", "anus"
    ];

    /// <summary>
    /// FNV-1a → uint32, then base-25 encode to 5 letters.
    /// Mirrors <c>hashToId</c> from <c>channelPermissions.ts</c>.
    /// </summary>
    private static string HashToId(string input)
    {
        uint h = 0x811c9dc5u;
        foreach (var c in input)
        {
            h ^= (uint)c;
            h = unchecked(h * 0x01000193u);
        }

        var sb = new StringBuilder(5);
        for (var i = 0; i < 5; i++)
        {
            sb.Append(IdAlphabet[(int)(h % 25)]);
            h /= 25;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a short request ID from a tool-use ID.
    /// Mirrors <c>shortRequestId</c> from <c>channelPermissions.ts</c>.
    /// 5 letters from a 25-char alphabet (a-z minus 'l').
    /// Re-hashes with a salt suffix if the result contains a blocklisted substring.
    /// </summary>
    public static string ShortRequestId(string toolUseId)
    {
        var candidate = HashToId(toolUseId);
        for (var salt = 0; salt < 10; salt++)
        {
            if (!ContainsBlocklisted(candidate))
            {
                return candidate;
            }

            candidate = HashToId($"{toolUseId}:{salt}");
        }

        return candidate;
    }

    private static bool ContainsBlocklisted(string candidate)
    {
        foreach (var bad in IdAvoidSubstrings)
        {
            if (candidate.Contains(bad, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Truncate tool input to a phone-sized JSON preview (200 chars).
    /// Mirrors <c>truncateForPreview</c> from <c>channelPermissions.ts</c>.
    /// </summary>
    public static string TruncateForPreview(object? input)
    {
        string s;
        try
        {
            s = JsonSerializer.Serialize(input);
        }
        catch
        {
            return "(unserializable)";
        }

        return s.Length > 200 ? s[..200] + "…" : s;
    }

    /// <summary>
    /// Filters MCP connections to those that can relay permission prompts.
    /// Three conditions, ALL required: connected + allowlisted + declares BOTH capabilities.
    ///
    /// Mirrors <c>filterPermissionRelayClients</c> from <c>channelPermissions.ts</c>.
    /// </summary>
    public static IReadOnlyList<T> FilterPermissionRelayClients<T>(
        IEnumerable<T> clients,
        Func<string, bool> isInAllowlist)
        where T : IMcpPermissionRelayConnection
    {
        return clients
            .Where(c =>
                c.Type == "connected" &&
                isInAllowlist(c.Name) &&
                HasExperimentalCapability(c.Capabilities, "claude/channel") &&
                HasExperimentalCapability(c.Capabilities, "claude/channel/permission"))
            .ToArray();
    }

    private static bool HasExperimentalCapability(
        IReadOnlyDictionary<string, object?> capabilities,
        string key)
    {
        if (!capabilities.TryGetValue("experimental", out var experimental))
        {
            return false;
        }

        return experimental switch
        {
            IReadOnlyDictionary<string, object?> dict => dict.ContainsKey(key),
            IDictionary<string, object?> dict => dict.ContainsKey(key),
            System.Text.Json.Nodes.JsonObject json => json.ContainsKey(key),
            _ => false
        };
    }

    /// <summary>
    /// Factory for the callbacks object. The pending map is closed over — not module-level,
    /// not in AppState.
    ///
    /// Mirrors <c>createChannelPermissionCallbacks</c> from <c>channelPermissions.ts</c>.
    ///
    /// resolve() is called from a dedicated notification handler
    /// (notifications/claude/channel/permission) with the structured payload.
    /// </summary>
    public static IChannelPermissionCallbacks CreateChannelPermissionCallbacks()
    {
        return new ChannelPermissionCallbacksImpl();
    }

    private sealed class ChannelPermissionCallbacksImpl : IChannelPermissionCallbacks
    {
        private readonly Dictionary<string, Action<ChannelPermissionResponse>> _pending =
            new(StringComparer.OrdinalIgnoreCase);

        public Action OnResponse(string requestId, Action<ChannelPermissionResponse> handler)
        {
            // Lowercase for symmetry with Resolve(). shortRequestId always emits lowercase,
            // but future callers might pass mixed case — make the contract explicit.
            var key = requestId.ToLowerInvariant();
            _pending[key] = handler;
            return () => _pending.Remove(key);
        }

        public bool Resolve(string requestId, string behavior, string fromServer)
        {
            var key = requestId.ToLowerInvariant();
            if (!_pending.TryGetValue(key, out var resolver))
            {
                return false;
            }

            // Delete BEFORE calling — if resolver throws or re-enters, the
            // entry is already gone. Also handles duplicate events.
            _pending.Remove(key);
            resolver(new ChannelPermissionResponse(behavior, fromServer));
            return true;
        }
    }
}
