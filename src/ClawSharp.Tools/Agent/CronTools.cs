using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools;

internal static class CronToolSchemas
{
    public static JsonObject CreateInputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("cron", ToolJsonSchemaFactory.String("Standard 5-field cron expression in local time: M H DoM Mon DoW.")),
                ("prompt", ToolJsonSchemaFactory.String("Prompt text to associate with the schedule.")),
                ("recurring", ToolJsonSchemaFactory.Boolean("Whether the job repeats. Defaults to true.", defaultValue: true)),
                ("durable", ToolJsonSchemaFactory.Boolean("Persist the job to .clawsharp/scheduled_tasks.json.", defaultValue: false))
            ],
            required: ["cron", "prompt"]);

    public static JsonObject CreateOutputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("id", ToolJsonSchemaFactory.String()),
                ("humanSchedule", ToolJsonSchemaFactory.String()),
                ("recurring", ToolJsonSchemaFactory.Boolean()),
                ("durable", ToolJsonSchemaFactory.Boolean()),
                ("nextRunUtc", ToolJsonSchemaFactory.String("Next matching run in UTC"))
            ],
            required: ["id", "humanSchedule", "recurring", "durable", "nextRunUtc"]);

    public static JsonObject ListOutputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("jobs", ToolJsonSchemaFactory.Array(
                    ToolJsonSchemaFactory.StrictObject(
                        [
                            ("id", ToolJsonSchemaFactory.String()),
                            ("cron", ToolJsonSchemaFactory.String()),
                            ("humanSchedule", ToolJsonSchemaFactory.String()),
                            ("prompt", ToolJsonSchemaFactory.String()),
                            ("recurring", ToolJsonSchemaFactory.Boolean()),
                            ("durable", ToolJsonSchemaFactory.Boolean()),
                            ("nextRunUtc", ToolJsonSchemaFactory.String("Next matching run in UTC"))
                        ],
                        required: ["id", "cron", "humanSchedule", "prompt", "recurring", "durable", "nextRunUtc"])))
            ],
            required: ["jobs"]);

    public static JsonObject DeleteInputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("id", ToolJsonSchemaFactory.String("Job ID returned by CronCreate."))
            ],
            required: ["id"]);

    public static JsonObject DeleteOutputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("id", ToolJsonSchemaFactory.String())
            ],
            required: ["id"]);
}

internal sealed class CronCreateTool : BaseTool
{
    public CronCreateTool()
        : base(
            new ToolDescriptor(
                "CronCreate",
                "Create a scheduled prompt definition for the current workspace",
                SearchHint: "schedule a recurring or one-shot prompt",
                ShouldDefer: true,
                InputSchema: CronToolSchemas.CreateInputSchema,
                OutputSchema: CronToolSchemas.CreateOutputSchema,
                Strict: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments)
    {
        return true;
    }

    public override bool IsReadOnly(string arguments)
    {
        return false;
    }

    public override Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!CronToolInputParser.TryParseCreate(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return Task.FromResult(ToolValidationResult.Invalid(errorMessage ?? "Invalid cron create input."));
        }

        if (!CronExpression.TryParse(input.Cron, out var expression))
        {
            return Task.FromResult(ToolValidationResult.Invalid(
                $"Invalid cron expression '{input.Cron}'. Expected 5 fields: M H DoM Mon DoW."));
        }

        if (expression.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Local) is null)
        {
            return Task.FromResult(ToolValidationResult.Invalid(
                $"Cron expression '{input.Cron}' does not match any calendar date in the next year."));
        }

        var existingJobs = CronJobStore.ListAll(context.WorkspaceRoot);
        if (existingJobs.Count >= 50)
        {
            return Task.FromResult(ToolValidationResult.Invalid("Too many scheduled jobs (max 50). Delete one first."));
        }

        if (!string.IsNullOrWhiteSpace(context.AgentId))
        {
            return Task.FromResult(ToolValidationResult.Invalid(
                "scheduled crons are not supported for teammates in ClawSharp yet because cron delivery is only wired for the main REPL session."));
        }

        return Task.FromResult(ToolValidationResult.Valid());
    }

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!CronToolInputParser.TryParseCreate(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return Task.FromResult(Failure(errorMessage ?? "Invalid cron create input."));
        }

        if (!CronExpression.TryParse(input.Cron, out var expression))
        {
            return Task.FromResult(Failure($"Invalid cron expression '{input.Cron}'."));
        }

        if (!string.IsNullOrWhiteSpace(context.AgentId))
        {
            return Task.FromResult(Failure(
                "scheduled crons are not supported for teammates in ClawSharp yet because cron delivery is only wired for the main REPL session."));
        }

        var created = CronJobStore.Add(
            context.WorkspaceRoot,
            input.Cron,
            input.Prompt,
            input.Recurring ?? true,
            input.Durable ?? false,
            context.AgentId);
        var nextRun = expression.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
        if (nextRun is null)
        {
            return Task.FromResult(Failure($"Cron expression '{input.Cron}' does not match any calendar date in the next year."));
        }

        return Task.FromResult(Success(
            created.Recurring
                ? $"Scheduled recurring job {created.Id} ({CronExpressionFormatter.ToHumanReadable(input.Cron)})."
                : $"Scheduled one-shot task {created.Id} ({CronExpressionFormatter.ToHumanReadable(input.Cron)}).",
            new JsonObject
            {
                ["id"] = created.Id,
                ["humanSchedule"] = CronExpressionFormatter.ToHumanReadable(input.Cron),
                ["recurring"] = created.Recurring,
                ["durable"] = created.Durable,
                ["nextRunUtc"] = nextRun.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            }));
    }
}

internal sealed class CronListTool : BaseTool
{
    public CronListTool()
        : base(
            new ToolDescriptor(
                "CronList",
                "List scheduled prompt definitions for the current workspace",
                SearchHint: "list active cron jobs",
                ShouldDefer: true,
                InputSchema: ToolJsonSchemaFactory.StrictObject(Array.Empty<(string Name, JsonNode Schema)>(), required: []),
                OutputSchema: CronToolSchemas.ListOutputSchema,
                Strict: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments)
    {
        return true;
    }

    public override bool IsReadOnly(string arguments)
    {
        return true;
    }

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var jobs = CronJobStore.ListAll(context.WorkspaceRoot)
            .Where(job => string.IsNullOrWhiteSpace(context.AgentId) || string.Equals(job.AgentId, context.AgentId, StringComparison.Ordinal))
            .OrderBy(static job => job.Id, StringComparer.Ordinal)
            .Select(static job =>
            {
                var nextRun = CronExpression.TryParse(job.Cron, out var expression)
                    ? expression.GetNextOccurrence(job.LastFiredAtUtc ?? job.CreatedAtUtc, TimeZoneInfo.Local)
                    : null;
                return new JsonObject
                {
                    ["id"] = job.Id,
                    ["cron"] = job.Cron,
                    ["humanSchedule"] = CronExpressionFormatter.ToHumanReadable(job.Cron),
                    ["prompt"] = job.Prompt,
                    ["recurring"] = job.Recurring,
                    ["durable"] = job.Durable,
                    ["nextRunUtc"] = nextRun?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
                };
            })
            .ToArray();

        return Task.FromResult(Success(
            jobs.Length == 0 ? "No scheduled jobs." : $"Found {jobs.Length} scheduled job(s).",
            new JsonObject
            {
                ["jobs"] = new JsonArray(jobs)
            }));
    }
}

internal sealed class CronDeleteTool : BaseTool
{
    public CronDeleteTool()
        : base(
            new ToolDescriptor(
                "CronDelete",
                "Delete a scheduled prompt definition by ID",
                SearchHint: "cancel a scheduled cron job",
                ShouldDefer: true,
                InputSchema: CronToolSchemas.DeleteInputSchema,
                OutputSchema: CronToolSchemas.DeleteOutputSchema,
                Strict: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments)
    {
        return true;
    }

    public override bool IsReadOnly(string arguments)
    {
        return false;
    }

    public override Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!CronToolInputParser.TryParseDelete(context.Arguments, out var id, out var errorMessage))
        {
            return Task.FromResult(ToolValidationResult.Invalid(errorMessage ?? "Invalid cron delete input."));
        }

        var job = CronJobStore.Find(context.WorkspaceRoot, id);
        if (job is null)
        {
            return Task.FromResult(ToolValidationResult.Invalid($"No scheduled job with id '{id}'."));
        }

        if (!string.IsNullOrWhiteSpace(context.AgentId) &&
            !string.Equals(job.AgentId, context.AgentId, StringComparison.Ordinal))
        {
            return Task.FromResult(ToolValidationResult.Invalid($"Cannot delete cron job '{id}': owned by another agent."));
        }

        return Task.FromResult(
            ToolValidationResult.Valid());
    }

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!CronToolInputParser.TryParseDelete(context.Arguments, out var id, out var errorMessage))
        {
            return Task.FromResult(Failure(errorMessage ?? "Invalid cron delete input."));
        }

        var job = CronJobStore.Find(context.WorkspaceRoot, id);
        if (job is null)
        {
            return Task.FromResult(Failure($"No scheduled job with id '{id}'."));
        }

        if (!string.IsNullOrWhiteSpace(context.AgentId) &&
            !string.Equals(job.AgentId, context.AgentId, StringComparison.Ordinal))
        {
            return Task.FromResult(Failure($"Cannot delete cron job '{id}': owned by another agent."));
        }

        if (!CronJobStore.Remove(context.WorkspaceRoot, id))
        {
            return Task.FromResult(Failure($"No scheduled job with id '{id}'."));
        }

        return Task.FromResult(Success(
            $"Cancelled job {id}.",
            new JsonObject
            {
                ["id"] = id
            }));
    }
}

internal static class CronToolInputParser
{
    public static bool TryParseCreate(string arguments, out CronCreateInput? input, out string? errorMessage)
    {
        input = null;
        errorMessage = null;

        try
        {
            var root = JsonNode.Parse(arguments)?.AsObject();
            if (root is null)
            {
                errorMessage = "Arguments must be a JSON object.";
                return false;
            }

            var cron = root["cron"]?.GetValue<string>();
            var prompt = root["prompt"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(cron) || string.IsNullOrWhiteSpace(prompt))
            {
                errorMessage = "'cron' and 'prompt' are required.";
                return false;
            }

            input = new CronCreateInput(
                cron,
                prompt,
                root["recurring"]?.GetValue<bool?>(),
                root["durable"]?.GetValue<bool?>());
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public static bool TryParseDelete(string arguments, out string id, out string? errorMessage)
    {
        id = string.Empty;
        errorMessage = null;

        try
        {
            var root = JsonNode.Parse(arguments)?.AsObject();
            if (root is null)
            {
                errorMessage = "Arguments must be a JSON object.";
                return false;
            }

            id = root["id"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                errorMessage = "'id' is required.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}

internal sealed record CronCreateInput(
    string Cron,
    string Prompt,
    bool? Recurring,
    bool? Durable);

internal sealed record CronJobDefinition(
    string Id,
    string Cron,
    string Prompt,
    bool Recurring,
    bool Durable,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastFiredAtUtc = null,
    string? AgentId = null);

internal static class CronJobStore
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, CronJobDefinition>> SessionJobs =
        new(StringComparer.Ordinal);

    public static CronJobDefinition Add(
        string workspaceRoot,
        string cron,
        string prompt,
        bool recurring,
        bool durable,
        string? agentId)
    {
        var job = new CronJobDefinition(
            $"cron-{Guid.NewGuid():N}"[..13],
            cron,
            prompt,
            recurring,
            durable,
            DateTimeOffset.UtcNow,
            LastFiredAtUtc: null,
            AgentId: agentId);

        if (durable)
        {
            var jobs = LoadDurableJobs(workspaceRoot).ToList();
            jobs.Add(job);
            SaveDurableJobs(workspaceRoot, jobs);
        }
        else
        {
            var store = SessionJobs.GetOrAdd(
                Path.GetFullPath(workspaceRoot),
                static _ => new ConcurrentDictionary<string, CronJobDefinition>(StringComparer.Ordinal));
            store[job.Id] = job;
        }

        return job;
    }

    public static IReadOnlyList<CronJobDefinition> ListAll(string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var sessionJobs = SessionJobs.TryGetValue(root, out var session)
            ? session.Values
            : [];
        return sessionJobs.Concat(LoadDurableJobs(workspaceRoot))
            .OrderBy(static job => job.CreatedAtUtc)
            .ToArray();
    }

    public static bool Exists(string workspaceRoot, string id)
    {
        return ListAll(workspaceRoot).Any(job => string.Equals(job.Id, id, StringComparison.Ordinal));
    }

    public static CronJobDefinition? Find(string workspaceRoot, string id)
    {
        return ListAll(workspaceRoot)
            .FirstOrDefault(job => string.Equals(job.Id, id, StringComparison.Ordinal));
    }

    public static bool Remove(string workspaceRoot, string id)
    {
        var removed = false;
        var root = Path.GetFullPath(workspaceRoot);
        if (SessionJobs.TryGetValue(root, out var session))
        {
            removed |= session.TryRemove(id, out _);
        }

        var durableJobs = LoadDurableJobs(workspaceRoot).ToList();
        var newDurableJobs = durableJobs.Where(job => !string.Equals(job.Id, id, StringComparison.Ordinal)).ToList();
        if (newDurableJobs.Count != durableJobs.Count)
        {
            SaveDurableJobs(workspaceRoot, newDurableJobs);
            removed = true;
        }

        return removed;
    }

    public static void MarkFired(string workspaceRoot, string id, DateTimeOffset firedAtUtc)
    {
        var root = Path.GetFullPath(workspaceRoot);
        if (SessionJobs.TryGetValue(root, out var session) &&
            session.TryGetValue(id, out var sessionJob))
        {
            session[id] = sessionJob with { LastFiredAtUtc = firedAtUtc };
            return;
        }

        var durableJobs = LoadDurableJobs(workspaceRoot).ToList();
        var changed = false;
        for (var index = 0; index < durableJobs.Count; index++)
        {
            if (!string.Equals(durableJobs[index].Id, id, StringComparison.Ordinal))
            {
                continue;
            }

            durableJobs[index] = durableJobs[index] with { LastFiredAtUtc = firedAtUtc };
            changed = true;
            break;
        }

        if (changed)
        {
            SaveDurableJobs(workspaceRoot, durableJobs);
        }
    }

    private static IReadOnlyList<CronJobDefinition> LoadDurableJobs(string workspaceRoot)
    {
        var path = GetDurableStorePath(workspaceRoot);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<CronJobDefinition>>(File.ReadAllText(path)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void SaveDurableJobs(string workspaceRoot, IReadOnlyList<CronJobDefinition> jobs)
    {
        var path = GetDurableStorePath(workspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(jobs, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string GetDurableStorePath(string workspaceRoot)
    {
        return Path.Combine(Path.GetFullPath(workspaceRoot), ".clawsharp", "scheduled_tasks.json");
    }
}

internal sealed class CronExpression
{
    private readonly CronField _minute;
    private readonly CronField _hour;
    private readonly CronField _dayOfMonth;
    private readonly CronField _month;
    private readonly CronField _dayOfWeek;

    private CronExpression(CronField minute, CronField hour, CronField dayOfMonth, CronField month, CronField dayOfWeek)
    {
        _minute = minute;
        _hour = hour;
        _dayOfMonth = dayOfMonth;
        _month = month;
        _dayOfWeek = dayOfWeek;
    }

    public static bool TryParse(string expression, out CronExpression? cronExpression)
    {
        cronExpression = null;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 5)
        {
            return false;
        }

        if (!CronField.TryParse(parts[0], 0, 59, out var minute) ||
            !CronField.TryParse(parts[1], 0, 23, out var hour) ||
            !CronField.TryParse(parts[2], 1, 31, out var dayOfMonth) ||
            !CronField.TryParse(parts[3], 1, 12, out var month) ||
            !CronField.TryParse(parts[4], 0, 6, out var dayOfWeek, allowSevenAsSunday: true))
        {
            return false;
        }

        cronExpression = new CronExpression(minute!, hour!, dayOfMonth!, month!, dayOfWeek!);
        return true;
    }

    public DateTimeOffset? GetNextOccurrence(DateTimeOffset fromUtc, TimeZoneInfo timeZone)
    {
        var currentLocal = TimeZoneInfo.ConvertTime(fromUtc, timeZone)
            .AddMinutes(1);
        currentLocal = new DateTimeOffset(
            currentLocal.Year,
            currentLocal.Month,
            currentLocal.Day,
            currentLocal.Hour,
            currentLocal.Minute,
            0,
            currentLocal.Offset);

        var endLocal = currentLocal.AddYears(1);
        while (currentLocal <= endLocal)
        {
            var dayOfWeek = (int)currentLocal.DayOfWeek;
            if (_minute.Matches(currentLocal.Minute) &&
                _hour.Matches(currentLocal.Hour) &&
                _dayOfMonth.Matches(currentLocal.Day) &&
                _month.Matches(currentLocal.Month) &&
                _dayOfWeek.Matches(dayOfWeek))
            {
                return currentLocal.ToUniversalTime();
            }

            currentLocal = currentLocal.AddMinutes(1);
        }

        return null;
    }
}

internal sealed class CronField
{
    private readonly HashSet<int> _allowed;

    private CronField(HashSet<int> allowed)
    {
        _allowed = allowed;
    }

    public bool Matches(int value)
    {
        return _allowed.Contains(value);
    }

    public static bool TryParse(string token, int min, int max, out CronField? field, bool allowSevenAsSunday = false)
    {
        field = null;
        var allowed = new HashSet<int>();
        foreach (var segment in token.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment == "*")
            {
                for (var value = min; value <= max; value++)
                {
                    allowed.Add(value);
                }

                continue;
            }

            if (segment.StartsWith("*/", StringComparison.Ordinal))
            {
                if (!int.TryParse(segment[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var step) || step <= 0)
                {
                    return false;
                }

                for (var value = min; value <= max; value += step)
                {
                    allowed.Add(value);
                }

                continue;
            }

            if (segment.Contains('-', StringComparison.Ordinal))
            {
                var rangeParts = segment.Split('-', 2, StringSplitOptions.TrimEntries);
                if (rangeParts.Length != 2 ||
                    !TryNormalize(rangeParts[0], min, max, allowSevenAsSunday, out var start) ||
                    !TryNormalize(rangeParts[1], min, max, allowSevenAsSunday, out var end) ||
                    end < start)
                {
                    return false;
                }

                for (var value = start; value <= end; value++)
                {
                    allowed.Add(value);
                }

                continue;
            }

            if (!TryNormalize(segment, min, max, allowSevenAsSunday, out var exact))
            {
                return false;
            }

            allowed.Add(exact);
        }

        if (allowed.Count == 0)
        {
            return false;
        }

        field = new CronField(allowed);
        return true;
    }

    private static bool TryNormalize(string token, int min, int max, bool allowSevenAsSunday, out int normalized)
    {
        normalized = 0;
        if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        if (allowSevenAsSunday && value == 7)
        {
            value = 0;
        }

        if (value < min || value > max)
        {
            return false;
        }

        normalized = value;
        return true;
    }
}

internal static class CronExpressionFormatter
{
    public static string ToHumanReadable(string cron)
    {
        var parts = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 5)
        {
            return $"{cron} (local time)";
        }

        if (parts[0].StartsWith("*/", StringComparison.Ordinal) &&
            parts[1] == "*" &&
            parts[2] == "*" &&
            parts[3] == "*" &&
            parts[4] == "*")
        {
            return $"every {parts[0][2..]} minutes";
        }

        if (parts[0] == "0" && parts[1] == "*" && parts[2] == "*" && parts[3] == "*" && parts[4] == "*")
        {
            return "hourly";
        }

        return $"{cron} (local time)";
    }
}
