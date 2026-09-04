// spaarkeai-compose-r8 (UAT item 8) — seam proof for the Document Revision Report appendix:
// ComposeRevisionReportGenerator.Build (pure content-model builder) + ComposeDocumentRenderer.AppendSection
// (the byte[]-in/byte[]-out OOXML author), driven against the REAL compose-corpus fixtures. Deliberately
// mirrors ComposeSummaryPageSeamTests.cs — the same shipped append path, a different report subject — so
// no new fixture and no new mechanism (root CLAUDE.md §11).
//
// "No second LLM call" proof: the input to ComposeRevisionReportGenerator.Build is deserialized DIRECTLY
// from a literal JSON string carrying the EXACT ledgered compose-summarize-word-changes shape
// ({summary, changes[]} per infra/dataverse/outputschemas/compose-summarize-word-changes.schema.json).
// No IOpenAiClient / executor / routing type appears in this file or in the production types under test.
//
// ADR-038 seam DoD: production types only, real corpus bytes, no Mock<HttpMessageHandler>, no
// DI-registration test, no ctor-null test.

using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeRevisionReportSeamTests
{
    private readonly ComposeDocumentRenderer _renderer = new();

    // The exact ledgered compose-summarize-word-changes shape — hand-authored JSON standing in for the
    // stored ledger entry. Deserializing it straight into ComposeRevisionReportInput IS the "derive from
    // the ledgered result" proof: no remapping, no second model call.
    private const string LedgeredResultJson = """
        {
          "summary": "The reviewer narrowed the indemnity to direct damages and added a 12-month liability cap, and questioned whether the confidentiality obligation should be mutual.",
          "changes": [
            {
              "kind": "deletion",
              "location": "Section 7.2 (Indemnification)",
              "description": "Removed 'consequential and indirect' from the categories of recoverable damages."
            },
            {
              "kind": "insertion",
              "location": "Section 7.4 (Limitation of Liability)",
              "description": "Added a cap of twelve (12) months' fees on aggregate liability."
            },
            {
              "kind": "comment",
              "location": "Section 4.1 (Confidentiality)",
              "description": "Asked whether the confidentiality obligation should run mutually."
            }
          ],
          "documentName": "Master Services Agreement.docx",
          "documentVersion": "7",
          "asOf": "2026-09-03T14:30:00Z"
        }
        """;

    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(path => new object[] { path });

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. The report lands as a well-formed, page-broken section at the END of every corpus document.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void AppendSection_WithLedgeredChangeSummary_AddsPageBrokenReportAtDocumentEnd(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);
        var originalParagraphIds = ReadParagraphIds(original);

        var input = JsonSerializer.Deserialize<ComposeRevisionReportInput>(LedgeredResultJson)!;
        var blocks = ComposeRevisionReportGenerator.Build(input);

        var result = _renderer.AppendSection(original, blocks);

        using var doc = WordprocessingDocument.Open(new MemoryStream(result, writable: false), isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var paragraphs = body.Descendants<Paragraph>().ToList();

        // Every original paragraph still resolves — the append disturbed nothing.
        foreach (var id in originalParagraphIds)
        {
            paragraphs.Any(p => string.Equals(p.ParagraphId?.Value, id, StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue($"original paragraph '{id}' must still resolve after appending the report for '{docName}'");
        }

        body.Descendants<Break>()
            .Any(b => b.Type is not null && b.Type.Value == BreakValues.Page)
            .Should().BeTrue($"the report must be preceded by a manual page break for '{docName}'");

        var allText = string.Join("\n", paragraphs.Select(p => p.InnerText));
        allText.Should().Contain("Document Revision Report");
        allText.Should().Contain("Summary of Changes");
        allText.Should().Contain("12-month liability cap");
        allText.Should().Contain("Section 7.2 (Indemnification)");
        allText.Should().Contain("[deletion]");
        allText.Should().Contain("has not been verified");

        // The appended content landed INSIDE the final section, not after or outside it.
        body.Elements().Last().Should().BeOfType<SectionProperties>(
            $"the trailing sectPr must remain the last body child for '{docName}'");
        body.Elements<SectionProperties>().Should().HaveCount(1,
            $"AppendSection must never introduce a second body-level sectPr for '{docName}'");

        var appendedIds = paragraphs
            .Select(p => p.ParagraphId?.Value)
            .Where(id => !string.IsNullOrEmpty(id) && !originalParagraphIds.Contains(id!, StringComparer.OrdinalIgnoreCase))
            .ToList();
        appendedIds.Should().NotBeEmpty($"the appended report paragraphs must carry minted paraIds for '{docName}'");
        appendedIds.Should().OnlyHaveUniqueItems($"every appended paraId must be unique for '{docName}'");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. The report cannot disturb the host document's numbering or styles.
    //
    // This is the constraint the generator is BUILT around (plain paragraphs, literal "•", never a real
    // w:numPr), and it is the one a future "let's make the bullets proper list items" change would break
    // silently — a merged numbering part can renumber the agreement the report is appended to. Asserted
    // against the corpus, where the documents that HAVE numbering are the ones at risk.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void AppendSection_WithLedgeredChangeSummary_LeavesNumberingAndStylePartsUntouched(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);
        var before = ReadPartFingerprints(original);

        var input = JsonSerializer.Deserialize<ComposeRevisionReportInput>(LedgeredResultJson)!;
        var result = _renderer.AppendSection(original, ComposeRevisionReportGenerator.Build(input));

        var after = ReadPartFingerprints(result);

        after.NumberingXml.Should().Be(before.NumberingXml,
            $"the report must not add or merge numbering definitions for '{docName}' — a merged numbering " +
            "part can renumber the host agreement");
        after.StylesXml.Should().Be(before.StylesXml,
            $"the report must not add or merge style definitions for '{docName}'");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. The scope line — which document, which version, when.
    //
    // Owner requirement (2026-09-03). The summary describes the document AS SAVED, because
    // pull-annotations reads stored bytes rather than the editor's unsaved state. A report that does not
    // say which version it covers is wrong in a way its reader cannot detect.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Build_WithDocumentIdentity_StatesTheVersionAndDateTheReportCovers()
    {
        var input = JsonSerializer.Deserialize<ComposeRevisionReportInput>(LedgeredResultJson)!;

        var text = TextOf(ComposeRevisionReportGenerator.Build(input));

        text.Should().Contain("as of the last save");
        text.Should().Contain("Master Services Agreement.docx");
        text.Should().Contain("version 7");
        text.Should().Contain("3 September 2026");
    }

    [Fact]
    public void Build_WithoutDocumentIdentity_SaysTheFieldsAreUnrecordedRatherThanOmittingTheScopeLine()
    {
        // Dropping the line when its fields are missing would leave a report that reads as current. The
        // honest failure is a stated unknown, so the absence is pinned rather than left to preference.
        var input = new ComposeRevisionReportInput("A reviewer made edits.", Array.Empty<ComposeRevisionChangeInput>());

        var text = TextOf(ComposeRevisionReportGenerator.Build(input));

        text.Should().Contain("as of the last save");
        text.Should().Contain("version not recorded");
        text.Should().Contain("date not recorded");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4. Nothing to report ⇒ nothing to append.
    //
    // The generator's half of the refusal contract the client-side producer enforces: a revision report
    // over no changes is the fabricated "[Insertion]" in document form.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Build_WithNoSummaryAndNoChanges_ReturnsAnEmptyListSoTheCallerAppendsNothing()
    {
        var input = new ComposeRevisionReportInput(string.Empty, Array.Empty<ComposeRevisionChangeInput>());

        ComposeRevisionReportGenerator.Build(input).Should().BeEmpty(
            "an appendix promising a report and delivering a bare heading is worse than no appendix");
    }

    [Fact]
    public void Build_WithASummaryButNoItemisedChanges_SaysSoRatherThanLeavingABareHeading()
    {
        var input = new ComposeRevisionReportInput("The reviewer made minor drafting edits throughout.", Array.Empty<ComposeRevisionChangeInput>());

        var text = TextOf(ComposeRevisionReportGenerator.Build(input));

        text.Should().Contain("The reviewer made minor drafting edits throughout.");
        text.Should().Contain("No individual changes were itemised.");
    }

    [Fact]
    public void Build_IsDeterministic_TheSameLedgeredResultProducesTheIdenticalReport()
    {
        // The appendix is a durable artifact; regenerating it from the same ledger entry must not produce
        // a differently-worded document.
        var first = TextOf(ComposeRevisionReportGenerator.Build(JsonSerializer.Deserialize<ComposeRevisionReportInput>(LedgeredResultJson)!));
        var second = TextOf(ComposeRevisionReportGenerator.Build(JsonSerializer.Deserialize<ComposeRevisionReportInput>(LedgeredResultJson)!));

        second.Should().Be(first);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static string TextOf(IReadOnlyList<ComposeBlock> blocks) =>
        string.Join("\n", blocks.Select(b => string.Concat(b.Runs.Select(r => r.Text))));

    private static HashSet<string> ReadParagraphIds(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Select(p => p.ParagraphId?.Value)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static (string NumberingXml, string StylesXml) ReadPartFingerprints(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        var main = doc.MainDocumentPart!;
        return (
            main.NumberingDefinitionsPart?.Numbering?.OuterXml ?? "(no numbering part)",
            main.StyleDefinitionsPart?.Styles?.OuterXml ?? "(no styles part)");
    }
}
