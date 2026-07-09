// =============================================================================
// SPIKE 2 THROWAWAY PROTOTYPE — IComposeEditValidator (match_mode + ambiguity)
// Project: spaarkeai-compose-r2 · Task 002 · Phase 0 Spikes
// NOT production code. The production service is FR-19 / task 020 under
// src/server/api/Sprk.Bff.Api/Services/Compose/. This file is the spike
// reference the note points to. It is DELIBERATELY dependency-free and
// deterministic — it can be run headlessly (no LLM, no BFF, no Azure).
//
// ADR-013 facade boundary: this validator is PURE deterministic text
// processing over a caller-supplied document string. It injects NOTHING —
// no IOpenAiClient, no executor, no IConsumerRoutingService. It never reaches
// the model. That is exactly the boundary the production Tier-1 NetArchTest
// enforces (task 025).
// =============================================================================

using System.Text;

// ---------------------------------------------------------------------------
// CONTRACT — the shapes task 020 (ComposeEditModels.cs) should mirror.
// The proposed-edit shape mirrors the Draft-Alternative catalog payload
// (HANDOFF §1: target_text / new_text / match_mode / rationale / sources) so
// /edit-batch/validate consumes the same contract the catalog action emits.
// ---------------------------------------------------------------------------

public enum MatchMode { Strict, First, All }

/// <summary>One LLM-proposed find-and-replace edit (mirror of the compose-draft-alternative payload).</summary>
public sealed record ProposedEdit(
    string TargetText,
    string NewText,
    MatchMode Mode,
    string? Rationale = null);

/// <summary>A resolved span in the document (offset + length), addressable for downstream apply.</summary>
public sealed record ResolvedMatch(int Offset, int Length);

/// <summary>One example occurrence with adeu's ~50-char pre/post context window.</summary>
public sealed record MatchExample(int Offset, string ContextBefore, string Matched, string ContextAfter);

public enum EditErrorKind { Ambiguous, NoMatch, EmptyTarget, Overlap }

/// <summary>Structured, actionable refusal — match count + up to 5 examples + copy-pasteable resolution.</summary>
public sealed record EditValidationError(
    EditErrorKind Kind,
    string Message,
    int MatchCount,
    IReadOnlyList<MatchExample> Examples,
    string ResolutionHint);

/// <summary>Per-edit verdict: resolved spans OR a structured error (never a silent wrong match).</summary>
public sealed record EditVerdict(
    int EditIndex,
    bool IsValid,
    IReadOnlyList<ResolvedMatch> Matches,
    EditValidationError? Error);

/// <summary>Batch outcome: all per-edit verdicts + any cross-edit overlap flags.</summary>
public sealed record BatchValidationResult(
    IReadOnlyList<EditVerdict> Verdicts,
    IReadOnlyList<EditValidationError> BatchErrors)
{
    public bool IsValid => Verdicts.All(v => v.IsValid) && BatchErrors.Count == 0;
}

public interface IComposeEditValidator
{
    BatchValidationResult Validate(string documentText, IReadOnlyList<ProposedEdit> edits);
}

// ---------------------------------------------------------------------------
// IMPLEMENTATION
// ---------------------------------------------------------------------------

public sealed class ComposeEditValidator : IComposeEditValidator
{
    private const int ContextChars = 50;   // adeu format_ambiguity_error window
    private const int MaxExamples  = 5;    // adeu shows first 5 occurrences

    public BatchValidationResult Validate(string documentText, IReadOnlyList<ProposedEdit> edits)
    {
        var verdicts = new List<EditVerdict>(edits.Count);

        for (int i = 0; i < edits.Count; i++)
            verdicts.Add(ValidateOne(documentText, edits[i], i));

        // Cross-edit overlap detection (batch-level). Only spans from VALID edits
        // can collide meaningfully; strict/first pick one span, all picks many.
        var batchErrors = DetectOverlaps(documentText, verdicts);

        return new BatchValidationResult(verdicts, batchErrors);
    }

    private EditVerdict ValidateOne(string doc, ProposedEdit edit, int index)
    {
        // EDGE-1: empty / whitespace-only target_text is never a match — refuse.
        if (string.IsNullOrWhiteSpace(edit.TargetText))
        {
            var err = new EditValidationError(
                EditErrorKind.EmptyTarget,
                $"Edit {index + 1}: target_text is empty or whitespace-only.",
                0, Array.Empty<MatchExample>(),
                "Provide a non-empty target_text naming the exact span to replace.");
            return new EditVerdict(index, false, Array.Empty<ResolvedMatch>(), err);
        }

        var occurrences = FindAll(doc, edit.TargetText);

        // EDGE-2: zero matches — refuse with a nearest-candidate hint if we can find one.
        if (occurrences.Count == 0)
        {
            string hint = BuildNoMatchHint(doc, edit.TargetText);
            var err = new EditValidationError(
                EditErrorKind.NoMatch,
                $"Edit {index + 1}: target_text was not found in the document.",
                0, Array.Empty<MatchExample>(), hint);
            return new EditVerdict(index, false, Array.Empty<ResolvedMatch>(), err);
        }

        switch (edit.Mode)
        {
            case MatchMode.Strict:
                // EDGE-3: strict requires EXACTLY one match; N>1 is the core ambiguity refusal.
                if (occurrences.Count == 1)
                    return Resolve(index, doc, occurrences, edit.TargetText.Length);

                var ambErr = BuildAmbiguityError(doc, edit.TargetText, occurrences, index);
                return new EditVerdict(index, false, Array.Empty<ResolvedMatch>(), ambErr);

            case MatchMode.First:
                // EDGE-4: first resolves the earliest occurrence deterministically (document order).
                return Resolve(index, doc, new[] { occurrences[0] }, edit.TargetText.Length);

            case MatchMode.All:
                // EDGE-5: all resolves every occurrence.
                return Resolve(index, doc, occurrences, edit.TargetText.Length);

            default:
                throw new InvalidOperationException($"Unknown match_mode: {edit.Mode}");
        }
    }

    private static EditVerdict Resolve(int index, string doc, IReadOnlyList<int> offsets, int len)
    {
        var matches = offsets.Select(o => new ResolvedMatch(o, len)).ToList();
        return new EditVerdict(index, true, matches, null);
    }

    // Ordinal, overlap-aware scan (advance by +1 so "aa" in "aaaa" reports 3, not 2).
    // Ordinal (not culture) so behavior is deterministic across hosts.
    private static List<int> FindAll(string doc, string needle)
    {
        var hits = new List<int>();
        int from = 0;
        while (from <= doc.Length - needle.Length)
        {
            int idx = doc.IndexOf(needle, from, StringComparison.Ordinal);
            if (idx < 0) break;
            hits.Add(idx);
            from = idx + 1;
        }
        return hits;
    }

    private static EditValidationError BuildAmbiguityError(
        string doc, string needle, IReadOnlyList<int> offsets, int index)
    {
        var examples = offsets.Take(MaxExamples).Select(o => ExtractExample(doc, o, needle.Length)).ToList();

        // adeu's copy-pasteable resolution — the "built-in escape hatch" the LLM otherwise loops forever missing.
        var hint = new StringBuilder();
        hint.AppendLine($"target_text \"{needle}\" matched {offsets.Count} times. Re-send this edit using ONE of:");
        hint.AppendLine("  1. Set \"match_mode\":\"all\"  — apply to every occurrence.");
        hint.AppendLine("  2. Set \"match_mode\":\"first\" — apply to the earliest occurrence only.");
        hint.AppendLine("  3. Extend target_text with surrounding context so it is unique (see the examples above).");

        return new EditValidationError(
            EditErrorKind.Ambiguous,
            $"Edit {index + 1}: target_text is ambiguous ({offsets.Count} matches) under match_mode:strict.",
            offsets.Count, examples, hint.ToString().TrimEnd());
    }

    private static MatchExample ExtractExample(string doc, int offset, int len)
    {
        int beforeStart = Math.Max(0, offset - ContextChars);
        int afterEnd = Math.Min(doc.Length, offset + len + ContextChars);
        string before = doc.Substring(beforeStart, offset - beforeStart).Replace("\n", " ");
        string matched = doc.Substring(offset, len);
        string after = doc.Substring(offset + len, afterEnd - (offset + len)).Replace("\n", " ");
        return new MatchExample(offset, before, matched, after);
    }

    // Minimal nearest-candidate probe (adeu _nearest_match_hint spirit): try
    // trimmed, case-insensitive, and whitespace-collapsed variants.
    private static string BuildNoMatchHint(string doc, string needle)
    {
        string trimmed = needle.Trim();
        if (trimmed.Length > 0 && trimmed != needle && doc.Contains(trimmed, StringComparison.Ordinal))
            return $"Did you mean \"{trimmed}\"? It appears in the document (your target_text had extra whitespace).";

        int ci = doc.IndexOf(needle.Trim(), StringComparison.OrdinalIgnoreCase);
        if (ci >= 0)
        {
            string actual = doc.Substring(ci, Math.Min(needle.Trim().Length, doc.Length - ci));
            return $"Did you mean \"{actual}\"? A case-insensitive match exists — match is case-SENSITIVE; copy the exact casing.";
        }

        // whitespace-collapsed probe (target spans a line break the LLM flattened)
        string collapsedNeedle = CollapseWs(needle);
        string collapsedDoc = CollapseWs(doc);
        if (collapsedNeedle.Length > 0 && collapsedDoc.Contains(collapsedNeedle, StringComparison.Ordinal))
            return "A whitespace-normalized match exists (target likely spans a line break). Copy the exact run of text including newlines, or narrow the target to a single line.";

        return "No near match found. Re-read the current document window and copy an exact span verbatim.";
    }

    private static string CollapseWs(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool prevWs = false;
        foreach (char c in s)
        {
            bool ws = char.IsWhiteSpace(c);
            if (ws) { if (!prevWs) sb.Append(' '); }
            else sb.Append(c);
            prevWs = ws;
        }
        return sb.ToString();
    }

    // EDGE-6: two edits in one batch whose resolved spans overlap. Flag it —
    // do NOT return conflicting silent resolutions. Production FR-20 batch-apply
    // pre-resolves + sorts descending; this validator just surfaces the collision.
    private static List<EditValidationError> DetectOverlaps(string doc, IReadOnlyList<EditVerdict> verdicts)
    {
        var errors = new List<EditValidationError>();
        var spans = verdicts
            .Where(v => v.IsValid)
            .SelectMany(v => v.Matches.Select(m => (v.EditIndex, m.Offset, End: m.Offset + m.Length)))
            .OrderBy(s => s.Offset)
            .ToList();

        for (int i = 1; i < spans.Count; i++)
        {
            var a = spans[i - 1];
            var b = spans[i];
            if (b.EditIndex != a.EditIndex && b.Offset < a.End)
            {
                var ex = new List<MatchExample>
                {
                    ExtractExample(doc, a.Offset, a.End - a.Offset),
                    ExtractExample(doc, b.Offset, b.End - b.Offset),
                };
                errors.Add(new EditValidationError(
                    EditErrorKind.Overlap,
                    $"Edits {a.EditIndex + 1} and {b.EditIndex + 1} resolve to overlapping spans " +
                    $"([{a.Offset},{a.End}) vs [{b.Offset},{b.End})).",
                    2, ex,
                    "Overlapping edits cannot both apply from the same original state. Merge them into one edit, " +
                    "or narrow one target_text so the spans are disjoint."));
            }
        }
        return errors;
    }
}

// ===========================================================================
// SPIKE HARNESS — runs the 5 representative sample edits and prints verdicts.
// This is the "run 5 sample edits" step; the output is captured in the note.
// ===========================================================================

public static class Spike
{
    public static void Main()
    {
        // A representative legal fragment with intentional repetition.
        const string doc =
            "This Master Services Agreement (the \"Agreement\") is entered into by the parties.\n" +
            "The Agreement governs all Statements of Work. Each Party shall perform under the Agreement.\n" +
            "Termination of the Agreement requires thirty (30) days notice. The Effective Date is the date\n" +
            "on which the Agreement is signed by the last Party to sign. Confidential Information means any\n" +
            "information disclosed by a Party under the Agreement.";

        var validator = new ComposeEditValidator();

        var samples = new (string Label, ProposedEdit Edit)[]
        {
            // 1. UNAMBIGUOUS strict — unique phrase, one match -> resolve.
            ("S1 unambiguous/strict",
                new ProposedEdit("thirty (30) days notice", "sixty (60) days' written notice", MatchMode.Strict,
                    "Lengthen the cure period.")),

            // 2. AMBIGUOUS strict — "the Agreement" appears many times -> refuse with ambiguity error.
            ("S2 ambiguous/strict",
                new ProposedEdit("the Agreement", "this Agreement", MatchMode.Strict,
                    "Style: prefer 'this Agreement'.")),

            // 3. NO-MATCH strict — typo/casing -> refuse with nearest-candidate hint.
            ("S3 no-match/strict",
                new ProposedEdit("the agreemnt", "the Agreement", MatchMode.Strict,
                    "Fix spelling.")),

            // 4. FIRST on a multi-match target -> resolve earliest deterministically.
            ("S4 multi-match/first",
                new ProposedEdit("Party", "party", MatchMode.First,
                    "Lowercase the first occurrence only.")),

            // 5. ALL on a multi-match target -> resolve every occurrence (4 here).
            ("S5 multi-match/all",
                new ProposedEdit("the Agreement", "this Agreement", MatchMode.All,
                    "Apply the style change to every occurrence.")),
        };

        Console.WriteLine("=== DOCUMENT (len=" + doc.Length + ") ===");
        Console.WriteLine(doc);
        Console.WriteLine();

        // Run each sample as its own single-edit batch (isolates per-edit verdicts).
        foreach (var (label, edit) in samples)
        {
            var result = validator.Validate(doc, new[] { edit });
            PrintVerdict(label, edit, result.Verdicts[0]);
        }

        // 6 (bonus negative). EMPTY target -> EmptyTarget refusal.
        var emptyRes = validator.Validate(doc, new[] { new ProposedEdit("   ", "x", MatchMode.Strict) });
        PrintVerdict("S6 empty-target (bonus)", new ProposedEdit("   ", "x", MatchMode.Strict), emptyRes.Verdicts[0]);

        // 7 (bonus batch). OVERLAP across two edits in one batch -> Overlap batch error.
        var overlapEdits = new[]
        {
            new ProposedEdit("Confidential Information means any\ninformation", "CI means all information", MatchMode.Strict),
            new ProposedEdit("means any\ninformation disclosed", "covers disclosures", MatchMode.Strict),
        };
        var overlapRes = validator.Validate(doc, overlapEdits);
        Console.WriteLine("--- S7 overlapping-batch (bonus) ---");
        Console.WriteLine($"  batch IsValid = {overlapRes.IsValid}");
        foreach (var be in overlapRes.BatchErrors)
        {
            Console.WriteLine($"  BATCH ERROR [{be.Kind}]: {be.Message}");
            Console.WriteLine($"    hint: {be.ResolutionHint}");
        }
        Console.WriteLine();
    }

    private static void PrintVerdict(string label, ProposedEdit edit, EditVerdict v)
    {
        Console.WriteLine($"--- {label} ---");
        Console.WriteLine($"  target_text = \"{Show(edit.TargetText)}\"  match_mode = {edit.Mode}");
        Console.WriteLine($"  VERDICT: {(v.IsValid ? "RESOLVE" : "REFUSE")}");
        if (v.IsValid)
        {
            Console.WriteLine($"  matches ({v.Matches.Count}): " +
                string.Join(", ", v.Matches.Select(m => $"[{m.Offset},{m.Offset + m.Length})")));
        }
        else
        {
            var e = v.Error!;
            Console.WriteLine($"  error kind : {e.Kind}");
            Console.WriteLine($"  message    : {e.Message}");
            Console.WriteLine($"  matchCount : {e.MatchCount}");
            if (e.Examples.Count > 0)
            {
                Console.WriteLine($"  examples ({e.Examples.Count}):");
                foreach (var ex in e.Examples)
                    Console.WriteLine($"    @{ex.Offset}: ...{Show(ex.ContextBefore)}[{Show(ex.Matched)}]{Show(ex.ContextAfter)}...");
            }
            Console.WriteLine("  resolution hint:");
            foreach (var line in e.ResolutionHint.Split('\n'))
                Console.WriteLine($"    {line.TrimEnd()}");
        }
        Console.WriteLine();
    }

    private static string Show(string s) => s.Replace("\n", "\\n");
}
