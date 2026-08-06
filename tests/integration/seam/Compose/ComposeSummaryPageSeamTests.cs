// Task 041 (ai-advanced-capabilities-nda-r1, Phase 4) — seam proof for the NDA-REVIEW Summary Page writer:
// ComposeSummaryPageGenerator.Build (pure content-model builder) + ComposeDocumentRenderer.AppendSection
// (the byte[]-in/byte[]-out OOXML author), driven against the REAL compose-corpus fixtures (the same
// production types + fixtures ComposeShadowPatchEngineByteDiffSeamTests / ComposePatchEngineSaveSeamTests
// use — no new fixture, per root CLAUDE.md §11).
//
// "No second LLM call" proof: the input to ComposeSummaryPageGenerator.Build is deserialized DIRECTLY from
// a literal JSON string carrying the EXACT ledgered NDA-REVIEW shape ({overallRisk, flaggedSections[]} per
// infra/dataverse/actions/nda-review.action.json's outputSchema — the SAME shape task 023's dispatch spine
// ledgers before any render). No IOpenAiClient / executor / routing type appears anywhere in this file or
// in the production types under test — by construction there is no model call in this path.
//
// ADR-038 seam DoD: production types only (ComposeDocumentRenderer, ComposeSummaryPageGenerator), real
// corpus bytes, no Mock<HttpMessageHandler>, no DI-registration test, no ctor-null test.

using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeSummaryPageSeamTests
{
    private readonly ComposeDocumentRenderer _renderer = new();

    // The exact ledgered NDA-REVIEW shape (task 023) — hand-authored JSON standing in for the stored
    // ledger entry. Deserializing it straight into NdaReviewSummaryPageInput IS the "derive from the
    // ledgered result" proof: no remapping, no second model call.
    private const string LedgeredHighRiskResultJson = """
        {
          "overallRisk": "High",
          "flaggedSections": [
            {
              "sectionRef": "Section 3.1, para 2 (p. 2)",
              "quotedText": "Confidential Information means any information marked confidential.",
              "riskLevel": "High",
              "explanation": "The definition is gated on marking, so oral and unmarked disclosures receive no protection at all, which is materially narrower than the firm standard and risks losing coverage for the bulk of what is actually shared in due diligence conversations.",
              "standardRef": "B3 - Definition of Confidential Information"
            },
            {
              "sectionRef": "Section 7, para 1 (p. 4)",
              "quotedText": "This Agreement shall survive indefinitely.",
              "riskLevel": "Medium",
              "explanation": "An indefinite survival term for ordinary (non-trade-secret) information exceeds the standard's 3-5 year window.",
              "standardRef": "B8 - Term & confidentiality period"
            }
          ]
        }
        """;

    private const string LedgeredCleanResultJson = """
        {
          "overallRisk": "Low",
          "flaggedSections": []
        }
        """;

    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(path => new object[] { path });

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. The Summary Page lands as a well-formed, page-broken section at the END of the document.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void AppendSection_WithLedgeredHighRiskResult_AddsPageBrokenSummaryAtDocumentEnd(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);
        var originalParagraphIds = ReadParagraphIds(original);

        var input = JsonSerializer.Deserialize<NdaReviewSummaryPageInput>(LedgeredHighRiskResultJson)!;
        var blocks = ComposeSummaryPageGenerator.Build(input);

        var result = _renderer.AppendSection(original, blocks);

        // Well-formed OOXML: the SDK can re-open the result and every original paragraph still resolves.
        using var doc = WordprocessingDocument.Open(new MemoryStream(result, writable: false), isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var paragraphs = body.Descendants<Paragraph>().ToList();

        foreach (var id in originalParagraphIds)
        {
            paragraphs.Any(p => string.Equals(p.ParagraphId?.Value, id, StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue($"original paragraph '{id}' must still resolve after appending the Summary Page for '{docName}'");
        }

        // A manual page break exists among the appended content.
        body.Descendants<Break>()
            .Any(b => b.Type is not null && b.Type.Value == BreakValues.Page)
            .Should().BeTrue($"the Summary Page must be preceded by a manual page break for '{docName}'");

        // The Summary Page content (TL;DR + overview + recommendation) is present, in document order AFTER
        // every original paragraph.
        var allText = string.Join("\n", paragraphs.Select(p => p.InnerText));
        allText.Should().Contain("NDA Review — Summary");
        allText.Should().Contain("TL;DR:");
        allText.Should().Contain("2 flagged section(s)"); // deterministic count, not free-form prose
        allText.Should().Contain("Overall risk: High");
        allText.Should().Contain("Section 3.1, para 2 (p. 2)");
        allText.Should().Contain("B3 - Definition of Confidential Information");
        allText.Should().Contain("Recommendations");
        allText.Should().Contain("Recommend attorney review and negotiation of the High-risk finding(s) before signature.");
        allText.Should().Contain("not legal advice");

        // The LAST body child is still the (single) trailing SectionProperties — the appended content landed
        // INSIDE the final section, not after/outside it.
        body.Elements().Last().Should().BeOfType<SectionProperties>(
            $"the trailing sectPr must remain the last body child for '{docName}'");
        body.Elements<SectionProperties>().Should().HaveCount(1,
            $"AppendSection must never introduce a second body-level sectPr for '{docName}'");

        // Every appended paragraph carries a unique, non-empty w14:paraId (E2 substrate).
        var appendedIds = paragraphs
            .Select(p => p.ParagraphId?.Value)
            .Where(id => !string.IsNullOrEmpty(id) && !originalParagraphIds.Contains(id!, StringComparer.OrdinalIgnoreCase))
            .ToList();
        appendedIds.Should().NotBeEmpty($"the appended Summary Page paragraphs must carry minted paraIds for '{docName}'");
        appendedIds.Should().OnlyHaveUniqueItems($"every appended paraId must be unique for '{docName}'");

        // No tracked-change marks — the Summary Page is server-authored FINAL content, not a proposed edit.
        body.Descendants<InsertedRun>().Should().BeEmpty(
            $"the Summary Page must NOT be emitted as a tracked w:ins insertion for '{docName}' (ADR-049 — this is not a ComposeShadowPatchEngine operation)");

        // NFR-01 (reused comparer, task 004): every package part OTHER than document.xml — styles,
        // numbering, headers/footers, theme, media, ... — is byte-identical. AppendSection only opens
        // MainDocumentPart.Document; every other part the SDK copies verbatim.
        var comparison = ComposeOoxmlPackagePartComparer.Compare(original, result, strictDocumentXmlByteIdentity: false);
        comparison.AllUntouchedPartsByteIdentical.Should().BeTrue(
            $"NFR-01: appending the Summary Page must leave every non-document.xml package part byte-identical for '{docName}' — {comparison.DescribeMismatches()}");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. A clean NDA (zero findings) still gets a positive Summary Page — never a blank/absent one.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AppendSection_WithLedgeredCleanResult_AddsNoMaterialDeviationsSummary()
    {
        var corpusDocPath = ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First();
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);

        var input = JsonSerializer.Deserialize<NdaReviewSummaryPageInput>(LedgeredCleanResultJson)!;
        var blocks = ComposeSummaryPageGenerator.Build(input);

        var result = _renderer.AppendSection(original, blocks);

        using var doc = WordprocessingDocument.Open(new MemoryStream(result, writable: false), isEditable: false);
        var allText = string.Join("\n", doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().Select(p => p.InnerText));

        allText.Should().Contain("No material deviations from the firm NDA standard were found.");
        allText.Should().Contain("Overall risk: Low");
        allText.Should().Contain("No material concerns identified; safe to proceed, subject to standard review.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. Untouched original content stays byte-for-byte unchanged (I-4 spirit) — only new trailing content
    //    is added; every existing paragraph's OuterXml is preserved.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void AppendSection_LeavesEveryOriginalParagraphOuterXmlUnchanged(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);
        var originalOuterXmlById = ReadParagraphOuterXmlById(original);

        var input = JsonSerializer.Deserialize<NdaReviewSummaryPageInput>(LedgeredHighRiskResultJson)!;
        var blocks = ComposeSummaryPageGenerator.Build(input);
        var result = _renderer.AppendSection(original, blocks);

        var resultOuterXmlById = ReadParagraphOuterXmlById(result);

        // Task 013 re-baseline (the AppendSection dup-paraId finding, notes S8/S17): a source document can
        // carry DUPLICATE w14:paraId values (the NDA: the SAME ids appear in a construct's mc:Choice AND
        // mc:Fallback text-box copies). AppendSection's E2 dedup re-mints the duplicate occurrences so the
        // package it returns is anchorable - which legitimately mutates the HOST paragraph's OuterXml.
        // Those paragraphs are exempt from byte-identity; their CONTENT (InnerText) must still be
        // unchanged. Every other paragraph stays byte-identical (I-4 spirit).
        HashSet<string> sourceDuplicatedIds;
        using (var srcDoc = WordprocessingDocument.Open(new MemoryStream(original, writable: false), isEditable: false))
        {
            sourceDuplicatedIds = srcDoc.MainDocumentPart!.Document!.Body!
                .Descendants<Paragraph>()
                .Select(p => p.ParagraphId?.Value)
                .Where(v => !string.IsNullOrEmpty(v))
                .GroupBy(v => v!, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        bool SubtreeCarriesDuplicatedId(string xml) => sourceDuplicatedIds.Any(dup =>
            xml.Contains($"w14:paraId=\"{dup}\"", StringComparison.OrdinalIgnoreCase));

        foreach (var (id, outerXml) in originalOuterXmlById)
        {
            resultOuterXmlById.Should().ContainKey(id, $"original paragraph '{id}' must still exist for '{docName}'");
            if (sourceDuplicatedIds.Count > 0 && SubtreeCarriesDuplicatedId(outerXml))
            {
                var strippedOriginal = System.Text.RegularExpressions.Regex.Replace(outerXml, "w14:paraId=\"[0-9A-Fa-f]{1,8}\"", "w14:paraId=\"X\"");
                var strippedResult = System.Text.RegularExpressions.Regex.Replace(resultOuterXmlById[id], "w14:paraId=\"[0-9A-Fa-f]{1,8}\"", "w14:paraId=\"X\"");
                strippedResult.Should().Be(strippedOriginal,
                    $"paragraph '{id}' in '{docName}' carries source-duplicated nested paraIds - the E2 dedup may re-mint THOSE ids, but nothing else in the paragraph may change");
                continue;
            }
            resultOuterXmlById[id].Should().Be(outerXml,
                $"original paragraph '{id}' must stay byte-identical after appending the Summary Page for '{docName}'");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4. No-op contract — an empty block list returns the input unchanged (mirrors
    //    ComposeShadowPatchEngine.Apply's empty-log no-op).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AppendSection_WithEmptyBlocks_ReturnsInputByteIdentical()
    {
        var corpusDocPath = ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First();
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);

        var result = _renderer.AppendSection(original, Array.Empty<ComposeBlock>());

        result.Should().Equal(original, "an empty block list must be a byte-identical no-op passthrough");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    private static List<string> ReadParagraphIds(byte[] bytes)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Select(p => p.ParagraphId?.Value)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToList();
    }

    private static Dictionary<string, string> ReadParagraphOuterXmlById(byte[] bytes)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>())
        {
            var id = p.ParagraphId?.Value;
            if (!string.IsNullOrEmpty(id) && !map.ContainsKey(id!))
            {
                map[id!] = p.OuterXml;
            }
        }

        return map;
    }
}
