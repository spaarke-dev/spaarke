using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="SemanticAppendixGenerator"/> (FR-22, task 023).
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>domain-logic</c> — <see cref="SemanticAppendixGenerator"/>
/// is a pure text transformer (<see cref="SemanticAppendixInput"/> in, <see cref="string"/>
/// out) with no I/O and no collaborators to mock; every test constructs the generator
/// directly and asserts on its returned string.
/// </para>
/// <para>
/// Reference: the exact appendix format (section order, bullet grammar, boundary markers,
/// duplicate/zero-use/missing-cross-ref handling) is specified in
/// <c>projects/spaarkeai-compose-r2/notes/spikes/spike-4-semantic-appendix.md</c> §4.
/// </para>
/// </summary>
public class SemanticAppendixGeneratorTests
{
    private readonly SemanticAppendixGenerator _sut = new();

    [Fact]
    public void Build_WithEmptyBodyText_ReturnsEmptyString()
    {
        // EDGE-4: empty document yields an empty appendix.
        var result = _sut.Build(new SemanticAppendixInput(BodyText: ""));

        result.Should().BeEmpty();
    }

    [Fact]
    public void Build_WithWhitespaceOnlyBodyText_ReturnsEmptyString()
    {
        var result = _sut.Build(new SemanticAppendixInput(BodyText: "   \n\n   "));

        result.Should().BeEmpty();
    }

    [Fact]
    public void Build_WithNoExtractableSemantics_ReturnsEmptyString()
    {
        // Plain prose with no quoted defined terms, no headings, no cross-refs.
        var body = "This is a plain sentence with no structure or definitions at all.";

        var result = _sut.Build(new SemanticAppendixInput(BodyText: body));

        result.Should().BeEmpty();
    }

    [Fact]
    public void Build_WithDefinedTermUsedAgain_EmitsBulletWithLocationAndUsageCount()
    {
        var body =
            "1.1 \"Agreement\" means this contract.\n" +
            "The Agreement shall commence on the Effective Date.\n" +
            "The Agreement is confidential.";

        var result = _sut.Build(new SemanticAppendixInput(BodyText: body));

        result.Should().Contain("<!-- READONLY_BOUNDARY_START -->");
        result.Should().Contain("<!-- READONLY_BOUNDARY_END -->");
        result.Should().Contain("### Defined Terms");
        result.Should().Contain("- \"Agreement\" — defined at §1.1 — used 2×");
    }

    [Fact]
    public void Build_WithZeroUseTerm_DropsTermFromAppendix()
    {
        // The term is defined but never referenced again — drop it (Spike 4 §3.2/§4 rule 2).
        // A heading is included so the appendix is non-empty overall (isolates the drop).
        var body =
            "## Definitions\n" +
            "1.1 \"Confidential Information\" means proprietary data.";

        var result = _sut.Build(new SemanticAppendixInput(BodyText: body));

        result.Should().NotContain("Confidential Information");
        result.Should().Contain("### Structure");
        result.Should().Contain("- Definitions");
    }

    [Fact]
    public void Build_WithDuplicateDefinition_EmitsErrorLineInsteadOfNormalBullet()
    {
        // EDGE-2: duplicate term definitions.
        var body =
            "1.2 \"Party\" means each of Purchaser and Seller.\n" +
            "3.4 \"Party\" means any signatory hereto.";

        var result = _sut.Build(new SemanticAppendixInput(BodyText: body));

        result.Should().Contain("[Error] Duplicate Definition: \"Party\" defined at §1.2 and §3.4");
        result.Should().NotContain("— defined at §1.2 — used");
        result.Should().NotContain("— defined at §3.4 — used");
    }

    [Fact]
    public void Build_WithNestedDefinedTerms_CapturesBothLeadingAndInlineDefinitions()
    {
        // EDGE-1: nested defined terms — a leading quoted definition AND an inline
        // parenthetical definition on the SAME line.
        var body =
            "## Definitions\n" +
            "\"Buyer\" (the \"Purchasing Entity\") shall pay all amounts due.\n" +
            "The Buyer represents that it has authority.\n" +
            "The Purchasing Entity must comply with law.";

        var result = _sut.Build(new SemanticAppendixInput(BodyText: body));

        result.Should().Contain("- \"Buyer\" — defined at Definitions — used 1×");
        result.Should().Contain("- \"Purchasing Entity\" — defined at Definitions — used 1×");
    }

    [Fact]
    public void Build_WithNumberedHeadings_EmitsStructureSectionAsHeadingOutline()
    {
        var body =
            "1 Introduction\n" +
            "1.1 Background information about the deal.\n" +
            "2 Definitions\n" +
            "2.1 \"Agreement\" means this contract.\n" +
            "The Agreement remains binding.";

        var result = _sut.Build(new SemanticAppendixInput(BodyText: body));

        result.Should().Contain("### Structure");
        result.Should().Contain("- §1 Introduction");
        result.Should().Contain("- §2 Definitions");
        // The numbered definition clause itself (2.1 "Agreement" ...) is NOT a heading —
        // only 2 structural entries should be present, not a 3rd bullet for "2.1".
        result.Should().NotContain("- §2.1 ");
        result.Should().Contain("- \"Agreement\" — defined at §2.1 — used 1×");
    }

    [Fact]
    public void Build_WithNullCrossRefs_OmitsCrossReferenceSectionEntirely()
    {
        var body = "1.1 \"Agreement\" means this contract.\nThe Agreement governs.";

        var result = _sut.Build(new SemanticAppendixInput(BodyText: body, CrossRefs: null));

        result.Should().NotContain("### Cross-Reference Targets");
    }

    [Fact]
    public void Build_WithCrossRefResolvingToKnownSection_EmitsNormalBullet()
    {
        var body = "4 Payment Terms\nPayment is due within 30 days.";
        var crossRefs = new List<CrossReferenceTarget>
        {
            new("_Ref481207", "§4.2 Payment Terms", new[] { "§7.1", "§9.3" }),
        };

        var result = _sut.Build(new SemanticAppendixInput(body, crossRefs));

        result.Should().Contain("### Cross-Reference Targets");
        result.Should().Contain(
            "- _Ref481207 — anchored to \"§4.2 Payment Terms\" — referenced from §7.1, §9.3");
    }

    [Fact]
    public void Build_WithCrossRefToMissingSection_FlagsInsteadOfSilentlyEmitting()
    {
        // EDGE-3 / NEGATIVE acceptance case: a cross-ref target that does not resolve to
        // any detected structural heading MUST be flagged, not silently rendered as valid.
        var body = "4 Payment Terms\nPayment is due within 30 days.";
        var crossRefs = new List<CrossReferenceTarget>
        {
            new("_Ref999999", "§99 Nonexistent Section", new[] { "§1.1" }),
        };

        var result = _sut.Build(new SemanticAppendixInput(body, crossRefs));

        result.Should().Contain(
            "[Error] Missing Cross-Reference Target: _Ref999999 anchored to \"§99 Nonexistent Section\" (section not found in document)");
        result.Should().NotContain("- _Ref999999 — anchored to");
    }

    [Fact]
    public void Build_WithAllThreeSections_EmitsFixedSectionOrder()
    {
        // Section order fixed: Defined Terms → Cross-Reference Targets → Structure (Spike 4 §4 rule 1).
        var body =
            "4 Payment Terms\n" +
            "1.1 \"Agreement\" means this contract.\n" +
            "The Agreement governs payment.";
        var crossRefs = new List<CrossReferenceTarget>
        {
            new("_Ref481207", "§4 Payment Terms", new[] { "§7.1" }),
        };

        var result = _sut.Build(new SemanticAppendixInput(body, crossRefs));

        var definedTermsIndex = result.IndexOf("### Defined Terms", StringComparison.Ordinal);
        var crossRefIndex = result.IndexOf("### Cross-Reference Targets", StringComparison.Ordinal);
        var structureIndex = result.IndexOf("### Structure", StringComparison.Ordinal);

        definedTermsIndex.Should().BeGreaterThan(-1);
        crossRefIndex.Should().BeGreaterThan(definedTermsIndex);
        structureIndex.Should().BeGreaterThan(crossRefIndex);
    }
}
