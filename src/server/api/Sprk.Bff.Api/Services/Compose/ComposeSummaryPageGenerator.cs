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
                var explanation = Truncate(finding.Explanation, MaxOverviewExplanationChars);
                var line =
                    $"[{finding.RiskLevel}] {finding.SectionRef} — {explanation} (Standard: {finding.StandardRef})";
                blocks.Add(Paragraph(Run("• "), Run(line)));
            }
        }

        blocks.Add(Paragraph(Run("Recommendations", bold: true)));
        blocks.Add(Paragraph(Run(BuildRecommendation(input.OverallRisk, counts))));
        blocks.Add(Paragraph(Run(
            "This is an AI-generated advisory summary for attorney review — not legal advice.",
            italic: true)));

        return blocks;
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
public sealed record NdaReviewFlaggedSectionInput(
    [property: JsonPropertyName("sectionRef")] string SectionRef,
    [property: JsonPropertyName("quotedText")] string QuotedText,
    [property: JsonPropertyName("riskLevel")] string RiskLevel,
    [property: JsonPropertyName("explanation")] string Explanation,
    [property: JsonPropertyName("standardRef")] string StandardRef);
