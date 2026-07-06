using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Narrators;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Narrators;

/// <summary>
/// Unit tests for <see cref="DailyBriefingCompositeService"/> — FR-P3-04
/// (spaarke-ai-architecture-redesign-r1 task 043): the Daily Briefing as the FIRST full
/// <c>coded</c> composite Action on the catalog path.
///
/// <para>
/// <b>KEEP rationale (maintain-class)</b>: each fact anchors a cutover contract —
/// dispatch is decided EXCLUSIVELY by the resolved Binding (ADR-039: kind=coded +
/// sprk_workflowclass class-ref, no flag, no engine fallback per NFR-08); ledger entries
/// (section-name-keyed Output + ToolChain) are written through IOutputRouter BEFORE the
/// renderable response is returned (ADR-040 store-precedes-render); the email Binding leg
/// carries the capability-supplied email envelope in the routed payload.
/// </para>
/// <para>
/// Module boundaries per ADR-038: IConsumerRoutingService / ICodedWorkflowRegistry /
/// IOutputRouter are interface seams; the collector is a recording subclass over its
/// protected-ctor + virtual production shape (same pattern as NullDailyBriefingCollector).
/// </para>
/// </summary>
public sealed class DailyBriefingCompositeServiceTests
{
    private const string TenantId = "tenant-briefing";
    private static readonly Guid BindingId = Guid.Parse("b4503359-1771-f111-ab0e-7ced8ddc4a05");
    private static readonly Guid SystemUserId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly Mock<IConsumerRoutingService> _routing = new(MockBehavior.Strict);
    private readonly Mock<ICodedWorkflowRegistry> _registry = new(MockBehavior.Strict);
    private readonly Mock<IOutputRouter> _outputRouter = new(MockBehavior.Strict);
    private readonly StubCollector _collector = new();
    private readonly RecordingWorkflow _workflow = new();

    private DailyBriefingCompositeService CreateSut() => new(
        _routing.Object,
        _registry.Object,
        _collector,
        _outputRouter.Object,
        NullLogger<DailyBriefingCompositeService>.Instance);

    // ─── Dispatch-via-Binding: the ADR-039 cutover contract ───────────────────────────────

    [Fact]
    public async Task NarrateAsync_DispatchesViaCodedBinding_ResolvedByWorkflowClassRef()
    {
        SetupBinding(BuildCodedBinding());
        SetupWorkflowResolution();
        var routed = SetupRouterPassthrough();

        var response = await CreateSut().NarrateAsync(BuildRequest(), TenantId, CancellationToken.None);

        // The Binding decided: workflow resolved by the Action row's class ref and executed.
        _registry.Verify(r => r.GetWorkflow("DailyBriefingNarrator"), Times.Once);
        _workflow.Executions.Should().ContainSingle();
        response.Tldr.Summary.Should().Be("narrated summary");

        // The workflow received the request payload as its ArgumentsJson (typed contract).
        var args = JsonSerializer.Deserialize<DailyBriefingNarrateRequest>(
            _workflow.Executions[0].ArgumentsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        args!.Channels.Should().HaveCount(1);

        routed.Invocations.Should().ContainSingle("every execution routes exactly one composite output");
    }

    [Fact]
    public async Task NarrateAsync_NoBindingRow_ThrowsDispatchUnconfigured_NeverFallsBack()
    {
        SetupBinding(null);

        var act = () => CreateSut().NarrateAsync(BuildRequest(), TenantId, CancellationToken.None);

        await act.Should().ThrowAsync<DailyBriefingDispatchUnconfiguredException>(
            "no Binding row → unconfigured (503 at the endpoint) — NEVER a flag or engine fallback (NFR-08)");
        _workflow.Executions.Should().BeEmpty();
    }

    [Fact]
    public async Task NarrateAsync_PromptedKindBinding_ThrowsLoudConfigError()
    {
        SetupBinding(BuildCodedBinding() with { ActionKind = ActionKind.Prompted, WorkflowClass = null });

        var act = () => CreateSut().NarrateAsync(BuildRequest(), TenantId, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>(
            "the briefing Binding MUST target a coded-kind Action — a prompted row is an admin error, not a fallback"))
            .Which.Message.Should().Contain("coded");
    }

    [Fact]
    public async Task NarrateAsync_WorkflowClassNotRegistered_ThrowsWithRegisteredList()
    {
        SetupBinding(BuildCodedBinding() with { WorkflowClass = "NoSuchWorkflow" });
        _registry.Setup(r => r.GetWorkflow("NoSuchWorkflow")).Returns((ICodedWorkflow?)null);
        _registry.Setup(r => r.GetRegisteredWorkflowClassRefs())
            .Returns(new[] { "DailyBriefingNarrator", "DailyBriefingCollector" });

        var act = () => CreateSut().NarrateAsync(BuildRequest(), TenantId, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>("closed catalog — unknown class refs fail loudly (ADR-039)"))
            .Which.Message.Should().Contain("NoSuchWorkflow").And.Contain("DailyBriefingNarrator");
    }

    // ─── Ledger write-before-render (ADR-040) ─────────────────────────────────────────────

    [Fact]
    public async Task NarrateAsync_WritesSectionKeyedOutputAndToolChain_BeforeReturningResponse()
    {
        SetupBinding(BuildCodedBinding());
        SetupWorkflowResolution();
        var routed = SetupRouterPassthrough();

        await CreateSut().NarrateAsync(BuildRequest(), TenantId, CancellationToken.None);

        var (session, binding, payload) = routed.Invocations.Single();

        // Routed under the RESOLVED Binding identity (ledger key = {bindingId}@t{n}).
        binding.BindingId.Should().Be(BindingId);

        // Section-name-keyed payload (amended ADR-037 section contract in the payload shape).
        payload.GetProperty("sections").TryGetProperty("tldr", out _).Should().BeTrue();
        payload.GetProperty("sections").GetProperty("channels")
            .TryGetProperty("tasks", out _).Should().BeTrue("channel sections are keyed by category name");

        // The briefing-run session carries the ToolChain entry in the SAME ledger write
        // (identifiers/counts only per NFR-07).
        session.TenantId.Should().Be(TenantId);
        session.ToolChains.Should().ContainSingle()
            .Which.Calls.Should().Contain(c => c.ToolId == DailyBriefingCompositeService.NarrateToolId);
    }

    [Fact]
    public async Task NarrateAsync_RouterFailure_PropagatesAndNothingRenders()
    {
        SetupBinding(BuildCodedBinding());
        SetupWorkflowResolution();
        _outputRouter
            .Setup(r => r.RouteAsync(
                It.IsAny<ChatSession>(), It.IsAny<Binding>(), It.IsAny<JsonElement>(),
                It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ledger store failed"));

        var act = () => CreateSut().NarrateAsync(BuildRequest(), TenantId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "unstored output is never rendered — a ledger-write failure fails the invocation (ADR-040 D2/D8)");
    }

    // ─── Render leg (widget path) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_CollectsThenDispatches_AttachesHighPriorityAndCollectToolCall()
    {
        _collector.Payload = BuildRequest();
        _collector.HighPriority = new[] { new HighPriorityItemDto { EntityType = "sprk_matter", Name = "Acme" } };
        SetupBinding(BuildCodedBinding());
        SetupWorkflowResolution();
        var routed = SetupRouterPassthrough();

        var response = await CreateSut().RenderAsync(SystemUserId, TenantId, CancellationToken.None);

        response.HighPriorityItems.Should().ContainSingle(i => i.Name == "Acme");
        _workflow.Executions.Single().UserId.Should().Be(SystemUserId, "user-OBO identity flows into the workflow context");

        var invocation = routed.Invocations.Single();
        invocation.Session.ToolChains!.Single().Calls
            .Should().Contain(c => c.ToolId == DailyBriefingCompositeService.CollectToolId);

        // The stored payload carries the high-priority section — the widget response, the
        // ledger entry, and any email rendering must agree (attached BEFORE the write).
        invocation.Payload.GetProperty("sections").GetProperty("highPriority")[0]
            .GetProperty("name").GetString().Should().Be("Acme");
    }

    [Fact]
    public async Task RenderAsync_EmptyCollect_ShortCircuits_NoDispatchNoLedger()
    {
        _collector.Payload = new DailyBriefingNarrateRequest();
        _collector.HighPriority = new[] { new HighPriorityItemDto { EntityType = "sprk_event", Name = "Urgent" } };

        var response = await CreateSut().RenderAsync(SystemUserId, TenantId, CancellationToken.None);

        // Nothing to narrate: no Binding resolve, no workflow, no ledger entry — but the
        // structured high-priority list still surfaces (operator design decision).
        response.ChannelNarratives.Should().BeEmpty();
        response.HighPriorityItems.Should().ContainSingle(i => i.Name == "Urgent");
        _routing.VerifyNoOtherCalls();
        _workflow.Executions.Should().BeEmpty();
    }

    // ─── Email leg (email disposition Binding) ────────────────────────────────────────────

    [Fact]
    public async Task EmailAsync_EmailBinding_RoutesPayloadCarryingEmailEnvelope()
    {
        _collector.Payload = BuildRequest();
        _collector.HighPriority = new[] { new HighPriorityItemDto { EntityType = "sprk_matter", Name = "Acme Corp" } };
        SetupBinding(
            BuildCodedBinding() with { Disposition = BindingDisposition.Email },
            consumerCode: "email");
        SetupWorkflowResolution();
        var routed = SetupRouterPassthrough();

        await CreateSut().EmailAsync(SystemUserId, TenantId, "user@contoso.com", CancellationToken.None);

        var (_, binding, payload) = routed.Invocations.Single();
        binding.Disposition.Should().Be(BindingDisposition.Email);

        // Capability supplies presentation: the routed payload carries the email envelope
        // the OutputRouter email leg delivers (to / subject / htmlBody), and the emailed
        // HTML includes the high-priority section (attached BEFORE the ledger write).
        var email = payload.GetProperty("email");
        email.GetProperty("to")[0].GetString().Should().Be("user@contoso.com");
        email.GetProperty("subject").GetString().Should().Contain("Daily Briefing");
        email.GetProperty("htmlBody").GetString().Should().Contain("narrated summary").And.Contain("Acme Corp");
    }

    [Fact]
    public async Task EmailAsync_EmptyCollect_SendsNothing()
    {
        _collector.Payload = new DailyBriefingNarrateRequest();
        _collector.HighPriority = Array.Empty<HighPriorityItemDto>();

        var response = await CreateSut().EmailAsync(SystemUserId, TenantId, "user@contoso.com", CancellationToken.None);

        response.ChannelNarratives.Should().BeEmpty();
        _routing.VerifyNoOtherCalls();
        _workflow.Executions.Should().BeEmpty("an empty briefing produces no email and no ledger entry");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────────────

    private void SetupBinding(Binding? binding, string consumerCode = "default")
    {
        _routing
            .Setup(r => r.ResolveBindingAsync(
                ConsumerTypes.DailyBriefingNarrate, consumerCode, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(binding);
    }

    private void SetupWorkflowResolution()
    {
        _registry.Setup(r => r.GetWorkflow("DailyBriefingNarrator")).Returns(_workflow);
    }

    private RouterRecorder SetupRouterPassthrough()
    {
        var recorder = new RouterRecorder();
        _outputRouter
            .Setup(r => r.RouteAsync(
                It.IsAny<ChatSession>(), It.IsAny<Binding>(), It.IsAny<JsonElement>(),
                It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .Returns<ChatSession, Binding, JsonElement, IReadOnlyList<string>?, CancellationToken>(
                (session, binding, payload, _, _) =>
                {
                    recorder.Invocations.Add((session, binding, payload.Clone()));
                    var entry = new SessionOutput
                    {
                        Key = SessionLedger.BuildOutputKey(binding.BindingId.ToString(), 1),
                        BindingId = binding.BindingId.ToString(),
                        UcId = binding.Ucid ?? binding.ConsumerType,
                        Turn = 1,
                        Disposition = "informational",
                        Payload = payload.Clone(),
                        CreatedAt = DateTimeOffset.UtcNow,
                    };
                    return Task.FromResult(new RoutedOutput
                    {
                        Entry = entry,
                        Session = session with { Outputs = new[] { entry } },
                    });
                });
        return recorder;
    }

    private sealed class RouterRecorder
    {
        public List<(ChatSession Session, Binding Binding, JsonElement Payload)> Invocations { get; } = new();
    }

    private static Binding BuildCodedBinding() => new()
    {
        BindingId = BindingId,
        ConsumerType = ConsumerTypes.DailyBriefingNarrate,
        Ucid = "UC-D-1",
        ActionKind = ActionKind.Coded,
        WorkflowClass = "DailyBriefingNarrator",
        Disposition = BindingDisposition.Informational,
    };

    private static DailyBriefingNarrateRequest BuildRequest() => new()
    {
        Categories = [new NotificationCategoryDto { Name = "Tasks Overdue", Count = 1 }],
        PriorityItems = [new PriorityItemDto { Category = "Tasks", Title = "Review engagement letter" }],
        TotalNotificationCount = 1,
        Channels =
        [
            new ChannelNarrationInput
            {
                Category = "tasks",
                Label = "Tasks Overdue",
                Items = [new ChannelItemDto { Id = "n-1", Title = "Review engagement letter" }]
            }
        ],
    };

    /// <summary>Recording ICodedWorkflow standing in for the narrator (the registry seam).</summary>
    private sealed class RecordingWorkflow : ICodedWorkflow
    {
        public List<CodedWorkflowContext> Executions { get; } = new();

        public string WorkflowClassRef => "DailyBriefingNarrator";

        public Task<object?> ExecuteAsync(CodedWorkflowContext context, CancellationToken cancellationToken)
        {
            Executions.Add(context);
            return Task.FromResult<object?>(new DailyBriefingNarrateResponse
            {
                Tldr = new TldrResult { Summary = "narrated summary", KeyTakeaways = ["one"], TopAction = "act" },
                ChannelNarratives =
                [
                    new ChannelNarrationResult
                    {
                        Category = "tasks",
                        Bullets = [new NarrativeBulletDto { Narrative = "One overdue task." }]
                    }
                ],
                GeneratedAtUtc = DateTimeOffset.UtcNow,
            });
        }
    }

    /// <summary>Stub collector over the protected-ctor + virtual production shape.</summary>
    private sealed class StubCollector : DailyBriefingCollector
    {
        public StubCollector() : base(NullLogger<DailyBriefingCollector>.Instance)
        {
        }

        public DailyBriefingNarrateRequest Payload { get; set; } = new();
        public HighPriorityItemDto[] HighPriority { get; set; } = Array.Empty<HighPriorityItemDto>();

        public override Task<DailyBriefingNarrateRequest> CollectAsync(Guid systemUserId, CancellationToken ct)
            => Task.FromResult(Payload);

        public override Task<HighPriorityItemDto[]> CollectHighPriorityAsync(Guid systemUserId, CancellationToken ct)
            => Task.FromResult(HighPriority);
    }
}
