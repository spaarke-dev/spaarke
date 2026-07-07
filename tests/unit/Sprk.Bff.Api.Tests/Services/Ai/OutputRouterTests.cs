using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for <see cref="OutputRouter"/> — the universal ledger write-before-render seam
/// (ADR-040 / FR-P1-02, spaarke-ai-architecture-redesign-r1 task 021).
///
/// <para>
/// Module boundary per ADR-038: the only test double is the <see cref="ChatSessionManager"/>
/// persistence seam (recording subclass over the same internal-virtual production write path
/// the orchestrator suite uses). Everything else is the real router.
/// </para>
/// <para>
/// <b>KEEP rationale (maintain-class)</b>: each fact anchors a contract later phases build on —
/// the <c>{bindingId}@t{n}</c> addressing scheme (P3 <c>ledger_resolution</c> resolves these
/// keys), store-before-route ordering, the loud P3 disposition stubs (silent inline-render
/// fallback is the failure mode ADR-040 exists to prevent), and the monotonic turn-ordinal
/// allocation documented in the task-021 turn-numbering decision.
/// </para>
/// </summary>
public class OutputRouterTests
{
    private const string TenantId = "tenant-router";
    private const string SessionId = "session-router";

    private static readonly Guid BindingId = Guid.Parse("2f9c30d1-aaaa-f111-ab0e-70a8a590c51c");

    private readonly RecordingChatSessionManager _sessionManager = new();

    private OutputRouter CreateSut() => new(_sessionManager, Mock.Of<ILogger<OutputRouter>>());

    // ─── Store-then-route: the ADR-040 core contract ───────────────────────────────────────────

    [Fact]
    public async Task RouteAsync_InformationalDisposition_StoresEntryThenReturnsIt()
    {
        var session = BuildSession();
        var output = ParseJson("""{"summary":"stored summary"}""");

        var routed = await CreateSut().RouteAsync(
            session, BuildBinding(), output, sourceRefs: new[] { "file-1" });

        // The write went through the persistence seam BEFORE RouteAsync returned…
        _sessionManager.PersistedSessions.Should().ContainSingle();
        var persistedEntry = _sessionManager.PersistedSessions[0].Outputs!.Single();

        // …and the returned entry IS the stored entry (render follows store — informational
        // renderers read the payload from here, never from pre-store state).
        routed.Entry.Should().BeSameAs(persistedEntry);
        routed.Entry.Key.Should().Be(SessionLedger.BuildOutputKey(BindingId.ToString(), 1));
        routed.Entry.Payload.GetRawText().Should().Be("""{"summary":"stored summary"}""");
        routed.Entry.SourceRefs.Should().BeEquivalentTo(new[] { "file-1" });
        routed.Session.Outputs.Should().ContainSingle(o => o.Key == routed.Entry.Key);
    }

    [Fact]
    public async Task RouteAsync_AppendsToExistingOutputs_NeverMutatesOrDrops()
    {
        var existing = BuildOutput("earlier-binding", turn: 1);
        var session = BuildSession() with { Outputs = new[] { existing } };

        var routed = await CreateSut().RouteAsync(session, BuildBinding(), ParseJson("{}"));

        routed.Session.Outputs.Should().HaveCount(2, "the ledger is append-only (ADR-040)");
        routed.Session.Outputs![0].Should().BeSameAs(existing,
            "existing entries are never mutated or dropped by a new write");
    }

    // ─── Turn allocation: monotonic per-session output ordinal (task-021 decision) ────────────

    [Fact]
    public async Task RouteAsync_TurnOrdinal_AllocatesMaxPlusOne_NotCount()
    {
        // A session restored with a single output at turn 5 must allocate turn 6 — max+1, not
        // count+1 — so keys stay unique even if earlier entries were compacted away.
        var session = BuildSession() with { Outputs = new[] { BuildOutput("other", turn: 5) } };

        var routed = await CreateSut().RouteAsync(session, BuildBinding(), ParseJson("{}"));

        routed.Entry.Turn.Should().Be(6);
        routed.Entry.Key.Should().Be(SessionLedger.BuildOutputKey(BindingId.ToString(), 6));
    }

    // ─── Ledger vocabulary mapping ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RouteAsync_LegacyBindingWithoutUcid_FallsBackToConsumerType()
    {
        var binding = BuildBinding() with { Ucid = null };

        var routed = await CreateSut().RouteAsync(BuildSession(), binding, ParseJson("{}"));

        routed.Entry.UcId.Should().Be("chat-summarize",
            "legacy Binding rows carry null sprk_ucid; the consumer-type code is the stable fallback vocabulary id");
    }

    // ─── Loud P3 stubs: no silent fallback, storage never couples to rendering ────────────────
    // (email left this list at task 043; work_product at task 047 — their legs are
    // implemented; see the email + work_product facts below)

    [Theory]
    [InlineData(BindingDisposition.Overlay, "overlay")]
    [InlineData(BindingDisposition.Record, "record")]
    [InlineData(BindingDisposition.Notification, "notification")]
    public async Task RouteAsync_NonInformationalDisposition_StoresEntryThenThrowsLoudNotSupported(
        BindingDisposition disposition, string expectedLedgerValue)
    {
        var binding = BuildBinding() with { Disposition = disposition };

        var act = () => CreateSut().RouteAsync(BuildSession(), binding, ParseJson("{}"));

        var thrown = await act.Should().ThrowAsync<NotSupportedException>(
            "P3 disposition legs FAIL LOUDLY — a silent inline-render fallback would " +
            "violate the disposition-is-the-only-rendering-contract rule");
        thrown.Which.Message.Should().Contain(expectedLedgerValue).And.Contain("P3");

        // Storage preceded the (failed) routing: the entry exists and is addressable.
        _sessionManager.PersistedSessions.Should().ContainSingle()
            .Which.Outputs.Should().ContainSingle(o =>
                o.Disposition == expectedLedgerValue
                && o.Key == SessionLedger.BuildOutputKey(BindingId.ToString(), 1));
    }

    // ─── Email disposition leg (FR-P3-04, task 043): store THEN deliver ───────────────────────

    [Fact]
    public async Task RouteAsync_EmailDisposition_StoresEntryThenDeliversEnvelopeViaSender()
    {
        var binding = BuildBinding() with { Disposition = BindingDisposition.Email };
        var sender = new RecordingEmailSender(_sessionManager);
        var payload = ParseJson("""
            {
              "sections": { "tldr": { "summary": "s" } },
              "email": { "to": ["user@contoso.com"], "subject": "Your Daily Briefing", "htmlBody": "<html>b</html>" }
            }
            """);

        var routed = await new OutputRouter(_sessionManager, Mock.Of<ILogger<OutputRouter>>(), sender)
            .RouteAsync(BuildSession(), binding, payload);

        // Store preceded delivery, and the entry is the addressable stored one.
        _sessionManager.PersistedSessions.Should().ContainSingle();
        routed.Entry.Disposition.Should().Be("email");

        var envelope = sender.Sent.Should().ContainSingle().Subject;
        envelope.To.Should().BeEquivalentTo(new[] { "user@contoso.com" });
        envelope.Subject.Should().Be("Your Daily Briefing");
        envelope.HtmlBody.Should().Contain("<html>");
        envelope.CorrelationId.Should().Be(routed.Entry.Key, "the ledger key is the delivery correlation id");
        sender.StoredCountAtSend.Should().Be(1, "the ledger write happens BEFORE delivery (ADR-040)");
    }

    [Fact]
    public async Task RouteAsync_EmailDisposition_MissingEnvelope_StoresThenThrowsLoud()
    {
        var binding = BuildBinding() with { Disposition = BindingDisposition.Email };
        var sender = new RecordingEmailSender(_sessionManager);

        var act = () => new OutputRouter(_sessionManager, Mock.Of<ILogger<OutputRouter>>(), sender)
            .RouteAsync(BuildSession(), binding, ParseJson("""{"sections":{}}"""));

        (await act.Should().ThrowAsync<InvalidOperationException>(
            "a malformed capability payload fails LOUDLY — never a silent skip"))
            .Which.Message.Should().Contain("email");
        _sessionManager.PersistedSessions.Should().ContainSingle("storage still precedes the failed delivery");
        sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task RouteAsync_EmailDisposition_NoSenderRegistered_StoresThenThrowsLoud()
    {
        var binding = BuildBinding() with { Disposition = BindingDisposition.Email };

        // Constructed WITHOUT a sender (the pre-P3 shape) — delivery must fail loudly.
        var act = () => CreateSut().RouteAsync(BuildSession(), binding, ParseJson("""
            {"email":{"to":["u@x.com"],"subject":"s","htmlBody":"b"}}
            """));

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("IEmailDispositionSender");
        _sessionManager.PersistedSessions.Should().ContainSingle();
    }

    // ─── Work-product disposition leg (FR-P3-08, task 047): store THEN persist ────────────────

    [Fact]
    public async Task RouteAsync_WorkProductDisposition_StoresEntryThenPersistsToHostRecord()
    {
        var binding = BuildBinding() with { Disposition = BindingDisposition.WorkProduct };
        var persister = new RecordingWorkProductPersister(_sessionManager);
        var session = BuildSessionWithHostContext();

        var routed = await new OutputRouter(
                _sessionManager, Mock.Of<ILogger<OutputRouter>>(), emailSender: null, workProductPersister: persister)
            .RouteAsync(session, binding, ParseJson("""{"summary":"matter work product"}"""));

        // Store preceded persistence, and the persister received the STORED entry.
        _sessionManager.PersistedSessions.Should().ContainSingle();
        persister.StoredCountAtPersist.Should().Be(1,
            "the ledger write happens BEFORE the host-record persistence (ADR-040 storage precedes persistence)");
        var call = persister.Persisted.Should().ContainSingle().Subject;
        call.Entry.Should().BeSameAs(routed.Entry, "the persisted envelope derives from the STORED ledger entry");
        call.Entry.Disposition.Should().Be("work_product");
        call.Binding.Should().BeSameAs(binding);
        call.HostContext.Should().BeSameAs(session.HostContext,
            "the session's host record IS the persistence target");
    }

    [Fact]
    public async Task RouteAsync_WorkProductDisposition_NoPersisterRegistered_StoresThenThrowsLoud()
    {
        var binding = BuildBinding() with { Disposition = BindingDisposition.WorkProduct };

        // Constructed WITHOUT a persister — persistence must fail loudly, never a silent skip.
        var act = () => CreateSut().RouteAsync(BuildSessionWithHostContext(), binding, ParseJson("{}"));

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("IWorkProductRecordPersister");
        _sessionManager.PersistedSessions.Should().ContainSingle("storage still precedes the failed persistence");
    }

    [Fact]
    public async Task RouteAsync_WorkProductDisposition_NoHostContext_StoresThenThrowsLoud()
    {
        var binding = BuildBinding() with { Disposition = BindingDisposition.WorkProduct };
        var persister = new RecordingWorkProductPersister(_sessionManager);

        // Session WITHOUT a host record context — there is no record to persist to.
        var act = () => new OutputRouter(
                _sessionManager, Mock.Of<ILogger<OutputRouter>>(), emailSender: null, workProductPersister: persister)
            .RouteAsync(BuildSession(), binding, ParseJson("{}"));

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("HostContext");
        _sessionManager.PersistedSessions.Should().ContainSingle("storage still precedes the failed persistence");
        persister.Persisted.Should().BeEmpty("a session without a host record has no persistence target");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static Binding BuildBinding() => new()
    {
        BindingId = BindingId,
        ConsumerType = "chat-summarize",
        Ucid = "UC-A-1",
        Disposition = BindingDisposition.Informational,
    };

    private static ChatSession BuildSession() => new(
        SessionId: SessionId,
        TenantId: TenantId,
        DocumentId: null,
        PlaybookId: null,
        CreatedAt: DateTimeOffset.UtcNow,
        LastActivity: DateTimeOffset.UtcNow,
        Messages: Array.Empty<ChatMessage>());

    private static ChatSession BuildSessionWithHostContext() => BuildSession() with
    {
        HostContext = new ChatHostContext("matter", "7d9c30d1-bbbb-f111-ab0e-70a8a590c51c"),
    };

    private static SessionOutput BuildOutput(string bindingId, int turn) => new()
    {
        Key = SessionLedger.BuildOutputKey(bindingId, turn),
        BindingId = bindingId,
        UcId = "UC-X-0",
        Turn = turn,
        Disposition = "informational",
        Payload = ParseJson("{}"),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Recording <see cref="IEmailDispositionSender"/> — captures delivered envelopes and
    /// the persisted-session count AT send time (proves store-precedes-delivery ordering).
    /// </summary>
    private sealed class RecordingEmailSender : IEmailDispositionSender
    {
        private readonly RecordingChatSessionManager? _sessionManager;

        public RecordingEmailSender(RecordingChatSessionManager? sessionManager = null)
        {
            _sessionManager = sessionManager;
        }

        public List<EmailDispositionEnvelope> Sent { get; } = new();

        public int StoredCountAtSend { get; private set; }

        public Task SendAsync(EmailDispositionEnvelope envelope, CancellationToken cancellationToken = default)
        {
            StoredCountAtSend = _sessionManager?.PersistedSessions.Count ?? 0;
            Sent.Add(envelope);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Recording <see cref="IWorkProductRecordPersister"/> — captures persisted entries and
    /// the persisted-session count AT persist time (proves store-precedes-persistence ordering).
    /// </summary>
    private sealed class RecordingWorkProductPersister : IWorkProductRecordPersister
    {
        private readonly RecordingChatSessionManager? _sessionManager;

        public RecordingWorkProductPersister(RecordingChatSessionManager? sessionManager = null)
        {
            _sessionManager = sessionManager;
        }

        public List<(SessionOutput Entry, Binding Binding, ChatHostContext HostContext)> Persisted { get; } = new();

        public int StoredCountAtPersist { get; private set; }

        public Task<WorkProductPersistenceReceipt> PersistAsync(
            SessionOutput entry,
            Binding binding,
            ChatHostContext hostContext,
            CancellationToken cancellationToken = default)
        {
            StoredCountAtPersist = _sessionManager?.PersistedSessions.Count ?? 0;
            Persisted.Add((entry, binding, hostContext));
            return Task.FromResult(new WorkProductPersistenceReceipt
            {
                EntityLogicalName = "sprk_matter",
                RecordId = Guid.Parse(hostContext.EntityId),
                TargetField = "sprk_mattersummary",
                LedgerKey = entry.Key,
            });
        }
    }

    /// <summary>
    /// Recording subclass over the internal-virtual production persistence seam
    /// (<see cref="ChatSessionManager.UpdateSessionCacheAsync"/>) — the ADR-038 module
    /// boundary for these tests.
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

        public List<ChatSession> PersistedSessions { get; } = new();

        internal override Task UpdateSessionCacheAsync(ChatSession session, CancellationToken ct = default)
        {
            PersistedSessions.Add(session);
            return Task.CompletedTask;
        }
    }
}
