using FluentAssertions;
using Sprk.Bff.Api.Services.Communication.Engine;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// FR-12 (regarding-vs-related intent) — <see cref="NewRecordIntentDetector"/> golden-utterance + precision
/// tests. This deterministic detector replaces an LLM Action (owner decision 2026-07-30,
/// <c>notes/042-regarding-vs-related-owner-decision.md</c>: ONE relationship = regarding, no related field, no
/// new Action/Binding), so the NFR-07 golden-utterance obligation is met HERE — a curated table of
/// representative utterances asserting correct classification — rather than a catalog eval-case json.
///
/// The safety-critical property under test: an utterance that PRESENTS A NEW RECORD while REFERENCING an
/// existing one fires (→ the referenced identifier is later demoted below auto-file at capture), while an
/// utterance that merely mentions / updates / files onto an existing record does NOT fire (→ normal
/// auto-file/update). Because the only downstream action is a demotion-to-Suggested, a false negative is safe;
/// these adversarial near-misses guard the false-POSITIVE direction (over-suppression is a UX cost, not a
/// misfile, but we still bias to precision).
/// </summary>
public class NewRecordIntentDetectorTests
{
    // ── GOLDEN UTTERANCES — the detector FIRES (presents a new record, references an existing one) ──

    [Theory]
    // The canonical acceptance case (owner example).
    [InlineData("This is a new litigation matter related to matter LIT-123456", "LIT-123456", "sprk_matter")]
    // The spec acceptance case — "filing" is a generic type (no pinned entity).
    [InlineData("New filing based on PAT-908068", "PAT-908068", null)]
    // Verb-led framing + pinned project type.
    [InlineData("Please open a new project referencing PRJT.10001.01", "PRJT.10001.01", "sprk_project")]
    // Adjective-bridged type ("new corporate matter") + a connector.
    [InlineData("Opening a new corporate matter based on MAT-2020-01 for the client", "MAT-2020-01", "sprk_matter")]
    // Cross-type reference: a NEW matter that continues an invoice — suppress the invoice id, propose a matter.
    [InlineData("Starting a new matter, continuation of INV-002", "INV-002", "sprk_matter")]
    public void Detect_PresentsNewRecordReferencingExisting_Fires(
        string text, string expectedReferenced, string? expectedEntityHint)
    {
        var intent = NewRecordIntentDetector.Detect(subject: null, bodyText: text);

        intent.Should().NotBeNull("the utterance presents a new record while referencing an existing one");
        intent!.ReferencedIdentifiers.Should().Contain(expectedReferenced);
        intent.ProposedEntityHint.Should().Be(expectedEntityHint);
        NewRecordIntentDetector.IsReferencedNotFiled(intent, expectedReferenced).Should().BeTrue();
    }

    [Fact]
    public void Detect_TriggerAndReferenceAcrossSentences_Fires()
    {
        // The trigger and the connector-introduced identifier live in different sentences.
        var intent = NewRecordIntentDetector.Detect(
            subject: "New matter to open",
            bodyText: "We are opening a new matter this week. It relates to PRJT.10001.01 from last year.");

        intent.Should().NotBeNull();
        intent!.ReferencedIdentifiers.Should().Contain("PRJT.10001.01");
    }

    // ── ADVERSARIAL NEAR-MISSES — the detector does NOT fire (bias to precision) ──

    [Theory]
    // Plain update to an EXISTING record — no new-record framing.
    [InlineData("Please update the status on matter LIT-123456")]
    // "matter" is the object of a preposition after "new update" — an existing record, NOT a new matter.
    [InlineData("We have a new update on matter MAT-123, please review")]
    // A type noun appears but is not preceded by "new".
    [InlineData("See the attached invoice for matter LIT-123456")]
    // "new" precedes a non-record noun ("email"); no record-type trigger.
    [InlineData("Sending you a new email regarding INV-002")]
    // File-onto intent — the opposite of new-record; must keep auto-filing.
    [InlineData("Please file this correspondence onto matter MAT-123")]
    public void Detect_NoNewRecordFraming_DoesNotFire(string text)
    {
        NewRecordIntentDetector.Detect(subject: null, bodyText: text).Should().BeNull();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "   ")]
    [InlineData("Quarterly review meeting Thursday", "No identifiers or new-record language here")]
    public void Detect_EmptyOrIrrelevant_ReturnsNull(string? subject, string? body)
    {
        NewRecordIntentDetector.Detect(subject, body).Should().BeNull();
    }

    [Fact]
    public void IsReferencedNotFiled_NullIntentOrUnknownIdentifier_ReturnsFalse()
    {
        NewRecordIntentDetector.IsReferencedNotFiled(null, "LIT-1").Should().BeFalse();

        var intent = NewRecordIntentDetector.Detect(null, "New matter based on LIT-123456");
        intent.Should().NotBeNull();
        NewRecordIntentDetector.IsReferencedNotFiled(intent, "MAT-999").Should().BeFalse("that identifier was not referenced");
        NewRecordIntentDetector.IsReferencedNotFiled(intent, "lit-123456").Should().BeTrue("matching is case-insensitive");
    }
}
