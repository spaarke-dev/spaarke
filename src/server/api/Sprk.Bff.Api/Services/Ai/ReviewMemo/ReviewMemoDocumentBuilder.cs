using Sprk.Bff.Api.Services.Compose;

namespace Sprk.Bff.Api.Services.Ai.ReviewMemo;

/// <summary>
/// FR-14 (ai-advanced-capabilities-agreements-r1 task 051) — builds the "Generate memo" .docx layout
/// (title, doc/analysis metadata, a per-section table {location, before, after, why, golden-ref}) as a
/// plain <see cref="ComposeContentModel"/>, DERIVED ENTIRELY from the PERSISTED task-050
/// <see cref="ReviewMemoDocument"/> — never a fresh LLM call, never client-supplied content (§11 /
/// render-from-persisted, the project's binding constraint for FR-14: the .docx and the "Email memo"
/// body both derive from the SAME server-persisted record, so exports ≡ the durable artifact).
/// </summary>
/// <remarks>
/// <para>
/// <b>Reuse, not a new content model (§11)</b>: mirrors <see cref="ComposeSummaryPageGenerator"/>'s
/// posture exactly — a pure, deterministic template/count over the supplied fields, emitting
/// <see cref="ComposeBlock"/>s that <see cref="ComposeDocumentRenderer.SynthesizeDocument"/> already
/// knows how to author into a from-scratch <c>.docx</c> (the SAME engine the nda-r1 Summary-Page writer
/// used via <c>AppendSection</c> — here used via the from-scratch <c>SynthesizeDocument</c> entry point
/// instead, since the memo is its OWN standalone downloadable file, not an appendix to an existing
/// document). No AI, no second <c>IOpenAiClient</c> call anywhere in this type (ADR-039 complied by
/// construction).
/// </para>
/// <para>
/// <b>Column mapping</b>: the spec's five named columns map onto <see cref="ReviewMemoSection"/> as
/// Location→Location, Before→Before, After→After, Why→Why, "golden-ref"→StandardRef (the retrieved
/// firm-standard citation — the "golden" precedent the clause is measured against).
/// <see cref="ReviewMemoSection.FlaggedClause"/> and <see cref="ReviewMemoSection.RiskLevel"/> are
/// carried on the persisted record for completeness but are NOT separate table columns here (the spec's
/// closed column set is exactly the five named fields) — <c>FlaggedClause</c> restates the same grounded
/// text as <c>Before</c>, and <c>RiskLevel</c> has no assigned column in the FR-14 layout.
/// </para>
/// </remarks>
public static class ReviewMemoDocumentBuilder
{
    private static readonly string[] ColumnHeaders = { "Location", "Before", "After", "Why", "Golden Ref" };

    /// <summary>
    /// Builds the memo's <see cref="ComposeContentModel"/>: a title heading, one metadata paragraph
    /// (document/analysis names + overall risk + section count), and a per-section table. Never returns
    /// an empty model — a memo with zero sections (defensive; the persist path requires ≥1 section, per
    /// task 050) still renders the title + metadata + a "No flagged sections." paragraph.
    /// </summary>
    public static ComposeContentModel Build(ReviewMemoDocument memo, string? documentName, string? analysisName)
    {
        ArgumentNullException.ThrowIfNull(memo);

        var blocks = new List<ComposeBlock>
        {
            Heading("Review Summary Memo", level: 1),
            Paragraph(Run(BuildMetadataLine(memo, documentName, analysisName))),
        };

        blocks.Add(
            memo.Sections.Count == 0
                ? Paragraph(Run("No flagged sections."))
                : BuildTable(memo.Sections));

        return new ComposeContentModel { Blocks = blocks };
    }

    private static string BuildMetadataLine(ReviewMemoDocument memo, string? documentName, string? analysisName)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(documentName))
        {
            parts.Add($"Document: {documentName}");
        }

        if (!string.IsNullOrWhiteSpace(analysisName))
        {
            parts.Add($"Analysis: {analysisName}");
        }

        parts.Add($"Overall Risk: {memo.OverallRisk}");
        parts.Add($"Sections: {memo.SectionCount}");
        return string.Join("   •   ", parts);
    }

    private static ComposeBlock BuildTable(IReadOnlyList<ReviewMemoSection> sections)
    {
        var headerRow = new ComposeTableRow
        {
            Cells = ColumnHeaders.Select(HeaderCell).ToList(),
        };

        var rows = new List<ComposeTableRow> { headerRow };
        foreach (var section in sections)
        {
            rows.Add(new ComposeTableRow
            {
                Cells = new List<ComposeTableCell>
                {
                    Cell(section.Location),
                    Cell(section.Before),
                    Cell(section.After),
                    Cell(section.Why),
                    Cell(section.StandardRef),
                },
            });
        }

        return new ComposeBlock
        {
            Kind = ComposeBlockKind.Table,
            Table = new ComposeTable { Rows = rows },
        };
    }

    // Bolding for header cells is applied by ComposeDocumentRenderer.BuildTableCell itself
    // (cell.IsHeader ? EmphasizeBlock : ...) — not duplicated here.
    private static ComposeTableCell HeaderCell(string text) => new()
    {
        IsHeader = true,
        Blocks = new[] { Paragraph(Run(text)) },
    };

    private static ComposeTableCell Cell(string? text) => new()
    {
        Blocks = new[] { Paragraph(Run(text ?? string.Empty)) },
    };

    private static ComposeBlock Heading(string text, int level) => new()
    {
        Kind = ComposeBlockKind.Heading,
        Level = level,
        Runs = new[] { Run(text) },
    };

    private static ComposeBlock Paragraph(params ComposeInlineRun[] runs) => new()
    {
        Kind = ComposeBlockKind.Paragraph,
        Runs = runs,
    };

    private static ComposeInlineRun Run(string text) => new() { Text = text };
}
