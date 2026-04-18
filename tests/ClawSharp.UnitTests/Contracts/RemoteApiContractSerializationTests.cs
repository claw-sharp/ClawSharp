using System.Text.Json;
using ClawSharp.Contracts.Approvals;
using ClawSharp.Contracts.Review;
using ClawSharp.Contracts.Runs;
using ClawSharp.Contracts.Threads;

namespace ClawSharp.UnitTests.Contracts;

public sealed class RemoteApiContractSerializationTests
{
    [Fact]
    public void ThreadDetail_round_trips_through_json()
    {
        var detail = new ThreadDetail(
            new ThreadSummary(
                "thread-1",
                "project-1",
                "Demo Thread",
                "Summary",
                ThreadStatus.Running,
                ThreadTarget.Cloud,
                "anthropic",
                "claude-haiku-4-5-20251001",
                2,
                DateTimeOffset.Parse("2026-04-18T10:00:00Z")),
            [
                new ThreadMessage(
                    "msg-1",
                    "thread-1",
                    "assistant",
                    "Hello",
                    DateTimeOffset.Parse("2026-04-18T10:00:01Z"))
            ],
            false,
            null);

        var json = JsonSerializer.Serialize(detail);
        var restored = JsonSerializer.Deserialize<ThreadDetail>(json);

        Assert.NotNull(restored);
        Assert.Equal(detail.Thread.Title, restored!.Thread.Title);
        Assert.Single(restored.Messages);
        Assert.Equal("assistant", restored.Messages[0].Role);
    }

    [Fact]
    public void RunEventEnvelope_round_trips_through_json()
    {
        var envelope = new RunEventEnvelope(
            "run-1",
            "thread-1",
            RunEventKind.TextDelta,
            DateTimeOffset.Parse("2026-04-18T10:00:00Z"),
            TextDelta: "partial");

        var json = JsonSerializer.Serialize(envelope);
        var restored = JsonSerializer.Deserialize<RunEventEnvelope>(json);

        Assert.NotNull(restored);
        Assert.Equal(RunEventKind.TextDelta, restored!.Kind);
        Assert.Equal("partial", restored.TextDelta);
    }

    [Fact]
    public void Approval_and_diff_contracts_round_trip_through_json()
    {
        var approval = new ApprovalSummary(
            "approval-1",
            "thread-1",
            "Review the diff",
            ApprovalDecision.Pending,
            DateTimeOffset.Parse("2026-04-18T10:00:00Z"));
        var diff = new FileDiff(
            "Program.cs",
            [
                new DiffHunk(
                    "@@ -1,1 +1,2 @@",
                    [
                        new DiffLine("context", "using System;"),
                        new DiffLine("add", "using System.Text;")
                    ])
            ]);

        var approvalJson = JsonSerializer.Serialize(approval);
        var diffJson = JsonSerializer.Serialize(diff);

        var restoredApproval = JsonSerializer.Deserialize<ApprovalSummary>(approvalJson);
        var restoredDiff = JsonSerializer.Deserialize<FileDiff>(diffJson);

        Assert.NotNull(restoredApproval);
        Assert.NotNull(restoredDiff);
        Assert.Equal(ApprovalDecision.Pending, restoredApproval!.Decision);
        Assert.Single(restoredDiff!.Hunks);
        Assert.Equal("add", restoredDiff.Hunks[0].Lines[1].Type);
    }
}
