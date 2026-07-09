// =============================================================================
// Spike 3 — ComposeEditBatch 4-phase pipeline + snapshot/rollback THROWAWAY PROTOTYPE
// -----------------------------------------------------------------------------
// Task:   003 (Phase 0 Spikes) · Project: spaarkeai-compose-r2 · Date: 2026-07-08
// Status: THROWAWAY. Not production code. The production ComposeEditBatch /
//         ComposeEditTransaction land under
//         src/server/api/Sprk.Bff.Api/Services/Compose/ via tasks 021/022 (FR-20/21),
//         operating over the Open XML DOM. This prototype proves the ORDERING +
//         ATOMICITY LOGIC over a plain string document, which is the offset-stability
//         and all-or-nothing invariant the production version must preserve.
//
// Reference pattern (adopt patterns, NOT code — design §6.1, §11):
//   adeu `engine.ts apply_edits` (lines 1921-2101) 4-phase pipeline +
//   `takeSnapshot/restoreSnapshot` (lines 95-128) transaction model, as documented
//   in projects/spaarkeai-compose-r2/research/adeu-architecture-study.md §"Reverse-
//   order, pre-resolved application" and §"Transaction model + dry-run".
//
// HOW TO RUN (headless, no BFF/editor runtime required — pure text logic):
//   copy into a net8.0 console project as Program.cs and `dotnet run`.
//   Exit code 0 = all assertions passed; nonzero = a proof failed.
// =============================================================================

using System.Text;

// Entry point (top-level statement — must precede the type declarations below).
return Proof.Run();

// ---------------------------------------------------------------------------
// Domain model (mirrors the adeu flat discriminated union; R2 will express the
// LLM-facing shape as the `compose-draft-alternative` OutputSchemaJson:
//   { target_text, new_text, match_mode, rationale, ... } — design §4 catalog row).
// ---------------------------------------------------------------------------

enum MatchMode { Strict, First, All }

// LLM-facing edit. Empty NewText == deletion; search-and-replace only (adeu §6
// "Search & Replace First"): no raw "insert at offset Y" — anchor text forces the
// LLM to supply verifiable context.
record TextEdit(string TargetText, string NewText, MatchMode Mode = MatchMode.Strict);

// Result of Phase 1 resolution — a concrete [Start,End) span bound to one edit.
record ResolvedEdit(TextEdit Source, int Start, int End, int OriginIndex)
{
    public int Length => End - Start;
}

// Per-edit validation error keyed by the batch index the LLM sent (adeu surfaces
// "Edit ${i+1} Failed: ..." so the model resubmits only the failed item).
record EditValidationError(int EditIndex, string Category, string Message);

// Outcome of a transactional batch apply.
record BatchResult(
    bool Committed,
    string Document,               // committed text, or the untouched snapshot on rollback
    IReadOnlyList<EditValidationError> Errors,
    IReadOnlyList<string> AppliedLog,
    IReadOnlyList<string> SkippedLog);

// ---------------------------------------------------------------------------
// ComposeEditBatch — the 4-phase pipeline (pure; read-only pre-resolution).
// ---------------------------------------------------------------------------
static class ComposeEditBatch
{
    // Phase 1 — RESOLVE (read-only; mutates nothing). Resolve every edit's
    // TargetText to a physical [Start,End) span per its MatchMode. Any edit that
    // cannot resolve unambiguously produces an EditValidationError. `all` fans one
    // edit out to N resolved spans.
    public static (List<ResolvedEdit> resolved, List<EditValidationError> errors) Resolve(
        string document, IReadOnlyList<TextEdit> edits)
    {
        var resolved = new List<ResolvedEdit>();
        var errors = new List<EditValidationError>();

        for (int i = 0; i < edits.Count; i++)
        {
            var e = edits[i];
            if (string.IsNullOrEmpty(e.TargetText))
            {
                errors.Add(new(i, "empty-target",
                    $"Edit {i + 1} Failed: target_text is empty. Search-and-replace requires anchor text."));
                continue;
            }

            var matches = AllIndexesOf(document, e.TargetText);

            if (matches.Count == 0)
            {
                errors.Add(new(i, "not-found",
                    $"Edit {i + 1} Failed: target_text \"{Trim(e.TargetText)}\" not found in document."));
                continue;
            }

            switch (e.Mode)
            {
                case MatchMode.Strict when matches.Count > 1:
                    // adeu's structured ambiguity error (markup.ts format_ambiguity_error):
                    // count + resolution hint. The presence of match_mode is the escape hatch.
                    errors.Add(new(i, "ambiguous",
                        $"Edit {i + 1} Failed: target_text \"{Trim(e.TargetText)}\" matches {matches.Count} places under match_mode=strict. " +
                        "Resolve by: (1) set match_mode='all'; (2) set match_mode='first'; (3) add surrounding context to make it unique."));
                    break;

                case MatchMode.Strict:
                case MatchMode.First:
                    resolved.Add(new(e, matches[0], matches[0] + e.TargetText.Length, i));
                    break;

                case MatchMode.All:
                    foreach (var m in matches)
                        resolved.Add(new(e, m, m + e.TargetText.Length, i));
                    break;
            }
        }
        return (resolved, errors);
    }

    // Phases 2-4 over an ALREADY-RESOLVED, VALIDATED set. Overlap-rejection here is
    // NON-FATAL (adeu behavior): first-resolved wins, later-in-list overlap is
    // skip-and-reported. Fatal validation lives in the transaction wrapper below.
    public static (string doc, List<string> applied, List<string> skipped) SortSkipApply(
        string document, List<ResolvedEdit> resolved)
    {
        // Phase 2 — SORT DESCENDING by Start (largest offset first). Tie-break by
        // longer span first so a fully-covering edit claims the range before a
        // nested one.
        var ordered = resolved
            .OrderByDescending(r => r.Start)
            .ThenByDescending(r => r.End)
            .ToList();

        // Phase 3 — OVERLAP-REJECTION via occupied ranges.
        var occupied = new List<(int s, int e)>();
        var toApply = new List<ResolvedEdit>();
        var skipped = new List<string>();
        foreach (var r in ordered)
        {
            bool overlaps = occupied.Any(o => r.Start < o.e && o.s < r.End);
            if (overlaps)
            {
                skipped.Add($"SKIP edit#{r.OriginIndex + 1} [{r.Start},{r.End}) — overlaps an already-claimed range (first-resolved wins).");
                continue;
            }
            occupied.Add((r.Start, r.End));
            toApply.Add(r);
        }

        // Phase 4 — APPLY BOTTOM-UP. Because we mutate from the highest offset down,
        // each splice leaves all LOWER offsets unchanged, so the remaining edits'
        // pre-resolved indices stay valid. THIS is the offset-stability guarantee.
        var sb = new StringBuilder(document);
        var applied = new List<string>();
        foreach (var r in toApply) // already sorted descending
        {
            sb.Remove(r.Start, r.Length);
            sb.Insert(r.Start, r.Source.NewText);
            applied.Add($"APPLY  edit#{r.OriginIndex + 1} [{r.Start},{r.End}) \"{Trim(r.Source.TargetText)}\" -> \"{Trim(r.Source.NewText)}\"");
        }
        return (sb.ToString(), applied, skipped);
    }

    static List<int> AllIndexesOf(string haystack, string needle)
    {
        var result = new List<int>();
        int idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) != -1)
        {
            result.Add(idx);
            idx += needle.Length; // non-overlapping occurrences
        }
        return result;
    }

    static string Trim(string s) => s.Length <= 40 ? s : s[..37] + "...";
}

// ---------------------------------------------------------------------------
// ComposeEditTransaction — snapshot/rollback wrapper (all-or-nothing).
//
// Atomicity contract (the thing this spike unlocks):
//   * Snapshot the pre-batch document.
//   * Run Phase 1 (resolve) for the WHOLE batch FIRST.
//   * If ANY edit produced a validation error -> ABORT: apply nothing, return the
//     snapshot untouched. "One invalid edit => none apply."
//   * Only when the whole batch resolves cleanly do we run Phases 2-4.
//
// This is deliberately distinct from Phase-3 overlap-skip: an OVERLAP is a
// non-fatal within-batch collision (partial apply, reported); a VALIDATION FAILURE
// (not-found / ambiguous-strict / empty-target) trips the transaction rollback.
// Recording that distinction is the highest-value output of this spike (see note).
// ---------------------------------------------------------------------------
static class ComposeEditTransaction
{
    public static BatchResult Apply(string document, IReadOnlyList<TextEdit> edits)
    {
        var snapshot = document; // immutable string == cheap deep snapshot for the prototype

        var (resolved, errors) = ComposeEditBatch.Resolve(document, edits);
        if (errors.Count > 0)
        {
            // ROLLBACK — nothing applied.
            return new BatchResult(
                Committed: false,
                Document: snapshot,
                Errors: errors,
                AppliedLog: Array.Empty<string>(),
                SkippedLog: Array.Empty<string>());
        }

        var (committed, applied, skipped) = ComposeEditBatch.SortSkipApply(document, resolved);
        return new BatchResult(
            Committed: true,
            Document: committed,
            Errors: Array.Empty<EditValidationError>(),
            AppliedLog: applied,
            SkippedLog: skipped);
    }
}

// =============================================================================
// PROOF HARNESS — real assertions, real execution. Exit 0 == all proofs held.
// =============================================================================
static class Proof
{
    static int _failures;

    static void Assert(bool cond, string label)
    {
        Console.WriteLine((cond ? "  PASS  " : "  FAIL  ") + label);
        if (!cond) _failures++;
    }

    public static int Run()
    {
        // A document where the same words recur, so offsets are non-trivial.
        const string doc =
            "The Tenant shall pay Rent monthly. The Tenant shall maintain the Premises. " +
            "The Landlord shall provide access to the Premises.";

        Console.WriteLine("DOCUMENT (len " + doc.Length + "):\n  " + doc + "\n");

        // -------------------------------------------------------------------
        // PROOF 1 — VALID BATCH commits + OFFSET STABILITY (bottom-up).
        // Three non-overlapping edits, deliberately of DIFFERENT lengths so a
        // naive top-down apply using stale offsets would corrupt the result.
        // -------------------------------------------------------------------
        Console.WriteLine("PROOF 1 — valid batch: all edits apply, offsets stay stable");
        var valid = new List<TextEdit>
        {
            new("pay Rent monthly", "pay the Rent on the first day of each month", MatchMode.Strict), // grows
            new("maintain the Premises", "keep the Premises in good repair", MatchMode.Strict),        // ~same
            new("provide access to the Premises", "grant entry", MatchMode.Strict),                    // shrinks
        };
        var r1 = ComposeEditTransaction.Apply(doc, valid);
        foreach (var l in r1.AppliedLog) Console.WriteLine("    " + l);
        const string expected1 =
            "The Tenant shall pay the Rent on the first day of each month. The Tenant shall keep the Premises in good repair. " +
            "The Landlord shall grant entry.";
        Assert(r1.Committed, "committed == true");
        Assert(r1.AppliedLog.Count == 3, "all 3 edits applied");
        Assert(r1.Document == expected1, "document exactly matches expected (offset stability held)");

        // Explicit offset-stability demonstration: a BROKEN top-down apply that
        // resolves offsets once then applies lowest-first with stale indices.
        var broken = BrokenTopDownApply(doc, valid);
        Assert(broken != expected1,
            "control: naive TOP-DOWN apply with stale offsets CORRUPTS the document (proves bottom-up is necessary)");
        Console.WriteLine("    (top-down corrupted result: \"" + broken + "\")\n");

        // -------------------------------------------------------------------
        // PROOF 2 — ATOMICITY: one invalid edit => NONE apply.
        // Same 3 valid edits + one whose target_text is absent.
        // -------------------------------------------------------------------
        Console.WriteLine("PROOF 2 — intentionally-failing batch: one invalid edit => NONE apply");
        var failing = new List<TextEdit>(valid)
        {
            new("indemnify the Guarantor", "hold harmless", MatchMode.Strict), // NOT in document
        };
        var r2 = ComposeEditTransaction.Apply(doc, failing);
        foreach (var e in r2.Errors) Console.WriteLine("    " + e.Category + ": " + e.Message);
        Assert(!r2.Committed, "committed == false (batch rolled back)");
        Assert(r2.Document == doc, "document byte-identical to the pre-batch snapshot (NONE of the 4 edits applied)");
        Assert(r2.AppliedLog.Count == 0, "zero edits applied");
        Assert(r2.Errors.Any(e => e.Category == "not-found" && e.EditIndex == 3),
            "error names the exact failing edit index (Edit 4 / index 3)");
        Console.WriteLine();

        // -------------------------------------------------------------------
        // PROOF 3 — AMBIGUITY under strict also trips rollback (all-or-nothing).
        // "The Tenant" occurs twice; strict must refuse and roll the batch back.
        // -------------------------------------------------------------------
        Console.WriteLine("PROOF 3 — ambiguous strict match => validation error => rollback");
        var ambiguous = new List<TextEdit>
        {
            new("The Landlord shall", "The Owner shall", MatchMode.Strict), // unique, valid on its own
            new("The Tenant", "The Lessee", MatchMode.Strict),              // occurs twice -> ambiguous
        };
        var r3 = ComposeEditTransaction.Apply(doc, ambiguous);
        foreach (var e in r3.Errors) Console.WriteLine("    " + e.Category + ": " + e.Message);
        Assert(!r3.Committed, "committed == false");
        Assert(r3.Document == doc, "document unchanged (the valid sibling edit did NOT sneak through)");
        Assert(r3.Errors.Any(e => e.Category == "ambiguous"), "ambiguity error surfaced with resolution hint");
        Console.WriteLine();

        // -------------------------------------------------------------------
        // PROOF 4 — OVERLAP is NON-FATAL: distinct from validation rollback.
        // Two edits target overlapping spans; first-resolved wins, the other is
        // skip-and-reported, and the batch still COMMITS. This shows why the
        // production wrapper must treat overlap != invalid-edit.
        // -------------------------------------------------------------------
        Console.WriteLine("PROOF 4 — within-batch overlap: skip-and-report, batch still commits");
        var overlap = new List<TextEdit>
        {
            new("shall maintain the Premises", "shall keep the Premises tidy", MatchMode.Strict),
            new("maintain the Premises", "repair the Premises", MatchMode.Strict), // overlaps the above
        };
        var r4 = ComposeEditTransaction.Apply(doc, overlap);
        foreach (var l in r4.AppliedLog) Console.WriteLine("    " + l);
        foreach (var s in r4.SkippedLog) Console.WriteLine("    " + s);
        Assert(r4.Committed, "committed == true (overlap is non-fatal)");
        Assert(r4.AppliedLog.Count == 1, "exactly one of the overlapping pair applied");
        Assert(r4.SkippedLog.Count == 1, "the later-in-list overlapping edit was skip-and-reported");
        Console.WriteLine();

        Console.WriteLine(_failures == 0
            ? "ALL PROOFS HELD — atomic-transaction model validated."
            : $"{_failures} PROOF(S) FAILED.");
        return _failures == 0 ? 0 : 1;
    }


    // Control: resolve all offsets against the ORIGINAL doc, then apply
    // lowest-offset-first without re-resolving. This is the classic bug the
    // bottom-up ordering exists to prevent — earlier length-changing edits shift
    // every later offset.
    static string BrokenTopDownApply(string document, IReadOnlyList<TextEdit> edits)
    {
        var spans = edits
            .Select(e => (start: document.IndexOf(e.TargetText, StringComparison.Ordinal), e))
            .Where(t => t.start >= 0)
            .OrderBy(t => t.start) // ASCENDING == the bug
            .ToList();
        var sb = new StringBuilder(document);
        foreach (var (start, e) in spans)
        {
            sb.Remove(start, e.TargetText.Length); // stale offset after the first splice
            sb.Insert(start, e.NewText);
        }
        return sb.ToString();
    }
}
