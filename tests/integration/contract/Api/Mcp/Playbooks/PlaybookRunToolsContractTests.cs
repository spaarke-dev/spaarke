// CONTRACT — spaarke_run_playbook and spaarke_get_playbook_run (spaarkeai-mcp-bff-api-r2 task 030, spec FR-05, FR-13, FR-14, NFR-02).
// KEEP path: contract.
//
// WHAT RUNS: the tools' RunAsync over the REAL DetachedPlaybookRunner, task 007's REAL authorization and the REAL McpIdempotency, in
// PlaybookRunWorld (substitutes only at the engine, the OBO factories, the access-data boundary and the clock).

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Playbooks;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp.Playbooks;

public sealed class PlaybookRunToolsContractTests
{
    /// <summary>The largest tool result the host may return (NFR-02).</summary>
    private const int MaxResultBytes = 64 * 1024;

    private static JsonElement Body(CallToolResult result) => result.StructuredContent!.Value;

    private static Guid RunIdOf(CallToolResult result) => Guid.Parse(Body(result).GetProperty("runId").GetString()!);

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content.Single()).Text;

    // ---- start: answers at once, the run continues ---------------------------------------------------------------------------

    [Fact(DisplayName = "030 start: answers {runId, status: running, consoleLink} within 5 s while a slow run is still running; the run completes after, read through the status tool")]
    public async Task Start_AnswersAtOnce_AndTheRunCompletesAfterTheReply()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        var http = world.CallerContext();

        var clock = Stopwatch.StartNew();
        var started = await world.RunToolAsync(http, new { playbookId, documentIds = new[] { documentId }, idempotencyKey = "run-1" });
        clock.Elapsed.Should().BeLessThan(PlaybookRunLimits.AcceptBudget, "FR-14: the start never waits for the run");

        started.IsError.Should().NotBe(true, started.StructuredContent?.ToString());
        world.Gate.Task.IsCompleted.Should().BeFalse("the slow run is still held when the tool has answered");
        Body(started).GetProperty("status").GetString().Should().Be("running");
        var link = Body(started).GetProperty("consoleLink").GetString();
        link.Should().Be(new Sprk.Bff.Api.Services.Ai.Handoff.HandoffUrlBuilder(PlaybookRunWorld.EnvironmentUrl).BuildConsoleUrl(documentId));
        link.Should().StartWith(PlaybookRunWorld.EnvironmentUrl).And.Contain("sprk_spaarkeai");
        Text(started).Should().Contain("continues").And.Contain(GetPlaybookRunTool.ToolName);
        var runId = RunIdOf(started);

        var running = await world.GetToolAsync(http, runId);
        Body(running).GetProperty("status").GetString().Should().Be("running");
        Body(running).GetProperty("progress").GetInt32().Should().Be(0);

        world.Gate.SetResult();
        await world.RunFinished();

        var done = await world.GetToolAsync(http, runId);
        done.IsError.Should().NotBe(true);
        Body(done).GetProperty("runId").GetString().Should().Be(runId.ToString("D"));
        Body(done).GetProperty("status").GetString().Should().Be("completed");
        var result = Body(done).GetProperty("result");
        result.GetProperty("outcome").GetString().Should().Be("completed");
        result.GetProperty("completedNodes").GetInt32().Should().Be(2);
        result.GetProperty("playbookId").GetString().Should().Be(playbookId.ToString("D"));
        Text(done).Should().Contain("completed");
    }

    // ---- idempotency ----------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "030 retry: the same idempotencyKey and inputs answers the same runId and starts the engine once")]
    public async Task Start_SameKey_ReplaysTheSameRun()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        var http = world.CallerContext();
        var arguments = new { playbookId, documentIds = new[] { documentId }, parameters = new { timeWindowHours = "24" }, idempotencyKey = "same" };

        var first = await world.RunToolAsync(http, arguments);
        var second = await world.RunToolAsync(world.CallerContext(), arguments);

        second.IsError.Should().NotBe(true, second.StructuredContent?.ToString());
        RunIdOf(second).Should().Be(RunIdOf(first));
        Text(second).Should().Contain("already started");
        world.Engine.Verify(e => e.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<Microsoft.AspNetCore.Http.HttpContext>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact(DisplayName = "030 retry: the same idempotencyKey with different inputs is refused (mcp.idempotency_key_reused); nothing new starts")]
    public async Task Start_SameKeyDifferentInputs_IsRefused()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        var other = world.Host.ReadableDocument();

        await world.RunToolAsync(world.CallerContext(), new { playbookId, documentIds = new[] { documentId }, idempotencyKey = "same" });
        var second = await world.RunToolAsync(world.CallerContext(), new { playbookId, documentIds = new[] { other }, idempotencyKey = "same" });

        second.IsError.Should().BeTrue();
        Body(second).GetProperty("reasonCode").GetString().Should().Be(McpIdempotency.KeyReusedReasonCode);
        world.Engine.Verify(e => e.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<Microsoft.AspNetCore.Http.HttpContext>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    // ---- status: the durable record and the interrupted mapping ---------------------------------------------------------------

    [Fact(DisplayName = "030 status: a run this process does not hold is read from the engine's status (the durable record) for its starter")]
    public async Task Status_RunNotInMemory_IsReadFromTheDurableRecord()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var runId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        world.StoredRuns[runId] = new PlaybookRunStatus
        {
            RunId = runId,
            PlaybookId = playbookId,
            State = PlaybookRunState.Completed,
            StartedAt = world.Time.GetUtcNow().AddMinutes(-20),
            CompletedAt = world.Time.GetUtcNow().AddMinutes(-15),
            StartedByOid = PlaybookRunWorld.CallerOid,
            Metrics = new PlaybookRunMetrics { TotalNodes = 3, CompletedNodes = 2, SkippedNodes = 1 },
        };

        var status = await world.GetToolAsync(world.CallerContext(), runId);

        status.IsError.Should().NotBe(true, status.StructuredContent?.ToString());
        Body(status).GetProperty("status").GetString().Should().Be("completed");
        var result = Body(status).GetProperty("result");
        result.GetProperty("playbookId").GetString().Should().Be(playbookId.ToString("D"));
        result.GetProperty("completedNodes").GetInt32().Should().Be(2);
        result.GetProperty("skippedNodes").GetInt32().Should().Be(1);
        result.GetProperty("completedAt").GetString().Should().Be("2026-11-26T08:45:00Z");
    }

    [Theory(DisplayName = "030 status: a durable record still Running past the cap + margin reads failed/interrupted (playbook.run-interrupted); before that, running")]
    [InlineData(59, "running", null)]
    [InlineData(61, "failed", PlaybookRunReasonCodes.Interrupted)]
    public async Task Status_StaleRunningRecord_IsInterrupted(int minutesAgo, string expectedStatus, string? expectedReason)
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var runId = Guid.NewGuid();
        world.StoredRuns[runId] = new PlaybookRunStatus
        {
            RunId = runId,
            PlaybookId = Guid.NewGuid(),
            State = PlaybookRunState.Running,
            StartedAt = world.Time.GetUtcNow().AddMinutes(-minutesAgo),
            StartedByOid = PlaybookRunWorld.CallerOid,
        };

        var status = await world.GetToolAsync(world.CallerContext(), runId);

        status.IsError.Should().NotBe(true);
        Body(status).GetProperty("status").GetString().Should().Be(expectedStatus);
        var result = Body(status).GetProperty("result");
        if (expectedReason is null)
        {
            result.GetProperty("outcome").GetString().Should().Be("running");
        }
        else
        {
            result.GetProperty("outcome").GetString().Should().Be("interrupted");
            result.GetProperty("reasonCode").GetString().Should().Be(expectedReason);
            Text(status).Should().Contain("interrupted");
        }
    }

    [Fact(DisplayName = "030 status: a live run in this process is never mapped to interrupted (the mapping is for durable records only)")]
    public void Status_LiveRun_IsNeverInterrupted()
    {
        var old = new PlaybookRunView(Guid.NewGuid(), Guid.NewGuid(), PlaybookRunPhase.Running, 10, 1, 0, 0, null,
            new DateTimeOffset(2026, 11, 26, 7, 0, 0, TimeSpan.Zero), null, FromDurableRecord: false);

        GetPlaybookRunTool.IsInterrupted(old, new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero)).Should().BeFalse();
        GetPlaybookRunTool.IsInterrupted(old with { FromDurableRecord = true }, new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero)).Should().BeTrue();
    }

    // ---- size (NFR-02) and model-visible shape (ADR-015) ----------------------------------------------------------------------

    [Fact(DisplayName = "030 size: the worst-case status and start results are under 64 KiB, the summary at most 1,000 characters, one line of text")]
    public void WorstCaseResults_AreBounded()
    {
        var worst = new PlaybookRunView(
            Guid.NewGuid(), Guid.NewGuid(), PlaybookRunPhase.Failed, 99, int.MaxValue, int.MaxValue, int.MaxValue,
            new string('x', 50_000) + "\nsecond line", DateTimeOffset.MaxValue, DateTimeOffset.MaxValue, FromDurableRecord: false);

        var status = GetPlaybookRunTool.StatusResult(worst, DateTimeOffset.UtcNow);
        var start = RunPlaybookTool.StartedResult(
            worst with { Phase = PlaybookRunPhase.Running }, "https://spaarke-test.crm.dynamics.com/" + new string('a', 2_000), replayed: true, DateTimeOffset.UtcNow);

        foreach (var result in new[] { status, start })
        {
            Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions)).Should().BeLessThan(MaxResultBytes);
            Text(result).Should().NotContainAny("\r", "\n");
        }

        var body = Body(status).GetProperty("result");
        body.GetProperty("summary").GetString()!.Length.Should().BeLessThanOrEqualTo(GetPlaybookRunTool.SummaryMaxChars);
        body.GetProperty("reasonCode").GetString()!.Length.Should().BeLessThanOrEqualTo(64);
        body.GetProperty("reasonCode").GetString().Should().NotContain("\n");
    }

    [Fact(DisplayName = "030 descriptions: both name a JPS playbook, say use for / not for, fit 1,024 characters; the start's hints are a reversible, idempotent write")]
    public void Descriptions_AndHints()
    {
        foreach (var description in new[] { RunPlaybookTool.Description, GetPlaybookRunTool.Description })
        {
            description.Length.Should().BeLessThanOrEqualTo(1_024);
            description.Should().Contain("JPS playbook").And.Contain("Use for").And.Contain("Not for");
        }

        var run = new RunPlaybookTool().Create(new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider()).ProtocolTool;
        run.Annotations!.ReadOnlyHint.Should().BeFalse();
        run.Annotations.DestructiveHint.Should().BeFalse();
        run.Annotations.IdempotentHint.Should().BeTrue();
        McpToolAuthorization.ModelCallableWrites.Should().Contain(RunPlaybookTool.ToolName);

        var get = new GetPlaybookRunTool().Create(new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider()).ProtocolTool;
        get.Annotations!.ReadOnlyHint.Should().BeTrue();
    }
}
