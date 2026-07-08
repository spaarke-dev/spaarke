using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Narrators;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Unit tests for <see cref="DailyBriefingEndpoints"/> — FR-P3-04
/// (spaarke-ai-architecture-redesign-r1 task 043, hard cutover per NFR-08).
///
/// HandleNarrate is a thin wrapper over <see cref="DailyBriefingCompositeService"/> (the
/// coded-composite dispatch boundary): the R4 playbook-engine path (the since-deleted generic facade +
/// StructuredData projection) and the R7 narrator parallel-run feature flag were DELETED —
/// the Binding decides (ADR-039), and there is no fallback.
///
/// Tests verify the endpoint contract that survives the cutover:
///   - Empty-payload tolerance (200 + empty bullets) short-circuits BEFORE dispatch
///   - Non-empty requests dispatch through the composite and return its response
///   - Unconfigured dispatch (no Binding row) → 503 ProblemDetails
///   - FeatureDisabledException (ADR-032 compound-OFF Null peers) → 503
///   - Generic execution failure → 500; cancellation propagates
///
/// Prior tests for the playbook-engine invocation path
/// (HandleNarrate_Invokes_Playbook_*, the playbook-result projection facts) were
/// REMOVED with the deleted code — the projection helper and engine dispatch no longer exist.
/// </summary>
[Trait("status", "task-043-coded-composite")]
public sealed class DailyBriefingEndpointsTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Invokes the private static HandleNarrate handler via reflection.
    ///
    ///   HandleNarrate(
    ///       DailyBriefingNarrateRequest request,
    ///       ILoggerFactory loggerFactory,
    ///       DailyBriefingCompositeService composite,
    ///       HttpContext httpContext,
    ///       CancellationToken cancellationToken)
    /// </summary>
    private static async Task<IResult> InvokeHandleNarrateAsync(
        DailyBriefingNarrateRequest request,
        DailyBriefingCompositeService composite,
        HttpContext? httpContext = null,
        CancellationToken cancellationToken = default)
    {
        var method = typeof(DailyBriefingEndpoints)
            .GetMethod("HandleNarrate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("HandleNarrate not found via reflection");

        var task = (Task<IResult>)method.Invoke(null, new object?[]
        {
            request,
            NullLoggerFactory.Instance,
            composite,
            httpContext ?? new DefaultHttpContext(),
            cancellationToken
        })!;

        return await task;
    }

    private static DailyBriefingNarrateRequest BuildNonEmptyRequest() => new()
    {
        Categories =
        [
            new NotificationCategoryDto { Name = "Tasks Overdue", Count = 1, UnreadCount = 1 }
        ],
        PriorityItems =
        [
            new PriorityItemDto { Category = "Tasks", Title = "Review engagement letter" }
        ],
        TotalNotificationCount = 1,
        Channels =
        [
            new ChannelNarrationInput
            {
                Category = "tasks",
                Label = "Tasks Overdue",
                Items =
                [
                    new ChannelItemDto
                    {
                        Id = "notif-1",
                        Title = "Review engagement letter",
                        RegardingName = "Acme Corp",
                        RegardingEntityType = "sprk_matter",
                        RegardingId = Guid.NewGuid().ToString(),
                        Priority = "high"
                    }
                ]
            }
        ]
    };

    private static DailyBriefingNarrateResponse BuildResponse() => new()
    {
        Tldr = new TldrResult { Summary = "composite summary", KeyTakeaways = ["k1"], TopAction = "act" },
        ChannelNarratives =
        [
            new ChannelNarrationResult
            {
                Category = "tasks",
                Bullets = [new NarrativeBulletDto { Narrative = "One overdue task." }]
            }
        ],
        GeneratedAtUtc = DateTimeOffset.UtcNow,
    };

    // ── Tests: empty payload → 200 (short-circuits BEFORE composite dispatch) ──

    [Fact]
    public async Task HandleNarrate_Returns_200_Empty_On_Empty_Payload_Without_Invoking_Dispatch()
    {
        var request = new DailyBriefingNarrateRequest
        {
            Categories = [],
            PriorityItems = [],
            TotalNotificationCount = 0,
            Channels = []
        };
        var composite = new StubComposite(_ => throw new InvalidOperationException("must not dispatch"));

        var result = await InvokeHandleNarrateAsync(request, composite);

        result.Should().BeOfType<Ok<DailyBriefingNarrateResponse>>();
        var ok = (Ok<DailyBriefingNarrateResponse>)result;
        ok.Value!.Tldr.Summary.Should().BeEmpty();
        ok.Value.Tldr.KeyTakeaways.Should().BeEmpty();
        ok.Value.Tldr.CategoryCount.Should().Be(0);
        ok.Value.ChannelNarratives.Should().BeEmpty();
        composite.NarrateCalls.Should().Be(0, "the empty short-circuit predates dispatch — no LLM call, no ledger entry");
    }

    // ── Tests: coded-composite dispatch (the Binding decides — ADR-039) ────────

    [Fact]
    public async Task HandleNarrate_Dispatches_Request_Through_Composite_And_Returns_Its_Response()
    {
        var request = BuildNonEmptyRequest();
        DailyBriefingNarrateRequest? dispatched = null;
        var composite = new StubComposite(req => { dispatched = req; return BuildResponse(); });

        var result = await InvokeHandleNarrateAsync(request, composite);

        result.Should().BeOfType<Ok<DailyBriefingNarrateResponse>>();
        ((Ok<DailyBriefingNarrateResponse>)result).Value!.Tldr.Summary.Should().Be("composite summary");
        composite.NarrateCalls.Should().Be(1);
        dispatched.Should().BeSameAs(request,
            "the endpoint forwards the caller payload untouched — collection is the caller's concern on /narrate");
    }

    [Fact]
    public async Task HandleNarrate_Returns_503_When_Dispatch_Unconfigured()
    {
        var composite = new StubComposite(_ =>
            throw new DailyBriefingDispatchUnconfiguredException("no Binding row for daily-briefing-narrate/default"));

        var result = await InvokeHandleNarrateAsync(BuildNonEmptyRequest(), composite);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(503);
        problem.ProblemDetails.Detail.Should().Contain("daily-briefing-narrate");
    }

    [Fact]
    public async Task HandleNarrate_Returns_503_When_Feature_Disabled()
    {
        // ADR-032 compound-OFF: the Null composite peer throws FeatureDisabledException.
        var composite = new StubComposite(_ =>
            throw new FeatureDisabledException("ai.briefing.disabled", "Daily briefing requires the compound AI gate."));

        var result = await InvokeHandleNarrateAsync(BuildNonEmptyRequest(), composite);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task HandleNarrate_Returns_500_When_Composite_Throws_Generic_Exception()
    {
        var composite = new StubComposite(_ => throw new InvalidOperationException("workflow exploded"));

        var result = await InvokeHandleNarrateAsync(BuildNonEmptyRequest(), composite);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task HandleNarrate_Propagates_OperationCanceledException_When_Caller_Cancels()
    {
        var composite = new StubComposite(_ => throw new OperationCanceledException());

        var act = () => InvokeHandleNarrateAsync(BuildNonEmptyRequest(), composite);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "caller cancellation propagates cleanly — never converted to a 500");
    }

    // ── Stub composite over the protected-ctor + virtual production shape ──────

    private sealed class StubComposite : DailyBriefingCompositeService
    {
        private readonly Func<DailyBriefingNarrateRequest, DailyBriefingNarrateResponse> _narrate;

        public StubComposite(Func<DailyBriefingNarrateRequest, DailyBriefingNarrateResponse> narrate)
            : base(NullLogger<DailyBriefingCompositeService>.Instance)
        {
            _narrate = narrate;
        }

        public int NarrateCalls { get; private set; }

        public override Task<DailyBriefingNarrateResponse> NarrateAsync(
            DailyBriefingNarrateRequest request, string tenantId, CancellationToken cancellationToken)
        {
            NarrateCalls++;
            return Task.FromResult(_narrate(request));
        }
    }
}
