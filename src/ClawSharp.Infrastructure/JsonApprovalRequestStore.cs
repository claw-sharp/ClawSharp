using System.Text.Json;
using System.Text.Json.Serialization;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class JsonApprovalRequestStore : IApprovalRequestStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    private readonly string _path;

    public JsonApprovalRequestStore(string? path = null)
    {
        _path = path ?? Path.Combine(SessionStoragePaths.GetClaudeConfigHomeDir(), "approval-requests.json");
    }

    public IReadOnlyList<ApprovalRequest> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<ApprovalRequest>>(File.ReadAllText(_path), SerializerOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<ApprovalRequest>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<IReadOnlyList<ApprovalRequest>>(stream, SerializerOptions, cancellationToken) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<ApprovalRequest> requests)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(requests, SerializerOptions));
    }
}
