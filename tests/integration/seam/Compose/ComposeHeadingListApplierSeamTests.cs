// R5 task 011 (G3 heading/list applier, spec FR-03 + gaps SDL-1/2) — seam proof for
// ComposeShadowPatchEngine's setBlockAttr Style / ListOrdered / ListLevel appliers. Extends the surface
// task 010 opened (Alignment): each attribute resolves the target paragraph by paraId ONLY (I-7 — no
// text-search anywhere in the write path) and records the change as a tracked w:pPrChange.
//
// The LIST cases reuse R4.5's read-side numbering ENGINE unchanged (referenced in place per task 005,
// R5-D4): after applying a list op, the numbering the target paragraph shows is computed by the REAL
// ComposeNumbering.NumberingComputationEngine over the patched bytes — the same engine the
// read/projection path uses — so "edit-side renumber matches read-time model" is proven end-to-end, not
// asserted against a hand-rolled expectation. The engine is NEVER re-implemented here.
//
// Corpus reuse: the same ComposeCorpusFixtureLocator + ComposeOoxmlPackagePartComparer as tasks 004/010.
// Fixtures with existing numbering (multilevel-1-1-1, symbol-section-mark, nda-interrupted-clauses) prove
// the REFERENCE-in-place path (numbering.xml stays byte-identical); a fixture WITHOUT numbering
// (Engagement Letter) proves the AUTHOR fallback succeeds (NFR-08 — no user-triggerable error).
//
// Banned-pattern clean: no Mock<HttpMessageHandler>, no DI-registration test, no ctor-null test.

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Compose.Operations;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeHeadingListApplierSeamTests
{
    private static readonly DateTimeOffset When = new(2026, 7, 29, 0, 0, 0, TimeSpan.Zero);
    private readonly ComposeShadowPatchEngine _engine = new();

    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(path => new object[] { path });

    // ==========================================================================================
    // Style (heading level / list-paragraph style)
    // ==========================================================================================

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void SetBlockAttrStyle_Heading_OnEveryCorpusDoc_AppliesTrackedPPrChange_LeavesUntouchedSubtreesByteIdentical(
        string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        var (targetParaId, otherOuterXmlBefore) = ReadParagraphSnapshot(original);
        if (targetParaId is null)
        {
            return; // no addressable paragraph — nothing to prove
        }

        var log = OneOp(new SetBlockAttrOperation { ParaId = targetParaId, Attr = ComposeBlockAttr.Style, Value = "Heading2" });
        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        // NFR-01: a style change never authors numbering — every part except document.xml stays byte-identical.
        var comparison = ComposeOoxmlPackagePartComparer.Compare(original, patched, strictDocumentXmlByteIdentity: false);
        comparison.AllUntouchedPartsByteIdentical.Should().BeTrue(
            $"a heading-style change must leave every package part it never opened byte-identical for '{docName}' — {comparison.DescribeMismatches()}");

        using (var doc = OpenRead(patched))
        {
            var pPr = ResolvePara(doc, targetParaId).ParagraphProperties!;
            pPr.GetFirstChild<ParagraphStyleId>()?.Val?.Value.Should().Be("Heading2",
                $"the edited paragraph must carry w:pStyle='Heading2' for '{docName}'");
            pPr.GetFirstChild<ParagraphPropertiesChange>().Should().NotBeNull(
                $"the style change must be recorded as a tracked w:pPrChange for '{docName}'");
        }

        ReadOtherParagraphOuterXml(patched, targetParaId).Should().Equal(otherOuterXmlBefore,
            $"only the edited paragraph may change for '{docName}'");
    }

    [Fact]
    public void SetBlockAttrStyle_Normal_OnAListParagraph_ClearsPStyleAndDirectNumPr_RecordingPriorState()
    {
        var original = LoadFixture("multilevel-1-1-1");
        var listParaId = FirstParagraphWithDirectNumPr(original);
        listParaId.Should().NotBeNull("multilevel-1-1-1 must have a direct-numbered paragraph to clear");

        var log = OneOp(new SetBlockAttrOperation { ParaId = listParaId!, Attr = ComposeBlockAttr.Style, Value = "Normal" });
        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        using var doc = OpenRead(patched);
        var pPr = ResolvePara(doc, listParaId!).ParagraphProperties!;

        pPr.GetFirstChild<ParagraphStyleId>().Should().BeNull("Style=Normal reverts to the default paragraph style (drops w:pStyle)");
        pPr.GetFirstChild<NumberingProperties>().Should().BeNull("moving to a non-list style must drop the direct w:numPr — the paragraph leaves the list");

        var change = pPr.GetFirstChild<ParagraphPropertiesChange>();
        change.Should().NotBeNull();
        // The pPrChange snapshots the PRIOR state (the numbering that was there), so Word's Reject restores the list item.
        change!.GetFirstChild<ParagraphPropertiesExtended>()!.GetFirstChild<NumberingProperties>()
            .Should().NotBeNull("the pPrChange must record the prior direct numPr so Reject restores the list item");
    }

    [Fact]
    public void SetBlockAttrStyle_ChangedTwiceInOneApply_PPrChangeNeverStacks_LiveValueIsLast()
    {
        var (original, paraId) = LoadFirstAddressable("nda-interrupted-clauses");

        var log = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new SetBlockAttrOperation { ParaId = paraId, Attr = ComposeBlockAttr.Style, Value = "Heading1" },
                new SetBlockAttrOperation { ParaId = paraId, Attr = ComposeBlockAttr.Style, Value = "Heading3" },
            },
        };

        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        using var doc = OpenRead(patched);
        var pPr = ResolvePara(doc, paraId).ParagraphProperties!;
        pPr.GetFirstChild<ParagraphStyleId>()?.Val?.Value.Should().Be("Heading3", "the LIVE style is the last op's value");
        pPr.Elements<ParagraphPropertiesChange>().Should().HaveCount(1, "w:pPrChange never stacks two revisions on one paragraph");
    }

    [Fact]
    public void SetBlockAttrStyle_UnrecognizedValue_ThrowsInvalidBlockAttrValue_NeverAppliesSilently()
    {
        var (original, paraId) = LoadFirstAddressable("nda-interrupted-clauses");
        var log = OneOp(new SetBlockAttrOperation { ParaId = paraId, Attr = ComposeBlockAttr.Style, Value = "Heading9" });

        _engine.Invoking(e => e.Apply(original, log, author: "Seam", timestamp: When))
            .Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.InvalidBlockAttrValue);
    }

    // ==========================================================================================
    // ListOrdered / ListLevel — reference existing numbering (numbering.xml byte-identical)
    // ==========================================================================================

    [Fact]
    public void SetBlockAttrListOrdered_True_OnDocWithDecimalNumbering_ReferencesExistingNumId_NumberingXmlByteIdentical_LabelIsDecimal()
    {
        var original = LoadFixture("multilevel-1-1-1");
        var (targetParaId, _) = ReadParagraphSnapshot(original);
        targetParaId.Should().NotBeNull();

        var log = OneOp(new SetBlockAttrOperation { ParaId = targetParaId!, Attr = ComposeBlockAttr.ListOrdered, Value = "true" });
        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        // Referenced an EXISTING numId → numbering.xml (and every other part) stays byte-identical.
        var comparison = ComposeOoxmlPackagePartComparer.Compare(original, patched, strictDocumentXmlByteIdentity: false);
        comparison.AllUntouchedPartsByteIdentical.Should().BeTrue(
            $"referencing the document's existing ordered numId must not touch numbering.xml — {comparison.DescribeMismatches()}");

        using (var doc = OpenRead(patched))
        {
            var pPr = ResolvePara(doc, targetParaId!).ParagraphProperties!;
            pPr.GetFirstChild<NumberingProperties>().Should().NotBeNull("the paragraph must carry a direct w:numPr");
            pPr.GetFirstChild<ParagraphPropertiesChange>().Should().NotBeNull("the list change is a tracked w:pPrChange");
        }

        // "Matches read-time model": the REAL read-side engine computes a decimal label for the target.
        ComputeLabelForPara(patched, targetParaId!).Should().MatchRegex(@"^\d+\.?$",
            "the resulting numbering must render as a decimal label under R4.5's NumberingComputationEngine");
    }

    [Fact]
    public void SetBlockAttrListOrdered_False_OnDocWithBulletNumbering_ReferencesBulletNumId_NumberingXmlByteIdentical()
    {
        var original = LoadFixture("symbol-section-mark");
        var (targetParaId, _) = ReadParagraphSnapshot(original);
        targetParaId.Should().NotBeNull();

        var log = OneOp(new SetBlockAttrOperation { ParaId = targetParaId!, Attr = ComposeBlockAttr.ListOrdered, Value = "false" });
        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        var comparison = ComposeOoxmlPackagePartComparer.Compare(original, patched, strictDocumentXmlByteIdentity: false);
        comparison.AllUntouchedPartsByteIdentical.Should().BeTrue(
            $"referencing the document's existing bullet numId must not touch numbering.xml — {comparison.DescribeMismatches()}");

        // The referenced numId must resolve to a BULLET format in the read-side model.
        using var doc = OpenRead(patched);
        var mainPart = doc.MainDocumentPart!;
        var model = ComposeNumbering.BuildNumberingModel(mainPart);
        var target = ResolvePara(doc, targetParaId!);
        var numbering = ComposeNumbering.ResolveParagraphNumbering(target, model);
        numbering.Should().NotBeNull("the bulleted paragraph must resolve numbering in the model");
        model.ResolveLevel(numbering!.NumId, 0)?.NumFmt.Should().Be(NumberFormatValues.Bullet,
            "ListOrdered=false must reference a bullet-format numId");
    }

    [Fact]
    public void SetBlockAttrListLevel_OnDirectNumberedParagraph_ChangesIlvl_KeepsNumId_Tracked()
    {
        var original = LoadFixture("multilevel-1-1-1");
        var paraId = FirstParagraphWithDirectNumPr(original);
        paraId.Should().NotBeNull();

        int originalNumId;
        using (var doc = OpenRead(original))
        {
            originalNumId = ResolvePara(doc, paraId!).ParagraphProperties!.GetFirstChild<NumberingProperties>()!.NumberingId!.Val!.Value;
        }

        var log = OneOp(new SetBlockAttrOperation { ParaId = paraId!, Attr = ComposeBlockAttr.ListLevel, Value = "2" });
        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        using var patchedDoc = OpenRead(patched);
        var numPr = ResolvePara(patchedDoc, paraId!).ParagraphProperties!.GetFirstChild<NumberingProperties>()!;
        numPr.NumberingLevelReference!.Val!.Value.Should().Be(2, "ListLevel=2 re-nests the paragraph to depth 2");
        numPr.NumberingId!.Val!.Value.Should().Be(originalNumId, "ListLevel keeps the paragraph's existing list identity (same numId)");
        ResolvePara(patchedDoc, paraId!).ParagraphProperties!.GetFirstChild<ParagraphPropertiesChange>()
            .Should().NotBeNull("the level change is a tracked w:pPrChange");
    }

    // ==========================================================================================
    // Author fallback (NFR-08 — no user-triggerable error on a doc with no list numbering)
    // ==========================================================================================

    [Fact]
    public void SetBlockAttrListOrdered_OnDocWithoutNumbering_AuthorsNumbering_OpSucceeds_LabelResolvableByReadEngine()
    {
        var original = LoadFixture("Engagement Letter");
        var (targetParaId, _) = ReadParagraphSnapshot(original);
        targetParaId.Should().NotBeNull();

        // No throw — removing the SDL-2 guard must never trap the user on a doc that has no list numbering.
        var log = OneOp(new SetBlockAttrOperation { ParaId = targetParaId!, Attr = ComposeBlockAttr.ListOrdered, Value = "true" });
        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        using (var doc = OpenRead(patched))
        {
            doc.MainDocumentPart!.NumberingDefinitionsPart.Should().NotBeNull("a numbering part must be authored when the doc had none");
            ResolvePara(doc, targetParaId!).ParagraphProperties!.GetFirstChild<NumberingProperties>()
                .Should().NotBeNull("the target paragraph must carry the authored direct w:numPr");
        }

        // The authored numbering is resolvable + renders as a decimal label under the real read-side engine.
        ComputeLabelForPara(patched, targetParaId!).Should().MatchRegex(@"^\d+\.?$",
            "authored ordered numbering must compute a decimal label under NumberingComputationEngine");
    }

    // ==========================================================================================
    // I-7 / validation guards
    // ==========================================================================================

    [Fact]
    public void SetBlockAttrList_WhenParaIdNotFound_ThrowsParagraphNotFound_NeverTextSearches()
    {
        var original = LoadFixture("multilevel-1-1-1");
        var log = OneOp(new SetBlockAttrOperation { ParaId = "DEADBEEF", Attr = ComposeBlockAttr.ListOrdered, Value = "true" });

        _engine.Invoking(e => e.Apply(original, log, author: "Seam", timestamp: When))
            .Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.ParagraphNotFound);
    }

    [Theory]
    [InlineData(ComposeBlockAttr.ListOrdered, "maybe")]
    [InlineData(ComposeBlockAttr.ListLevel, "9")]
    [InlineData(ComposeBlockAttr.ListLevel, "-1")]
    [InlineData(ComposeBlockAttr.ListLevel, "notanumber")]
    public void SetBlockAttrList_UnrecognizedValue_ThrowsInvalidBlockAttrValue(ComposeBlockAttr attr, string value)
    {
        var (original, paraId) = LoadFirstAddressable("multilevel-1-1-1");
        var log = OneOp(new SetBlockAttrOperation { ParaId = paraId, Attr = attr, Value = value });

        _engine.Invoking(e => e.Apply(original, log, author: "Seam", timestamp: When))
            .Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.InvalidBlockAttrValue);
    }

    // -- helpers --------------------------------------------------------------------------------

    private static ComposeOperationLog OneOp(ComposeOperation op) => new() { Operations = new[] { op } };

    private static WordprocessingDocument OpenRead(byte[] bytes) =>
        WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);

    private static Paragraph ResolvePara(WordprocessingDocument doc, string paraId) =>
        doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .First(p => string.Equals(p.ParagraphId?.Value, paraId, StringComparison.OrdinalIgnoreCase));

    private static byte[] LoadFixture(string nameContains)
    {
        var path = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .First(p => Path.GetFileName(p).Contains(nameContains, StringComparison.OrdinalIgnoreCase));
        return ComposeCorpusFixtureLocator.LoadVerifiedBytes(path);
    }

    private static (byte[] Original, string ParaId) LoadFirstAddressable(string nameContains)
    {
        var original = LoadFixture(nameContains);
        var (paraId, _) = ReadParagraphSnapshot(original);
        paraId.Should().NotBeNull($"fixture '{nameContains}' must have an addressable paragraph");
        return (original, paraId!);
    }

    private static string? FirstParagraphWithDirectNumPr(byte[] bytes)
    {
        using var doc = OpenRead(bytes);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .FirstOrDefault(p => !string.IsNullOrEmpty(p.ParagraphId?.Value)
                && p.ParagraphProperties?.GetFirstChild<NumberingProperties>() is not null)
            ?.ParagraphId?.Value;
    }

    /// <summary>Drives the REAL read-side numbering engine over the patched bytes — build the model, then
    /// call Compute once per numbered paragraph in document order (the engine's stateful contract) — and
    /// returns the computed label for <paramref name="paraId"/>. This is the literal "matches read-time
    /// NumberingComputationEngine output" proof (R5-D4 reuse, task 005).</summary>
    private static string? ComputeLabelForPara(byte[] bytes, string paraId)
    {
        using var doc = OpenRead(bytes);
        var mainPart = doc.MainDocumentPart!;
        var model = ComposeNumbering.BuildNumberingModel(mainPart);
        var engine = new ComposeNumbering.NumberingComputationEngine(model);

        string? label = null;
        foreach (var p in mainPart.Document!.Body!.Descendants<Paragraph>())
        {
            var numbering = ComposeNumbering.ResolveParagraphNumbering(p, model);
            if (numbering is null)
            {
                continue;
            }

            var result = engine.Compute(numbering);
            if (string.Equals(p.ParagraphId?.Value, paraId, StringComparison.OrdinalIgnoreCase))
            {
                label = result?.Label;
            }
        }

        return label;
    }

    /// <summary>First addressable paragraph's id + the OuterXml of every OTHER id-bearing paragraph
    /// (the untouched-subtree baseline). Mirrors ComposeAlignmentApplierSeamTests.</summary>
    private static (string? TargetParaId, List<string> OtherOuterXml) ReadParagraphSnapshot(byte[] bytes)
    {
        using var doc = OpenRead(bytes);
        var paragraphs = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().ToList();
        var target = paragraphs.FirstOrDefault(p => !string.IsNullOrEmpty(p.ParagraphId?.Value));
        var others = paragraphs.Where(p => !ReferenceEquals(p, target)).Select(p => p.OuterXml).ToList();
        return (target?.ParagraphId?.Value, others);
    }

    private static List<string> ReadOtherParagraphOuterXml(byte[] bytes, string excludedParaId)
    {
        using var doc = OpenRead(bytes);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Where(p => !string.Equals(p.ParagraphId?.Value, excludedParaId, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.OuterXml)
            .ToList();
    }
}
