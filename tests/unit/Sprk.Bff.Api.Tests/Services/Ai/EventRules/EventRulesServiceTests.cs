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
/// (spaarke-ai-architecture-redesign-r1 task 022; revised by the G-P1 UAT round-1
/// fix wave, 2026-07-05 operator ruling: "auto-classify, chip-offered summarize").
///
/// <para>
/// <b>KEEP rationale (maintain-class)</b>: each fact anchors a contract the Event path
/// is BUILT on — the ordered-member resolution from <c>sprk_oneventbindings</c>, the
/// FR-P1-03 bounds (daily cap counted members × batch files / opt-out / explicit-command
/// supersede) plus the empty-attachments precondition and its bounded manifest readiness
/// probe (Defect 3), the every-file execution + per-file failure resilience (Defect 2
/// ruling), the chip emission contract (single-file transition chips, bulk
/// "…all N files?" + per-file chips), the M4 classify-confidence policy branches
/// (latent on the classify-only launch rule; live for multi-member rules), and the
/// ADR-040 ledger-before-render ordering (proven through the REAL
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

    /// <summary>Ordered record of executed (actionId, fileName) pairs — proves member order + every-file execution.</summary>
    private readonly List<(Guid ActionId, string FileName)> _executed = new();

    public EventRulesServiceTests()
    {
        // Default happy-path wiring mirrors the POST-RULING launch catalog: session with
        // 1 file, [chat-classify(1)] rule whose chip transitions offer Summarize, no
        // opt-out, empty budget, high-confidence classify output. Readiness-probe delay
        // is zeroed so probe-path tests stay instant (attempts still count).
        _options.ReadinessProbeDelayMs = 0;
        _sessionManager.SessionToReturn = BuildSession(BuildFile("file-1", "contract.pdf"));

        UseRule(ClassifyBinding());

        _scopeResolver
            .Setup(s => s.GetActionAsync(ClassifyActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction { Id = ClassifyActionId, Name = "CLS-CHAT@v1" });
        _scopeResolver
            .Setup(s => s.GetActionAsync(SummarizeActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction { Id = SummarizeActionId, Name = "SUM-CHAT@v1" });

        _textSource
            .Setup(t => t.FetchAsync(TenantId, SessionId, It.IsAny<IReadOnlyList<ChatSessionFile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, IReadOnlyList<ChatSessionFile> files, CancellationToken _) =>
                new SessionFileText { ExtractedText = "extracted text", DisplayName = files[0].FileName });

        _userState.Setup(u => u.IsOptedOutAsync(TenantId, UserOid, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _userState.Setup(u => u.GetTodayExecutionCountAsync(TenantId, UserOid, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _userState.Setup(u => u.AddExecutionsAsync(TenantId, UserOid, It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        SetupActionOutput(ClassifyActionId, """{"docType":"nda","confidence":0.95,"rationale":"mutual confidentiality"}""");
        SetupActionOutput(SummarizeActionId, """{"tldr":["bullet"],"summary":"a summary"}""");
    }

    private void UseRule(params Binding[] members)
        => _routing
            .Setup(r => r.ResolveEventBindingsAsync("document_uploaded", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(members);

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

    // ─── The headline flow (post-ruling): upload, type nothing → classify, chips ───────

    [Fact]
    public async Task FireAsync_UploadNoTypedCommand_YieldsClassificationThenChips_NoAutoSummary()
    {
        var events = await FireAsync(new[] { "file-1" });

        events.Select(e => e.Type).Should().ContainInOrder(
            EventRuleSseEvents.Classification,
            EventRuleSseEvents.Chips,
            EventRuleSseEvents.Done);
        events.Should().NotContain(e => e.Type == EventRuleSseEvents.Output,
            "2026-07-05 operator ruling: summarize NO LONGER auto-runs on upload — it is chip-offered");

        _executed.Select(x => x.ActionId).Should().Equal(ClassifyActionId);

        var classification = (EventClassificationData)events.Single(e => e.Type == EventRuleSseEvents.Classification).Data!;
        classification.DocType.Should().Be("nda");
        classification.Confidence.Should().Be(0.95);
        classification.BindingId.Should().Be(ClassifyBindingId.ToString());
    }

    [Fact]
    public async Task FireAsync_ChipsEvent_CarriesBindingChipTransitions_WithBatchFileIdsAndAttachmentFlag()
    {
        var events = await FireAsync(new[] { "file-1" });

        var chips = (EventChipsData)events.Single(e => e.Type == EventRuleSseEvents.Chips).Data!;
        chips.SourceBindingId.Should().Be(ClassifyBindingId.ToString());
        var summarizeChip = chips.Chips.Should().ContainSingle().Subject;
        summarizeChip.Label.Should().Be("Summarize");
        summarizeChip.TargetBindingId.Should().Be(SummarizeBindingId.ToString());
        summarizeChip.RequiresAttachments.Should().BeTrue(
            "the authored requires_attachments flag must survive the transition → chip mapping (G-P1 Defect 1)");
        JsonSerializer.Serialize(summarizeChip.Args).Should().Contain("file-1",
            "the chip pre-fills the batch's fileIds so the Click dispatch targets the same files");
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

    // ─── Precondition: empty attachments + Defect-3 manifest readiness probe ───────────

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
    public async Task FireAsync_FileIdsNeverAppearInManifest_ProbesThenNoticesNoAttachments()
    {
        var events = await FireAsync(new[] { "not-in-manifest" });

        ((EventNoticeData)events.Single(e => e.Type == EventRuleSseEvents.Notice).Data!)
            .Reason.Should().Be(EventNoticeReasons.NoAttachments);
        _executed.Should().BeEmpty();
        // Wait-briefly-or-degrade: the bounded probe re-read the session before degrading.
        _sessionManager.GetSessionCallCount.Should().Be(1 + _options.ReadinessProbeAttempts,
            "Defect 3 — the service re-reads the manifest up to ReadinessProbeAttempts times before the notice");
    }

    [Fact]
    public async Task FireAsync_FileAppearsInManifestDuringReadinessProbe_Runs()
    {
        // First read: manifest does NOT contain the uploaded file yet (202 → manifest
        // visibility lag). Second read (probe): it does. The rule must run, not notice.
        _sessionManager.SessionQueue.Enqueue(BuildSession()); // initial read — empty manifest
        _sessionManager.SessionToReturn = BuildSession(BuildFile("file-late", "late.pdf"));

        var events = await FireAsync(new[] { "file-late" });

        events.Should().Contain(e => e.Type == EventRuleSseEvents.Classification,
            "the readiness probe resolved the late manifest entry — automatic analysis ran");
        events.Should().NotContain(e => e.Type == EventRuleSseEvents.Notice,
            "no 'files not available' notice when the probe succeeds (Defect 3 acceptance)");
        _executed.Should().ContainSingle(x => x.FileName == "late.pdf");
    }

    [Fact]
    public async Task FireAsync_ProbeDisabled_SingleReadThenNotice()
    {
        _options.ReadinessProbeAttempts = 0;

        var events = await FireAsync(new[] { "not-in-manifest" });

        ((EventNoticeData)events.Single(e => e.Type == EventRuleSseEvents.Notice).Data!)
            .Reason.Should().Be(EventNoticeReasons.NoAttachments);
        _sessionManager.GetSessionCallCount.Should().Be(1, "attempts=0 disables the re-check");
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
            "an opted-out user can still reach the rule's user-visible value (the curated " +
            "chip-transition target — summarize) by explicit Click");
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
    public async Task FireAsync_RemainingBudgetSmallerThanMembersTimesFiles_Defers()
    {
        _options.DailyExecutionCap = 10;
        // 8 used + (1 member × 3 files) = 11 > 10 — the WHOLE batch must fit; no partial runs.
        _userState.Setup(u => u.GetTodayExecutionCountAsync(TenantId, UserOid, It.IsAny<CancellationToken>())).ReturnsAsync(8);
        _sessionManager.SessionToReturn = BuildSession(
            BuildFile("file-1", "first.pdf"),
            BuildFile("file-2", "second.pdf"),
            BuildFile("file-3", "third.pdf"));

        var events = await FireAsync(new[] { "file-1", "file-2", "file-3" });

        ((EventNoticeData)events.Single(e => e.Type == EventRuleSseEvents.Notice).Data!)
            .Reason.Should().Be(EventNoticeReasons.DailyCap);
        _executed.Should().BeEmpty();
    }

    [Fact]
    public async Task FireAsync_HappyPath_ConsumesOneBudgetUnitPerExecution()
    {
        await FireAsync(new[] { "file-1" });

        _userState.Verify(u => u.AddExecutionsAsync(TenantId, UserOid, 1, It.IsAny<CancellationToken>()),
            Times.Exactly(1), "the classify-only rule on a single file is one budget unit (the NFR-09 counting unit)");
    }

    [Fact]
    public async Task FireAsync_BulkUpload_ConsumesOneBudgetUnitPerFile()
    {
        _sessionManager.SessionToReturn = BuildSession(
            BuildFile("file-1", "first.pdf"),
            BuildFile("file-2", "second.pdf"),
            BuildFile("file-3", "third.pdf"));

        await FireAsync(new[] { "file-1", "file-2", "file-3" });

        _userState.Verify(u => u.AddExecutionsAsync(TenantId, UserOid, 1, It.IsAny<CancellationToken>()),
            Times.Exactly(3), "every file of the gesture consumes one classify execution (2026-07-05 ruling — cap accounting)");
    }

    // ─── Bulk batches (bound c revised 2026-07-05): classify EVERY file + bulk chips ────

    [Fact]
    public async Task FireAsync_BulkUpload_ClassifiesEveryFile_AndEmitsBulkPlusPerFileChips()
    {
        _sessionManager.SessionToReturn = BuildSession(
            BuildFile("file-1", "first.pdf"),
            BuildFile("file-2", "second.pdf"),
            BuildFile("file-3", "third.pdf"));

        var events = await FireAsync(new[] { "file-1", "file-2", "file-3" });

        // Every file of the gesture is classified, in request (upload) order.
        _executed.Select(x => x.FileName).Should().Equal("first.pdf", "second.pdf", "third.pdf");
        events.Count(e => e.Type == EventRuleSseEvents.Classification).Should().Be(3,
            "one classification line per file (2026-07-05 ruling)");
        events.Should().NotContain(e => e.Type == EventRuleSseEvents.Output,
            "no auto-summary — summarization is chip-offered");

        var chips = (EventChipsData)events.Single(e => e.Type == EventRuleSseEvents.Chips).Data!;
        var bulk = chips.Chips.Should().ContainSingle(c => c.Label == "Summarize all 3 files?").Subject;
        bulk.TargetBindingId.Should().Be(SummarizeBindingId.ToString(),
            "the bulk chip moved from the retired summarize member to the classify member's transition target");
        JsonSerializer.Serialize(bulk.Args).Should().ContainAll("file-1", "file-2", "file-3");

        chips.Chips.Should().Contain(c => c.Label == "Summarize: second.pdf"
            && c.TargetBindingId == SummarizeBindingId.ToString(),
            "small batches (≤3) also offer per-file summarize chips");
    }

    [Fact]
    public async Task FireAsync_LargeBulkUpload_EmitsBulkChipOnly_NoPerFileChips()
    {
        _sessionManager.SessionToReturn = BuildSession(
            BuildFile("file-1", "a.pdf"), BuildFile("file-2", "b.pdf"),
            BuildFile("file-3", "c.pdf"), BuildFile("file-4", "d.pdf"));

        var events = await FireAsync(new[] { "file-1", "file-2", "file-3", "file-4" });

        var chips = (EventChipsData)events.Single(e => e.Type == EventRuleSseEvents.Chips).Data!;
        chips.Chips.Should().ContainSingle(c => c.Label == "Summarize all 4 files?");
        chips.Chips.Should().NotContain(c => c.Label.StartsWith("Summarize:"),
            "above the per-file chip cap only the bulk chip renders (strip readability)");
    }

    // ─── Composite chip labels use the SHORT form (G-P2 UAT round-1 finding 1) ─────────

    [Fact]
    public async Task FireAsync_BulkUpload_MultiWordChipLabel_DerivesFirstWordForCompositeLabels()
    {
        // The catalog now authors chip_label as a full phrase ("Summarize this document").
        // Composite (bulk + per-file) labels must NOT read "Summarize this document all
        // 3 files?" — without an authored bulk_chip_label the short form is
        // deterministically the first word of chip_label.
        UseRule(ClassifyBinding() with
        {
            ChipTransitions = new[]
            {
                new ChipTransition
                {
                    TargetBindingId = SummarizeBindingId.ToString(),
                    ChipLabel = "Summarize this document",
                    RequiresAttachments = true,
                },
            },
        });
        _sessionManager.SessionToReturn = BuildSession(
            BuildFile("file-1", "first.pdf"),
            BuildFile("file-2", "second.pdf"),
            BuildFile("file-3", "third.pdf"));

        var events = await FireAsync(new[] { "file-1", "file-2", "file-3" });

        var chips = (EventChipsData)events.Single(e => e.Type == EventRuleSseEvents.Chips).Data!;
        chips.Chips.Should().ContainSingle(c => c.Label == "Summarize all 3 files?",
            "the derived bulk label uses the first word of the phrase, never the whole phrase");
        chips.Chips.Should().Contain(c => c.Label == "Summarize: second.pdf",
            "per-file composite labels use the same short form");
        chips.Chips.Should().NotContain(c => c.Label.Contains("this document all"),
            "the pre-fix concatenation bug: '{phrase} all N files?' must not resurface");
    }

    [Fact]
    public async Task FireAsync_BulkUpload_AuthoredBulkChipLabel_WinsOverDerivedFirstWord()
    {
        // Maker-authored bulk_chip_label is the explicit data-driven override for phrases
        // whose first word is not the verb ("Give me a summary" → "Give" would be wrong).
        UseRule(ClassifyBinding() with
        {
            ChipTransitions = new[]
            {
                new ChipTransition
                {
                    TargetBindingId = SummarizeBindingId.ToString(),
                    ChipLabel = "Give me a summary",
                    BulkChipLabel = "Summarize",
                    RequiresAttachments = true,
                },
            },
        });
        _sessionManager.SessionToReturn = BuildSession(
            BuildFile("file-1", "first.pdf"),
            BuildFile("file-2", "second.pdf"));

        var events = await FireAsync(new[] { "file-1", "file-2" });

        var chips = (EventChipsData)events.Single(e => e.Type == EventRuleSseEvents.Chips).Data!;
        chips.Chips.Should().ContainSingle(c => c.Label == "Summarize all 2 files?");
        chips.Chips.Should().Contain(c => c.Label == "Summarize: first.pdf");
        chips.Chips.Should().NotContain(c => c.Label.StartsWith("Give all"),
            "the authored bulk_chip_label wins over the first-word fallback");
    }

    [Fact]
    public async Task FireAsync_SingleFile_TransitionChipKeepsFullPhraseLabel()
    {
        // Single-file transitions render chip_label VERBATIM — the phrase ("Summarize
        // this document") is exactly what the operator ruled the strip should read.
        UseRule(ClassifyBinding() with
        {
            ChipTransitions = new[]
            {
                new ChipTransition
                {
                    TargetBindingId = SummarizeBindingId.ToString(),
                    ChipLabel = "Summarize this document",
                    BulkChipLabel = "Summarize",
                    RequiresAttachments = true,
                },
            },
        });

        var events = await FireAsync(new[] { "file-1" });

        var chips = (EventChipsData)events.Single(e => e.Type == EventRuleSseEvents.Chips).Data!;
        chips.Chips.Should().ContainSingle(c => c.Label == "Summarize this document",
            "bulk_chip_label only affects DERIVED composite labels, never the authored single-file chip");
    }

    // ─── Per-file failure resilience (G-P1 Defect 2/3): one bad file ≠ dead batch ──────

    [Fact]
    public async Task FireAsync_OneFileFailsExecution_OthersStillClassify_AndChipsStillEmit()
    {
        _sessionManager.SessionToReturn = BuildSession(
            BuildFile("file-1", "first.pdf"),
            BuildFile("file-2", "second.pdf"));
        _actionRunner
            .Setup(a => a.RunAsync(
                It.Is<AnalysisAction>(x => x.Id == ClassifyActionId),
                It.Is<DocumentText>(d => d.FileName == "first.pdf"),
                It.IsAny<LinearRunContext>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("executor boom"));

        var events = await FireAsync(new[] { "file-1", "file-2" });

        events.Should().Contain(e => e.Type == "error",
            "the failed file surfaces a rendered error line (never a silent drop)");
        events.Count(e => e.Type == EventRuleSseEvents.Classification).Should().Be(1,
            "the healthy file still classifies — a per-file failure must not kill the batch");
        events.Should().Contain(e => e.Type == EventRuleSseEvents.Chips,
            "next-step chips still emit after a partial batch (Defect 1 acceptance: chips never vanish)");
        events[^1].Type.Should().Be(EventRuleSseEvents.Done);
    }

    // ─── M4 classify-confidence policy branches (multi-member rules; latent on launch rule) ─

    [Fact]
    public async Task FireAsync_MultiMemberRule_ClassifyConfidenceBelowThreshold_FiresM4Confirmation_NextMemberSuspended()
    {
        UseRule(ClassifyBinding(), SummarizeBinding());
        SetupActionOutput(ClassifyActionId, """{"docType":"memo","confidence":0.55,"rationale":"weak signals"}""");

        var events = await FireAsync(new[] { "file-1" });

        events.Select(e => e.Type).Should().ContainInOrder(
            EventRuleSseEvents.Classification,
            EventRuleSseEvents.Confirmation,
            EventRuleSseEvents.Done);
        events.Should().NotContain(e => e.Type == EventRuleSseEvents.Output,
            "below-threshold confidence suspends the following member behind the M4 gate");

        _executed.Select(x => x.ActionId).Should().Equal(ClassifyActionId);

        var confirmation = (EventConfirmationData)events.Single(e => e.Type == EventRuleSseEvents.Confirmation).Data!;
        confirmation.DocType.Should().Be("memo");
        confirmation.Confidence.Should().Be(0.55);
        confirmation.Threshold.Should().Be(0.85);
        confirmation.Chips.Should().ContainSingle(c => c.TargetBindingId == SummarizeBindingId.ToString(),
            "the confirm chip resumes the suspended member via the Click path");
    }

    [Fact]
    public async Task FireAsync_MultiMemberRule_ClassifyConfidenceAtThreshold_ProceedsSilently()
    {
        UseRule(ClassifyBinding(), SummarizeBinding());
        SetupActionOutput(ClassifyActionId, """{"docType":"nda","confidence":0.85,"rationale":"clear"}""");

        var events = await FireAsync(new[] { "file-1" });

        events.Should().NotContain(e => e.Type == EventRuleSseEvents.Confirmation,
            "at-threshold confidence is NOT below the dial — no confirmation turn");
        _executed.Select(x => x.ActionId).Should().ContainInOrder(ClassifyActionId, SummarizeActionId);
    }

    [Fact]
    public async Task FireAsync_MultiMemberRule_ConfigurableThreshold_IsRespected()
    {
        UseRule(ClassifyBinding(), SummarizeBinding());
        _options.ClassifyConfidenceThreshold = 0.5;
        SetupActionOutput(ClassifyActionId, """{"docType":"memo","confidence":0.55,"rationale":"weak"}""");

        var events = await FireAsync(new[] { "file-1" });

        events.Should().NotContain(e => e.Type == EventRuleSseEvents.Confirmation,
            "0.55 clears a 0.5 threshold — the dial is the operator-owned policy bound");
        _executed.Should().HaveCount(2);
    }

    // ─── ADR-040: outputs land in the ledger BEFORE their rendering events ──────────────

    [Fact]
    public async Task FireAsync_MultiMemberRule_BothOutputs_LedgerWrittenBeforeRendering_AndRenderedFromStoredEntry()
    {
        UseRule(ClassifyBinding(), SummarizeBinding());

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
        UseRule();

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

    /// <summary>
    /// Mirrors the post-2026-07-05 seeded chat-classify row: sole document_uploaded
    /// member whose chip transitions offer Summarize (requires_attachments) — the
    /// "auto-classify, chip-offered summarize" launch rule.
    /// </summary>
    private static Binding ClassifyBinding() => new()
    {
        BindingId = ClassifyBindingId,
        ConsumerType = "chat-classify",
        Ucid = "UC-A-7",
        ActionId = ClassifyActionId,
        ActionKind = ActionKind.Prompted,
        Disposition = BindingDisposition.Informational,
        OnEventBindings = new[] { new OnEventBinding { Event = "document_uploaded", Order = 1 } },
        ChipTransitions = new[]
        {
            new ChipTransition
            {
                TargetBindingId = SummarizeBindingId.ToString(),
                ChipLabel = "Summarize",
                RequiresAttachments = true,
            },
        },
    };

    /// <summary>
    /// A second ordered member for the generic multi-member/M4 contract tests (the
    /// launch rule no longer includes summarize, but the Event path stays capability-
    /// agnostic and multi-member rules remain supported).
    /// </summary>
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
    /// GetSessionAsync (public virtual). <see cref="SessionQueue"/> lets readiness-probe
    /// tests script a per-read session sequence (queue drains first, then
    /// <see cref="SessionToReturn"/>); <see cref="GetSessionCallCount"/> proves the
    /// bounded-probe read count.
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

        public Queue<ChatSession?> SessionQueue { get; } = new();

        public int GetSessionCallCount { get; private set; }

        public List<ChatSession> PersistedSessions { get; } = new();

        public override Task<ChatSession?> GetSessionAsync(string tenantId, string sessionId, CancellationToken ct = default)
        {
            GetSessionCallCount++;
            return Task.FromResult(SessionQueue.Count > 0 ? SessionQueue.Dequeue() : SessionToReturn);
        }

        internal override Task UpdateSessionCacheAsync(ChatSession session, CancellationToken ct = default)
        {
            PersistedSessions.Add(session);
            return Task.CompletedTask;
        }
    }
}
