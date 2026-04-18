using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ClawSharp.Api.Tests;

public sealed class ApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiSmokeTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Projects_endpoint_returns_demo_project()
    {
        var response = await _client.GetFromJsonAsync<List<ProjectResponse>>("/v1/projects");

        Assert.NotNull(response);
        Assert.Contains(response!, project => project.Id == "project-demo");
    }

    [Fact]
    public async Task Thread_detail_endpoint_returns_demo_messages()
    {
        var response = await _client.GetFromJsonAsync<ThreadDetailResponse>("/v1/projects/project-demo/threads/thread-demo");

        Assert.NotNull(response);
        Assert.Equal("thread-demo", response!.Thread.Id);
        Assert.NotEmpty(response.Messages);
    }

    [Fact]
    public async Task Approvals_can_be_listed_and_resolved()
    {
        var approvals = await _client.GetFromJsonAsync<List<ApprovalResponse>>("/v1/approvals?threadId=thread-demo");
        Assert.NotNull(approvals);
        var approval = Assert.Single(approvals!);

        var resolveResponse = await _client.PostAsJsonAsync("/v1/approvals/resolve", new
        {
            approvalId = approval.Id,
            decision = "Approved"
        });

        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);
    }

    private sealed record ProjectResponse(string Id, string Name);

    private sealed record ApprovalResponse(string Id, string Decision);

    private sealed record ThreadDetailResponse(ThreadSummaryResponse Thread, IReadOnlyList<MessageResponse> Messages);

    private sealed record ThreadSummaryResponse(string Id, string Title);

    private sealed record MessageResponse(string Id, string Role, string Content);
}
