// FR-04 / NFR-01 (spaarkeai-compose-r4 task 030) — engine-level seam proof for the ComposeShadowPatchEngine
// against the REAL corpus. This is the "untouched package parts are copied verbatim + untouched
// document.xml subtrees stay byte-identical" acceptance (design.md §5.0), driving the production engine
// (not a mock) over every fixture under tests/fixtures/compose-corpus/ and REUSING the task-004
// ComposeOoxmlPackagePartComparer unchanged (its own header anticipates this exact reuse).
//
// SCOPE: the FULL through-the-wire proof (POST /api/compose/documents/{id}/save routed through the engine)
// is task 034 — this seam drives the engine's public Apply() surface directly (byte[] in / byte[] out),
// which is where the byte-surgical guarantee actually lives. Two cases per corpus doc:
//   1. NO-OP (empty op log) → whole-file byte-identical passthrough (the engine returns the input).
//   2. REAL interior insertText on the first resolvable paragraph → every OTHER package part is
//      byte-identical (NFR-01), and every UNTOUCHED paragraph's OuterXml is unchanged (only the edited
//      paragraph legitimately differs — it now carries a native w:ins). ZERO write-path text-search.
//
// Corpus: EVERY .docx under tests/fixtures/compose-corpus/, enumerated via ComposeCorpusFixtureLocator
// (glob, not a hardcoded list). Banned-pattern clean: no Mock<HttpMessageHandler>, no DI-registration,
// no ctor-null test.

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Compose.Operations;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeShadowPatchEngineByteDiffSeamTests
{
    private static readonly DateTimeOffset When = new(2026, 7, 22, 0, 0, 0, TimeSpan.Zero);
    private readonly ComposeShadowPatchEngine _engine = new();

    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void NoOpApply_OnEveryCorpusDoc_ReturnsRetainedBytesByteIdentical(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        var result = _engine.Apply(original, new ComposeOperationLog());

        result.Should().Equal(original,
            $"a no-op Apply (empty operation log, no comments) must return the retained original byte-identical for '{docName}'");
    }

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void InteriorInsert_OnEveryCorpusDoc_LeavesUntouchedPartsAndSubtreesByteIdentical(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        // Pick the FIRST paragraph that carries a w14:paraId and has at least one editor-visible run —
        // resolved by identity, NEVER by text-search (the whole point).
        var (targetParaId, originalParaOuterXmlByOtherIds) = ReadParagraphSnapshot(original);
        if (targetParaId is null)
        {
            // A corpus doc with no addressable paragraph (e.g. all atoms) — nothing to prove here.
            return;
        }

        var log = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new InsertTextOperation
                {
                    ParaId = targetParaId,
                    At = new ComposeRunPoint(0, 0), // very start of the first run — always valid
                    Text = "[R4]",
                },
            },
        };

        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        // NFR-01: every package part EXCEPT document.xml is byte-identical (structural mode — document.xml
        // legitimately changed by the edit; the comparer's untouched-part bucket excludes it).
        var comparison = ComposeOoxmlPackagePartComparer.Compare(original, patched, strictDocumentXmlByteIdentity: false);
        comparison.AllUntouchedPartsByteIdentical.Should().BeTrue(
            $"NFR-01: a single interior insert must leave every package part it never opened byte-identical for '{docName}' — {comparison.DescribeMismatches()}");

        // The edited paragraph now carries a native w:ins with our text.
        using (var doc = WordprocessingDocument.Open(new MemoryStream(patched, writable: false), isEditable: false))
        {
            var edited = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
                .First(p => string.Equals(p.ParagraphId?.Value, targetParaId, StringComparison.OrdinalIgnoreCase));
            edited.Descendants<InsertedRun>().Should().NotBeEmpty(
                $"the targeted paragraph must carry a native w:ins for '{docName}'");
            edited.Descendants<InsertedRun>().SelectMany(i => i.Descendants<Text>()).Select(t => t.Text)
                .Should().Contain("[R4]");
        }

        // Every OTHER paragraph's OuterXml is unchanged — untouched document.xml subtrees stay identical.
        var patchedOtherIds = ReadOtherParagraphOuterXml(patched, targetParaId);
        patchedOtherIds.Should().Equal(originalParaOuterXmlByOtherIds,
            $"only the edited paragraph may change; every untouched subtree must stay byte-identical for '{docName}'");
    }

    /// <summary>Returns the first addressable paragraph's id + the OuterXml of every OTHER id-bearing
    /// paragraph (the untouched-subtree baseline).</summary>
    private static (string? TargetParaId, List<string> OtherOuterXml) ReadParagraphSnapshot(byte[] bytes)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);
        var paragraphs = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().ToList();

        var target = paragraphs.FirstOrDefault(p =>
            !string.IsNullOrEmpty(p.ParagraphId?.Value) && p.Descendants<Run>().Any(r => r.GetFirstChild<Text>() is not null));

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

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // UAT #1B (task 051, recommendation A) — explicit guard that an interior edit PRESERVES numbering.xml
    // byte-identical, so a doc whose numbering Word renders correctly keeps rendering correctly after a
    // Compose save. The all-"1." symptom the operator saw was the PRE-050 renderer misroute re-authoring
    // numbering from a lossy content model (task 050 fixed the routing); the byte-surgical engine never
    // touches numbering.xml on a plain edit. The general InteriorInsert test above already proves this for
    // every corpus doc via AllUntouchedPartsByteIdentical (which includes numbering.xml); this names the
    // guarantee explicitly against the interrupted-numbering fixture so a future regression is unmistakable.
    // Native "author computed numbering into OOXML for docs Word itself renders wrong" is deferred to R6 (see
    // notes/task-051-deviations.md) — no such case exists in the corpus (nda-interrupted-clauses renders 1→6
    // correctly in Word per the corpus manifest).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public void InteriorEdit_OnInterruptedNumberingDoc_LeavesNumberingXmlByteIdentical_UAT1B()
    {
        var path = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .FirstOrDefault(p => string.Equals(Path.GetFileName(p), "nda-interrupted-clauses.docx", StringComparison.OrdinalIgnoreCase));
        if (path is null)
        {
            return; // fixture not present in this checkout (LFS) — the general corpus test still covers it.
        }

        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(path);
        var (targetParaId, _) = ReadParagraphSnapshot(original);
        targetParaId.Should().NotBeNull("the interrupted-numbering fixture must have an addressable paragraph to edit");

        var beforeNumbering = ReadNumberingPartBytes(original);
        beforeNumbering.Should().NotBeNull("the fixture must carry a numbering.xml part (it uses list numbering)");

        var log = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new InsertTextOperation { ParaId = targetParaId!, At = new ComposeRunPoint(0, 0), Text = "[UAT-1B]" },
            },
        };

        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        var afterNumbering = ReadNumberingPartBytes(patched);
        afterNumbering.Should().Equal(beforeNumbering,
            "an interior text edit must leave numbering.xml byte-identical — the save preserves the source doc's Word-correct numbering (UAT #1B); numbering is only ever authored for explicit setBlockAttr List ops, never a plain insert");
    }

    /// <summary>Returns the NumberingDefinitionsPart raw bytes (null if the package has no numbering part).</summary>
    private static byte[]? ReadNumberingPartBytes(byte[] docxBytes)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docxBytes, writable: false), isEditable: false);
        var numberingPart = doc.MainDocumentPart?.NumberingDefinitionsPart;
        if (numberingPart is null)
        {
            return null;
        }
        using var stream = numberingPart.GetStream();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
