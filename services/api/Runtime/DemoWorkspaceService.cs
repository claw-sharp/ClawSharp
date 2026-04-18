using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ClawSharp.Application.Approvals;
using ClawSharp.Application.Projects;
using ClawSharp.Application.Review;
using ClawSharp.Application.Runs;
using ClawSharp.Application.Settings;
using ClawSharp.Application.Threads;
using ClawSharp.Contracts.Approvals;
using ClawSharp.Contracts.Projects;
using ClawSharp.Contracts.Review;
using ClawSharp.Contracts.Runs;
using ClawSharp.Contracts.Settings;
using ClawSharp.Contracts.Threads;

namespace ClawSharp.Api.Runtime;

public sealed class DemoWorkspaceService :
    IProjectQueryService,
    IThreadQueryService,
    IThreadDetailQueryService,
    IReviewQueryService,
    ISettingsQueryService,
    IRunCommandService,
    IRunEventStreamService,
    IApprovalService
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ProjectState> _projects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ThreadState> _threads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ApprovalSummary>> _approvalsByThread = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ChangedFileSummary>> _changedFilesByThread = new(StringComparer.Ordinal);
    private readonly Dictionary<(string ThreadId, string Path), FileDiff> _diffs = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<RunEventEnvelope>>> _subscriptions = new(StringComparer.Ordinal);

    public DemoWorkspaceService()
    {
        var project = new ProjectState(
            Id: "project-demo",
            Name: "ClawSharp",
            Path: "/workspace/ClawSharp",
            GitBranch: "mobile-app",
            LastUpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var thread = new ThreadState(
            Id: "thread-demo",
            ProjectId: project.Id,
            Title: "Mobile API migration",
            Summary: "Initial remote-mode prototype thread",
            Status: ThreadStatus.Idle,
            Target: ThreadTarget.Cloud,
            Provider: "anthropic",
            Model: "claude-haiku-4-5-20251001",
            LastUpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-3),
            Messages:
            [
                new ThreadMessage(
                    "msg-1",
                    "thread-demo",
                    "user",
                    "Set up a remote API that mobile and desktop can share.",
                    DateTimeOffset.UtcNow.AddMinutes(-4)),
                new ThreadMessage(
                    "msg-2",
                    "thread-demo",
                    "assistant",
                    "I’ll scaffold a shared contracts layer and a minimal ASP.NET host first.",
                    DateTimeOffset.UtcNow.AddMinutes(-4).AddSeconds(20))
            ]);

        _projects[project.Id] = project;
        _threads[thread.Id] = thread;
        _approvalsByThread[thread.Id] =
        [
            new ApprovalSummary(
                "approval-demo",
                thread.Id,
                "Review the remote API architecture before merging.",
                ApprovalDecision.Pending,
                DateTimeOffset.UtcNow.AddMinutes(-2))
        ];
        _changedFilesByThread[thread.Id] =
        [
            new ChangedFileSummary("src/ClawSharp.Contracts/Projects/ProjectSummary.cs", "M", 12, 1, thread.Id),
            new ChangedFileSummary("services/api/Program.cs", "M", 28, 0, thread.Id)
        ];
        _diffs[(thread.Id, "services/api/Program.cs")] = new FileDiff(
            "services/api/Program.cs",
            [
                new DiffHunk(
                    "@@ -1,4 +1,9 @@",
                    [
                        new DiffLine("context", "using ClawSharp.Application.Health;", 1, 1),
                        new DiffLine("add", "using ClawSharp.Application.Runs;", null, 2),
                        new DiffLine("add", "using ClawSharp.Api.Runtime;", null, 3),
                        new DiffLine("context", "var builder = WebApplication.CreateBuilder(args);", 2, 4),
                        new DiffLine("add", "builder.Services.AddSingleton<DemoWorkspaceService>();", null, 5)
                    ])
            ]);
    }

    public Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ProjectSummary>>(
                _projects.Values
                    .Select(project => new ProjectSummary(
                        project.Id,
                        project.Name,
                        project.Path,
                        project.GitBranch,
                        _threads.Values.Count(thread => thread.ProjectId == project.Id),
                        project.LastUpdatedAt))
                    .OrderByDescending(project => project.LastUpdatedAt)
                    .ToArray());
        }
    }

    public Task<IReadOnlyList<ThreadSummary>> ListThreadsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ThreadSummary>>(
                _threads.Values
                    .Where(thread => thread.ProjectId == projectId)
                    .OrderByDescending(thread => thread.LastUpdatedAt)
                    .Select(MapThreadSummary)
                    .ToArray());
        }
    }

    public Task<ThreadDetail?> GetThreadAsync(
        string projectId,
        string threadId,
        string? beforeMessageId = null,
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_threads.TryGetValue(threadId, out var thread) || !string.Equals(thread.ProjectId, projectId, StringComparison.Ordinal))
            {
                return Task.FromResult<ThreadDetail?>(null);
            }

            var messages = thread.Messages.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(beforeMessageId))
            {
                messages = messages.TakeWhile(message => !string.Equals(message.Id, beforeMessageId, StringComparison.Ordinal));
            }

            var limit = Math.Max(1, pageSize ?? 50);
            var selectedMessages = messages.TakeLast(limit).ToArray();
            var hasMore = thread.Messages.Count > selectedMessages.Length;
            var nextBeforeMessageId = hasMore ? selectedMessages.FirstOrDefault()?.Id : null;

            return Task.FromResult<ThreadDetail?>(new ThreadDetail(
                MapThreadSummary(thread),
                selectedMessages,
                hasMore,
                nextBeforeMessageId));
        }
    }

    public Task<ThreadDetail> CreateThreadAsync(CreateThreadRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var id = $"thread-{Guid.NewGuid():N}"[..14];
            var title = string.IsNullOrWhiteSpace(request.Title) ? "New Remote Thread" : request.Title.Trim();
            var thread = new ThreadState(
                id,
                request.ProjectId,
                title,
                "Remote thread created from API mode",
                ThreadStatus.Idle,
                ThreadTarget.Cloud,
                "anthropic",
                "claude-haiku-4-5-20251001",
                DateTimeOffset.UtcNow,
                []);

            _threads[id] = thread;
            _approvalsByThread[id] = [];
            _changedFilesByThread[id] = [];

            return Task.FromResult(new ThreadDetail(MapThreadSummary(thread), [], false, null));
        }
    }

    public Task<IReadOnlyList<ChangedFileSummary>> ListChangedFilesAsync(
        string projectId,
        string? threadId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(threadId) && _changedFilesByThread.TryGetValue(threadId, out var threadFiles))
            {
                return Task.FromResult<IReadOnlyList<ChangedFileSummary>>(threadFiles.ToArray());
            }

            return Task.FromResult<IReadOnlyList<ChangedFileSummary>>(
                _threads.Values
                    .Where(thread => thread.ProjectId == projectId)
                    .SelectMany(thread => _changedFilesByThread.GetValueOrDefault(thread.Id) ?? [])
                    .ToArray());
        }
    }

    public Task<FileDiff?> GetDiffAsync(
        string projectId,
        string filePath,
        string? threadId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(threadId) && _diffs.TryGetValue((threadId, filePath), out var diff))
            {
                return Task.FromResult<FileDiff?>(diff);
            }

            var projectThreadIds = _threads.Values
                .Where(thread => thread.ProjectId == projectId)
                .Select(thread => thread.Id)
                .ToArray();

            foreach (var candidateThreadId in projectThreadIds)
            {
                if (_diffs.TryGetValue((candidateThreadId, filePath), out diff))
                {
                    return Task.FromResult<FileDiff?>(diff);
                }
            }

            return Task.FromResult<FileDiff?>(null);
        }
    }

    public Task<RemoteRuntimeSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new RemoteRuntimeSettings(
            Provider: "anthropic",
            Model: "claude-haiku-4-5-20251001",
            FallbackModel: "claude-sonnet-4-5",
            PermissionMode: "Default",
            EnableTelemetry: true,
            BaseUrl: "https://api.anthropic.com",
            Transport: "AnthropicMessages"));
    }

    public Task<IReadOnlyList<ApprovalSummary>> ListPendingApprovalsAsync(
        string? threadId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var approvals = string.IsNullOrWhiteSpace(threadId)
                ? _approvalsByThread.Values.SelectMany(list => list).Where(approval => approval.Decision == ApprovalDecision.Pending)
                : (_approvalsByThread.TryGetValue(threadId, out var threadApprovals)
                    ? threadApprovals.Where(approval => approval.Decision == ApprovalDecision.Pending)
                    : []);

            return Task.FromResult<IReadOnlyList<ApprovalSummary>>(approvals.OrderByDescending(approval => approval.CreatedAt).ToArray());
        }
    }

    public Task<ApprovalSummary> ResolveApprovalAsync(
        ResolveApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            foreach (var (threadId, approvals) in _approvalsByThread)
            {
                var approvalIndex = approvals.FindIndex(item => item.Id == request.ApprovalId);
                if (approvalIndex < 0)
                {
                    continue;
                }

                var resolved = approvals[approvalIndex] with { Decision = request.Decision };
                approvals[approvalIndex] = resolved;
                return Task.FromResult(resolved);
            }
        }

        throw new InvalidOperationException($"Approval '{request.ApprovalId}' was not found.");
    }

    public Task<string> StartRunAsync(StartRunRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string runId;
        ThreadMessage assistantMessage;
        ThreadMessage userMessage;
        string responseText;

        lock (_gate)
        {
            if (!_threads.TryGetValue(request.ThreadId, out var thread) || !string.Equals(thread.ProjectId, request.ProjectId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Thread '{request.ThreadId}' was not found.");
            }

            runId = $"run-{Guid.NewGuid():N}"[..14];
            userMessage = new ThreadMessage($"msg-{Guid.NewGuid():N}"[..14], request.ThreadId, "user", request.Prompt, DateTimeOffset.UtcNow);
            responseText = $"Remote API mode received: {request.Prompt}\n\nI’m simulating a streamed assistant response so desktop remote mode and mobile can exercise the same event flow.";
            assistantMessage = new ThreadMessage($"msg-{Guid.NewGuid():N}"[..14], request.ThreadId, "assistant", responseText, DateTimeOffset.UtcNow.AddSeconds(1));

            thread.Messages.Add(userMessage);
            _threads[thread.Id] = thread with
            {
                Status = ThreadStatus.Running,
                LastUpdatedAt = DateTimeOffset.UtcNow,
                Summary = "Streaming a remote assistant response"
            };
        }

        _ = Task.Run(async () =>
        {
            await PublishAsync(new RunEventEnvelope(runId, request.ThreadId, RunEventKind.Started, DateTimeOffset.UtcNow, Message: request.Prompt));
            foreach (var chunk in ChunkResponse(responseText))
            {
                await Task.Delay(120);
                await PublishAsync(new RunEventEnvelope(runId, request.ThreadId, RunEventKind.TextDelta, DateTimeOffset.UtcNow, TextDelta: chunk));
            }

            lock (_gate)
            {
                var thread = _threads[request.ThreadId];
                thread.Messages.Add(assistantMessage);
                _threads[request.ThreadId] = thread with
                {
                    Status = ThreadStatus.Completed,
                    LastUpdatedAt = DateTimeOffset.UtcNow,
                    Summary = "Remote assistant response completed"
                };

                _changedFilesByThread[request.ThreadId] =
                [
                    new ChangedFileSummary("services/api/Program.cs", "M", 12, 0, request.ThreadId)
                ];
                _diffs[(request.ThreadId, "services/api/Program.cs")] = new FileDiff(
                    "services/api/Program.cs",
                    [
                        new DiffHunk(
                            "@@ -20,3 +20,6 @@",
                            [
                                new DiffLine("context", "app.MapGet(\"/v1/projects\", ...);", 20, 20),
                                new DiffLine("add", "app.MapGet(\"/v1/threads/{threadId}/events\", ...);", null, 21),
                                new DiffLine("add", "app.MapPost(\"/v1/approvals/resolve\", ...);", null, 22)
                            ])
                    ]);
            }

            await PublishAsync(new RunEventEnvelope(runId, request.ThreadId, RunEventKind.MessageCompleted, DateTimeOffset.UtcNow, Message: assistantMessage.Content));
            await PublishAsync(new RunEventEnvelope(runId, request.ThreadId, RunEventKind.Completed, DateTimeOffset.UtcNow, Detail: "completed"));
        }, CancellationToken.None);

        return Task.FromResult(runId);
    }

    public Task CancelRunAsync(CancelRunRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<string> RetryRunAsync(RetryRunRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lastUserPrompt = "Retry the last remote prompt.";
        lock (_gate)
        {
            if (_threads.TryGetValue(request.ThreadId, out var thread))
            {
                lastUserPrompt = thread.Messages.LastOrDefault(message => string.Equals(message.Role, "user", StringComparison.Ordinal))?.Content
                    ?? lastUserPrompt;
            }
        }

        return StartRunAsync(new StartRunRequest(request.ProjectId ?? "project-demo", request.ThreadId, lastUserPrompt), cancellationToken);
    }

    public Task ArchiveThreadAsync(ArchiveThreadRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _threads.Remove(request.ThreadId);
            _approvalsByThread.Remove(request.ThreadId);
            _changedFilesByThread.Remove(request.ThreadId);
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<RunEventEnvelope> StreamThreadEventsAsync(
        string threadId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var subscriptionId = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<RunEventEnvelope>();
        var bucket = _subscriptions.GetOrAdd(threadId, _ => new ConcurrentDictionary<Guid, Channel<RunEventEnvelope>>());
        bucket[subscriptionId] = channel;

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }
        }
        finally
        {
            if (_subscriptions.TryGetValue(threadId, out var subscriptions))
            {
                subscriptions.TryRemove(subscriptionId, out _);
            }
        }
    }

    private async Task PublishAsync(RunEventEnvelope envelope)
    {
        if (!_subscriptions.TryGetValue(envelope.ThreadId, out var subscriptions))
        {
            return;
        }

        foreach (var channel in subscriptions.Values)
        {
            await channel.Writer.WriteAsync(envelope);
        }
    }

    private static ThreadSummary MapThreadSummary(ThreadState thread)
    {
        return new ThreadSummary(
            thread.Id,
            thread.ProjectId,
            thread.Title,
            thread.Summary,
            thread.Status,
            thread.Target,
            thread.Provider,
            thread.Model,
            thread.Messages.Count,
            thread.LastUpdatedAt);
    }

    private static IReadOnlyList<string> ChunkResponse(string response)
    {
        var words = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>(words.Length);
        for (var index = 0; index < words.Length; index += 4)
        {
            chunks.Add(string.Join(' ', words.Skip(index).Take(4)) + " ");
        }

        return chunks;
    }

    private sealed record ProjectState(
        string Id,
        string Name,
        string Path,
        string GitBranch,
        DateTimeOffset LastUpdatedAt);

    private sealed record ThreadState(
        string Id,
        string ProjectId,
        string Title,
        string Summary,
        ThreadStatus Status,
        ThreadTarget Target,
        string Provider,
        string Model,
        DateTimeOffset LastUpdatedAt,
        List<ThreadMessage> Messages);
}
