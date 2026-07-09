using System.Text;
using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-22 (task 023): produces the "Semantic Appendix" — a read-only reference block
/// (defined terms, cross-reference targets, structural metadata) appended below the body
/// text of the <c>compose-document</c> scope payload so the LLM scans document semantics
/// before editing capitalized phrases or citing sections (design §6.1; hallucination
/// reduction measured protocol in <c>projects/spaarkeai-compose-r2/notes/spikes/spike-4-semantic-appendix.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-013</b>: this class is a PURE TEXT TRANSFORMER — <see cref="Build"/> takes a
/// <see cref="SemanticAppendixInput"/> and returns a <see cref="string"/>. It injects no
/// <c>IOpenAiClient</c>, executor, or routing type, calls no model, and adds no endpoint.
/// The output is payload TEXT consumed by the dispatch seam (Spike 4 §3.4).
/// </para>
/// <para>
/// <b>Distinct from <c>compose-defined-terms</c></b> (an LLM consistency-checker Action):
/// this generator is a deterministic typography pre-scan that PRIMES that Action (and every
/// other <c>compose-document</c> consumer) — it does not replace it (Spike 4 §6.2).
/// </para>
/// <para>
/// <b>Two tiers</b> (Spike 4 §3.1): Tier A (this class, always available) — Defined Terms +
/// Structure from flat extracted text. Tier B (Phase 2, optional input) — Cross-Reference
/// Targets, which need OOXML bookmark/REF-field walking (<c>DocxAnnotationReader</c>,
/// design §6.2) that does not exist in <see cref="Ai.LinearConsumers.DocumentText.ExtractedText"/>.
/// Callers pass Tier B data via <see cref="SemanticAppendixInput.CrossRefs"/> when available;
/// this generator does NOT extract cross-refs itself — it only validates the supplied
/// targets against the Tier A structural outline (flagging targets that don't resolve).
/// </para>
/// <para>
/// <b>No renderer change needed</b> (Spike 4 §3.3/§6.4): the returned appendix string is
/// concatenated below the body Markdown by the (future) scope-payload assembler and rides
/// the existing <c>## Document</c> section of <c>PromptSchemaRenderer</c> unchanged.
/// </para>
/// </remarks>
public sealed class SemanticAppendixGenerator
{
    private const string BoundaryStart = "<!-- READONLY_BOUNDARY_START -->";
    private const string BoundaryEnd = "<!-- READONLY_BOUNDARY_END -->";

    // Leading quoted term at (or near) a paragraph/clause start, optionally preceded by a
    // dotted section number (e.g. `1.1 "Agreement" means ...`). Faithful to adeu's
    // domain.ts typography pattern (Spike 4 §3.2), narrowed to a numeric-dotted leading
    // group (rather than the looser `[\d.\-()a-zA-Z]+`) so the captured number can double
    // as the term's definition location.
    private static readonly Regex LeadingDefinitionPattern = new(
        @"^\s*(?:(?:§\s*)?(?<num>\d+(?:\.\d+)*)\.?\s+)?[""“](?<term>[A-Z][A-Za-z0-9\s\-&'’]{1,60})[""”]",
        RegexOptions.Compiled);

    // Inline parenthetical definition: `(the "Agreement")`, `(as amended, the "Amended Agreement")`.
    private static readonly Regex InlineDefinitionPattern = new(
        @"\([^)]*?[""“](?<term>[A-Z][A-Za-z0-9\s\-&'’]{1,60})[""”][^)]*?\)",
        RegexOptions.Compiled);

    // Structural heading: either a Markdown ATX heading, or a numbered section heading whose
    // title does not end in sentence punctuation (EDGE-1 below explains why the trailing-
    // punctuation check exists).
    private static readonly Regex AtxHeadingPattern = new(
        @"^#{1,6}\s+(?<title>.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex NumberedHeadingPattern = new(
        @"^\s*(?:§\s*)?(?<num>\d+(?:\.\d+)*)\.?\s+(?<title>[A-Z][^.]{1,100})\s*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Builds the semantic appendix for <paramref name="input"/>. Returns
    /// <see cref="string.Empty"/> when the body is empty/whitespace or when no defined
    /// terms, cross-references, or structure are found (EDGE-4: empty document).
    /// </summary>
    public string Build(SemanticAppendixInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.BodyText))
        {
            return string.Empty;
        }

        var lines = input.BodyText.Replace("\r\n", "\n").Split('\n');

        var structure = ExtractStructure(lines);
        var definedTerms = ExtractDefinedTerms(lines, input.BodyText, structure);
        var crossRefs = ValidateCrossReferences(input.CrossRefs, structure);

        if (definedTerms.Count == 0 && crossRefs.Count == 0 && structure.Count == 0)
        {
            // EDGE-4: empty document (or a document with no extractable semantics at all)
            // yields an empty appendix — nothing worth priming the LLM with.
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine(BoundaryStart);
        sb.AppendLine("## Document Semantics (read-only reference — do NOT edit or cite as body text)");
        sb.AppendLine();

        if (definedTerms.Count > 0)
        {
            sb.AppendLine("### Defined Terms");
            foreach (var line in definedTerms)
            {
                sb.AppendLine(line);
            }
            sb.AppendLine();
        }

        if (crossRefs.Count > 0)
        {
            sb.AppendLine("### Cross-Reference Targets");
            foreach (var line in crossRefs)
            {
                sb.AppendLine(line);
            }
            sb.AppendLine();
        }

        if (structure.Count > 0)
        {
            sb.AppendLine("### Structure");
            foreach (var heading in structure)
            {
                sb.AppendLine($"- {heading.Label}");
            }
            sb.AppendLine();
        }

        sb.Append(BoundaryEnd);
        return sb.ToString();
    }

    /// <summary>
    /// First pass: detect structural headings (Markdown ATX or numbered-section) and record,
    /// per line index, the nearest preceding heading — used both for the Structure section
    /// and to attribute a definition location when the defining line itself carries no
    /// leading section number.
    /// </summary>
    private static List<HeadingEntry> ExtractStructure(string[] lines)
    {
        var headings = new List<HeadingEntry>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var atx = AtxHeadingPattern.Match(line);
            if (atx.Success)
            {
                headings.Add(new HeadingEntry(i, Num: null, Title: atx.Groups["title"].Value.Trim()));
                continue;
            }

            var numbered = NumberedHeadingPattern.Match(line);
            if (numbered.Success)
            {
                headings.Add(new HeadingEntry(
                    i,
                    Num: numbered.Groups["num"].Value,
                    Title: numbered.Groups["title"].Value.Trim()));
            }
        }

        return headings;
    }

    private static List<string> ExtractDefinedTerms(string[] lines, string bodyText, List<HeadingEntry> structure)
    {
        // term -> list of (location, lineIndex) — location is the "defined at" label.
        var definitions = new List<(string Term, string Location, int LineIndex)>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // EDGE-1: nested defined terms — a line can carry more than one definition
            // (a leading definition AND an inline parenthetical definition in the same
            // sentence, e.g. `"Agreement" (as amended, the "Amended Agreement") shall...`).
            // Use Regex.Matches (not just the first match) on BOTH patterns per line.
            var leading = LeadingDefinitionPattern.Match(line);
            if (leading.Success)
            {
                var term = leading.Groups["term"].Value.Trim();
                var location = leading.Groups["num"].Success
                    ? $"§{leading.Groups["num"].Value}"
                    : NearestPrecedingHeadingLabel(structure, i);
                definitions.Add((term, location, i));
            }

            foreach (Match inline in InlineDefinitionPattern.Matches(line))
            {
                var term = inline.Groups["term"].Value.Trim();
                var location = NearestPrecedingHeadingLabel(structure, i);
                definitions.Add((term, location, i));
            }
        }

        // Group by term (ordinal), preserving first-seen order.
        var order = new List<string>();
        var byTerm = new Dictionary<string, List<(string Location, int LineIndex)>>(StringComparer.Ordinal);
        foreach (var (term, location, lineIndex) in definitions)
        {
            if (!byTerm.TryGetValue(term, out var list))
            {
                list = new List<(string, int)>();
                byTerm[term] = list;
                order.Add(term);
            }
            list.Add((location, lineIndex));
        }

        var output = new List<string>();
        foreach (var term in order)
        {
            var defs = byTerm[term];

            // EDGE-2: duplicate term definitions — flag as an error line instead of a
            // normal bullet; do not silently pick one definition over the other.
            if (defs.Count > 1)
            {
                var locations = string.Join(" and ", defs.Select(d => d.Location));
                output.Add($"[Error] Duplicate Definition: \"{term}\" defined at {locations}");
                continue;
            }

            var (location, _) = defs[0];
            var definitionOccurrences = defs.Count;
            var totalOccurrences = CountWholeWordOccurrences(bodyText, term);
            var usageCount = totalOccurrences - definitionOccurrences;

            // Zero-use terms (defined but never referenced again) are dropped — they add
            // appendix noise without grounding value.
            if (usageCount <= 0)
            {
                continue;
            }

            output.Add($"- \"{term}\" — defined at {location} — used {usageCount}×");
        }

        return output;
    }

    private static string NearestPrecedingHeadingLabel(List<HeadingEntry> structure, int lineIndex)
    {
        HeadingEntry? nearest = null;
        foreach (var heading in structure)
        {
            if (heading.LineIndex <= lineIndex)
            {
                nearest = heading;
            }
            else
            {
                break;
            }
        }

        return nearest is null ? "n/a" : nearest.Value.Label;
    }

    private static int CountWholeWordOccurrences(string bodyText, string term)
    {
        var pattern = $@"(?<![A-Za-z0-9]){Regex.Escape(term)}(?![A-Za-z0-9])";
        return Regex.Matches(bodyText, pattern).Count;
    }

    /// <summary>
    /// Tier B cross-reference targets are supplied by the caller (this generator does not
    /// walk OOXML itself — Spike 4 §6.3 / task-brief deferral). Each supplied target is
    /// validated against the Tier A structural outline: if its anchor text does not
    /// resolve to any detected heading, the target is flagged rather than silently emitted
    /// (EDGE-3 / acceptance-criterion NEGATIVE case).
    /// </summary>
    private static List<string> ValidateCrossReferences(
        IReadOnlyList<CrossReferenceTarget>? crossRefs,
        List<HeadingEntry> structure)
    {
        var output = new List<string>();
        if (crossRefs is null || crossRefs.Count == 0)
        {
            // Entire section omitted (not emitted empty) when Tier B input is unavailable.
            return output;
        }

        foreach (var crossRef in crossRefs)
        {
            var resolved = structure.Any(h =>
                crossRef.AnchorText.Contains(h.Label, StringComparison.OrdinalIgnoreCase)
                || (h.Num is not null && crossRef.AnchorText.Contains(h.Num, StringComparison.OrdinalIgnoreCase))
                || h.Title.Contains(crossRef.AnchorText, StringComparison.OrdinalIgnoreCase));

            if (!resolved)
            {
                output.Add(
                    $"[Error] Missing Cross-Reference Target: {crossRef.RefId} anchored to \"{crossRef.AnchorText}\" (section not found in document)");
                continue;
            }

            var referencedFrom = string.Join(", ", crossRef.ReferencedFrom);
            output.Add($"- {crossRef.RefId} — anchored to \"{crossRef.AnchorText}\" — referenced from {referencedFrom}");
        }

        return output;
    }

    private readonly record struct HeadingEntry(int LineIndex, string? Num, string Title)
    {
        public string Label => Num is null ? Title : $"§{Num} {Title}";
    }
}

/// <summary>
/// Input to <see cref="SemanticAppendixGenerator.Build"/>. <see cref="BodyText"/> is the
/// Markdown projection (e.g. <see cref="Ai.LinearConsumers.DocumentText.ExtractedText"/>,
/// already CriticMarkup-rendered by <see cref="CriticMarkupRenderer"/> where applicable).
/// <see cref="CrossRefs"/> is Tier B (Phase 2, <c>DocxAnnotationReader</c>-sourced) and is
/// <c>null</c> for Tier A (flat-text-only) callers.
/// </summary>
public sealed record SemanticAppendixInput(string BodyText, IReadOnlyList<CrossReferenceTarget>? CrossRefs = null);

/// <summary>
/// A single Tier B cross-reference target (OOXML bookmark / REF field), supplied by a
/// future <c>DocxAnnotationReader</c>-based caller. See
/// <see cref="SemanticAppendixGenerator"/> remarks — this generator validates, but does
/// not extract, cross-reference targets.
/// </summary>
public sealed record CrossReferenceTarget(string RefId, string AnchorText, IReadOnlyList<string> ReferencedFrom);
