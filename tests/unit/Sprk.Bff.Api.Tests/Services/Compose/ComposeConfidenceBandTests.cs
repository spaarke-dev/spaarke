using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for task 030 (FR-13/FR-16): the server-side <see cref="ComposeConfidenceBand"/>
/// derivation (<see cref="ComposeDraftDisposition.DeriveConfidenceBand"/>), the paraId/offset
/// anchor resolution (<see cref="ComposeDraftDisposition.ResolveParaIdAnchor"/>), and the
/// additive-mirror round-trip of the four new <see cref="ComposeDraftPayload"/> fields
/// (<c>confidence_band</c> / <c>paraId</c> / <c>start_offset</c> / <c>end_offset</c>).
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>unit/domain</c>. Every method under test is pure and
/// dependency-free (no DI, no I/O, no mocks) — real objects, real assertions. No
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration test, no ctor null-check test.
/// </para>
/// </summary>
public class ComposeConfidenceBandTests
{
    private static ComposeDraftPayload GroundedDraft() => new()
    {
        TargetText = "thirty (30) days notice",
        NewText = "sixty (60) days' written notice",
        MatchMode = "strict",
        Rationale = "Standard playbook term is 60 days.",
        Sources = new[] { "doc:precedent-123", "clause:playbook-7" },
    };

    // ── DeriveConfidenceBand: grounded => high ──────────────────────────────────────────

    [Fact]
    public void DeriveConfidenceBand_CitedSourceAndResolvedAnchor_ReturnsHigh()
    {
        var draft = GroundedDraft() with { ParaId = "1A2B3C4D" };

        var band = ComposeDraftDisposition.DeriveConfidenceBand(draft);

        band.Should().Be(ComposeConfidenceBand.High);
    }

    [Fact]
    public void DeriveConfidenceBand_CitedSourceAndStrictTargetClaim_ReturnsHigh()
    {
        // No resolved paraId yet, but a cited source + a non-insert match_mode target claim is
        // enough — the strict/first match_mode is itself a precision-scoped anchor claim.
        var draft = GroundedDraft();

        var band = ComposeDraftDisposition.DeriveConfidenceBand(draft);

        band.Should().Be(ComposeConfidenceBand.High);
    }

    // ── DeriveConfidenceBand: ungrounded => low ─────────────────────────────────────────

    [Fact]
    public void DeriveConfidenceBand_NoSourcesNoAnchorNoTarget_ReturnsLow()
    {
        // A bare insertion: no target to anchor, no citation. This is the "no grounding signal"
        // case — never guessed, never defaulted to a mid/high band.
        var draft = new ComposeDraftPayload { NewText = "Insert this clause.", MatchMode = "insert" };

        var band = ComposeDraftDisposition.DeriveConfidenceBand(draft);

        band.Should().Be(ComposeConfidenceBand.Low);
    }

    [Fact]
    public void DeriveConfidenceBand_TargetTextButInsertMatchMode_ReturnsLow()
    {
        // A target_text carried under match_mode "insert" is not a genuine anchor claim (insert
        // mode does not resolve against existing text) — with no sources either, this is low.
        var draft = new ComposeDraftPayload
        {
            TargetText = "some text",
            NewText = "new text",
            MatchMode = "insert",
        };

        var band = ComposeDraftDisposition.DeriveConfidenceBand(draft);

        band.Should().Be(ComposeConfidenceBand.Low);
    }

    // ── DeriveConfidenceBand: exactly one signal => medium ──────────────────────────────

    [Fact]
    public void DeriveConfidenceBand_CitedSourceOnly_ReturnsMedium()
    {
        var draft = new ComposeDraftPayload
        {
            NewText = "Insert this clause.",
            MatchMode = "insert",
            Sources = new[] { "doc:precedent-123" },
        };

        var band = ComposeDraftDisposition.DeriveConfidenceBand(draft);

        band.Should().Be(ComposeConfidenceBand.Medium);
    }

    [Fact]
    public void DeriveConfidenceBand_ResolvedAnchorOnly_ReturnsMedium()
    {
        var draft = new ComposeDraftPayload
        {
            TargetText = "thirty (30) days notice",
            NewText = "sixty (60) days' written notice",
            MatchMode = "strict",
            ParaId = "1A2B3C4D",
        };

        var band = ComposeDraftDisposition.DeriveConfidenceBand(draft);

        band.Should().Be(ComposeConfidenceBand.Medium);
    }

    // ── DeriveConfidenceBand: never a model self-report ─────────────────────────────────

    [Fact]
    public void DeriveConfidenceBand_IgnoresAnyPreExistingConfidenceBandOnTheDraft()
    {
        // A hostile/buggy model output that smuggled confidence_band:"high" onto an otherwise
        // ungrounded draft must NOT influence the derivation — the method never reads
        // draft.ConfidenceBand. Verifiability evidence alone decides the outcome.
        var spoofedHighButUngrounded = new ComposeDraftPayload
        {
            NewText = "Insert this clause.",
            MatchMode = "insert",
            ConfidenceBand = ComposeConfidenceBand.High,
        };

        var band = ComposeDraftDisposition.DeriveConfidenceBand(spoofedHighButUngrounded);

        band.Should().Be(ComposeConfidenceBand.Low,
            "the derivation reads grounding evidence only, never the payload's own confidence_band field");
    }

    // ── ResolveParaIdAnchor: locates target_text against the E2 paraId map ─────────────

    [Fact]
    public void ResolveParaIdAnchor_TargetTextFoundInMappedParagraph_SetsParaIdAndOffsets()
    {
        var draft = new ComposeDraftPayload
        {
            TargetText = "thirty (30) days notice",
            NewText = "sixty (60) days' written notice",
            MatchMode = "strict",
        };
        var paragraphTexts = new[]
        {
            "This is the first paragraph.",
            "Either party may terminate this Agreement upon thirty (30) days notice to the other party.",
        };
        var paraIdMap = new[]
        {
            new ParaIdMapEntry(0, "AAAAAAAA", IsMinted: false),
            new ParaIdMapEntry(1, "BBBBBBBB", IsMinted: false),
        };

        var resolved = ComposeDraftDisposition.ResolveParaIdAnchor(draft, paraIdMap, paragraphTexts);

        resolved.ParaId.Should().Be("BBBBBBBB");
        var expectedStart = paragraphTexts[1].IndexOf(draft.TargetText, StringComparison.Ordinal);
        resolved.StartOffset.Should().Be(expectedStart);
        resolved.EndOffset.Should().Be(expectedStart + draft.TargetText!.Length);
        // The original fields are untouched — this is an additive enrichment, not a rewrite.
        resolved.NewText.Should().Be(draft.NewText);
        resolved.TargetText.Should().Be(draft.TargetText);
    }

    [Fact]
    public void ResolveParaIdAnchor_InsertionStyleDraft_ReturnsDraftUnchanged()
    {
        var draft = new ComposeDraftPayload { NewText = "Insert this clause.", MatchMode = "insert" };

        var resolved = ComposeDraftDisposition.ResolveParaIdAnchor(
            draft, Array.Empty<ParaIdMapEntry>(), Array.Empty<string>());

        resolved.Should().BeSameAs(draft);
        resolved.ParaId.Should().BeNull();
        resolved.StartOffset.Should().BeNull();
        resolved.EndOffset.Should().BeNull();
    }

    [Fact]
    public void ResolveParaIdAnchor_TargetTextNotFoundInAnyParagraph_ReturnsDraftUnanchored()
    {
        var draft = new ComposeDraftPayload
        {
            TargetText = "text that does not appear anywhere",
            NewText = "replacement",
            MatchMode = "strict",
        };
        var paragraphTexts = new[] { "Unrelated paragraph content." };
        var paraIdMap = new[] { new ParaIdMapEntry(0, "AAAAAAAA", IsMinted: false) };

        var resolved = ComposeDraftDisposition.ResolveParaIdAnchor(draft, paraIdMap, paragraphTexts);

        resolved.ParaId.Should().BeNull("an unresolved target is honestly left unanchored, never guessed");
        resolved.StartOffset.Should().BeNull();
        resolved.EndOffset.Should().BeNull();
    }

    [Fact]
    public void ResolveParaIdAnchor_MatchedParagraphHasNoMapEntry_ReturnsDraftUnanchored()
    {
        var draft = new ComposeDraftPayload
        {
            TargetText = "thirty (30) days notice",
            NewText = "sixty (60) days' written notice",
            MatchMode = "strict",
        };
        var paragraphTexts = new[] { "Either party may terminate this Agreement upon thirty (30) days notice." };
        // No paraId map entry for index 0 (e.g. a pre-parse gap) — the match cannot be anchored.
        var paraIdMap = Array.Empty<ParaIdMapEntry>();

        var resolved = ComposeDraftDisposition.ResolveParaIdAnchor(draft, paraIdMap, paragraphTexts);

        resolved.ParaId.Should().BeNull();
    }

    // ── Additive-mirror round-trip: the four new fields serialize/deserialize correctly ─

    [Fact]
    public void BuildDraftOutput_WithConfidenceBandAndAnchorFieldsSet_RoundTripsThroughDeserializePayload()
    {
        var draft = GroundedDraft() with
        {
            ConfidenceBand = ComposeConfidenceBand.High,
            ParaId = "1A2B3C4D",
            StartOffset = 42,
            EndOffset = 66,
        };

        var entry = ComposeDraftDisposition.BuildDraftOutput(
            "3f2504e0-4f89-41d3-9a0c-0305e82c3301", "compose-draft-alternative", turn: 1, draft);
        var roundTripped = ComposeDraftDisposition.DeserializePayload(entry);

        roundTripped.Should().BeEquivalentTo(draft);
    }

    [Fact]
    public void BuildDraftOutput_WithoutTheNewFieldsSet_LeavesThemNullOnRoundTrip()
    {
        // Existing-shape (task-016-era) draft with none of the task-030 fields set — proves the
        // additive fields are purely opt-in and never auto-populated by the serialize/deserialize
        // round trip itself (that stays the caller's explicit responsibility).
        var draft = GroundedDraft();

        var entry = ComposeDraftDisposition.BuildDraftOutput(
            "3f2504e0-4f89-41d3-9a0c-0305e82c3301", "compose-draft-alternative", turn: 1, draft);
        var roundTripped = ComposeDraftDisposition.DeserializePayload(entry);

        roundTripped.ConfidenceBand.Should().BeNull();
        roundTripped.ParaId.Should().BeNull();
        roundTripped.StartOffset.Should().BeNull();
        roundTripped.EndOffset.Should().BeNull();
        roundTripped.Should().BeEquivalentTo(draft);
    }

    [Fact]
    public void BuildDraftOutput_SerializesTheFourNewFieldsUnderTheContractedWireNames()
    {
        // Contract-anchor: a rename of any of these four JSON keys is a breaking client-integration
        // change (compose-contracts.ts mirror). confidence_band/start_offset/end_offset snake_case,
        // paraId camelCase (matching the AnchoredAnnotationAnchor/ParaIdMapEntry precedent).
        var draft = new ComposeDraftPayload
        {
            NewText = "x",
            ConfidenceBand = ComposeConfidenceBand.Medium,
            ParaId = "DEADBEEF",
            StartOffset = 1,
            EndOffset = 2,
        };

        var entry = ComposeDraftDisposition.BuildDraftOutput(
            "3f2504e0-4f89-41d3-9a0c-0305e82c3301", "compose-draft-alternative", turn: 1, draft);
        var json = entry.Payload.GetRawText();

        json.Should().Contain("\"confidence_band\":\"medium\"");
        json.Should().Contain("\"paraId\":\"DEADBEEF\"");
        json.Should().Contain("\"start_offset\":1");
        json.Should().Contain("\"end_offset\":2");
        json.Should().NotContain("\"para_id\"", "paraId is camelCase per the established precedent, never snake_case");
    }
}
