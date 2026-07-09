using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// <b>ComposeDisposition v1 contract test</b> (spaarke-ai-architecture-redesign-r2 task 010 —
/// the FIRST Phase-A0 seam; spec FR-A0-06 / FR-A0-08). Locks the shape the parallel
/// <c>spaarkeai-compose-r2</c> project consumes for FR-04 / FR-16 / FR-17 so a schema drift
/// (dropped field, renamed key, opened version, a payload field leaking into the platform
/// envelope, or a broken supersession round-trip) fails HERE, not in Compose's editor at UAT.
/// </summary>
/// <remarks>
/// <para>
/// <b>Self-contained by design</b>: the walking-skeleton producer (<see cref="ComposeDisposition.BuildFrame"/>)
/// and consumer (<see cref="ComposeDisposition.Materialize"/> / <see cref="ComposeDisposition.ResolveCurrent"/>)
/// are pure over an in-memory append-only ledger — no DI, no <c>WebApplicationFactory</c>, no
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration assertions (ADR-038 KEEP-path contract test).
/// </para>
/// <para>
/// <b>KEEP rationale (maintain-class, contract path)</b>: this is a published cross-project seam.
/// The frame + supersession keying are a contract Compose builds its draft-into-editor leg on;
/// this round-trip is its regression anchor.
/// </para>
/// </remarks>
public class ComposeDispositionContractTests
{
    private const string BindingId = "4b2c30d1-0010-f111-ab0e-70a8a590c51c";

    // The structured-edit payload is COMPOSE-OWNED and OPAQUE to the platform. This test treats it
    // as an arbitrary blob and NEVER asserts on its internal shape — proving the core stays
    // envelope-only. (These keys mirror Compose's design §6.1 purely to prove opacity.)
    private const string OpaqueComposePayload =
        """{"target_text":"the party shall","new_text":"each party shall","match_mode":"strict","rationale":"symmetry","sources":[{"type":"playbook","id":"pb-7"}]}""";

    // ─── Shape + version pin ──────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildFrame_FromStoredComposeEntry_PinsVersionedEnvelopeShape()
    {
        var entry = StoreComposeOutput(turn: 1, OpaqueComposePayload);

        var frame = ComposeDisposition.BuildFrame(entry);

        frame.Version.Should().Be("1.0", "v1 frames carry a literal version field (tolerant-reader gate)");
        frame.Disposition.Should().Be("compose", "the frame rides the ADR-040 compose rendering member");
        frame.LedgerRef.Should().Be(SessionLedger.BuildOutputKey(BindingId, 1),
            "the frame's load-bearing field is the addressable {bindingId}@t{n} ledger key");
        frame.BindingId.Should().Be(BindingId);
        frame.Turn.Should().Be(1);
        frame.SupersedesRef.Should().BeNull("the first compose output of a binding supersedes nothing");
        frame.Status.Should().Be("ready");
    }

    [Fact]
    public void ComposeFrame_SerializedWire_CarriesEnvelopeOnly_NoStructuredEditPayload()
    {
        var entry = StoreComposeOutput(turn: 1, OpaqueComposePayload);
        var frame = ComposeDisposition.BuildFrame(entry);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(frame));
        var root = doc.RootElement;

        // Envelope members present with snake_case wire names.
        root.GetProperty("version").GetString().Should().Be("1.0");
        root.GetProperty("disposition").GetString().Should().Be("compose");
        root.GetProperty("ledger_ref").GetString().Should().Be(SessionLedger.BuildOutputKey(BindingId, 1));
        root.GetProperty("binding_id").GetString().Should().Be(BindingId);
        root.TryGetProperty("turn", out _).Should().BeTrue();
        root.TryGetProperty("status", out _).Should().BeTrue();
        root.TryGetProperty("created_at", out _).Should().BeTrue();

        // PAYLOAD-OWNERSHIP: the platform envelope leaks ZERO editor semantics. The frame carries
        // a ledger reference, not the edit body (storage-precedes-rendering).
        foreach (var composeOwnedField in new[] { "target_text", "new_text", "match_mode", "rationale", "sources", "payload" })
        {
            root.TryGetProperty(composeOwnedField, out _).Should().BeFalse(
                $"'{composeOwnedField}' is Compose-owned + opaque inside the SessionOutput payload — it must NOT appear on the platform frame");
        }
    }

    // ─── Tolerant reader: unknown field ignored ───────────────────────────────────────────────

    [Fact]
    public void Deserialize_FrameWithUnknownAdditiveField_IgnoresUnknown_AndKeepsKnownMembers()
    {
        // A forward v1.x wire frame carrying a member this v1 reader has never seen.
        var wireWithUnknown =
            $$"""
            {
              "version": "1.0",
              "disposition": "compose",
              "ledger_ref": "{{SessionLedger.BuildOutputKey(BindingId, 1)}}",
              "binding_id": "{{BindingId}}",
              "turn": 1,
              "status": "ready",
              "created_at": "2026-07-08T12:00:00Z",
              "future_v1x_field": {"nested": true}
            }
            """;

        var frame = JsonSerializer.Deserialize<ComposeDispositionFrame>(wireWithUnknown);

        frame.Should().NotBeNull("an unknown additive field must NEVER fail v1 deserialization (tolerant reader)");
        frame!.Version.Should().Be("1.0");
        frame.LedgerRef.Should().Be(SessionLedger.BuildOutputKey(BindingId, 1));
        frame.Turn.Should().Be(1);
        frame.UnknownMembers.Should().ContainKey("future_v1x_field",
            "unknown members are captured for forward-compat and ignored by v1 rendering");
    }

    // ─── Store-before-render round-trip (positive) ────────────────────────────────────────────

    [Fact]
    public void Materialize_WhenLedgerHoldsReferencedEntry_ReMaterializesOpaquePayloadVerbatim()
    {
        var ledger = new InMemoryLedger();
        var entry = StoreComposeOutput(turn: 1, OpaqueComposePayload);
        ledger.Append(entry); // STORE precedes...

        var frame = ComposeDisposition.BuildFrame(entry); // ...frame emission (render-follows-store)

        var materialized = ComposeDisposition.Materialize(frame, ledger.Lookup);

        materialized.Key.Should().Be(entry.Key, "the consumer re-materializes the exact stored entry via ledger_ref");
        // Payload is opaque: assert it round-trips verbatim, WITHOUT the core parsing its shape.
        materialized.Payload.GetRawText().Should().Be(entry.Payload.GetRawText());
    }

    // ─── Store-before-render (NEGATIVE) ───────────────────────────────────────────────────────

    [Fact]
    public void Materialize_WhenNoPriorLedgerEntry_Throws_EnforcingStoreBeforeRender()
    {
        var emptyLedger = new InMemoryLedger();
        // A frame whose ledger_ref was never stored (the store-before-render violation).
        var orphanFrame = new ComposeDispositionFrame
        {
            Version = ComposeDisposition.Version,
            Disposition = ComposeDisposition.DispositionValue,
            LedgerRef = SessionLedger.BuildOutputKey(BindingId, 99),
            BindingId = BindingId,
            Turn = 99,
        };

        var act = () => ComposeDisposition.Materialize(orphanFrame, emptyLedger.Lookup);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*store-before-render*", "a compose disposition MUST NOT render before its ledger write (ADR-040)");
    }

    [Fact]
    public void BuildFrame_FromNonComposeEntry_Throws()
    {
        var workProduct = new SessionOutput
        {
            Key = SessionLedger.BuildOutputKey(BindingId, 1),
            BindingId = BindingId,
            UcId = "UC-A-1",
            Turn = 1,
            Disposition = "work_product",
            Payload = ParseJson("""{"tldr":"t"}"""),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var act = () => ComposeDisposition.BuildFrame(workProduct);

        act.Should().Throw<ArgumentException>("a compose frame can only be produced from a compose ledger entry");
    }

    // ─── Supersession round-trip (undo/replace via new entry, re-materialize from ledger) ─────

    [Fact]
    public void Supersession_NewComposeOutput_SupersedesPrior_AndConsumerReMaterializesCurrentFromLedger()
    {
        var ledger = new InMemoryLedger();

        // t1 — original draft.
        var original = StoreComposeOutput(turn: 1, """{"target_text":"a","new_text":"b"}""");
        ledger.Append(original);
        var originalFrame = ComposeDisposition.BuildFrame(original);

        // t2 — undo/replace is a NEW compose output superseding t1, addressable by {bindingId}@t2.
        // There is NO client-only DOM undo: the replacement is a ledger append.
        var replacement = StoreComposeOutput(turn: 2, """{"target_text":"a","new_text":"c"}""");
        ledger.Append(replacement);
        var replacementFrame = ComposeDisposition.BuildFrame(
            replacement, supersedesRef: originalFrame.LedgerRef);

        // The superseding frame names its predecessor.
        replacementFrame.SupersedesRef.Should().Be(SessionLedger.BuildOutputKey(BindingId, 1));
        replacementFrame.LedgerRef.Should().Be(SessionLedger.BuildOutputKey(BindingId, 2));

        // The consumer re-materializes CURRENT state from the ledger (highest turn) — not from any
        // prior client render.
        var current = ComposeDisposition.ResolveCurrent(ledger.All, BindingId);
        current.Should().NotBeNull();
        current!.Key.Should().Be(replacement.Key, "current compose state is the superseding (highest-turn) entry");

        // Both prior + current remain addressable (append-only ledger; supersession is not deletion).
        ComposeDisposition.Materialize(originalFrame, ledger.Lookup).Key.Should().Be(original.Key);
        ComposeDisposition.Materialize(replacementFrame, ledger.Lookup).Key.Should().Be(replacement.Key);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static SessionOutput StoreComposeOutput(int turn, string opaquePayloadJson) => new()
    {
        Key = SessionLedger.BuildOutputKey(BindingId, turn),
        BindingId = BindingId,
        UcId = "UC-COMPOSE-1",
        Turn = turn,
        Disposition = ComposeDisposition.DispositionValue,
        Payload = ParseJson(opaquePayloadJson),
        CreatedAt = DateTimeOffset.Parse("2026-07-08T12:00:00Z"),
    };

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>Minimal append-only in-memory ledger — the store the walking-skeleton consumer reads from.</summary>
    private sealed class InMemoryLedger
    {
        private readonly List<SessionOutput> _entries = new();
        public void Append(SessionOutput entry) => _entries.Add(entry);
        public IEnumerable<SessionOutput> All => _entries;
        public SessionOutput? Lookup(string key)
        {
            foreach (var e in _entries)
            {
                if (string.Equals(e.Key, key, StringComparison.Ordinal)) return e;
            }
            return null;
        }
    }
}
