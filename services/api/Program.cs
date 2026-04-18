using System.Text.Json;
using System.Text.Json.Serialization;
using ClawSharp.Api.Capabilities;
using ClawSharp.Api.Health;
using ClawSharp.Api.Runtime;
using ClawSharp.Application.Approvals;
using ClawSharp.Application.Capabilities;
using ClawSharp.Application.Health;
using ClawSharp.Application.Projects;
using ClawSharp.Application.Review;
using ClawSharp.Application.Runs;
using ClawSharp.Application.Settings;
using ClawSharp.Application.Threads;
using ClawSharp.Contracts.Approvals;
using ClawSharp.Contracts.Runs;
using ClawSharp.Contracts.Threads;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<IHealthQueryService, DefaultHealthQueryService>();
builder.Services.AddSingleton<ICapabilityQueryService, DefaultCapabilityQueryService>();
builder.Services.AddSingleton<DemoWorkspaceService>();
builder.Services.AddSingleton<IProjectQueryService>(serviceProvider => serviceProvider.GetRequiredService<DemoWorkspaceService>());
builder.Services.AddSingleton<IThreadQueryService>(serviceProvider => serviceProvider.GetRequiredService<DemoWorkspaceService>());
builder.Services.AddSingleton<IThreadDetailQueryService>(serviceProvider => serviceProvider.GetRequiredService<DemoWorkspaceService>());
builder.Services.AddSingleton<ISettingsQueryService>(serviceProvider => serviceProvider.GetRequiredService<DemoWorkspaceService>());
builder.Services.AddSingleton<IReviewQueryService>(serviceProvider => serviceProvider.GetRequiredService<DemoWorkspaceService>());
builder.Services.AddSingleton<IRunCommandService>(serviceProvider => serviceProvider.GetRequiredService<DemoWorkspaceService>());
builder.Services.AddSingleton<IRunEventStreamService>(serviceProvider => serviceProvider.GetRequiredService<DemoWorkspaceService>());
builder.Services.AddSingleton<IApprovalService>(serviceProvider => serviceProvider.GetRequiredService<DemoWorkspaceService>());

var app = builder.Build();

app.MapGet("/health", async (IHealthQueryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetHealthAsync(cancellationToken)));

app.MapGet("/v1/capabilities", async (ICapabilityQueryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetCapabilitiesAsync(cancellationToken)));

app.MapGet("/v1/projects", async (IProjectQueryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListProjectsAsync(cancellationToken)));

app.MapGet("/v1/projects/{projectId}/threads", async (
    string projectId,
    IThreadQueryService service,
    CancellationToken cancellationToken) =>
    Results.Ok(await service.ListThreadsAsync(projectId, cancellationToken)));

app.MapPost("/v1/threads", async (
    CreateThreadRequest request,
    IThreadDetailQueryService service,
    CancellationToken cancellationToken) =>
    Results.Ok(await service.CreateThreadAsync(request, cancellationToken)));

app.MapGet("/v1/projects/{projectId}/threads/{threadId}", async (
    string projectId,
    string threadId,
    string? beforeMessageId,
    int? pageSize,
    IThreadDetailQueryService service,
    CancellationToken cancellationToken) =>
{
    var detail = await service.GetThreadAsync(projectId, threadId, beforeMessageId, pageSize, cancellationToken);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
});

app.MapGet("/v1/settings", async (ISettingsQueryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetSettingsAsync(cancellationToken)));

app.MapGet("/v1/projects/{projectId}/changed-files", async (
    string projectId,
    string? threadId,
    IReviewQueryService service,
    CancellationToken cancellationToken) =>
    Results.Ok(await service.ListChangedFilesAsync(projectId, threadId, cancellationToken)));

app.MapGet("/v1/projects/{projectId}/diff", async (
    string projectId,
    string filePath,
    string? threadId,
    IReviewQueryService service,
    CancellationToken cancellationToken) =>
{
    var diff = await service.GetDiffAsync(projectId, filePath, threadId, cancellationToken);
    return diff is null ? Results.NotFound() : Results.Ok(diff);
});

app.MapGet("/v1/approvals", async (
    string? threadId,
    IApprovalService service,
    CancellationToken cancellationToken) =>
    Results.Ok(await service.ListPendingApprovalsAsync(threadId, cancellationToken)));

app.MapPost("/v1/approvals/resolve", async (
    ResolveApprovalRequest request,
    IApprovalService service,
    CancellationToken cancellationToken) =>
    Results.Ok(await service.ResolveApprovalAsync(request, cancellationToken)));

app.MapPost("/v1/runs/start", async (
    StartRunRequest request,
    IRunCommandService service,
    CancellationToken cancellationToken) =>
{
    var runId = await service.StartRunAsync(request, cancellationToken);
    return Results.Accepted($"/v1/runs/{runId}", new { runId, threadId = request.ThreadId, acceptedAt = DateTimeOffset.UtcNow });
});

app.MapPost("/v1/runs/cancel", async (
    CancelRunRequest request,
    IRunCommandService service,
    CancellationToken cancellationToken) =>
{
    await service.CancelRunAsync(request, cancellationToken);
    return Results.Accepted();
});

app.MapPost("/v1/runs/retry", async (
    RetryRunRequest request,
    IRunCommandService service,
    CancellationToken cancellationToken) =>
{
    var runId = await service.RetryRunAsync(request, cancellationToken);
    return Results.Accepted($"/v1/runs/{runId}", new { runId, threadId = request.ThreadId, acceptedAt = DateTimeOffset.UtcNow });
});

app.MapPost("/v1/threads/archive", async (
    ArchiveThreadRequest request,
    IRunCommandService service,
    CancellationToken cancellationToken) =>
{
    await service.ArchiveThreadAsync(request, cancellationToken);
    return Results.Accepted();
});

app.MapGet("/v1/threads/{threadId}/events", async (
    string threadId,
    HttpContext httpContext,
    IRunEventStreamService service,
    CancellationToken cancellationToken) =>
{
    httpContext.Response.Headers.Append("Cache-Control", "no-cache");
    httpContext.Response.Headers.Append("X-Accel-Buffering", "no");
    httpContext.Response.ContentType = "text/event-stream";

    await foreach (var item in service.StreamThreadEventsAsync(threadId, cancellationToken))
    {
        var payload = JsonSerializer.Serialize(item);
        await httpContext.Response.WriteAsync($"event: run\n", cancellationToken);
        await httpContext.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }
});

app.Run();

public partial class Program;
