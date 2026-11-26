// AUTH — the detached caller run (spaarkeai-mcp-bff-api-r2 task 030; owner decision D-030-run-model; ADR-052 amendment 2026-11-26).
// KEEP path: auth (the run acts as the caller after the caller's request has answered: OBO, the caller's token, fail-closed).
//
// WHAT RUNS: the REAL DetachedPlaybookRunner and task 007's REAL authorization over PlaybookRunWorld's boundary substitutes (a scripted
// engine, the OBO factories, FakeTimeProvider, a test host lifetime). Each test names the amendment condition it proves.

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Identity.Client;
using Moq;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PlaybookRuns;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Api.Mcp.Playbooks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Auth.Ai;

public sealed class DetachedPlaybookRunnerTests
{
    // ---- conditions 1, 4, 6: the detached context, the untouched request accessor, the authorized instances -------------------

    [Fact(DisplayName = "030 detach: the original request's IHttpContextAccessor still holds the request after the run detached, set and cleared its own")]
    public async Task Start_LeavesTheRequestsAccessorUnchanged()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        var http = world.CallerContext();
        world.Accessor.HttpContext = http;

        var started = await world.Runs.StartAsCallerAsync(http, playbookId, [documentId], null, CancellationToken.None);
        started.Outcome.Should().Be(PlaybookRunStartOutcome.Started);
        world.Gate.SetResult();
        await world.RunFinished();

        // ReferenceEquals: FluentAssertions cannot format an HttpContext in a failure message.
        var held = world.Accessor.HttpContext;
        ReferenceEquals(held, http).Should().BeTrue(
            $"the detached flow's accessor write and its clear-on-exit never reach the request's own holder (#3224); it holds {(held is null ? "null" : "another context")}");
        ReferenceEquals(world.Calls.Single().AccessorDuringRun, world.Calls.Single().Context).Should().BeTrue(
            "inside the run the accessor is the detached context, not the request's");
    }

    [Fact(DisplayName = "030 detach: the engine runs on a new context carrying the caller's principal, bearer header and trace id, in a new scope")]
    public async Task Start_RunsOnADetachedContextActingAsTheCaller()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        var http = world.CallerContext(traceId: "trace-detach-1");

        await world.Runs.StartAsCallerAsync(http, playbookId, [documentId], null, CancellationToken.None);

        var call = world.Calls.Single();
        ReferenceEquals(call.Context, http).Should().BeFalse("the run has its own context");
        ReferenceEquals(call.Context.RequestServices, http.RequestServices).Should().BeFalse("a new DI scope that outlives the request");
        ReferenceEquals(call.User, http.User).Should().BeTrue("the run acts as the caller's principal");
        call.Authorization.Should().Be(PlaybookRunWorld.Bearer);
        call.TraceIdentifier.Should().Be("trace-detach-1");
        call.Token.Should().NotBe(http.RequestAborted, "the run is not ended by the request ending");
    }

    [Fact(DisplayName = "030 authorized first: the run gets the exact documentIds and parameters instances 007 decided on, 007's RunUserId and StartedBy=Person")]
    public async Task Start_HandsTheRunTheAuthorizedInstances()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        var documents = new[] { documentId };
        var parameters = new Dictionary<string, string> { ["timeWindowHours"] = "24" };

        await world.Runs.StartAsCallerAsync(world.CallerContext(), playbookId, documents, parameters, CancellationToken.None);

        var request = world.Calls.Single().Request;
        request.PlaybookId.Should().Be(playbookId);
        request.DocumentIds.Should().BeSameAs(documents);
        request.Parameters.Should().BeSameAs(parameters);
        request.RunUserId.Should().Be(world.Host.Probe.SystemUserId);
        request.StartedBy.Should().Be(PlaybookRunStartedBy.Person);
    }

    [Fact(DisplayName = "030 authorized first: a 007 refusal (no document ids) refuses the start; no token is fetched and nothing runs")]
    public async Task Start_RefusedByTask007_StartsNothing()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, _) = world.AllowedRun();

        var started = await world.Runs.StartAsCallerAsync(world.CallerContext(), playbookId, [], null, CancellationToken.None);

        started.Outcome.Should().Be(PlaybookRunStartOutcome.Refused);
        started.Authorization.Should().BeSameAs(PlaybookRunAuthorization.DocumentIdsRequired);
        world.VerifyNothingStarted();
        world.Graph.Verify(g => g.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    // ---- condition 5: bounded --------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "030 bounded: an engine that never accepts the run within the 5 s budget is NotAccepted, and its run is cancelled")]
    public async Task Start_EngineNeverAccepts_IsNotAccepted_AndTheRunIsStopped()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Script = PlaybookRunWorld.NeverAccepts(cancelled);

        var start = world.Runs.StartAsCallerAsync(world.CallerContext(), playbookId, [documentId], null, CancellationToken.None);
        await world.Time.TimerArmed(PlaybookRunLimits.AcceptBudget);
        start.IsCompleted.Should().BeFalse("the start waits out the accept budget on the fake clock");
        world.Time.Advance(PlaybookRunLimits.AcceptBudget);

        (await start.WaitAsync(TimeSpan.FromSeconds(15))).Outcome.Should().Be(PlaybookRunStartOutcome.NotAccepted);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await world.RunFinished();
    }

    [Theory(DisplayName = "030 bounded: the 50-minute cap ends the run as failed with playbook.run-time-limit (engine throws, or ends with RunCancelled)")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_ReachingTheCap_FailsWithTimeLimit(bool engineEndsWithEvent)
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        world.Script = PlaybookRunWorld.RunsUntilCancelled(engineEndsWithEvent);

        var started = await world.Runs.StartAsCallerAsync(world.CallerContext(), playbookId, [documentId], null, CancellationToken.None);
        started.Run!.Phase.Should().Be(PlaybookRunPhase.Running);

        world.Time.Advance(PlaybookRunLimits.MaxRunDuration - TimeSpan.FromSeconds(1));
        (await world.Runs.ReadAsCallerAsync(world.CallerContext(), started.Run.RunId, CancellationToken.None))!.Phase
            .Should().Be(PlaybookRunPhase.Running, "the cap has not been reached yet");

        world.Time.Advance(TimeSpan.FromSeconds(1));
        var ended = await world.ReadFinishedAsync(started.Run.RunId);

        ended.Phase.Should().Be(PlaybookRunPhase.Failed);
        ended.ReasonCode.Should().Be(PlaybookRunReasonCodes.TimeLimit);
        world.Calls.Single().Token.IsCancellationRequested.Should().BeTrue();
    }

    [Theory(DisplayName = "030 bounded: ApplicationStopping ends the run as cancelled with playbook.run-host-stopping")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_HostStopping_IsCancelled(bool engineEndsWithEvent)
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        world.Script = PlaybookRunWorld.RunsUntilCancelled(engineEndsWithEvent);

        var started = await world.Runs.StartAsCallerAsync(world.CallerContext(), playbookId, [documentId], null, CancellationToken.None);
        world.Lifetime.StopApplication();
        var ended = await world.ReadFinishedAsync(started.Run!.RunId);

        ended.Phase.Should().Be(PlaybookRunPhase.Cancelled);
        ended.ReasonCode.Should().Be(PlaybookRunReasonCodes.HostStopping);
    }

    [Fact(DisplayName = "030 bounded: a caller may have 3 runs running at once; a 4th is refused (playbook.run-limit) before 007 or any token; once one finishes a new start is allowed")]
    public async Task Start_BeyondThePerCallerCap_IsRefused_UntilARunFinishes()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        var gates = Enumerable.Range(0, 5).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var next = -1;
        world.Script = (request, ct) => PlaybookRunWorld.GatedRunOn(gates[Interlocked.Increment(ref next)], request, ct);

        var running = new List<Guid>();
        for (var i = 0; i < PlaybookRunLimits.MaxConcurrentRunsPerCaller; i++)
        {
            var started = await world.Runs.StartAsCallerAsync(world.CallerContext(), playbookId, [documentId], null, CancellationToken.None);
            started.Outcome.Should().Be(PlaybookRunStartOutcome.Started, $"run {i + 1} of {PlaybookRunLimits.MaxConcurrentRunsPerCaller} is within the cap");
            running.Add(started.Run!.RunId);
        }

        world.Graph.Invocations.Clear();
        var refused = await world.Runs.StartAsCallerAsync(world.CallerContext(), playbookId, [documentId], null, CancellationToken.None);

        refused.Outcome.Should().Be(PlaybookRunStartOutcome.RunLimit);
        refused.Authorization.Should().BeNull("the cap is checked before 007's authorization");
        refused.Run.Should().BeNull();
        world.Calls.Should().HaveCount(PlaybookRunLimits.MaxConcurrentRunsPerCaller, "the refused start ran nothing");
        world.Graph.Verify(g => g.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Never());

        var otherCaller = await world.Runs.StartAsCallerAsync(
            world.CallerContext(PlaybookRunWorld.OtherOid), playbookId, [documentId], null, CancellationToken.None);
        otherCaller.Outcome.Should().NotBe(PlaybookRunStartOutcome.RunLimit, "the cap is per caller, not per instance");

        gates[0].SetResult();
        await world.RunFinished();
        (await world.Runs.ReadAsCallerAsync(world.CallerContext(), running[0], CancellationToken.None))!.Phase
            .Should().Be(PlaybookRunPhase.Completed);

        var afterOneFinished = await world.Runs.StartAsCallerAsync(world.CallerContext(), playbookId, [documentId], null, CancellationToken.None);
        afterOneFinished.Outcome.Should().Be(PlaybookRunStartOutcome.Started, "a finished run frees its place");
    }

    [Fact(DisplayName = "030 bounded: the tool answers a start beyond the per-caller cap as isError playbook.run-limit, status 429, and keeps no retry key")]
    public async Task Tool_BeyondThePerCallerCap_Is429RunLimit()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        for (var i = 0; i < PlaybookRunLimits.MaxConcurrentRunsPerCaller; i++)
        {
            (await world.Runs.StartAsCallerAsync(world.CallerContext(), playbookId, [documentId], null, CancellationToken.None))
                .Outcome.Should().Be(PlaybookRunStartOutcome.Started);
        }

        var refused = await world.RunToolAsync(world.CallerContext(), new { playbookId, documentIds = new[] { documentId }, idempotencyKey = "over-cap" });

        refused.IsError.Should().BeTrue();
        var body = refused.StructuredContent!.Value;
        body.GetProperty("reasonCode").GetString().Should().Be(PlaybookRunReasonCodes.RunLimit);
        body.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status429TooManyRequests);
        ((ModelContextProtocol.Protocol.TextContentBlock)refused.Content.Single()).Text.Should().Contain("Wait for a run to finish");

        // The refusal kept no entry: once a run finishes, the same key starts a run.
        world.Gate.SetResult();
        await world.RunFinished(count: PlaybookRunLimits.MaxConcurrentRunsPerCaller);
        var retried = await world.RunToolAsync(world.CallerContext(), new { playbookId, documentIds = new[] { documentId }, idempotencyKey = "over-cap" });
        retried.IsError.Should().NotBe(true, retried.StructuredContent?.ToString());
    }

    public enum PrewarmFailure
    {
        GraphOboThrows,
        DataverseWhoAmIRefused,
        DataverseOboThrows,
    }

    [Theory(DisplayName = "030 bounded: a Graph or Dataverse OBO pre-warm failure refuses the start (AuthUnavailable); nothing runs")]
    [InlineData(PrewarmFailure.GraphOboThrows)]
    [InlineData(PrewarmFailure.DataverseWhoAmIRefused)]
    [InlineData(PrewarmFailure.DataverseOboThrows)]
    public async Task Start_PrewarmFails_IsAuthUnavailable(PrewarmFailure failure)
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        switch (failure)
        {
            case PrewarmFailure.GraphOboThrows:
                world.Graph.Setup(g => g.ForUserAsync(It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new MsalUiRequiredException("invalid_grant", "AADSTS50013 " + PlaybookRunWorld.TokenSecret));
                break;
            case PrewarmFailure.DataverseWhoAmIRefused:
                world.DataverseUser.Setup(d => d.GetAsync("WhoAmI", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(DataverseUserResponse.Fail(401, DataverseUserClientErrorCodes.OboExchangeFailed, "refused"));
                break;
            default:
                world.DataverseUser.Setup(d => d.GetAsync("WhoAmI", It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new HttpRequestException("token " + PlaybookRunWorld.TokenSecret));
                break;
        }

        var started = await world.Runs.StartAsCallerAsync(world.CallerContext(), playbookId, [documentId], null, CancellationToken.None);

        started.Outcome.Should().Be(PlaybookRunStartOutcome.AuthUnavailable);
        world.VerifyNothingStarted();
        world.VerifyNoAppOnlyRun();
        string.Join("\n", world.Logs.Entries).Should().NotContain(PlaybookRunWorld.TokenSecret);
    }

    [Theory(DisplayName = "030 fail closed: an authentication failure mid-run ends it failed (playbook.run-auth-expired); nothing is retried, never app-only")]
    [InlineData("ui-required")]
    [InlineData("expired-assertion")]
    public async Task Run_AuthFailureMidRun_FailsClosed(string kind)
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        Exception failure = kind == "ui-required"
            ? new MsalUiRequiredException("invalid_grant", "AADSTS70043 " + PlaybookRunWorld.TokenSecret)
            : new InvalidOperationException("node failed", new MsalServiceException("invalid_request", "AADSTS500133: Assertion is not within its valid time range " + PlaybookRunWorld.TokenSecret));
        world.Script = world.ThrowsAfterStart(failure);

        var started = await world.Runs.StartAsCallerAsync(world.CallerContext(), playbookId, [documentId], null, CancellationToken.None);
        world.Gate.SetResult();
        var ended = await world.ReadFinishedAsync(started.Run!.RunId);

        ended.Phase.Should().Be(PlaybookRunPhase.Failed);
        ended.ReasonCode.Should().Be(PlaybookRunReasonCodes.AuthExpired);
        world.Engine.Verify(e => e.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Once());
        world.VerifyNoAppOnlyRun();
        string.Join("\n", world.Logs.Entries).Should().NotContain(PlaybookRunWorld.TokenSecret, "only the exception type is logged");
    }

    [Fact(DisplayName = "030 fail closed: the engine's own RunFailed ends the run failed with its code; nothing is retried")]
    public async Task Run_EngineReportsFailure_IsFailedWithTheEnginesCode()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        world.Script = (request, _) => new[]
        {
            PlaybookStreamEvent.RunStarted(Guid.Parse("00000000-0030-4030-8030-00000000f001"), request.PlaybookId, 1),
            PlaybookStreamEvent.BuildRunFailedEvent(Guid.Parse("00000000-0030-4030-8030-00000000f001"), request.PlaybookId, "trace"),
        }.ToAsyncEnumerable();

        var started = await world.Runs.StartAsCallerAsync(world.CallerContext(), playbookId, [documentId], null, CancellationToken.None);
        var ended = await world.ReadFinishedAsync(started.Run!.RunId);

        ended.Phase.Should().Be(PlaybookRunPhase.Failed);
        ended.ReasonCode.Should().Be(PlaybookStreamEvent.RunFailedErrorCode);
        world.VerifyNoAppOnlyRun();
    }

    // ---- condition 3: memory only ----------------------------------------------------------------------------------------------

    [Fact(DisplayName = "030 memory only: the caller's token appears in no log line, cache entry, run view or tool result, across a run and a failed run")]
    public async Task Token_NeverLeavesMemory()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        var http = world.CallerContext();

        var first = await world.RunToolAsync(http, new { playbookId, documentIds = new[] { documentId }, idempotencyKey = "k-1" });
        world.Gate.SetResult();
        await world.RunFinished();
        var runId = Guid.Parse(first.StructuredContent!.Value.GetProperty("runId").GetString()!);
        var status = await world.GetToolAsync(http, runId);
        var view = await world.Runs.ReadAsCallerAsync(http, runId, CancellationToken.None);

        world.Script = world.ThrowsAfterStart(new MsalUiRequiredException("invalid_grant", "expired " + PlaybookRunWorld.Bearer));
        var second = await world.RunToolAsync(http, new { playbookId, documentIds = new[] { documentId }, idempotencyKey = "k-2" });
        await world.RunFinished(count: 2);
        var secondStatus = await world.GetToolAsync(http, Guid.Parse(second.StructuredContent!.Value.GetProperty("runId").GetString()!));

        var everything = string.Join("\n", world.Logs.Entries.Concat(world.Cache.Writes).Concat(new[]
        {
            Serialize(first), Serialize(status), Serialize(second), Serialize(secondStatus), System.Text.Json.JsonSerializer.Serialize(view),
        }));
        world.Logs.Entries.Should().Contain(e => e.Contains("Detached playbook run finished", StringComparison.Ordinal), "the capture saw the run's logs");
        world.Cache.Writes.Should().NotBeEmpty("the capture saw the retry-key writes");
        everything.Should().NotContain(PlaybookRunWorld.TokenSecret);
    }

    [Fact(DisplayName = "030 IsAuthenticationFailure: MSAL UI-required, a claims challenge, invalid_grant and AADSTS500133 anywhere in the chain; nothing else")]
    public void IsAuthenticationFailure_Classifies()
    {
        DetachedPlaybookRunner.IsAuthenticationFailure(new MsalUiRequiredException("invalid_grant", "x")).Should().BeTrue();
        DetachedPlaybookRunner.IsAuthenticationFailure(new AggregateException(new MsalServiceException("invalid_grant", "x"))).Should().BeTrue();
        DetachedPlaybookRunner.IsAuthenticationFailure(new MsalServiceException("invalid_request", "AADSTS500133: expired")).Should().BeTrue();
        DetachedPlaybookRunner.IsAuthenticationFailure(new MsalServiceException("temporarily_unavailable", "busy")).Should().BeFalse();
        DetachedPlaybookRunner.IsAuthenticationFailure(new HttpRequestException("500")).Should().BeFalse();
    }

    private static string Serialize(ModelContextProtocol.Protocol.CallToolResult result) =>
        System.Text.Json.JsonSerializer.Serialize(result, ModelContextProtocol.McpJsonUtilities.DefaultOptions);
}
