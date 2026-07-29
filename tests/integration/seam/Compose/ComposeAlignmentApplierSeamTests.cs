// R5 task 010 (G3 alignment applier, spec FR-03 partial + gap G3) — seam proof for
// ComposeShadowPatchEngine's setBlockAttr Alignment applier. This is the first R5 production-code
// task: it fills the StructuralOpNotYetImplemented throw for ComposeBlockAttr.Alignment with a real
// applier that resolves the target paragraph by paraId ONLY (I-7 — no text-search anywhere in the
// write path) and records the change as a tracked w:pPrChange (the paragraph-property analogue of
// the existing MarkParagraphMark tracked paragraph-MARK change).
//
// Drives the PRODUCTION engine over the real corpus, REUSING the task-004
// ComposeOoxmlPackagePartComparer + ComposeCorpusFixtureLocator unchanged (their own header
// anticipates exactly this reuse) — mirrors ComposeShadowPatchEngineByteDiffSeamTests' shape.
//
// SCOPE: task 010 implements ONLY the Alignment case of setBlockAttr. Style/ListOrdered/ListLevel
// stay StructuralOpNotYetImplemented until task 011 (G3 heading/list). The engine's sole caller
// today is the imported/tracked path — every case below exercises the TRACKED w:pPrChange path; a
// future trackChanges:false clean branch is G2/task 021 (notes/g2-clean-apply-decision.md).
//
// Banned-pattern clean: no Mock<HttpMessageHandler>, no DI-registration test, no ctor-null test.

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Compose.Operations;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeAlignmentApplierSeamTests
{
    private static readonly DateTimeOffset When = new(2026, 7, 29, 0, 0, 0, TimeSpan.Zero);
    private readonly ComposeShadowPatchEngine _engine = new();

    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void SetBlockAttrAlignment_OnEveryCorpusDoc_AppliesTrackedPPrChangeAndLeavesUntouchedSubtreesByteIdentical(
        string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        var (targetParaId, otherOuterXmlBefore) = ReadParagraphSnapshot(original);
        if (targetParaId is null)
        {
            // A corpus doc with no addressable paragraph (e.g. all atoms) — nothing to prove here.
            return;
        }

        var log = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new SetBlockAttrOperation { ParaId = targetParaId, Attr = ComposeBlockAttr.Alignment, Value = "Center" },
            },
        };

        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        // NFR-01: every package part EXCEPT document.xml is byte-identical (the edit legitimately
        // changes document.xml — the comparer's untouched-part bucket excludes it).
        var comparison = ComposeOoxmlPackagePartComparer.Compare(original, patched, strictDocumentXmlByteIdentity: false);
        comparison.AllUntouchedPartsByteIdentical.Should().BeTrue(
            $"an alignment change must leave every package part it never opened byte-identical for '{docName}' — " +
            comparison.DescribeMismatches());

        // The edited paragraph carries the new w:jc AND a tracked w:pPrChange recording the prior state.
        using (var doc = WordprocessingDocument.Open(new MemoryStream(patched, writable: false), isEditable: false))
        {
            var edited = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
                .First(p => string.Equals(p.ParagraphId?.Value, targetParaId, StringComparison.OrdinalIgnoreCase));
            var pPr = edited.ParagraphProperties;
            pPr.Should().NotBeNull($"the edited paragraph must carry w:pPr for '{docName}'");
            pPr!.GetFirstChild<Justification>()?.Val?.Value.Should().Be(JustificationValues.Center,
                $"the edited paragraph must carry the new w:jc='center' for '{docName}'");
            pPr.GetFirstChild<ParagraphPropertiesChange>().Should().NotBeNull(
                $"the alignment change must be recorded as a tracked w:pPrChange (Word 'Reject Formatting Change' " +
                $"anchor) for '{docName}'");
        }

        // Every OTHER paragraph's OuterXml is unchanged — untouched document.xml subtrees stay byte-identical (I-4).
        var otherOuterXmlAfter = ReadOtherParagraphOuterXml(patched, targetParaId);
        otherOuterXmlAfter.Should().Equal(otherOuterXmlBefore,
            $"only the edited paragraph may change; every untouched subtree must stay byte-identical for '{docName}'");
    }

    [Fact]
    public void SetBlockAttrAlignment_ChangedTwiceInOneApply_PPrChangeRecordsImmediatelyPriorValue_NeverStacks()
    {
        var (original, paraId) = LoadFirstFixtureParagraph();

        var log = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new SetBlockAttrOperation { ParaId = paraId, Attr = ComposeBlockAttr.Alignment, Value = "Center" },
                new SetBlockAttrOperation { ParaId = paraId, Attr = ComposeBlockAttr.Alignment, Value = "Right" },
            },
        };

        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        var pPr = ResolveParagraphProperties(patched, paraId);

        // The LIVE value is the LAST op's value.
        pPr.GetFirstChild<Justification>()?.Val?.Value.Should().Be(JustificationValues.Right);

        // Exactly ONE w:pPrChange — never stacks two revisions (mirrors MarkParagraphMark's rule).
        pPr.Elements<ParagraphPropertiesChange>().Should().HaveCount(1);

        // The pPrChange records the value set by the FIRST op (Center) — the state immediately BEFORE
        // the final write — not the document's original on-disk alignment.
        var pPrChange = pPr.Elements<ParagraphPropertiesChange>().Single();
        var previousJc = pPrChange.GetFirstChild<ParagraphPropertiesExtended>()?.GetFirstChild<Justification>();
        previousJc.Should().NotBeNull("the second op's pPrChange must record the value the FIRST op wrote (Center)");
        previousJc!.Val!.Value.Should().Be(JustificationValues.Center);
    }

    [Fact]
    public void SetBlockAttrAlignment_ValueDefault_ClearsJustificationAndRecordsPriorExplicitValue()
    {
        var (original, paraId) = LoadFirstFixtureParagraph();

        var setLog = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new SetBlockAttrOperation { ParaId = paraId, Attr = ComposeBlockAttr.Alignment, Value = "Center" },
            },
        };
        var afterSet = _engine.Apply(original, setLog, author: "Seam", timestamp: When);

        var clearLog = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new SetBlockAttrOperation { ParaId = paraId, Attr = ComposeBlockAttr.Alignment, Value = null },
            },
        };
        var afterClear = _engine.Apply(afterSet, clearLog, author: "Seam", timestamp: When);

        var pPr = ResolveParagraphProperties(afterClear, paraId);

        pPr.GetFirstChild<Justification>().Should().BeNull(
            "a null/'Default' value clears the explicit alignment back to the paragraph's style default");

        var pPrChange = pPr.Elements<ParagraphPropertiesChange>().Single();
        var previousJc = pPrChange.GetFirstChild<ParagraphPropertiesExtended>()?.GetFirstChild<Justification>();
        previousJc.Should().NotBeNull("the clear op's pPrChange must record the prior EXPLICIT alignment (Center)");
        previousJc!.Val!.Value.Should().Be(JustificationValues.Center);
    }

    [Fact]
    public void SetBlockAttrAlignment_UnrecognizedValue_ThrowsInvalidBlockAttrValue_NeverAppliesSilently()
    {
        var (original, paraId) = LoadFirstFixtureParagraph();

        var log = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new SetBlockAttrOperation { ParaId = paraId, Attr = ComposeBlockAttr.Alignment, Value = "Diagonal" },
            },
        };

        var act = () => _engine.Apply(original, log, author: "Seam", timestamp: When);

        act.Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.InvalidBlockAttrValue);
    }

    [Fact]
    public void SetBlockAttrAlignment_WhenParaIdNotFound_ThrowsParagraphNotFound_NeverTextSearches()
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First());

        var log = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                // A well-formed but non-existent paraId — proves resolution is a dictionary lookup
                // (I-7), never a fallback content-match against the document text.
                new SetBlockAttrOperation { ParaId = "DEADBEEF", Attr = ComposeBlockAttr.Alignment, Value = "Center" },
            },
        };

        var act = () => _engine.Apply(original, log, author: "Seam", timestamp: When);

        act.Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.ParagraphNotFound);
    }

    [Theory]
    [InlineData(ComposeBlockAttr.Style)]
    [InlineData(ComposeBlockAttr.ListOrdered)]
    [InlineData(ComposeBlockAttr.ListLevel)]
    public void SetBlockAttr_NonAlignmentAttrs_StillThrowStructuralOpNotYetImplemented_ScopeBoundaryHeldForTask011(
        ComposeBlockAttr attr)
    {
        var (original, paraId) = LoadFirstFixtureParagraph();

        var log = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new SetBlockAttrOperation { ParaId = paraId, Attr = attr, Value = "x" },
            },
        };

        var act = () => _engine.Apply(original, log, author: "Seam", timestamp: When);

        act.Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.StructuralOpNotYetImplemented);
    }

    // -- helpers --------------------------------------------------------------------------------

    /// <summary>Loads the first corpus fixture's bytes plus its first addressable paragraph's paraId.</summary>
    private static (byte[] Original, string ParaId) LoadFirstFixtureParagraph()
    {
        var path = ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First();
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(path);
        var (paraId, _) = ReadParagraphSnapshot(original);
        paraId.Should().NotBeNull($"the first corpus fixture '{path}' must have at least one addressable paragraph");
        return (original, paraId!);
    }

    private static ParagraphProperties ResolveParagraphProperties(byte[] bytes, string paraId)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);
        var para = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .First(p => string.Equals(p.ParagraphId?.Value, paraId, StringComparison.OrdinalIgnoreCase));
        return para.ParagraphProperties!;
    }

    /// <summary>Returns the first addressable paragraph's id + the OuterXml of every OTHER id-bearing
    /// paragraph (the untouched-subtree baseline) — mirrors
    /// ComposeShadowPatchEngineByteDiffSeamTests.ReadParagraphSnapshot.</summary>
    private static (string? TargetParaId, List<string> OtherOuterXml) ReadParagraphSnapshot(byte[] bytes)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);
        var paragraphs = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().ToList();

        var target = paragraphs.FirstOrDefault(p => !string.IsNullOrEmpty(p.ParagraphId?.Value));

        var targetId = target?.ParagraphId?.Value;
        var others = paragraphs
            .Where(p => !ReferenceEquals(p, target))
            .Select(p => p.OuterXml)
            .ToList();

        return (targetId, others);
    }

    private static List<string> ReadOtherParagraphOuterXml(byte[] bytes, string excludedParaId)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Where(p => !string.Equals(p.ParagraphId?.Value, excludedParaId, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.OuterXml)
            .ToList();
    }
}
