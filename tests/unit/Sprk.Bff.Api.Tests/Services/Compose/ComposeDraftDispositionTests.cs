using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="ComposeDraftDisposition"/> — FR-04's draft-into-editor consumer of
/// the core <see cref="ComposeDisposition"/> v1 A0 seam (spaarkeai-compose-r2 task 016).
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>unit/domain</c>. <see cref="ComposeDraftDisposition"/> is a
/// pure, dependency-free payload ↔ envelope bridge (ADR-013 facade boundary; no constructor,
/// nothing to mock). Every test exercises the real static contract with real objects over an
/// in-memory ledger — no <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration tests, no
/// ctor null-check tests (all banned per ADR-038 / <c>tests/CLAUDE.md</c>). Each test protects a
/// concrete FR-04 invariant (store-before-render, ledger_ref-not-payload, {bindingId}@t{n}
/// provenance, supersession, fail-loud-on-truncation).
/// </para>
/// </summary>
public class ComposeDraftDispositionTests
{
    private const string BindingId = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";
    private const string UcId = "compose-draft-alternative";

    private static ComposeDraftPayload SampleDraft() => new()
    {
        TargetText = "thirty (30) days notice",
        NewText = "sixty (60) days' written notice",
        MatchMode = "strict",
        Rationale = "Standard playbook term is 60 days.",
        Sources = new[] { "doc:precedent-123", "clause:playbook-7" },
    };

    // ── BuildDraftOutput: addressable compose entry ────────────────────────────────────

    [Fact]
    public void BuildDraftOutput_WithDraft_ProducesComposeEntryWithAddressableKeyAndOpaquePayload()
    {
        var draft = SampleDraft();

        var entry = ComposeDraftDisposition.BuildDraftOutput(BindingId, UcId, turn: 1, draft);

        entry.Key.Should().Be($"{BindingId}@t1");
        entry.BindingId.Should().Be(BindingId);
        entry.UcId.Should().Be(UcId);
        entry.Turn.Should().Be(1);
        entry.Disposition.Should().Be(ComposeDisposition.DispositionValue); // "compose"

        // Payload is Compose-owned + opaque to the core: it round-trips the structured edit.
        var roundTripped = ComposeDraftDisposition.DeserializePayload(entry);
        roundTripped.Should().BeEquivalentTo(draft);
    }

    // ── BuildFrame: envelope carries ledger_ref + provenance, NEVER the payload ─────────

    [Fact]
    public void BuildFrame_FromStoredComposeEntry_CarriesLedgerRefAndProvenanceNotPayload()
    {
        var entry = ComposeDraftDisposition.BuildDraftOutput(BindingId, UcId, turn: 4, SampleDraft());

        var frame = ComposeDraftDisposition.BuildFrame(entry);

        frame.Disposition.Should().Be(ComposeDisposition.DispositionValue);
        frame.LedgerRef.Should().Be(entry.Key);          // {bindingId}@t{n} — the load-bearing field
        frame.LedgerRef.Should().Be($"{BindingId}@t4");
        frame.BindingId.Should().Be(BindingId);           // provenance
        frame.Turn.Should().Be(4);
        frame.Status.Should().Be(ComposeDisposition.StatusReady);
        frame.SupersedesRef.Should().BeNull();

        // The frame is an envelope: it exposes no payload surface (storage precedes rendering).
        // Serializing the frame MUST NOT leak the draft body (target_text / new_text).
        var wire = JsonSerializer.Serialize(frame);
        wire.Should().NotContain("new_text");
        wire.Should().NotContain("sixty (60)");
    }

    [Fact]
    public void BuildFrame_WithSupersededOutput_RecordsSupersessionKey()
    {
        var prior = ComposeDraftDisposition.BuildDraftOutput(BindingId, UcId, turn: 1, SampleDraft());
        var replacement = ComposeDraftDisposition.BuildDraftOutput(BindingId, UcId, turn: 2, SampleDraft());

        var frame = ComposeDraftDisposition.BuildFrame(replacement, supersedesRef: prior.Key);

        frame.SupersedesRef.Should().Be($"{BindingId}@t1");
        frame.LedgerRef.Should().Be($"{BindingId}@t2");
    }

    [Fact]
    public void BuildFrame_FromNonComposeEntry_Throws()
    {
        var informational = new SessionOutput
        {
            Key = SessionLedger.BuildOutputKey(BindingId, 1),
            BindingId = BindingId,
            UcId = UcId,
            Turn = 1,
            Disposition = "informational",
            Payload = JsonSerializer.SerializeToElement(new { text = "hello" }),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var act = () => ComposeDraftDisposition.BuildFrame(informational);

        act.Should().Throw<ArgumentException>();
    }

    // ── MaterializeDraft: store-before-render (ADR-040) ─────────────────────────────────

    [Fact]
    public void MaterializeDraft_WhenEntryStored_ReturnsComposeOwnedPayload()
    {
        var draft = SampleDraft();
        var entry = ComposeDraftDisposition.BuildDraftOutput(BindingId, UcId, turn: 1, draft);
        var ledger = new Dictionary<string, SessionOutput> { [entry.Key] = entry };
        var frame = ComposeDraftDisposition.BuildFrame(entry);

        var materialized = ComposeDraftDisposition.MaterializeDraft(
            frame, key => ledger.TryGetValue(key, out var e) ? e : null);

        materialized.Should().BeEquivalentTo(draft);
    }

    [Fact]
    public void MaterializeDraft_WhenEntryAbsent_ThrowsStoreBeforeRender()
    {
        // A frame that references a ledger key that was never stored — the store-before-render
        // violation ADR-040 forbids. Materialization MUST throw, not render un-stored content.
        var entry = ComposeDraftDisposition.BuildDraftOutput(BindingId, UcId, turn: 1, SampleDraft());
        var frame = ComposeDraftDisposition.BuildFrame(entry);

        var act = () => ComposeDraftDisposition.MaterializeDraft(frame, _ => null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*store-before-render*");
    }

    [Fact]
    public void MaterializeDraft_WhenStoredPayloadTruncated_ThrowsLoud()
    {
        // Build an over-cap payload → the ledger stores it as an ADR-040 truncation marker.
        var huge = new string('x', SessionLedger.InlinePayloadCapBytes + 1);
        var overCap = ComposeDraftDisposition.BuildDraftOutput(
            BindingId, UcId, turn: 1, new ComposeDraftPayload { NewText = huge });
        var capped = SessionLedger.CapInlinePayload(overCap.Payload);
        capped.Truncated.Should().BeTrue();

        var stored = overCap with { Payload = capped.Payload };
        var ledger = new Dictionary<string, SessionOutput> { [stored.Key] = stored };
        var frame = ComposeDraftDisposition.BuildFrame(stored);

        var act = () => ComposeDraftDisposition.MaterializeDraft(
            frame, key => ledger.TryGetValue(key, out var e) ? e : null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*truncat*");
    }

    // ── ResolveCurrent: supersession / undo-replace (FR-17 foundation) ──────────────────

    [Fact]
    public void ResolveCurrent_WithSupersedingOutputs_ReturnsHighestTurnEntry()
    {
        var t1 = ComposeDraftDisposition.BuildDraftOutput(BindingId, UcId, turn: 1, SampleDraft());
        var t3 = ComposeDraftDisposition.BuildDraftOutput(
            BindingId, UcId, turn: 3, new ComposeDraftPayload { NewText = "the current draft" });
        var t2 = ComposeDraftDisposition.BuildDraftOutput(BindingId, UcId, turn: 2, SampleDraft());
        // Append order deliberately non-monotonic; ResolveCurrent is by turn, not position.
        var ledger = new[] { t1, t3, t2 };

        var current = ComposeDraftDisposition.ResolveCurrent(ledger, BindingId);

        current.Should().NotBeNull();
        current!.Turn.Should().Be(3);
        ComposeDraftDisposition.DeserializePayload(current).NewText.Should().Be("the current draft");
    }

    [Fact]
    public void ResolveCurrent_WhenBindingHasNoComposeOutput_ReturnsNull()
    {
        var otherBinding = ComposeDraftDisposition.BuildDraftOutput(
            "11111111-1111-1111-1111-111111111111", UcId, turn: 1, SampleDraft());

        var current = ComposeDraftDisposition.ResolveCurrent(new[] { otherBinding }, BindingId);

        current.Should().BeNull();
    }

    // ── DeserializePayload guards ───────────────────────────────────────────────────────

    [Fact]
    public void DeserializePayload_FromNonComposeEntry_Throws()
    {
        var recordEntry = new SessionOutput
        {
            Key = SessionLedger.BuildOutputKey(BindingId, 1),
            BindingId = BindingId,
            UcId = UcId,
            Turn = 1,
            Disposition = "record",
            Payload = JsonSerializer.SerializeToElement(new { id = "abc" }),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var act = () => ComposeDraftDisposition.DeserializePayload(recordEntry);

        act.Should().Throw<ArgumentException>();
    }
}
