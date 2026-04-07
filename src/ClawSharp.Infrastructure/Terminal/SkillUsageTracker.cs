using System.Text.Json;

namespace ClawSharp.Infrastructure;

public sealed class SkillUsageTracker
{
    private const int DebounceMilliseconds = 60_000;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private static readonly Lock WriteLock = new();
    private readonly string _globalConfigPath;
    private readonly Dictionary<string, long> _lastWriteBySkill = new(StringComparer.Ordinal);

    public SkillUsageTracker(string? globalConfigPath = null)
    {
        _globalConfigPath = globalConfigPath ?? ClaudeConfigPaths.GetGlobalClaudeFilePath();
    }

    public void RecordSkillUsage(string skillName, long? nowUnixMilliseconds = null)
    {
        var now = nowUnixMilliseconds ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (WriteLock)
        {
            if (_lastWriteBySkill.TryGetValue(skillName, out var lastWrite) && now - lastWrite < DebounceMilliseconds)
            {
                return;
            }

            _lastWriteBySkill[skillName] = now;
            var config = Load();
            config.SkillUsage.TryGetValue(skillName, out var existing);
            config.SkillUsage[skillName] = new SkillUsageEntry((existing?.UsageCount ?? 0) + 1, now);
            Save(config);
        }
    }

    public double GetSkillUsageScore(string skillName, long? nowUnixMilliseconds = null)
    {
        var now = nowUnixMilliseconds ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var config = Load();
        if (!config.SkillUsage.TryGetValue(skillName, out var usage))
        {
            return 0;
        }

        var daysSinceUse = (now - usage.LastUsedAt) / (1000d * 60d * 60d * 24d);
        var recencyFactor = Math.Pow(0.5d, daysSinceUse / 7d);
        return usage.UsageCount * Math.Max(recencyFactor, 0.1d);
    }

    private SkillUsageConfig Load()
    {
        if (!File.Exists(_globalConfigPath))
        {
            return new SkillUsageConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<SkillUsageConfig>(File.ReadAllText(_globalConfigPath), SerializerOptions) ?? new SkillUsageConfig();
        }
        catch
        {
            return new SkillUsageConfig();
        }
    }

    private void Save(SkillUsageConfig config)
    {
        var directory = Path.GetDirectoryName(_globalConfigPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_globalConfigPath, JsonSerializer.Serialize(config, SerializerOptions));
    }

    private sealed class SkillUsageConfig
    {
        public Dictionary<string, SkillUsageEntry> SkillUsage { get; init; } = new(StringComparer.Ordinal);
    }

    private sealed record SkillUsageEntry(int UsageCount, long LastUsedAt);
}
