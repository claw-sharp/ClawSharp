using ClawSharp.Api.Capabilities;
using ClawSharp.Api.Health;
using ClawSharp.Api.Projects;
using ClawSharp.Api.Review;
using ClawSharp.Api.Runs;
using ClawSharp.Api.Settings;
using ClawSharp.Api.Threads;
using ClawSharp.Application.Capabilities;
using ClawSharp.Application.Health;
using ClawSharp.Application.Projects;
using ClawSharp.Application.Review;
using ClawSharp.Application.Runs;
using ClawSharp.Application.Settings;
using ClawSharp.Application.Threads;
using ClawSharp.Contracts.Runs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IHealthQueryService, DefaultHealthQueryService>();
builder.Services.AddSingleton<ICapabilityQueryService, DefaultCapabilityQueryService>();
builder.Services.AddSingleton<IProjectQueryService, EmptyProjectQueryService>();
builder.Services.AddSingleton<IThreadQueryService, EmptyThreadQueryService>();
builder.Services.AddSingleton<ISettingsQueryService, DefaultSettingsQueryService>();
builder.Services.AddSingleton<IReviewQueryService, EmptyReviewQueryService>();
builder.Services.AddSingleton<IRunCommandService, NoOpRunCommandService>();

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

app.MapGet("/v1/settings", async (ISettingsQueryService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetSettingsAsync(cancellationToken)));

app.MapGet("/v1/projects/{projectId}/changed-files", async (
    string projectId,
    string? threadId,
    IReviewQueryService service,
    CancellationToken cancellationToken) =>
    Results.Ok(await service.ListChangedFilesAsync(projectId, threadId, cancellationToken)));

app.MapPost("/v1/runs/start", async (
    StartRunRequest request,
    IRunCommandService service,
    CancellationToken cancellationToken) =>
{
    await service.StartRunAsync(request, cancellationToken);
    return Results.Accepted();
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
    await service.RetryRunAsync(request, cancellationToken);
    return Results.Accepted();
});

app.MapPost("/v1/threads/archive", async (
    ArchiveThreadRequest request,
    IRunCommandService service,
    CancellationToken cancellationToken) =>
{
    await service.ArchiveThreadAsync(request, cancellationToken);
    return Results.Accepted();
});

app.Run();
