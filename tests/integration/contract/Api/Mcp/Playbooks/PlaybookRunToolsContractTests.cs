// CONTRACT — spaarke_run_playbook and spaarke_get_playbook_run (spaarkeai-mcp-bff-api-r2 task 030, spec FR-05, FR-13, FR-14, NFR-02).
// KEEP path: contract.
//
// WHAT RUNS: the tools' RunAsync over the REAL DetachedPlaybookRunner, task 007's REAL authorization and the REAL McpIdempotency, in
// PlaybookRunWorld (substitutes only at the engine, the OBO factories, the access-data boundary and the clock).

using System.Text;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
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

    [Fact(DisplayName = "030 start: answers {runId, status: running, consoleLink} while a slow run is still running; the run completes after, read through the status tool")]
    public async Task Start_AnswersAtOnce_AndTheRunCompletesAfterTheReply()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        var http = world.CallerContext();

        var started = await world.RunToolAsync(http, new { playbookId, documentIds = new[] { documentId }, idempotencyKey = "run-1" });

        // FR-14 (the start never waits for the run): the run is held by a gate that only this test opens, and is still held now.
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

    // ---- task 037a (D-037-engine): the results analysis -----------------------------------------------------------------------------

    [Theory(DisplayName = "037a refusal: a refused analysis create answers isError with the create's reasonCode and status; nothing runs, no retry key is kept")]
    [InlineData(AnalysisStartOutcome.Refused, "insufficient_rights", 403, "insufficient_rights")]
    [InlineData(AnalysisStartOutcome.Refused, "record_owner.unresolved", 409, "record_owner.unresolved")]
    [InlineData(AnalysisStartOutcome.Failed, AnalysisStartReasonCodes.CreateFailed, 500, AnalysisStartReasonCodes.CreateFailed)]
    [InlineData(AnalysisStartOutcome.Failed, null, 500, AnalysisStartReasonCodes.CreateFailed)]
    public async Task Start_AnalysisRefused_IsErrorWithTheCreatesCode(AnalysisStartOutcome outcome, string? reasonCode, int status, string expectedCode)
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        world.Analyses
            .Setup(a => a.StartAsCallerAsync(It.IsAny<HttpContext?>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnalysisStartResult.Failure(outcome, reasonCode, status, null));
        var arguments = new { playbookId, documentIds = new[] { documentId }, idempotencyKey = "refused-037a" };

        var refused = await world.RunToolAsync(world.CallerContext(), arguments);

        refused.IsError.Should().BeTrue();
        Body(refused).GetProperty("reasonCode").GetString().Should().Be(expectedCode);
        Body(refused).GetProperty("status").GetInt32().Should().Be(status);
        Text(refused).Should().Contain("JPS playbook run was not started");
        world.VerifyNothingStarted();

        // No retry key was kept: once the analysis can be created, the same key starts the run.
        world.Analyses
            .Setup(a => a.StartAsCallerAsync(It.IsAny<HttpContext?>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnalysisStartResult.Created(PlaybookRunWorld.AnalysisId, 0));
        var retried = await world.RunToolAsync(world.CallerContext(), arguments);
        retried.IsError.Should().NotBe(true, retried.StructuredContent?.ToString());
        world.Calls.Single().Request.AnalysisId.Should().Be(PlaybookRunWorld.AnalysisId);
    }

    [Fact(DisplayName = "037a security: no tool argument sets the run's AnalysisId; an `analysisId` argument is refused (mcp.invalid_argument) and is not in the schema")]
    public async Task Start_AnalysisIdArgument_IsRefused()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();

        var refused = await world.RunToolAsync(world.CallerContext(), new
        {
            playbookId,
            documentIds = new[] { documentId },
            idempotencyKey = "aid-037a",
            analysisId = Guid.NewGuid(),
        });

        refused.IsError.Should().BeTrue();
        Body(refused).GetProperty("reasonCode").GetString().Should().Be(DocumentToolSupport.InvalidArgumentReasonCode);
        world.VerifyNothingStarted();
        world.Analyses.Verify(
            a => a.StartAsCallerAsync(It.IsAny<HttpContext?>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never());
        var tool = new RunPlaybookTool().Create(new ServiceCollection().BuildServiceProvider()).ProtocolTool;
        tool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("analysis", StringComparison.OrdinalIgnoreCase));
    }

    // ---- task 038: the optional `instructions` argument (owner decision Q18) ------------------------------------------------------

    /// <summary>A marker inside the instructions: it must never reach a log, a cache entry, a tool result or a stored record (NFR-05, ADR-015).</summary>
    private const string InstructionsSecret = "FOCUS-SECRET-038-indemnity";

    private static string FocusText => "  Focus on indemnity and termination. " + InstructionsSecret + "  ";

    [Fact(DisplayName = "038 instructions: the text (trimmed) reaches the engine's PlaybookRunRequest.UserContext; the rest of the request is unchanged")]
    public async Task Start_WithInstructions_RunsWithThemAsUserContext()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();

        var started = await world.RunToolAsync(
            world.CallerContext(),
            new { playbookId, documentIds = new[] { documentId }, instructions = FocusText, idempotencyKey = "focus-1" });

        started.IsError.Should().NotBe(true, started.StructuredContent?.ToString());
        var request = world.Calls.Single().Request;
        request.UserContext.Should().Be("Focus on indemnity and termination. " + InstructionsSecret);
        request.PlaybookId.Should().Be(playbookId);
        request.DocumentIds.Should().Equal(documentId);
        request.StartedBy.Should().Be(PlaybookRunStartedBy.Person);
    }

    [Theory(DisplayName = "038 instructions: absent, null, empty or blank leaves UserContext unset, exactly today's request")]
    [InlineData("absent")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("blank")]
    public async Task Start_WithoutInstructions_LeavesUserContextUnset(string shape)
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        object arguments = shape switch
        {
            "absent" => new { playbookId, documentIds = new[] { documentId }, idempotencyKey = "no-focus" },
            "null" => new { playbookId, documentIds = new[] { documentId }, instructions = (string?)null, idempotencyKey = "no-focus" },
            "empty" => new { playbookId, documentIds = new[] { documentId }, instructions = string.Empty, idempotencyKey = "no-focus" },
            _ => new { playbookId, documentIds = new[] { documentId }, instructions = " \t\r\n ", idempotencyKey = "no-focus" },
        };

        var started = await world.RunToolAsync(world.CallerContext(), arguments);

        started.IsError.Should().NotBe(true, started.StructuredContent?.ToString());
        var request = world.Calls.Single().Request;
        request.UserContext.Should().BeNull();
        request.Parameters.Should().BeNull();
        request.DocumentIds.Should().Equal(documentId);
    }

    [Theory(DisplayName = "038 instructions: 2,000 characters are accepted; 2,001 and non-text are refused (mcp.invalid_argument), never cut; no run starts, and the refusal does not echo the text")]
    [InlineData(2_000, true)]
    [InlineData(2_001, false)]
    public async Task Start_InstructionsLength_IsCappedNotCut(int length, bool accepted)
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        var text = "q" + InstructionsSecret.PadRight(length - 1, 'x');

        var result = await world.RunToolAsync(
            world.CallerContext(), new { playbookId, documentIds = new[] { documentId }, instructions = text, idempotencyKey = $"len-{length}" });

        if (accepted)
        {
            result.IsError.Should().NotBe(true, result.StructuredContent?.ToString());
            world.Calls.Single().Request.UserContext.Should().Be(text, "the text reaches the engine whole, never cut");
        }
        else
        {
            result.IsError.Should().BeTrue();
            Body(result).GetProperty("reasonCode").GetString().Should().Be(DocumentToolSupport.InvalidArgumentReasonCode);
            Text(result).Should().Contain("2000").And.NotContain(InstructionsSecret);
            Body(result).ToString().Should().NotContain(InstructionsSecret);
            world.VerifyNothingStarted();
        }
    }

    [Fact(DisplayName = "038 instructions: a non-text value is refused (mcp.invalid_argument); no run starts")]
    public async Task Start_NonTextInstructions_AreRefused()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();

        var result = await world.RunToolAsync(
            world.CallerContext(), new { playbookId, documentIds = new[] { documentId }, instructions = new[] { "a" }, idempotencyKey = "non-text" });

        result.IsError.Should().BeTrue();
        Body(result).GetProperty("reasonCode").GetString().Should().Be(DocumentToolSupport.InvalidArgumentReasonCode);
        world.VerifyNothingStarted();
    }

    [Fact(DisplayName = "038 retry: the same key with different instructions is refused (mcp.idempotency_key_reused) and starts nothing; the same instructions replay the run")]
    public async Task Start_SameKey_InstructionsAreAPartOfTheFingerprint()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        var documents = new[] { documentId };

        var first = await world.RunToolAsync(world.CallerContext(), new { playbookId, documentIds = documents, instructions = "focus on indemnity", idempotencyKey = "same" });
        var changed = await world.RunToolAsync(world.CallerContext(), new { playbookId, documentIds = documents, instructions = "focus on termination", idempotencyKey = "same" });
        var dropped = await world.RunToolAsync(world.CallerContext(), new { playbookId, documentIds = documents, idempotencyKey = "same" });
        var repeated = await world.RunToolAsync(world.CallerContext(), new { playbookId, documentIds = documents, instructions = "  focus on indemnity ", idempotencyKey = "same" });

        first.IsError.Should().NotBe(true, first.StructuredContent?.ToString());
        foreach (var refused in new[] { changed, dropped })
        {
            refused.IsError.Should().BeTrue();
            Body(refused).GetProperty("reasonCode").GetString().Should().Be(McpIdempotency.KeyReusedReasonCode);
        }

        repeated.IsError.Should().NotBe(true, repeated.StructuredContent?.ToString());
        RunIdOf(repeated).Should().Be(RunIdOf(first), "the same instructions (trimmed) are the same input");
        world.Engine.Verify(e => e.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact(DisplayName = "038 retry: a call without instructions keeps the fingerprint it had before the argument existed (a retry across a deploy still replays)")]
    public void Fingerprint_WithoutInstructions_IsUnchanged()
    {
        var playbookId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var parameters = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" };

        var now = RunPlaybookTool.FingerprintOf(new RunPlaybookTool.RunInput(playbookId, [documentId], parameters, null, "k"));

        // The fingerprint task 030 computed: the playbook, the documents and the parameters by name, nothing else.
        var before = McpIdempotency.FingerprintOf(playbookId.ToString("D"), documentId.ToString("D"), "a=1\nb=2");
        now.Should().Be(before);
        RunPlaybookTool.FingerprintOf(new RunPlaybookTool.RunInput(playbookId, [documentId], parameters, "x", "k")).Should().NotBe(before);
    }

    [Fact(DisplayName = "038 no echo: the instructions text is in no log entry, tool result, retry-key entry or run status, over a start, a replay and a status read")]
    public async Task Instructions_NeverReachALogAResultOrAStoredRecord()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        var http = world.CallerContext();
        var arguments = new { playbookId, documentIds = new[] { documentId }, instructions = FocusText, idempotencyKey = "echo" };

        var started = await world.RunToolAsync(http, arguments);
        var replayed = await world.RunToolAsync(world.CallerContext(), arguments);
        var runId = RunIdOf(started);
        world.Gate.SetResult();
        await world.RunFinished();
        var status = await world.GetToolAsync(http, runId);
        var view = (await world.Runs.ReadAsCallerAsync(http, runId, CancellationToken.None))!;

        world.Calls.Single().Request.UserContext.Should().Contain(InstructionsSecret, "the run itself did get the text");
        foreach (var result in new[] { started, replayed, status })
        {
            Text(result).Should().NotContain(InstructionsSecret);
            JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions).Should().NotContain(InstructionsSecret);
        }

        JsonSerializer.Serialize(view).Should().NotContain(InstructionsSecret);
        world.Logs.Entries.Should().NotBeEmpty().And.NotContain(e => e.Contains(InstructionsSecret, StringComparison.Ordinal));
        world.Cache.Writes.Should().NotBeEmpty().And.NotContain(w => w.Contains(InstructionsSecret, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "038 tools/list: the schema names the optional `instructions` text (max 2,000), not required; the description says to omit it when the user said nothing")]
    public void Schema_AndDescription_NameInstructions()
    {
        var tool = new RunPlaybookTool().Create(new ServiceCollection().BuildServiceProvider()).ProtocolTool;

        var instructions = tool.InputSchema.GetProperty("properties").GetProperty("instructions");
        instructions.GetProperty("type").GetString().Should().Be("string");
        instructions.GetProperty("maxLength").GetInt32().Should().Be(2_000).And.Be(RunPlaybookTool.MaxInstructionsLength);
        instructions.GetProperty("description").GetString().Should().Be("Optional: what the user asked the run to focus on, in their words; omit if they said nothing.");
        tool.InputSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().NotContain("instructions");
        RunPlaybookTool.Description.Should().Contain("what the user asked the run to focus on").And.Contain("omit if they said nothing");
        RunPlaybookTool.Description.Length.Should().BeLessThanOrEqualTo(1_024);
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

    [Fact(DisplayName = "030 status: a durable Running record with no start time (sprk_startedon empty) reads running, never interrupted")]
    public async Task Status_RunningRecordWithNoStartTime_IsNotInterrupted()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var runId = Guid.NewGuid();
        world.StoredRuns[runId] = new PlaybookRunStatus
        {
            RunId = runId,
            PlaybookId = Guid.NewGuid(),
            State = PlaybookRunState.Running,
            StartedAt = DateTimeOffset.MinValue,   // what PlaybookRunRecordStore reads for a record with no sprk_startedon
            StartedByOid = PlaybookRunWorld.CallerOid,
        };

        var status = await world.GetToolAsync(world.CallerContext(), runId);

        status.IsError.Should().NotBe(true);
        Body(status).GetProperty("status").GetString().Should().Be("running");
        Body(status).GetProperty("result").GetProperty("outcome").GetString().Should().Be("running");
    }

    [Fact(DisplayName = "030 retry: a start whose result cannot be built (the link builder throws) answers the run's status and still keeps the retry key; a retry starts nothing")]
    public async Task Start_ResultBuildingFaults_StillKeepsTheRetryKey()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        var runs = new LinkThrows(world.Runs);
        var arguments = PlaybookRunWorld.Args(new { playbookId, documentIds = new[] { documentId }, idempotencyKey = "link-throws" });

        var first = await RunPlaybookTool.RunAsync(arguments, world.CallerContext(), world.Cache, runs, world.Time, NullLogger.Instance, CancellationToken.None);

        first.IsError.Should().NotBe(true, first.StructuredContent?.ToString());
        Body(first).GetProperty("status").GetString().Should().Be("running");
        var runId = RunIdOf(first);

        var retry = await RunPlaybookTool.RunAsync(arguments, world.CallerContext(), world.Cache, world.Runs, world.Time, NullLogger.Instance, CancellationToken.None);

        RunIdOf(retry).Should().Be(runId, "the retry key was stored although the first result had to fall back");
        world.Engine.Verify(e => e.ExecuteAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    /// <summary>The real runs seam, except that building the run link throws.</summary>
    private sealed class LinkThrows(IPlaybookRunAi inner) : IPlaybookRunAi
    {
        public Task<PlaybookRunStartResult> StartAsCallerAsync(
            HttpContext http, Guid playbookId, Guid[] documentIds, IReadOnlyDictionary<string, string>? parameters, string? userContext, CancellationToken cancellationToken) =>
            inner.StartAsCallerAsync(http, playbookId, documentIds, parameters, userContext, cancellationToken);

        public Task<PlaybookRunView?> ReadAsCallerAsync(HttpContext http, Guid runId, CancellationToken cancellationToken) =>
            inner.ReadAsCallerAsync(http, runId, cancellationToken);

        public Task<PlaybookRunResults?> ReadResultsAsCallerAsync(HttpContext http, Guid runId, CancellationToken cancellationToken) =>
            inner.ReadResultsAsCallerAsync(http, runId, cancellationToken);

        public string? RunConsoleLink(HttpContext http, Guid? documentId) => throw new InvalidOperationException("link builder fault");
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

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero));
        var status = GetPlaybookRunTool.StatusResult(worst, time.GetUtcNow());
        var start = RunPlaybookTool.StartedResult(
            worst with { Phase = PlaybookRunPhase.Running }, "https://spaarke-test.crm.dynamics.com/" + new string('a', 2_000), replayed: true, time.GetUtcNow());

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
