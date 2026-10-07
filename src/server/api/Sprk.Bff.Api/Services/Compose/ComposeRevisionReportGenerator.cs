using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// spaarkeai-compose-r8 (UAT item 8) — builds the "Document Revision Report" appendix content (scope line,
/// plain-language summary, itemised change list) as a plain <see cref="ComposeContentModel"/> block
/// sequence, DERIVED ENTIRELY from the ledgered <c>compose-summarize-word-changes</c> result (the Action's
/// closed <c>{summary, changes[]}</c> output schema —
/// <c>infra/dataverse/outputschemas/compose-summarize-word-changes.schema.json</c>) plus the document
/// identity the caller supplies. Every sentence this class emits is a DETERMINISTIC template over those
/// fields — NO free-form generation, NO <c>IOpenAiClient</c> call anywhere in this type.
/// </summary>
/// <remarks>
/// <para>
/// <b>The sibling this deliberately mirrors</b>: <see cref="ComposeSummaryPageGenerator"/> (the NDA-REVIEW
/// Summary Page, nda-r1 task 041). Same posture, same append path
/// (<see cref="ComposeDocumentRenderer.AppendSection"/>), same opt-in-per-save request shape. This is the
/// revision-report instance of an already-shipped pattern, not a new mechanism (CLAUDE.md §11):
/// </para>
/// <list type="bullet">
///   <item><b>Existing</b>: <see cref="ComposeSummaryPageGenerator"/> renders an NDA advisory digest —
///   risk-rated findings with firm-standard citations. A revision report is a different subject (what a
///   reviewer CHANGED) over a different closed contract (<c>{summary, changes[]}</c>, no risk rating and
///   no standard reference), so there is no shared body to extend.</item>
///   <item><b>Extension</b>: folding both into one generator would mean a type that switches on which
///   Action produced its input and shares no rendering logic — two templates in a trenchcoat. The reused
///   pieces are the ones that matter and they are already shared: the content model, the renderer, and
///   the append path.</item>
///   <item><b>Cost-of-doing-nothing</b>: the summary exists only as chat markdown in the Assistant pane. A
///   reviewer who wants to send the document back with "here is what we changed" retypes it by hand, and
///   nothing travels with the file to a print or a PDF.</item>
/// </list>
/// <para>
/// <b>Style- and numbering-INDEPENDENT by construction</b> — mirroring the Summary Page's own constraint
/// verbatim. Every emitted block is a <see cref="ComposeBlockKind.Paragraph"/> with plain runs (bold via
/// <see cref="ComposeInlineRun.Bold"/> only; a literal "•" character, never a real <c>w:numPr</c> list
/// item), so <see cref="ComposeDocumentRenderer.AppendSection"/> never has to merge into — or collide
/// with — the target document's own <c>StyleDefinitionsPart</c> / <c>NumberingDefinitionsPart</c>,
/// whatever they contain. A report appended to a heavily-styled agreement cannot disturb its numbering.
/// </para>
/// <para>
/// <b>The scope line is load-bearing, not decoration</b> (owner requirement, 2026-09-03). The summary is
/// produced from the tracked changes in the document AS SAVED — <c>pull-annotations</c> reads the stored
/// bytes, not the editor's unsaved state. A report that does not say which version it describes is wrong
/// in a way the reader cannot detect, so <see cref="Build"/> always emits the scope line, and states
/// plainly when a field was not supplied rather than omitting the line and implying currency.
/// </para>
/// <para>
/// <b>Empty input returns an EMPTY list</b> — deliberately unlike <see cref="ComposeSummaryPageGenerator"/>,
/// which always has content because a clean NDA is itself a finding ("no material deviations"). There is
/// no equivalent positive statement here: a revision report over no changes is exactly the fabricated
/// "[Insertion]" the upstream producer refuses to enable (see
/// <c>composeChangesText.ts</c>'s refusal contract). The caller MUST skip the append on an empty result
/// rather than append a heading with nothing under it.
/// </para>
/// </remarks>
public static class ComposeRevisionReportGenerator
{
    private const int MaxDescriptionChars = 300;

    /// <summary>
    /// Builds the revision-report block sequence from <paramref name="input"/>. Returns an EMPTY list when
    /// there is nothing to report (no summary text AND no itemised changes) — the caller must not append.
    /// </summary>
    public static IReadOnlyList<ComposeBlock> Build(ComposeRevisionReportInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var changes = input.Changes ?? Array.Empty<ComposeRevisionChangeInput>();
        var summary = (input.Summary ?? string.Empty).Trim();

        // Nothing to report — see the "Empty input" remark. An appendix promising a report and delivering a
        // bare heading is worse than no appendix.
        if (summary.Length == 0 && changes.Count == 0)
        {
            return Array.Empty<ComposeBlock>();
        }

        var blocks = new List<ComposeBlock>
        {
            Paragraph(Run("Document Revision Report", bold: true)),
            Paragraph(Run(BuildScopeLine(input), italic: true)),
        };

        if (summary.Length > 0)
        {
            blocks.Add(Paragraph(Run("Summary of Changes", bold: true)));
            blocks.Add(Paragraph(Run(summary)));
        }

        blocks.Add(Paragraph(Run("Changes", bold: true)));

        if (changes.Count == 0)
        {
            // The model produced a narrative but itemised nothing. Say that, rather than leaving a bare
            // heading the reader will read as "the list failed to render".
            blocks.Add(Paragraph(Run("No individual changes were itemised.")));
        }
        else
        {
            foreach (var change in changes)
            {
                blocks.Add(Paragraph(Run("• "), Run(BuildChangeLine(change))));
            }
        }

        blocks.Add(Paragraph(Run(
            "This report was generated from the document's tracked changes and has not been verified. " +
            "Review it against the document before relying on it.",
            italic: true)));

        return blocks;
    }

    /// <summary>
    /// The "as of" line: which document, which version, when. Every unsupplied field is stated as unknown
    /// rather than dropped — a scope line missing its version reads as "current", which is the specific
    /// wrong impression this line exists to prevent.
    /// </summary>
    private static string BuildScopeLine(ComposeRevisionReportInput input)
    {
        var name = string.IsNullOrWhiteSpace(input.DocumentName) ? "this document" : input.DocumentName!.Trim();
        var version = string.IsNullOrWhiteSpace(input.DocumentVersion)
            ? "version not recorded"
            : $"version {input.DocumentVersion!.Trim()}";
        var asOf = input.AsOf is { } stamp
            ? stamp.UtcDateTime.ToString("d MMMM yyyy 'at' HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture)
            : "date not recorded";

        return $"Covers the tracked changes in {name} as of the last save — {version}, {asOf}.";
    }

    private static string BuildChangeLine(ComposeRevisionChangeInput change)
    {
        var kind = string.IsNullOrWhiteSpace(change.Kind) ? "change" : change.Kind.Trim();
        var location = (change.Location ?? string.Empty).Trim();
        var description = Truncate((change.Description ?? string.Empty).Trim(), MaxDescriptionChars);

        var locationPart = location.Length > 0 ? $" {location} —" : string.Empty;
        return $"[{kind}]{locationPart} {description}".TrimEnd();
    }

    private static string Truncate(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        return text[..maxChars].TrimEnd() + "…";
    }

    private static ComposeBlock Paragraph(params ComposeInlineRun[] runs) =>
        new() { Kind = ComposeBlockKind.Paragraph, Runs = runs };

    private static ComposeInlineRun Run(string text, bool bold = false, bool italic = false) =>
        new() { Text = text, Bold = bold, Italic = italic };
}

/// <summary>
/// The ledgered <c>compose-summarize-word-changes</c> result plus the document identity the report is
/// scoped to. The <see cref="Summary"/> / <see cref="Changes"/> pair is a mirror-first projection of the
/// Action's closed <c>outputSchema</c> — deserializing the SAME ledgered JSON directly into this type IS
/// the "derive from the ledgered result, no second LLM call" proof; no field is renamed or re-synthesized.
/// The three document fields are supplied by the caller from the save it is describing.
/// </summary>
public sealed record ComposeRevisionReportInput(
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("changes")] IReadOnlyList<ComposeRevisionChangeInput> Changes,
    [property: JsonPropertyName("documentName")] string? DocumentName = null,
    [property: JsonPropertyName("documentVersion")] string? DocumentVersion = null,
    [property: JsonPropertyName("asOf")] DateTimeOffset? AsOf = null);

/// <summary>
/// One itemised change from the ledgered result — mirrors the Action's per-change output schema exactly
/// (<c>kind</c> / <c>location</c> / <c>description</c>, where <c>kind</c> is one of
/// <c>insertion</c>/<c>deletion</c>/<c>comment</c>/<c>structural</c>).
/// </summary>
public sealed record ComposeRevisionChangeInput(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("description")] string Description);
