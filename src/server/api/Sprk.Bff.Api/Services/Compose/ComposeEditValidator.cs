using System.Text;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Deterministic implementation of <see cref="IComposeEditValidator"/> — see the interface's
/// XML doc for the ADR-013 facade boundary and the document-projection contract. Ported from
/// the validated Spike 2 prototype
/// (<c>projects/spaarkeai-compose-r2/notes/spikes/edit-validator-prototype.cs</c>); the
/// <c>// EDGE-N:</c> comments below mirror the spike note's numbered edge cases 1:1
/// (notes/spikes/spike-2-edit-validator.md §2, §4).
/// </summary>
/// <remarks>
/// Stateless and dependency-free by design (ADR-010 — no constructor, nothing to inject).
/// Registered as a single service in <c>ComposeModule</c>; the private helpers below
/// (<see cref="FindAll"/>, <see cref="ExtractExample"/>, <see cref="BuildNoMatchHint"/>,
/// <see cref="DetectOverlaps"/>, etc.) are intentionally NOT separately DI-registered, per
/// ADR-010.
/// </remarks>
public sealed class ComposeEditValidator : IComposeEditValidator
{
    private const int ContextChars = 50; // adeu format_ambiguity_error window (spike-2 §3)
    private const int MaxExamples = 5;    // adeu shows first 5 occurrences (spike-2 §3)

    /// <inheritdoc />
    public BatchValidationResult Validate(string documentText, IReadOnlyList<ProposedEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(documentText);
        ArgumentNullException.ThrowIfNull(edits);

        var verdicts = new List<EditVerdict>(edits.Count);
        for (int i = 0; i < edits.Count; i++)
        {
            verdicts.Add(ValidateOne(documentText, edits[i], i));
        }

        // Cross-edit overlap detection (batch-level). Only spans from VALID edits can
        // collide meaningfully; strict/first each resolve one span, all resolves many.
        var batchErrors = DetectOverlaps(documentText, verdicts);

        return new BatchValidationResult(verdicts, batchErrors);
    }

    private static EditVerdict ValidateOne(string doc, ProposedEdit edit, int index)
    {
        // EDGE-1: empty / whitespace-only target_text is never a match — refuse.
        if (string.IsNullOrWhiteSpace(edit.TargetText))
        {
            var emptyError = new EditValidationError(
                EditErrorKind.EmptyTarget,
                $"Edit {index + 1}: target_text is empty or whitespace-only.",
                MatchCount: 0,
                Examples: Array.Empty<MatchExample>(),
                ResolutionHint: "Provide a non-empty target_text naming the exact span to replace.");
            return new EditVerdict(index, IsValid: false, Matches: Array.Empty<ResolvedMatch>(), Error: emptyError);
        }

        var occurrences = FindAll(doc, edit.TargetText);

        // EDGE-2: zero matches — refuse with a nearest-candidate hint if one exists.
        // NOTE (spike-2 §5.1 — deliberate scope boundary): the hint is CONSERVATIVE — it only
        // probes exact-variant matches (whitespace-trimmed, case-insensitive, whitespace-
        // collapsed/newline-flattened). Edit-distance / fuzzy-regex "did you mean" suggestions
        // for genuine misspellings are Phase-2 DEFERRED (design.md §6.2 lists this as a Phase 2
        // adoptable, not Phase 1) — do NOT add fuzzy/typo matching here.
        if (occurrences.Count == 0)
        {
            var hint = BuildNoMatchHint(doc, edit.TargetText);
            var noMatchError = new EditValidationError(
                EditErrorKind.NoMatch,
                $"Edit {index + 1}: target_text was not found in the document.",
                MatchCount: 0,
                Examples: Array.Empty<MatchExample>(),
                ResolutionHint: hint);
            return new EditVerdict(index, IsValid: false, Matches: Array.Empty<ResolvedMatch>(), Error: noMatchError);
        }

        switch (edit.Mode)
        {
            case MatchMode.Strict:
                // EDGE-3: strict requires EXACTLY one match; N>1 is the core ambiguity refusal
                // — the entire point of FR-19 is that a repeated target_text never applies
                // silently to the wrong occurrence.
                if (occurrences.Count == 1)
                {
                    return Resolve(index, occurrences, edit.TargetText.Length);
                }

                var ambiguityError = BuildAmbiguityError(doc, edit.TargetText, occurrences, index);
                return new EditVerdict(index, IsValid: false, Matches: Array.Empty<ResolvedMatch>(), Error: ambiguityError);

            case MatchMode.First:
                // EDGE-4: first resolves the earliest occurrence deterministically (document order).
                return Resolve(index, new[] { occurrences[0] }, edit.TargetText.Length);

            case MatchMode.All:
                // EDGE-5: all resolves every occurrence.
                return Resolve(index, occurrences, edit.TargetText.Length);

            default:
                throw new InvalidOperationException($"Unknown match_mode: {edit.Mode}");
        }
    }

    private static EditVerdict Resolve(int index, IReadOnlyList<int> offsets, int length)
    {
        var matches = offsets.Select(o => new ResolvedMatch(o, length)).ToList();
        return new EditVerdict(index, IsValid: true, Matches: matches, Error: null);
    }

    /// <summary>
    /// Ordinal, overlap-aware occurrence scan — advances by <c>+1</c> (not by
    /// <c>needle.Length</c>) so a target like <c>"aa"</c> in <c>"aaaa"</c> reports the true
    /// count of 3, not 2. Matching is Ordinal (not culture-aware) so behavior is deterministic
    /// across hosts and legal defined-terms ("Party" vs "party") are never silently conflated
    /// (spike-2 §2).
    /// </summary>
    private static List<int> FindAll(string doc, string needle)
    {
        var hits = new List<int>();
        int from = 0;
        while (from <= doc.Length - needle.Length)
        {
            int idx = doc.IndexOf(needle, from, StringComparison.Ordinal);
            if (idx < 0)
            {
                break;
            }

            hits.Add(idx);
            from = idx + 1;
        }

        return hits;
    }

    private static EditValidationError BuildAmbiguityError(
        string doc, string needle, IReadOnlyList<int> offsets, int index)
    {
        var examples = offsets.Take(MaxExamples).Select(o => ExtractExample(doc, o, needle.Length)).ToList();

        // The copy-pasteable resolution hint — adeu's "built-in escape hatch" that stops the
        // LLM's refine-forever loop (spike-2 §3). This is the load-bearing UX; keep the three
        // options in this exact order (all / first / extend-context).
        var hint = new StringBuilder();
        hint.AppendLine($"target_text \"{needle}\" matched {offsets.Count} times. Re-send this edit using ONE of:");
        hint.AppendLine("  1. Set \"match_mode\":\"all\"  — apply to every occurrence.");
        hint.AppendLine("  2. Set \"match_mode\":\"first\" — apply to the earliest occurrence only.");
        hint.AppendLine("  3. Extend target_text with surrounding context so it is unique (see the examples above).");

        return new EditValidationError(
            EditErrorKind.Ambiguous,
            $"Edit {index + 1}: target_text is ambiguous ({offsets.Count} matches) under match_mode:strict.",
            MatchCount: offsets.Count,
            Examples: examples,
            ResolutionHint: hint.ToString().TrimEnd());
    }

    private static MatchExample ExtractExample(string doc, int offset, int length)
    {
        int beforeStart = Math.Max(0, offset - ContextChars);
        int afterEnd = Math.Min(doc.Length, offset + length + ContextChars);

        string before = doc.Substring(beforeStart, offset - beforeStart).Replace('\n', ' ');
        string matched = doc.Substring(offset, length);
        string after = doc.Substring(offset + length, afterEnd - (offset + length)).Replace('\n', ' ');

        return new MatchExample(offset, before, matched, after);
    }

    /// <summary>
    /// Nearest-candidate probe in the spirit of adeu's <c>_nearest_match_hint</c> — but
    /// DELIBERATELY CONSERVATIVE (spike-2 §5.1). It only tries exact-variant probes: trimmed
    /// whitespace, case-insensitive, and whitespace-collapsed/newline-flattened. Edit-distance
    /// / fuzzy-regex "did you mean" for a genuine misspelling is explicitly OUT OF SCOPE for the
    /// FR-19 MVP (design.md §6.2 lists it as a Phase 2 adoptable). Do not extend this with fuzzy
    /// matching without a design revisit.
    /// </summary>
    private static string BuildNoMatchHint(string doc, string needle)
    {
        string trimmed = needle.Trim();
        if (trimmed.Length > 0 && trimmed != needle && doc.Contains(trimmed, StringComparison.Ordinal))
        {
            return $"Did you mean \"{trimmed}\"? It appears in the document (your target_text had extra whitespace).";
        }

        int caseInsensitiveIndex = doc.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase);
        if (caseInsensitiveIndex >= 0)
        {
            string actual = doc.Substring(caseInsensitiveIndex, Math.Min(trimmed.Length, doc.Length - caseInsensitiveIndex));
            return $"Did you mean \"{actual}\"? A case-insensitive match exists — match is case-SENSITIVE; copy the exact casing.";
        }

        // Whitespace-collapsed probe (target spans a line break the LLM flattened).
        string collapsedNeedle = CollapseWhitespace(needle);
        string collapsedDoc = CollapseWhitespace(doc);
        if (collapsedNeedle.Length > 0 && collapsedDoc.Contains(collapsedNeedle, StringComparison.Ordinal))
        {
            return "A whitespace-normalized match exists (target likely spans a line break). Copy the exact run of text including newlines, or narrow the target to a single line.";
        }

        return "No near match found. Re-read the current document window and copy an exact span verbatim.";
    }

    private static string CollapseWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool previousWasWhitespace = false;
        foreach (char c in s)
        {
            bool isWhitespace = char.IsWhiteSpace(c);
            if (isWhitespace)
            {
                if (!previousWasWhitespace)
                {
                    sb.Append(' ');
                }
            }
            else
            {
                sb.Append(c);
            }

            previousWasWhitespace = isWhitespace;
        }

        return sb.ToString();
    }

    /// <summary>
    /// EDGE-6: two edits in one batch whose resolved spans overlap. Flag it as a batch-level
    /// error — do NOT return conflicting silent resolutions. FR-20's <c>ComposeEditBatch</c>
    /// pipeline (resolve → sort descending → skip overlap → apply bottom-up) owns what happens
    /// next; this validator's only job is surfacing the collision (spike-2 §5.2; design.md §6.1).
    /// </summary>
    private static List<EditValidationError> DetectOverlaps(string doc, IReadOnlyList<EditVerdict> verdicts)
    {
        var errors = new List<EditValidationError>();

        var spans = verdicts
            .Where(v => v.IsValid)
            .SelectMany(v => v.Matches.Select(m => (EditIndex: v.EditIndex, m.Offset, End: m.Offset + m.Length)))
            .OrderBy(s => s.Offset)
            .ToList();

        for (int i = 1; i < spans.Count; i++)
        {
            var previous = spans[i - 1];
            var current = spans[i];

            if (current.EditIndex != previous.EditIndex && current.Offset < previous.End)
            {
                var examples = new List<MatchExample>
                {
                    ExtractExample(doc, previous.Offset, previous.End - previous.Offset),
                    ExtractExample(doc, current.Offset, current.End - current.Offset),
                };

                errors.Add(new EditValidationError(
                    EditErrorKind.Overlap,
                    $"Edits {previous.EditIndex + 1} and {current.EditIndex + 1} resolve to overlapping spans " +
                    $"([{previous.Offset},{previous.End}) vs [{current.Offset},{current.End})).",
                    MatchCount: 2,
                    Examples: examples,
                    ResolutionHint: "Overlapping edits cannot both apply from the same original state. Merge them into one edit, " +
                        "or narrow one target_text so the spans are disjoint."));
            }
        }

        return errors;
    }
}
