using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.EventRules;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.EventRules;

/// <summary>
/// Unit tests for <see cref="EventRulesService"/> — the FR-P1-03 Event entry path
/// (spaarke-ai-architecture-redesign-r1 task 022).
///
/// <para>
/// <b>KEEP rationale (maintain-class)</b>: each fact anchors a contract the Event path
/// is BUILT on — the ordered-member resolution from <c>sprk_oneventbindings</c>, the
/// four FR-P1-03 bounds (daily cap / opt-out / bulk top-1 / explicit-command supersede)
/// plus the empty-attachments precondition, the M4 classify-confidence policy branches,
/// and the ADR-040 ledger-before-render ordering (proven through the REAL
/// <see cref="OutputRouter"/> over the same recording persistence seam as
/// <see cref="OutputRouterTests"/>).
/// </para>
/// <para>
/// Module boundaries mocked per ADR-038: IConsumerRoutingService (Binding table),
/// IScopeResolverService + IActionRunner (catalog/executor), ISessionFileTextSource
/// (text retrieval), IEventPathUserState (budget/opt-out store). The router + ledger
/// write path is REAL.
/// </para>
/// </summary>
public class EventRulesServiceTests
{
    private const string TenantId = "tenant-events";
    private const string SessionId = "session-events";
    private const string UserOid = "user-oid-1";

    private static readonly Guid ClassifyBindingId = Guid.Parse("11111111-2222-3333-4444-555555555551");
    private static readonly Guid SummarizeBindingId = Guid.Parse("11111111-2222-3333-4444-555555555552");
    private static readonly Guid ClassifyActionId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444441");
    private static readonly Guid SummarizeActionId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444442");

    private readonly RecordingChatSessionManager _sessionManager = new();
    private readonly Mock<IConsumerRoutingService> _routing = new();
    private readonly Mock<IScopeResolverService> _scopeResolver = new();
    private readonly Mock<IActionRunner> _actionRunner = new();
    private readonly Mock<ISessionFileTextSource> _textSource = new();
    private readonly Mock<IEventPathUserState> _userState = new();
    private readonly EventRulesTelemetry _telemetry = new();
    private readonly EventRulesOptions _options = new();

    /// <summary>Ordered record of executed (actionId, fileName) pairs — proves member order + top-1.</summary>
    private readonly List<(Guid ActionId, string FileName)> _executed = new();

    public EventRulesServiceTests()
    {
        // Default happy-path wiring: session with 1 file, [classify(1), summarize(2)] rule,
        // no opt-out, empty budget, high-confidence classify output.
        _sessionManager.SessionToReturn = BuildSession(BuildFile("file-1", "contract.pdf"));

        _routing
            .Setup(r => r.ResolveEventBindingsAsync("document_uploaded", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ClassifyBinding(), SummarizeBinding() });

        _scopeResolver
            .Setup(s => s.GetActionAsync(ClassifyActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction { Id = ClassifyActionId, Name = "CLS-CHAT@v1" });
        _scopeResolver
            .Setup(s => s.GetActionAsync(SummarizeActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction { Id = SummarizeActionId, Name = "SUM-CHAT@v1" });

        _textSource
            .Setup(t => t.FetchAsync(TenantId, SessionId, It.IsAny<IReadOnlyList<ChatSessionFile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionFileText { ExtractedText = "extracted text", DisplayName = "contract.pdf" });

        _userState.Setup(u => u.IsOptedOutAsync(TenantId, UserOid, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _userState.Setup(u => u.GetTodayExecutionCountAsync(TenantId, UserOid, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _userState.Setup(u => u.AddExecutionsAsync(TenantId, UserOid, It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        SetupActionOutput(ClassifyActionId, """{"docType":"nda","confidence":0.95,"rationale":"mutual confidentiality"}""");
        SetupActionOutput(SummarizeActionId, """{"tldr":["bullet"],"summary":"a summary"}""");
    }

    private void SetupActionOutput(Guid actionId, string json)
    {
        _actionRunner
            .Setup(a => a.RunAsync(
                It.Is<AnalysisAction>(x => x.Id == actionId),
                It.IsAny<DocumentText>(),
                It.IsAny<LinearRunContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<AnalysisAction, DocumentText, LinearRunContext, CancellationToken>(
                (a, d, _, _) => _executed.Add((a.Id, d.FileName ?? string.Empty)))
            .ReturnsAsync(ParseJson(json));
    }

    private EventRulesService CreateSut() => new(
        _sessionManager,
        _routing.Object,
        _scopeResolver.Object,
        _actionRunner.Object,
        _textSource.Object,
        new OutputRouter(_sessionManager, Mock.Of<ILogger<OutputRouter>>()), // REAL router — ledger-before-render proven, not mocked
        _userState.Object,
        _telemetry,
        Options.Create(_options),
        Mock.Of<ILogger<EventRulesService>>());

    private Task<List<ChatSseEvent>> FireAsync(
        IReadOnlyList<string>? fileIds = null, string? typedCommand = null)
        => CollectAsync(CreateSut().FireAsync(new SurfaceEventRequest(
            EventName: SurfaceEventNames.DocumentUploaded,
            TenantId: TenantId,
            SessionId: SessionId,
            UserOid: UserOid,
            FileIds: fileIds,
            TypedCommand: typedCommand)));

    // ─── The headline flow: upload, type nothing → classify(1), summarize(2), chips ────

    [Fact]
    public async Task FireAsync_UploadNoTypedCommand_YieldsClassificationThenSummaryThenChips()
    {
        var events = await FireAsync(new[] { "file-1" });

        events.Select(e => e.Type).Should().ContainInOrder(
            EventRuleSseEvents.Classification,
            EventRuleSseEvents.Output,
            EventRuleSseEvents.Chips,
            EventRuleSseEvents.Done);

        // Declared member order (sprk_oneventbindings order 1 then 2) is the execution order.
        _executed.Select(x => x.ActionId).Should().ContainInOrder(ClassifyActionId, SummarizeActionId);

        var classification = (EventClassificationData)events.Single(e => e.Type == EventRuleSseEvents.Classification).Data!;
        classification.DocType.Should().Be("nda");
        classification.Confidence.Should().Be(0.95);
        classification.BindingId.Should().Be(ClassifyBindingId.ToString());

        var output = (EventOutputData)events.Single(e => e.Type == EventRuleSseEvents.Output).Data!;
        output.BindingId.Should().Be(SummarizeBindingId.ToString());
        output.Ucid.Should().Be("UC-A-1");
        output.Payload.GetProperty("summary").GetString().Should().Be("a summary");
    }

    [Fact]
    public async Task FireAsync_ChipsEvent_CarriesBindingChipTransitions_ForClickPathDispatch()
    {
        var events = await FireAsync(new[] { "file-1" });

        var chips = (EventChipsData)events.Single(e => e.Type == EventRuleSseEvents.Chips).Data!;
        chips.SourceBindingId.Should().Be(SummarizeBindingId.ToString());
        chips.Chips.Should().ContainSingle(c => c.Label == "Summarize again"
            && c.TargetBindingId == SummarizeBindingId.ToString());
    }

    // ─── Bound (d): explicit-command supersede ──────────────────────────────────────────

    [Fact]
    public async Task FireAsync_TypedCommandPresent_SupersedesRule_NothingExecutes()
    {
        var events = await FireAsync(new[] { "file-1" }, typedCommand: "/summarize in detail");

        var notice = (EventNoticeData)events.Single(e => e.Type == EventRuleSseEvents.Notice).Data!;
        notice.Reason.Should().Be(EventNoticeReasons.Superseded);
        events[^1].Type.Should().Be(EventRuleSseEvents.Done);

        _executed.Should().BeEmpty("the Text path wins — the event rule must not spend anything");
        _sessionManager.PersistedSessions.Should().BeEmpty("no execution ⇒ no ledger write");
        _userState.Verify(u => u.AddExecutionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── Precondition: empty attachments (task 025 r7 guard re-home) ────────────────────

    [Fact]
    public async Task FireAsync_EmptyAttachmentSet_DoesNotFire()
    {
        _sessionManager.SessionToReturn = BuildSession(); // no files in the manifest

        var events = await FireAsync();

        var notice = (EventNoticeData)events.Single(e => e.Type == EventRuleSseEvents.Notice).Data!;
        notice.Reason.Should().Be(EventNoticeReasons.NoAttachments);
        _executed.Should().BeEmpty("the rule never fires on a zero-file set (r7 guard, task 025 handoff)");
    }

    [Fact]
    public async Task FireAsync_FileIdsUnknownToManifest_TreatedAsEmpty()
    {
        var events = await FireAsync(new[] { "not-in-manifest" });

        ((EventNoticeData)events.Single(e => e.Type == EventRuleSseEvents.Notice).Data!)
            .Reason.Should().Be(EventNoticeReasons.NoAttachments);
        _executed.Should().BeEmpty();
    }

    // ─── Bound (b): per-user opt-out ────────────────────────────────────────────────────

    [Fact]
    public async Task FireAsync_OptedOutUser_SkipsWithManualRunChip()
    {
        _userState.Setup(u => u.IsOptedOutAsync(TenantId, UserOid, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var events = await FireAsync(new[] { "file-1" });

        var notice = (EventNoticeData)events.Single(e => e.Type == EventRuleSseEvents.Notice).Data!;
        notice.Reason.Should().Be(EventNoticeReasons.OptedOut);
        notice.Chips.Should().ContainSingle(c => c.TargetBindingId == SummarizeBindingId.ToString(),
            "an opted-out user can still run the capability by explicit Click");
        _executed.Should().BeEmpty();
    }

    // ─── Bound (a): per-user daily cost cap (NFR-09 / ADR-016) ──────────────────────────

    [Fact]
    public async Task FireAsync_DailyCapReached_DefersGracefullyWithChip_NeverSilentDrop()
    {
        _options.DailyExecutionCap = 10;
        _userState.Setup(u => u.GetTodayExecutionCountAsync(TenantId, UserOid, It.IsAny<CancellationToken>())).ReturnsAsync(10);

        var events = await FireAsync(new[] { "file-1" });

        var notice = (EventNoticeData)events.Single(e => e.Type == EventRuleSseEvents.Notice).Data!;
        notice.Reason.Should().Be(EventNoticeReasons.DailyCap);
        notice.Message.Should().NotBeNullOrWhiteSpace("cap-exceeded yields a rendered notice, not a silent drop (NFR-09)");
        notice.Chips.Should().ContainSingle(c => c.TargetBindingId == SummarizeBindingId.ToString(),
            "spec §7.1: defer with a chip when the cap is hit");
        _executed.Should().BeEmpty();
        _userState.Verify(u => u.AddExecutionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FireAsync_RemainingBudgetSmallerThanRuleMemberCount_Defers()
    {
        _options.DailyExecutionCap = 10;
        // 9 used + 2 members > 10 — the WHOLE rule must fit; no partial half-rule runs.
        _userState.Setup(u => u.GetTodayExecutionCountAsync(TenantId, UserOid, It.IsAny<CancellationToken>())).ReturnsAsync(9);

        var events = await FireAsync(new[] { "file-1" });

        ((EventNoticeData)events.Single(e => e.Type == EventRuleSseEvents.Notice).Data!)
            .Reason.Should().Be(EventNoticeReasons.DailyCap);
        _executed.Should().BeEmpty();
    }

    [Fact]
    public async Task FireAsync_HappyPath_ConsumesOneBudgetUnitPerMemberExecution()
    {
        await FireAsync(new[] { "file-1" });

        _userState.Verify(u => u.AddExecutionsAsync(TenantId, UserOid, 1, It.IsAny<CancellationToken>()),
            Times.Exactly(2), "classify + summarize each consume one budget unit (the NFR-09 counting unit)");
    }

    // ─── Bound (c): bulk upload = top-1 auto-run + "summarize all?" chip ───────────────

    [Fact]
    public async Task FireAsync_BulkUpload_AutoRunsFirstFileOnly_AndEmitsSummarizeAllChip()
    {
        _sessionManager.SessionToReturn = BuildSession(
            BuildFile("file-1", "first.pdf"),
            BuildFile("file-2", "second.pdf"),
            BuildFile("file-3", "third.pdf"));

        var events = await FireAsync(new[] { "file-1", "file-2", "file-3" });

        // Top-1: both members ran against the FIRST file of the batch only.
        _executed.Should().HaveCount(2);
        _executed.Should().OnlyContain(x => x.FileName == "first.pdf");

        var chips = (EventChipsData)events.Single(e => e.Type == EventRuleSseEvents.Chips).Data!;
        chips.Chips.Should().Contain(c =>
            c.Label.Contains("all 3 files") && c.TargetBindingId == SummarizeBindingId.ToString());
    }

    // ─── M4 classify-confidence policy branches ─────────────────────────────────────────

    [Fact]
    public async Task FireAsync_ClassifyConfidenceBelowThreshold_FiresM4Confirmation_SummarizeSuspended()
    {
        SetupActionOutput(ClassifyActionId, """{"docType":"memo","confidence":0.55,"rationale":"weak signals"}""");

        var events = await FireAsync(new[] { "file-1" });

        events.Select(e => e.Type).Should().ContainInOrder(
            EventRuleSseEvents.Classification,
            EventRuleSseEvents.Confirmation,
            EventRuleSseEvents.Done);
        events.Should().NotContain(e => e.Type == EventRuleSseEvents.Output,
            "below-threshold confidence suspends the summarize member behind the M4 gate");

        _executed.Select(x => x.ActionId).Should().Equal(ClassifyActionId);

        var confirmation = (EventConfirmationData)events.Single(e => e.Type == EventRuleSseEvents.Confirmation).Data!;
        confirmation.DocType.Should().Be("memo");
        confirmation.Confidence.Should().Be(0.55);
        confirmation.Threshold.Should().Be(0.85);
        confirmation.Chips.Should().ContainSingle(c => c.TargetBindingId == SummarizeBindingId.ToString(),
            "the confirm chip resumes the suspended member via the Click path");
    }

    [Fact]
    public async Task FireAsync_ClassifyConfidenceAtThreshold_ProceedsSilently()
    {
        SetupActionOutput(ClassifyActionId, """{"docType":"nda","confidence":0.85,"rationale":"clear"}""");

        var events = await FireAsync(new[] { "file-1" });

        events.Should().NotContain(e => e.Type == EventRuleSseEvents.Confirmation,
            "at-threshold confidence is NOT below the dial — no confirmation turn");
        _executed.Select(x => x.ActionId).Should().ContainInOrder(ClassifyActionId, SummarizeActionId);
    }

    [Fact]
    public async Task FireAsync_ConfigurableThreshold_IsRespected()
    {
        _options.ClassifyConfidenceThreshold = 0.5;
        SetupActionOutput(ClassifyActionId, """{"docType":"memo","confidence":0.55,"rationale":"weak"}""");

        var events = await FireAsync(new[] { "file-1" });

        events.Should().NotContain(e => e.Type == EventRuleSseEvents.Confirmation,
            "0.55 clears a 0.5 threshold — the dial is the operator-owned policy bound");
        _executed.Should().HaveCount(2);
    }

    // ─── ADR-040: both outputs land in the ledger BEFORE their rendering events ─────────

    [Fact]
    public async Task FireAsync_BothOutputs_LedgerWrittenBeforeRendering_AndRenderedFromStoredEntry()
    {
        var events = await FireAsync(new[] { "file-1" });

        // Two executions → two ledger writes through the REAL OutputRouter persistence seam.
        _sessionManager.PersistedSessions.Should().HaveCount(2);
        var finalOutputs = _sessionManager.PersistedSessions[^1].Outputs!;
        finalOutputs.Should().HaveCount(2, "classify + summarize each stored an addressable SessionOutput");

        // The classification event's ledger key IS a stored entry key.
        var classification = (EventClassificationData)events.Single(e => e.Type == EventRuleSseEvents.Classification).Data!;
        finalOutputs.Select(o => o.Key).Should().Contain(classification.LedgerKey);

        // The summary event renders the STORED payload (render follows store).
        var output = (EventOutputData)events.Single(e => e.Type == EventRuleSseEvents.Output).Data!;
        var stored = finalOutputs.Single(o => o.Key == output.LedgerKey);
        output.Payload.GetRawText().Should().Be(stored.Payload.GetRawText());
        stored.SourceRefs.Should().BeEquivalentTo(new[] { "file-1" }, "sourceRefs carry identifiers only (NFR-07)");
    }

    [Fact]
    public async Task FireAsync_ClassifyResult_PersistedOntoSessionFileManifestFields()
    {
        await FireAsync(new[] { "file-1" });

        // The pre-existing ChatSessionFile confidence fields (chat-routing-redesign-r1) are
        // the Layer-0 wiring target — persisted together with the classify ledger write.
        var persistedFile = _sessionManager.PersistedSessions[0].UploadedFiles!.Single();
        persistedFile.ClassifiedDocType.Should().Be("nda");
        persistedFile.ClassifiedConfidence.Should().Be(0.95);
    }

    // ─── Rule resolution edge: no members ───────────────────────────────────────────────

    [Fact]
    public async Task FireAsync_NoBindingDeclaresEvent_NoticesNoRule()
    {
        _routing
            .Setup(r => r.ResolveEventBindingsAsync("document_uploaded", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Binding>());

        var events = await FireAsync(new[] { "file-1" });

        ((EventNoticeData)events.Single(e => e.Type == EventRuleSseEvents.Notice).Data!)
            .Reason.Should().Be(EventNoticeReasons.NoRule);
        _executed.Should().BeEmpty();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────────────

    private static async Task<List<ChatSseEvent>> CollectAsync(IAsyncEnumerable<ChatSseEvent> stream)
    {
        var events = new List<ChatSseEvent>();
        await foreach (var e in stream)
        {
            events.Add(e);
        }
        return events;
    }

    private static Binding ClassifyBinding() => new()
    {
        BindingId = ClassifyBindingId,
        ConsumerType = "chat-classify",
        Ucid = "UC-A-7",
        ActionId = ClassifyActionId,
        ActionKind = ActionKind.Prompted,
        Disposition = BindingDisposition.Informational,
        OnEventBindings = new[] { new OnEventBinding { Event = "document_uploaded", Order = 1 } },
    };

    private static Binding SummarizeBinding() => new()
    {
        BindingId = SummarizeBindingId,
        ConsumerType = "chat-summarize",
        Ucid = "UC-A-1",
        ActionId = SummarizeActionId,
        ActionKind = ActionKind.Prompted,
        Disposition = BindingDisposition.Informational,
        OnEventBindings = new[] { new OnEventBinding { Event = "document_uploaded", Order = 2 } },
        ChipTransitions = new[]
        {
            new ChipTransition { TargetBindingId = SummarizeBindingId.ToString(), ChipLabel = "Summarize again" },
        },
    };

    private static ChatSession BuildSession(params ChatSessionFile[] files) => new(
        SessionId: SessionId,
        TenantId: TenantId,
        DocumentId: null,
        PlaybookId: null,
        CreatedAt: DateTimeOffset.UtcNow,
        LastActivity: DateTimeOffset.UtcNow,
        Messages: Array.Empty<ChatMessage>())
    {
        UploadedFiles = files,
    };

    private static ChatSessionFile BuildFile(string fileId, string fileName) => new(
        FileId: fileId,
        FileName: fileName,
        ContentType: "application/pdf",
        SizeBytes: 1024,
        SearchDocumentIdsCsv: $"{fileId}_s_0",
        UploadedAt: DateTimeOffset.UtcNow)
    {
        ExtractedText = "extracted text",
    };

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Recording seam over the production ChatSessionManager virtuals — the same ADR-038
    /// module boundary <see cref="OutputRouterTests"/> uses, extended with a stubbed
    /// GetSessionAsync (public virtual).
    /// </summary>
    private sealed class RecordingChatSessionManager : ChatSessionManager
    {
        public RecordingChatSessionManager() : base(
            cache: Mock.Of<ITenantCache>(),
            dataverseRepository: Mock.Of<IChatDataverseRepository>(),
            logger: Mock.Of<ILogger<ChatSessionManager>>(),
            persistence: null,
            cleanupSignal: null)
        {
        }

        public ChatSession? SessionToReturn { get; set; }

        public List<ChatSession> PersistedSessions { get; } = new();

        public override Task<ChatSession?> GetSessionAsync(string tenantId, string sessionId, CancellationToken ct = default)
            => Task.FromResult(SessionToReturn);

        internal override Task UpdateSessionCacheAsync(ChatSession session, CancellationToken ct = default)
        {
            PersistedSessions.Add(session);
            return Task.CompletedTask;
        }
    }
}
