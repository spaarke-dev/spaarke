using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Task 041 (ai-advanced-capabilities-nda-r1, Phase 4) — builds the NDA-REVIEW "Summary Page" content
/// (TL;DR + flagged-section overview + recommendations) as a plain <see cref="ComposeContentModel"/> block
/// sequence, DERIVED ENTIRELY from the ONE ledgered NDA-REVIEW result (task 023's
/// <c>{overallRisk, flaggedSections[]}</c> payload — the exact closed contract the <c>nda-review</c> Action's
/// <c>outputSchema</c> emits; see <c>infra/dataverse/actions/nda-review.action.json</c>). Every sentence this
/// class emits is a DETERMINISTIC template/count over the supplied fields — NO free-form generation, NO
/// second <c>IOpenAiClient</c>/model call anywhere in this type (ADR-039 complied by construction: this is a
/// pure text transformer, the same shape as <see cref="SemanticAppendixGenerator"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Reuse, not a new content model (§11)</b>: the output is <see cref="ComposeBlock"/>s — the SAME content
/// model <see cref="ComposeDocumentRenderer"/> already knows how to materialize (task 026/027). Every emitted
/// block is <see cref="ComposeBlockKind.Paragraph"/> with a plain (non-styled, non-numbered) run sequence —
/// deliberately style/numbering-INDEPENDENT (bold via <see cref="ComposeInlineRun.Bold"/> only; a literal
/// "•" bullet character, not a real <c>w:numPr</c> list item) — so <see cref="ComposeDocumentRenderer.AppendSection"/>
/// never needs to merge into (or collide with) the TARGET document's own <c>StyleDefinitionsPart</c> /
/// <c>NumberingDefinitionsPart</c>, whatever they contain.
/// </para>
/// <para>
/// <b>Concise by design</b>: each flagged-section overview line is a single line (locator + severity +
/// a truncated explanation + the firm-standard clause) — the full <c>explanation</c>/<c>quotedText</c> stays
/// in the advisory comment thread (task 031/040), not duplicated here.
/// </para>
/// </remarks>
public static class ComposeSummaryPageGenerator
{
    private const int MaxOverviewExplanationChars = 220;

    /// <summary>
    /// Builds the Summary Page block sequence from <paramref name="input"/>. Never returns an empty list
    /// (even a clean NDA with zero findings gets a positive "no material deviations" line) — the caller
    /// (<see cref="ComposeDocumentRenderer.AppendSection"/>) always has content to append when this is called.
    /// </summary>
    public static IReadOnlyList<ComposeBlock> Build(NdaReviewSummaryPageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var findings = input.FlaggedSections ?? Array.Empty<NdaReviewFlaggedSectionInput>();
        var counts = CountBySeverity(findings);

        var blocks = new List<ComposeBlock>
        {
            Paragraph(Run("NDA Review — Summary", bold: true)),
            Paragraph(Run("TL;DR: ", bold: true), Run(BuildTldr(input.OverallRisk, findings.Count, counts))),
            Paragraph(Run("Flagged Sections", bold: true)),
        };

        if (findings.Count == 0)
        {
            blocks.Add(Paragraph(Run("No material deviations from the firm NDA standard were found.")));
        }
        else
        {
            foreach (var finding in findings)
            {
                blocks.Add(Paragraph(Run("• "), Run(BuildOverviewLine(finding))));
            }
        }

        blocks.Add(Paragraph(Run("Recommendations", bold: true)));
        blocks.Add(Paragraph(Run(BuildRecommendation(input.OverallRisk, counts))));
        blocks.Add(Paragraph(Run(
            "This is an AI-generated advisory summary for attorney review — not legal advice.",
            italic: true)));

        return blocks;
    }

    /// <summary>
    /// One "• [Risk] Locator — why (Standard: ref)" overview line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Prefers <c>assessment</c> over <c>explanation</c></b> (R8, 2026-09-07). This line is a ONE-LINE
    /// digest truncated at 220 chars, and the judgment is what belongs in it. Post-FR-05 payloads compose
    /// <c>explanation</c> as <c>flaggedClause + "\n\n" + assessment</c>, so truncating it yields the
    /// grounded FACT and drops the judgment — the same wrong-half defect §GAPS-5 Phase 1 fixed in the
    /// panel. Legacy (pre-split) payloads still fall back to <c>explanation</c>.
    /// </para>
    /// <para>
    /// <b>Absent parts are OMITTED, not em-dashed</b> — deliberately unlike <c>ReviewMemoAssembler</c>,
    /// which fills a table CELL where a blank reads as a rendering bug. This is running prose: "(Standard: —)"
    /// is noise, whereas simply having no standard clause is self-evident. A missing locator still needs a
    /// word, because the line would otherwise start mid-sentence.
    /// </para>
    /// </remarks>
    private static string BuildOverviewLine(NdaReviewFlaggedSectionInput finding)
    {
        var risk = string.IsNullOrWhiteSpace(finding.RiskLevel) ? "Unrated" : finding.RiskLevel.Trim();
        var locator = string.IsNullOrWhiteSpace(finding.SectionRef) ? "Unreferenced" : finding.SectionRef.Trim();

        // The judgment is the digest. Fall back to the fused legacy blob only when there is no discrete one.
        var why = !string.IsNullOrWhiteSpace(finding.Assessment) ? finding.Assessment : finding.Explanation;
        var line = $"[{risk}] {locator}";

        var truncated = Truncate(why, MaxOverviewExplanationChars);
        if (!string.IsNullOrWhiteSpace(truncated))
        {
            line += $" — {truncated}";
        }

        if (!string.IsNullOrWhiteSpace(finding.StandardRef))
        {
            line += $" (Standard: {finding.StandardRef.Trim()})";
        }

        return line;
    }

    private static string BuildTldr(string overallRisk, int findingCount, SeverityCounts counts)
    {
        if (findingCount == 0)
        {
            return $"No material deviations from the firm NDA standard were found. Overall risk: {overallRisk}.";
        }

        return
            $"{findingCount} flagged section(s) — {counts.Critical} Critical, {counts.High} High, " +
            $"{counts.Medium} Medium, {counts.Low} Low. Overall risk: {overallRisk}.";
    }

    private static string BuildRecommendation(string overallRisk, SeverityCounts counts) =>
        NormalizeRisk(overallRisk) switch
        {
            "critical" =>
                "Do not sign until the Critical/High finding(s) above are resolved with counsel.",
            "high" =>
                "Recommend attorney review and negotiation of the High-risk finding(s) before signature.",
            "medium" =>
                "Recommend incorporating the noted edits before signature.",
            _ => counts.Total == 0
                ? "No material concerns identified; safe to proceed, subject to standard review."
                : "Minor drafting-integrity items noted above; safe to proceed once addressed.",
        };

    private static SeverityCounts CountBySeverity(IReadOnlyList<NdaReviewFlaggedSectionInput> findings)
    {
        int critical = 0, high = 0, medium = 0, low = 0;
        foreach (var finding in findings)
        {
            switch (NormalizeRisk(finding.RiskLevel))
            {
                case "critical": critical++; break;
                case "high": high++; break;
                case "medium": medium++; break;
                default: low++; break;
            }
        }

        return new SeverityCounts(critical, high, medium, low);
    }

    private static string NormalizeRisk(string? riskLevel) =>
        (riskLevel ?? string.Empty).Trim().ToLowerInvariant();

    private static string Truncate(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            return text ?? string.Empty;
        }

        return text[..maxChars].TrimEnd() + "…";
    }

    private static ComposeBlock Paragraph(params ComposeInlineRun[] runs) =>
        new() { Kind = ComposeBlockKind.Paragraph, Runs = runs };

    private static ComposeInlineRun Run(string text, bool bold = false, bool italic = false) =>
        new() { Text = text, Bold = bold, Italic = italic };

    private readonly record struct SeverityCounts(int Critical, int High, int Medium, int Low)
    {
        public int Total => Critical + High + Medium + Low;
    }
}

/// <summary>
/// The ledgered NDA-REVIEW result — a mirror-first projection of the <c>nda-review</c> Action's closed
/// <c>outputSchema</c> (<c>infra/dataverse/actions/nda-review.action.json</c>: <c>{overallRisk,
/// flaggedSections[]}</c>, task 020). Deserializing the SAME JSON task 023's dispatch spine ledgers
/// (<c>{bindingId}@t{n}</c>, ADR-040 store-before-render) directly into this type IS the "derive from the
/// ledgered result, no second LLM call" proof — no field is renamed, remapped, or re-synthesized.
/// </summary>
public sealed record NdaReviewSummaryPageInput(
    [property: JsonPropertyName("overallRisk")] string OverallRisk,
    [property: JsonPropertyName("flaggedSections")] IReadOnlyList<NdaReviewFlaggedSectionInput> FlaggedSections);

/// <summary>One flagged section from the ledgered NDA-REVIEW result (mirrors the Action's per-finding
/// output schema exactly — <c>sectionRef</c>/<c>quotedText</c>/<c>riskLevel</c>/<c>explanation</c>/
/// <c>standardRef</c>). <see cref="QuotedText"/> is carried for schema fidelity but is NOT rendered on the
/// Summary Page (kept concise; the verbatim excerpt lives in the advisory comment thread, task 031/040).</summary>
/// <remarks>
/// <para>
/// <b>Every field is optional (R8, 2026-09-07).</b> They were all required, which meant a
/// partially-grounded finding could not be sent at all — the same over-strict contract §GAPS-5 decision
/// D3 relaxed on the Review Summary. The model does not always ground every finding; the page renders
/// what it has rather than refusing the payload or dropping the row.
/// </para>
/// <para>
/// <b><see cref="FlaggedClause"/>/<see cref="Assessment"/> are the POST-SPLIT fields</b> (FR-05). This
/// type modelled only the pre-split fused <see cref="Explanation"/>, so on a current payload the one-line
/// overview would truncate the composed blob at 220 chars — which starts with the grounded FACT, cutting
/// off the judgment. That is the identical defect §GAPS-5 Phase 1 fixed in the review-summary panel, and
/// it would have re-appeared here the moment a client wired this up.
/// </para>
/// </remarks>
public sealed record NdaReviewFlaggedSectionInput(
    [property: JsonPropertyName("sectionRef")] string? SectionRef = null,
    [property: JsonPropertyName("quotedText")] string? QuotedText = null,
    [property: JsonPropertyName("riskLevel")] string? RiskLevel = null,
    [property: JsonPropertyName("explanation")] string? Explanation = null,
    [property: JsonPropertyName("standardRef")] string? StandardRef = null,
    [property: JsonPropertyName("flaggedClause")] string? FlaggedClause = null,
    [property: JsonPropertyName("assessment")] string? Assessment = null);
