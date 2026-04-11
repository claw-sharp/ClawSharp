using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.AgentHost.Mapping;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Services;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.AgentHost.Sessions;

public sealed class ThreadCatalogService
{
    private const int DefaultThreadPageSize = 50;
    private readonly WorkspaceApplicationRegistry _applicationRegistry;
    private readonly RecentProjectStore _recentProjectStore;
    private readonly ThreadStateStore _threadStateStore;
    private readonly ISessionLogStore _sessionLogStore;
    private readonly ThreadTranscriptPageReader _threadTranscriptPageReader;

    public ThreadCatalogService(
        WorkspaceApplicationRegistry applicationRegistry,
        RecentProjectStore recentProjectStore,
        ThreadStateStore threadStateStore,
        ISessionLogStore? sessionLogStore = null)
        : this(
            applicationRegistry,
            recentProjectStore,
            threadStateStore,
            sessionLogStore,
            new ThreadTranscriptPageReader())
    {
    }

    internal ThreadCatalogService(
        WorkspaceApplicationRegistry applicationRegistry,
        RecentProjectStore recentProjectStore,
        ThreadStateStore threadStateStore,
        ISessionLogStore? sessionLogStore,
        ThreadTranscriptPageReader threadTranscriptPageReader)
    {
        _applicationRegistry = applicationRegistry;
        _recentProjectStore = recentProjectStore;
        _threadStateStore = threadStateStore;
        _sessionLogStore = sessionLogStore ?? new DiskSessionLogStore();
        _threadTranscriptPageReader = threadTranscriptPageReader;
    }

    public ThreadCatalogService(
        WorkspaceApplicationRegistry applicationRegistry,
        RecentProjectStore recentProjectStore)
        : this(applicationRegistry, recentProjectStore, new ThreadStateStore())
    {
    }

    public async Task<ListThreadsResponse> ListThreadsAsync(
        ListThreadsRequest request,
        CancellationToken cancellationToken = default)
    {
        var projectPath = await ResolveProjectPathAsync(request.ProjectId, cancellationToken);
        var threads = await ListThreadsByPathAsync(projectPath, cancellationToken);
        var project = DesktopContractMapper.MapProject(
            projectPath,
            threads,
            (await _recentProjectStore.FindByIdAsync(request.ProjectId, cancellationToken))?.LastOpenedAt);
        return new ListThreadsResponse(project, threads);
    }

    public async Task<CreateThreadResponse> CreateThreadAsync(
        CreateThreadRequest request,
        CancellationToken cancellationToken = default)
    {
        var projectPath = await ResolveProjectPathAsync(request.ProjectId, cancellationToken);
        var app = await _applicationRegistry.GetOrCreateAsync(projectPath, cancellationToken);

        var session = app.SessionFactory.Create();
        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            session.SetCustomTitle(request.Title);
        }

        await EnsureTranscriptExistsAsync(session, cancellationToken);
        await app.TranscriptStore.RecordSessionMetadataAsync(session, cancellationToken);

        var detail = DesktopContractMapper.MapThreadDetail(
            request.ProjectId,
            session,
            DateTimeOffset.UtcNow);
        var threads = await ListThreadsByPathAsync(projectPath, cancellationToken);
        var project = DesktopContractMapper.MapProject(
            projectPath,
            threads,
            (await _recentProjectStore.FindByIdAsync(request.ProjectId, cancellationToken))?.LastOpenedAt);
        return new CreateThreadResponse(project, detail);
    }

    public async Task<GetThreadResponse> GetThreadAsync(
        GetThreadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ThreadId))
        {
            throw new AgentHostException("invalid_request", "threadId is required.");
        }

        if (!string.IsNullOrWhiteSpace(request.ProjectId))
        {
            return await GetThreadForProjectAsync(
                request.ProjectId,
                request.ThreadId,
                request.BeforeMessageId,
                request.PageSize,
                cancellationToken);
        }

        var recentProjects = await _recentProjectStore.ListAsync(cancellationToken);
        foreach (var recentProject in recentProjects)
        {
            var threads = await ListThreadsByPathAsync(recentProject.Path, cancellationToken);
            var thread = threads.FirstOrDefault(item => item.Id == request.ThreadId);
            if (thread is null)
            {
                continue;
            }

            var project = DesktopContractMapper.MapProject(recentProject.Path, threads, recentProject.LastOpenedAt);
            var detail = await ReadThreadDetailPageAsync(
                project.Id,
                thread,
                request.BeforeMessageId,
                request.PageSize,
                cancellationToken);
            return new GetThreadResponse(project, detail);
        }

        throw new AgentHostException("thread_not_found", $"Thread '{request.ThreadId}' was not found.");
    }

    public async Task<RenameThreadResponse> RenameThreadAsync(
        RenameThreadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ThreadId) || string.IsNullOrWhiteSpace(request.Title))
        {
            throw new AgentHostException("invalid_request", "threadId and title are required.");
        }

        var projectPath = await ResolveProjectPathAsync(request.ProjectId, cancellationToken);
        var app = await _applicationRegistry.GetOrCreateAsync(projectPath, cancellationToken);
        var session = await app.SessionFactory.ResumeAsync(request.ThreadId, cancellationToken);
        if (session is null)
        {
            throw new AgentHostException("thread_not_found", $"Thread '{request.ThreadId}' was not found in project '{request.ProjectId}'.");
        }

        session.SetCustomTitle(request.Title);
        await app.TranscriptStore.RecordSessionMetadataAsync(session, cancellationToken);
        var threads = await ListThreadsByPathAsync(projectPath, cancellationToken);
        var project = DesktopContractMapper.MapProject(
            projectPath,
            threads,
            (await _recentProjectStore.FindByIdAsync(request.ProjectId, cancellationToken))?.LastOpenedAt);
        var lastUpdatedAt = threads.FirstOrDefault(thread => thread.Id == request.ThreadId)?.LastUpdatedAt ?? DateTimeOffset.UtcNow;
        var detail = DesktopContractMapper.MapThreadDetail(request.ProjectId, session, lastUpdatedAt);
        return new RenameThreadResponse(project, detail);
    }

    public async Task<ArchiveThreadResponse> ArchiveThreadAsync(
        ArchiveThreadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId) || string.IsNullOrWhiteSpace(request.ThreadId))
        {
            throw new AgentHostException("invalid_request", "projectId and threadId are required.");
        }

        var projectPath = await ResolveProjectPathAsync(request.ProjectId, cancellationToken);
        var app = await _applicationRegistry.GetOrCreateAsync(projectPath, cancellationToken);
        var session = await app.SessionFactory.ResumeAsync(request.ThreadId, cancellationToken);
        if (session is null)
        {
            throw new AgentHostException("thread_not_found", $"Thread '{request.ThreadId}' was not found in project '{request.ProjectId}'.");
        }

        _threadStateStore.Archive(request.ProjectId, request.ThreadId);
        return new ArchiveThreadResponse(request.ProjectId, request.ThreadId, true, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<ThreadSummaryDto>> ListThreadsByPathAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var projectId = DesktopContractMapper.CreateProjectId(projectPath);
        var logs = await _sessionLogStore.LoadProjectLogsAsync(projectPath, cancellationToken);
        return logs
            .Select(log => DesktopContractMapper.MapThreadSummary(projectId, log))
            .Where(thread => !_threadStateStore.IsArchived(projectId, thread.Id))
            .OrderByDescending(thread => thread.LastUpdatedAt)
            .ToArray();
    }

    private async Task<GetThreadResponse> GetThreadForProjectAsync(
        string projectId,
        string threadId,
        string? beforeMessageId,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var projectPath = await ResolveProjectPathAsync(projectId, cancellationToken);
        var threads = await ListThreadsByPathAsync(projectPath, cancellationToken);
        var thread = threads.FirstOrDefault(item => item.Id == threadId);
        if (thread is null)
        {
            throw new AgentHostException("thread_not_found", $"Thread '{threadId}' was not found in project '{projectId}'.");
        }

        var project = DesktopContractMapper.MapProject(
            projectPath,
            threads,
            (await _recentProjectStore.FindByIdAsync(projectId, cancellationToken))?.LastOpenedAt);
        var detail = await ReadThreadDetailPageAsync(
            projectId,
            thread,
            beforeMessageId,
            pageSize,
            cancellationToken);
        return new GetThreadResponse(project, detail);
    }

    private async Task<ThreadDetailDto> ReadThreadDetailPageAsync(
        string projectId,
        ThreadSummaryDto thread,
        string? beforeMessageId,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var page = await _threadTranscriptPageReader.ReadPageAsync(
            thread.TranscriptPath,
            thread.Id,
            beforeMessageId,
            pageSize ?? DefaultThreadPageSize,
            cancellationToken);
        return new ThreadDetailDto(
            thread,
            page.Messages,
            page.HasMoreMessages,
            page.NextBeforeMessageId);
    }

    private async Task<string> ResolveProjectPathAsync(string projectId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new AgentHostException("invalid_request", "projectId is required.");
        }

        var recentProject = await _recentProjectStore.FindByIdAsync(projectId, cancellationToken);
        if (recentProject is null)
        {
            throw new AgentHostException(
                "project_not_found",
                $"Project '{projectId}' is not known to AgentHost yet. Open the project first.");
        }

        return recentProject.Path;
    }

    private static async Task EnsureTranscriptExistsAsync(ConversationSession session, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(session.TranscriptPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(session.TranscriptPath))
        {
            await File.WriteAllTextAsync(session.TranscriptPath, string.Empty, cancellationToken);
        }
    }
}
