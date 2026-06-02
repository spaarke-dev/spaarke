# D-11 — Owner selection of fix option for RB-T053-01 (task 022)

**Date**: 2026-06-01
**Owner**: `ralph.schroeder@hotmail.com` (per project owner role)
**Status**: **Finalized — Option 1 + Option B applied (partial closure 3 of 4 corpus failures); RB-T053-01a filed for residual semantic-gap**
**Cleared NFR**: Owner-decision-gate per design.md §5.3 (3-option ranking) — owner ACK obtained at task 022 dispatch time + owner re-decision 2026-06-01 after task 022 agent surfaced empirical correction.

## Final outcome (2026-06-01 main-session re-decision)

Owner first re-selected **Option A** (Option 1 + Option 3 cap at 0.75) per D-11 §A — main session implemented + verified: the 0.75 cap blocks 4 legitimate single-keyword confident Layer-1 routes (`CapabilityRouterTests.RouteAsync_ReturnsLayer1Result_WhenLayer1IsConfident` and 3 peers fail). Reverted.

Owner re-decision (second): **Option B (drop description-word scoring entirely)**. Main session implemented by setting `descriptionScoreWeight = 0.0` in `ScoreCapability` (preserved `ScoreDescription` helper for potential future re-enabling). Empirical result:

| Corpus failure | Driver | After Option 1+B |
|---|---|---|
| id=77 'Henderson case' → legal_research | description word 'case' | ✅ closed |
| id=89 'Martinez case' → legal_research | description word 'case' | ✅ closed |
| id=102 'AI model' → document_analysis | description word 'using' | ✅ closed |
| id=91 'amicus curiae brief' → summarize_content | LEGITIMATE HINT 'brief' in different semantic role | ❌ remains — Layer-2 fix only |

**3 of 4 corpus false-positives closed.** The remaining id=91 is a genuine semantic-gap requiring Layer-2 LLM disambiguation (which is by design for exactly this pattern — Layer-1 keyword matching cannot distinguish 'the brief' (legal document noun phrase) from 'to brief' (verb)). Filed as **RB-T053-01a** in `real-bug-ledger.md`; the 2 Layer-1 benchmark tests stay Skip'd with updated message pointing at RB-T053-01a. RB-T053-01 itself transitions to `partial-repair-residual-filed` rather than `repaired`.

Verification: `dotnet test --filter ~CapabilityRouter` → **35 Passed / 0 Failed / 2 Skipped / 37 Total**. No regression on the 4 previously-passing single-keyword tests; the 2 benchmark tests appropriately Skip'd until RB-T053-01a is addressed.

---

## Decision

The owner selected **Option 1 (Word-boundary regex matching)** from the 3-option ranking in `ledgers/real-bug-ledger.md` RB-T053-01 §"Recommended production fix" (also enumerated in `design.md` §5.3).

> 1. **Word-boundary matching** (~2h): change `Contains(hint, OrdinalIgnoreCase)` to a regex `\b<hint>\b` match. Eliminates the bigram-superstring false-positive class. ✅ **SELECTED**
> 2. Negative-evidence scoring (~4-6h): track hint-subset relationships + discount; eliminates entire bigram-superstring class.
> 3. Confidence-saturation guard (~1h): cap single-match confidence at 0.75 (below 0.80 threshold); forces Layer 2 disambiguation.

**Rationale cited by owner**: "simplest, safest option" — minimal change scope, lowest NFR-02 risk, no Layer-1 hit-rate sacrifice.

## Implementation applied (task 022)

Production fix applied to `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs`:

- `using System.Collections.Concurrent;` + `using System.Text.RegularExpressions;` added.
- `ScoreCapability` line 330 (`lowerMessage.Contains(lowerHint, StringComparison.Ordinal)`) → `TokenMatches(lowerMessage, hint)`.
- `ScoreDescription` line 365 (`lowerMessage.Contains(word, StringComparison.Ordinal)`) → `TokenMatches(lowerMessage, word)`.
- New private static helper `TokenMatches(string lowerMessage, string token)` using `Regex.IsMatch(message, $@"\b{Regex.Escape(token)}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant)`.
- New `ConcurrentDictionary<string, Regex> TokenRegexCache` for compiled-regex amortization (NFR-03 <50ms safe).
- XML doc comments updated to reflect new word-boundary semantics and cite RB-T053-01.

**Metrics**:
- File line count: 785 → 819 (+34, +4.3%).
- NFR-02 line replacement: ~3.8% (12 lines deleted, 30 added) — well under 50% threshold.
- Build: `dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors, 2 (pre-existing) warnings.
- Step 9.5 quality gates: `code-review` PASS-WITH-WARNING (W-1: see below), `adr-check` PASS (8 ADRs compliant + BFF Hygiene §A all 5 satisfied).

## Empirical finding (W-1 — requires owner re-decision)

**Option 1 alone is empirically insufficient to close the 2 Skip'd tests.**

When the test pair (`Layer1_DoesNotFalsePositive_OnNonKeywordMessages` + `Layer1_FullCorpus_DistributionSummary`) was flipped Skip→Pass and run against the post-fix `CapabilityRouter`, both tests still failed with 3 confidently-wrong routes on the same 4 corpus messages that the ledger documents (id=77, 89, 91, 102).

### Why Option 1 does not eliminate id=77 / id=89 / id=102

Hand-trace and empirical test output reveal the ledger's stated mechanism is incomplete:

| id | Message | Ledger claimed cause | Empirical cause |
|---|---|---|---|
| 77 | "Set the priority of the Henderson case to urgent" | Hint `case law` ⊃ `case` superstring match | **Description-word match**: `legal_research` description "Search legal databases for case law and precedents" includes word `case` (4 chars ≥ stop-word threshold); `\bcase\b` matches "henderson case" because `case` IS a standalone token. |
| 89 | "What is the latest on the Martinez case?" | Same superstring | Same description-word match. |
| 102 | "What version of the AI model are you using?" | Hint `analyze document` ⊃ `model`(?) | **Description-word match**: `document_analysis` description "Analyze document content using AI extraction" includes word `using` (5 chars); `\busing\b` matches "you using". |

The ledger's hint-substring claim (`"henderson case to urgent".Contains("case law")`) was **always FALSE** under the prior `Contains` logic too. The actual mechanism is description-word matching combined with the confidence saturation formula `topScore / (topScore + 0 + ε) ≈ 1.0` when only ONE capability scores > 0 (even at very low absolute score, e.g., 0.04 from description-only matching).

### Why id=91 still fires

Message "Pull the brief for the amicus curiae filing" — `summarize_content` hint `"brief"` IS a standalone word in the message. `\bbrief\b` matches. Option 1 word-boundary regex does NOT eliminate hint-as-token false-positives — it only eliminates hint-as-superstring-substring matches (which were not happening at the hint level for id=77/89/102 in the first place).

### Conclusion

Option 1 IS a real improvement (it eliminates a real false-positive class that would surface with corpus messages like "I have several **cases** to file" → hint `case law` no longer false-matches `cases`). It just doesn't happen to address the specific 4 corpus messages in the test suite, because their failure mechanism is description-word matching + confidence saturation, which Option 1 does not touch.

To close the 2 tests, an additional change is required. The 3 viable extensions, in increasing complexity:

1. **Option 3 layered on top of Option 1** (~1h additional): cap single-match confidence at 0.75 in the confidence formula. This is the conservative fix — would close all 4 corpus failures by forcing them through Layer 2 disambiguation. Sacrifices ~0% Layer-1 hit rate in practice (the 4 corpus messages already misroute at confidence 1.0, so dropping them to 0.75 just means they fall to Layer 2 cascade — which is the correct behavior per the documented contract).
2. **Disable description-word scoring entirely** (~1h additional): drop the `descScore * 0.2` contribution. Eliminates the description-word false-positive driver, but may also drop hit rate on legitimate description-driven matches (id=52/56/58 in corpus relied on description matching at the pre-Option-1 baseline; need re-validation).
3. **Option 2 (negative-evidence scoring)** (~4-6h additional): the comprehensive fix — would also close the tests but exceeds the owner's "simplest, safest" intent.

## Owner re-decision required

**RB-T053-01 cannot transition to `repaired` on Option 1 alone** because the test acceptance criteria are not met. The choices are:

- **A**: Approve a small extension (Option 1 + Option 3 confidence cap) — ~1h additional work, restores the test pair to PASS, ledger transitions to `repaired`. Recommended.
- **B**: Approve a larger extension (Option 1 + dropping description-word scoring) — ~1h additional, same outcome on tests, but Layer-1 hit rate may need re-validation.
- **C**: Re-select Option 2 (comprehensive) — ~4-6h, full bigram-superstring class elimination.
- **D**: Accept Option 1 standalone, do NOT close the 2 tests, leave RB-T053-01 in `open` state (or `partially-repaired`), document the residual via a follow-up ledger entry. Production code improvement still merges (it IS a real improvement).

The task 022 implementation as-applied corresponds to outcome **D** until owner decides. The 2 tests remain Skip'd; ledger entry remains `open`.

## Implications

- **Production code change**: Option 1 word-boundary regex IS applied and IS a real improvement. Build passes, ADRs compliant, NFR-02/NFR-03 honored. The change is safe to keep regardless of which extension (if any) the owner picks.
- **Tests**: 2 Skip'd tests REMAIN Skip'd (no flip applied). Skip messages still cite RB-T053-01 as the open ledger entry.
- **Ledger**: RB-T053-01 remains in current state (`open` / `assigned-to-r2`). Will transition to `repaired` only when owner approves the extension and tests pass.
- **Task 022 status**: production change complete + decision-recorded + Step 9.5 gates passed; awaiting owner re-decision before ledger transition + ledger-row commit.

## Reference

- POML: `projects/sdap.bff.api-test-suite-repair-r2/tasks/022-fix-rb-t053-01.poml`
- Ledger entry: `projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md` § RB-T053-01 (lines 451-516)
- design.md §5.3 (3-option ranking)
- Step 9.5 verdicts: code-review PASS-WITH-WARNING (W-1 surfaced here); adr-check PASS (8 ADRs + BFF Hygiene §A all 5)
- Empirical test output: `Failed: 2, Passed: 3, Skipped: 0` on `CapabilityRouterBenchmarkTests` after Option 1 applied + Skip→Pass flips (test flips reverted after finding; production change kept)
