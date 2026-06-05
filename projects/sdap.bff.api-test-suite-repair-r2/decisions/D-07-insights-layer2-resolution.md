# D-07 — Insights Layer 2 HOLD Resolution Path (RB-T028-02) — **RESOLVED**

> **Status**: **RESOLVED via path (b) on 2026-06-01**
> **Created**: 2026-06-01 (r2 Phase 0 task 002)
> **Resolved at**: r2 Phase 1 task 012 (path-b production fix applied; 3 tests Skip→Pass; ledger RB-T028-02 → `repaired`)
> **Binding requirement**: FR-05

---

## Decision

**Path (b) — r2-takes-bug — chosen by `dev@spaarke.com` on 2026-06-01.**

`dev@spaarke.com` (the consolidated sibling-coordination contact per task 002) reviewed the RB-T028-02 evidence and determined that:

1. The bug surfaces in r2-owned unit tests (`Sprk.Bff.Api.Tests`), not in any sibling-owned production code or test asset.
2. Static-inspection investigation (Phase 1 task 012, 2026-06-01) revealed the r1 ledger hypothesis ("LLM-mock fixture text drifted from prompt") was incorrect — the literal quote strings ARE present in fixture files; the actual root cause is a CRLF↔LF normalization gap between the CRLF-on-disk fixtures and the LF-only raw-string-literal evidence quotes in C# 11 raw strings, combined with a test-side use of raw `String.Contains` (byte-exact) where it should have used `GroundingVerifier`-equivalent normalization (whitespace-collapse + lowercase, line-ending-tolerant).
3. Path (a) — sibling-takes — was inapplicable because no sibling-owned code is involved.
4. Path (c) — archived — was already removed at Phase 0 (no separate sibling outreach thread to time out; same person is both r2 owner and Insights sibling owner).

The decision was therefore unambiguous: r2 owns the bug, applies the production fix, transitions the 3 tests to Pass, and closes the ledger entry as `repaired`.

---

## Actual root cause (corrected from r1's hypothesis)

**The r1 hypothesis ("Layer2OutcomeExtractor.cs fixture-text-drift") was wrong on three counts.**

1. **Wrong file**: `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Extraction/Layer2OutcomeExtractor.cs` does NOT exist. Equivalent extraction code is in `Services/Ai/Insights/Extraction/` (10 .cs files: `OutcomeExtractionProjection`, `OutcomeExtractionResponse`, `OutcomeExtractionResponseValidator`, `ObservationEmitter`, etc.) — confirmed by Phase 0 reproducibility verification (task 001).
2. **Wrong cause class**: Not "fixture-text-drift" (i.e., fixtures updated without prompt). The fixtures `closing-letter-M-2024-0341.txt`, `settlement-agreement-M-2024-0188.txt`, `decision-memo-M-2024-0512.txt` DO literally contain the evidence-quote text — verified byte-by-byte via Python on 2026-06-01.
3. **Wrong surface**: Not in `Services/Ai/Insights/Extraction/`. The bug lives at the test-side manual GroundingVerifier mirror in `Layer2OutcomeExtractionTests.cs` lines 165, 254, 338, which uses raw `String.Contains` instead of `GroundingVerifier.Normalize`-equivalent semantics.

**Actual cause**: The 3 fixture files use CRLF line endings on Windows. C# 11 raw-string literals (`"""..."""`) used for the mocked LLM JSON normalize to LF at compile time. Raw `String.Contains` is byte-exact — `\n` ≠ `\r\n` — so the LF-only quote string never matches the CRLF fixture content. The TEST'S assertion is stricter than production's `GroundingVerifier.VerifyOne` (which calls `Normalize` first, collapsing all whitespace runs including `\r\n` into a single space, then substring-matches the normalized strings). Production grounding verification is line-ending-tolerant; the test failed to mirror this.

---

## Resolution scope

**Production change** (1 file, additive):
- `src/server/api/Sprk.Bff.Api/Services/Ai/CitationVerification/GroundingVerifier.cs`:
  - `Normalize` method visibility: `internal static` → `public static`.
  - XML doc expanded from 3 lines to 16 lines documenting the canonical grounding-text normalization contract (CRLF↔LF tolerance) as a public API surface.

**Test change** (1 file, Skip→Pass per NFR-01):
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Insights/Layer2/Layer2OutcomeExtractionTests.cs`:
  - 3 tests' `[Skip = "..."]` attributes removed.
  - 3 tests' `[Trait("status", "real-bug-pending-fix")]` → `[Trait("status", "repaired")]`.
  - `using Sprk.Bff.Api.Services.Ai.CitationVerification;` added.
  - 7 raw `documentText.Should().Contain(quote)` calls replaced with `GroundingVerifier.Normalize(documentText).Should().Contain(GroundingVerifier.Normalize(quote))` — mirroring production semantics.
  - 3 comment blocks updated to cite RB-T028-02 resolution + 2026-06-01 + production-mirror rationale.

**Triple-run validation** (NFR-05):
- 3 × `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --logger trx` — Failed: 0 / Passed: 5902 / Skipped: 129 / Total: 6031 across all 3 runs. TRX files in `projects/sdap.bff.api-test-suite-repair-r2/baseline/trx-rb-t028-02/run-{1,2,3}.trx`.

**Quality gates** (D-03, FULL rigor, MEDIUM severity — no separate security review):
- `code-review`: ✅ PASS (0 Critical / 0 Warning / 1 cosmetic Suggestion; BFF Hygiene § A all 6 rules satisfied).
- `adr-check`: ✅ PASS (14 ADRs compliant: ADR-001, 002, 004, 007, 008, 009, 010, 013-refined, 014, 015, 016, 021, 028, 029; ADR-013 refined explicitly validated per directive — no facade-discipline violation; `.claude/constraints/ai.md` 100% compliant).

---

## Downstream task scoping (updated)

| Task | Status (post-resolution) | Notes |
|------|--------------------------|-------|
| Task 012 (Phase 1 P1-W1) | ✅ completed | Path-b chosen + executed 2026-06-01 |
| Task 026 (Phase 2 P2-W1, conditional fallback) | ⏭ **deferred (subsumed)** | Was scoped as a conditional Phase 2 fallback IF task 012 chose path-b but did NOT apply the fix at task 012. Path-b closure happened at task 012 itself, so task 026 is now obsolete. Marked ⏭ in TASK-INDEX with note "subsumed by task 012 path (b) — 2026-06-01". |

---

## Cross-references

| Reference | Purpose |
|---|---|
| [FR-05 in spec.md](../spec.md) | Binding source for the 3-path resolution requirement |
| [Consolidated sibling contact record](owner-responses/consolidated-sibling-contact-2026-06-01.md) | Why path (c) was N/A and same person owns both r2 and Insights sibling |
| [r1 real-bug-ledger.md RB-T028-02 entry (updated 2026-06-01)](../../sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md) | The ledger entry transitioned `open` → `repaired` with corrected root cause |
| [Phase 0 reproducibility verification (Path correction)](../baseline/20-entries-reproducibility-verification.md) | Confirmed `Layer2OutcomeExtractor.cs` does not exist; equivalent path = `Services/Ai/Insights/Extraction/` |
| [Per-fix triple-run evidence](../baseline/per-fix-triple-run-rb-t028-02-2026-06-01.md) | NFR-05 validation artifact for task 012 |
| [Task 012 POML](../tasks/012-resolve-insights-hold.poml) | Task definition (status: completed) |
| [Task 026 POML](../tasks/026-fix-rb-t028-02-fallback.poml) | Conditional fallback; now deferred ⏭ |

---

*Finalized 2026-06-01 by r2 Phase 1 task 012 (path-b). Originally created as placeholder by r2 Phase 0 task 002.*
