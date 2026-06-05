# r3 Follow-Up Backlog — aggregated from r2 closure (2026-06-01)

> **Status**: Reference (not a binding task list)
> **D-06 reminder**: r3 is **NOT planned** as of 2026-06-01. r2 was scoped as comprehensive closure. This document aggregates the residuals + recommendations that surfaced during r2 execution so they're not lost. Each item below is **optional** — addressable in a future ad-hoc PR, a Phase 5+ project, or a formal r3 if one is ever commissioned.
>
> **Purpose**: project memory. Without aggregation, the 4 Phase 4 baseline docs (Tracks A/B/C/D) + the partial closures end up scattered.
>
> **Cross-references**:
> - r2 closure: `projects/sdap.bff.api-test-suite-repair-r2/`
> - Source docs cited per item

---

## 1. Open ledger residuals (production work)

### 1.1 RB-T053-01a — CapabilityRouter Layer-1 semantic-gap (LOW)

- **Status**: `open` (filed 2026-06-01 by task 022 partial closure)
- **Severity**: LOW
- **Description**: id=91 in the corpus benchmark ("Pull the brief for the amicus curiae filing") — hint `brief` is legitimate for `summarize_content` capability AND a standalone token in the message, but the semantic role differs ('the brief' = legal-document noun phrase vs 'to brief' = verb).
- **Why open**: Layer-1 keyword matching has no way to distinguish; this is exactly what Layer-2 LLM disambiguation is designed for. Layer-2 cascade catches it in production.
- **Recommended path**: accept Layer-1 may produce single-hint false-positives in ambiguous semantic-role cases; rewrite the 2 affected Layer-1 benchmark tests (`Layer1_DoesNotFalsePositive_OnNonKeywordMessages` + `Layer1_FullCorpus_DistributionSummary`) to assert the Layer-2 contract (zero confidently-wrong AFTER cascade) rather than the Layer-1-alone contract.
- **Alternative**: stop-noun list for capability hints (`brief`, `case`, `argument`, etc.) — risks under-routing legitimate uses.
- **Effort**: ~2-4h
- **Fix-by date**: 2026-09-30 (90-day target — LOW; production behavior is already correct via Layer-2 cascade)
- **Source**: [`real-bug-ledger.md`](../../sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md) RB-T053-01a entry; [`decisions/D-11`](../decisions/D-11-rb-t053-01-fix-option.md) final §

### 1.2 RB-T053-01 — partial-repair-residual-filed (MED, parent of 1.1)

- **Status**: `partial-repair-residual-filed` (3 of 4 corpus failures closed by task 022 Option 1+B)
- **Recommendation**: Close as `repaired` once RB-T053-01a (1.1 above) is addressed.

---

## 2. Client-surface r3 candidates (RB-CLIENT-*)

### 2.1 RB-CLIENT-001 — SemanticSearch test wiring (HIGH)

- **Source**: Track A PCF audit ([`baseline/phase4-track-a-pcf-audit-2026-06-01.md`](../baseline/phase4-track-a-pcf-audit-2026-06-01.md))
- **Description**: 17 test files / ~358 test cases exist under `src/client/code-pages/SemanticSearch/` but `package.json` declares no `test` script, no jest devDependencies, no `jest.config`. Tests are **orphaned and cannot run** — mirror of RB-T028 cluster on the client surface.
- **Recommended fix**: add jest config + devDependencies + `test` npm script; verify all 358 cases pass; consider migrating to vitest if SemanticSearch moves to Vite.
- **Effort**: 1 P1 PR, ~4-8h
- **Fix-by date**: 60 days (LOW priority since the production app works; tests are just orphaned)

### 2.2 RB-CLIENT-002 — AnalysisWorkspace deprecated tests (MED)

- **Source**: Track A PCF audit
- **Description**: 2 test files (`useDiffReview.test.ts` + `streaming-e2e.test.ts`) under `src/client/code-pages/AnalysisWorkspace/__tests__/` are marked `@deprecated OBSOLETE`, import `__tests__/mocks/MockSprkChatBridge` which was **deleted in r1 task 043**. Tests fail at module resolution. `jest.config.js` lines 27-28 have an unresolved TODO comment about this debt.
- **Recommended fix**: Either delete the 2 files (deprecated) or rewrite them against the current `SprkChatBridge` interface. Owner decision.
- **Effort**: ~1-3h
- **Fix-by date**: 60 days

### 2.3 Client zero-test backlog

- **Source**: Track A PCF audit
- **Description**: 10 of 26 client surfaces (38.5%) have ZERO tests — 8 PCF controls + 2 code-pages. Owner triage needed (test some? archive some? defer?).
- **Effort**: 8 P2 triage decisions; per-decision scope TBD
- **Not a single ledger entry** — needs per-surface owner review.

---

## 3. Coverage rollout candidates (Track D)

### 3.1 Coverlet 3-phase rollout

- **Source**: Track D Coverlet baseline ([`baseline/phase4-track-d-coverlet-baseline-2026-06-01.md`](../baseline/phase4-track-d-coverlet-baseline-2026-06-01.md))
- **Current baseline (CI, Debug)**: 38.49% line / 29.98% branch on 115,805 coverable lines.
- **Per-assembly**: Spaarke.Core 68.3%, Sprk.Bff.Api 39.6%, Spaarke.Dataverse 4.1%.
- **Phase 1 (~1 week)**: soft gate via ReportGenerator PR comments — no hard threshold yet.
- **Phase 2 (~2 weeks)**: do-not-decrease >2pp hard gate anchored at 38% line / 30% branch.
- **Phase 3 (~3-6 weeks)**: absolute floor 45% line / 35% branch once Dataverse DTOs get `[ExcludeFromCodeCoverage]` or `runsettings` excludes.
- **DO NOT** add a threshold to the BFF CI before completing Phase 1+2 PR-comment baseline.

### 3.2 Spaarke.Plugins coverage gap (informational)

- **Source**: Track D
- **Description**: Spaarke.Plugins assembly missing from CI artifact (artifact-glob picks only one stream when tests run in parallel). Coverage measured at 0% by default but plugin tests DO run.
- **Recommended fix**: adjust CI artifact upload to merge per-project coverage streams, OR exclude Spaarke.Plugins from coverage report headline % until merge fix is in place.

---

## 4. Mutation-testing rollout candidates (Track B)

### 4.1 Stryker.NET — expand to full `Services/Ai/Safety/**`

- **Source**: Track B Stryker pilot ([`baseline/phase4-track-b-stryker-pilot-2026-06-01.md`](../baseline/phase4-track-b-stryker-pilot-2026-06-01.md))
- **Pilot scope**: `ConversationHistorySanitizer.cs` — 89.13% mutation score (41 killed / 5 survived / 46 covered).
- **5 survivors analyzed**: 2 equivalent mutants (boundary equality), 2 intentionally untested (LogDebug — ADR-015 no-content-logging), 1 real test gap (`GetPivotMatterId` line 170 non-System-role path).
- **Recommended r3 expansion**: full `Services/Ai/Safety/**` (13 files) at `concurrency=1`. Estimated wall-clock ~2-3h. NOT enable `--break-at` in r2 — r3 owns the per-file baseline + threshold.
- **Defer**: `Capabilities/` and `Chat/` — contingent on the Safety expansion findings.

### 4.2 Close the 1 real test gap (informational)

- **Description**: 1 surviving mutant on `Conditional (true)` mutation at `GetPivotMatterId` lines 170-172 — the non-System-role anchor path isn't exercised.
- **Recommended fix**: single one-line test would close it.
- **Effort**: ~15 min
- **Could be folded** into the r2.1 polish PR if one is created.

---

## 5. TestClock + IGuidProvider rollout candidates (Track C)

### 5.1 TimeProvider + IGuidProvider — generalize to existing code

- **Source**: Track C TestClock PoC ([`baseline/phase4-track-c-testclock-poc-2026-06-01.md`](../baseline/phase4-track-c-testclock-poc-2026-06-01.md))
- **Pilot scope**: `PortfolioService.cs` — 2 `DateTimeOffset.UtcNow` sites + 5 new tests.
- **Production code**: + new `IGuidProvider` interface + `DefaultGuidProvider`; both registered via `TryAddSingleton` in `WorkspaceModule`.
- **Recommended r3 rollout strategy**: file-by-file generalization with per-file PRs. Start with services that have BOTH `DateTime.UtcNow` and `Guid.NewGuid()` direct usage AND already-passing tests (to avoid bundling test rewrites with the seam introduction).
- **Pattern**: ADR-010 compliant via TryAddSingleton + ctor-default backward compatibility.
- **Sequence (suggested)**:
  1. Search for `DateTime\.UtcNow|DateTimeOffset\.UtcNow` across `src/server/api/Sprk.Bff.Api/Services/`
  2. Same for `Guid\.NewGuid\(\)`
  3. Cross-tabulate with files that have existing test classes
  4. Pick the next-smallest seam target (single-class, ≤3 sites, ≤2 deps)
  5. Repeat the Track C pattern

### 5.2 Promote test helpers to a shared utility assembly

- **Description**: Track C kept `FixedTimeProvider` + `FakeGuidProvider` inline in `PortfolioServiceTests.cs`. r3 should promote them to a shared `Sprk.Bff.Api.Tests.Helpers` or `Sprk.TestKit` assembly.
- **Effort**: ~2-4h
- **Risk**: introducing a new test-only package = small risk of versioning drift. Document carefully if you do it.

---

## 6. Documentation polish (doc-drift audit P2 advisory)

### 6.1 CLAUDE.md §10 cross-refs (D-01 + D-02)

- **Source**: Track 084 doc-drift audit ([`baseline/phase5-doc-drift-audit-2026-06-01.md`](../baseline/phase5-doc-drift-audit-2026-06-01.md))
- **Status (2026-06-01)**: **PARTIALLY ADDRESSED** by r2 wrap-up — §10 bullet 6 now links to ADR-030 + § F.1/F.2/F.3 + procedure §18.x via the documentation-hardening commit. The "by-section" individual links (e.g., one-liner anchors per F.1/F.2/F.3 from §10) are still implicit through the parent §F anchor.
- **Optional follow-up**: tighten anchor language if drift surfaces in a future audit.

### 6.2 D-03 + D-04 cosmetic (deferrable indefinitely)

- **Source**: doc-drift audit
- **Description**: Label-numbering and tense drift in `bff-extensions.md` / ADR-030. Cosmetic; no navigation impact.
- **Recommendation**: defer to whenever those files are next edited for substantive reasons.

---

## 7. Process / methodology recommendations (r2-derived)

### 7.1 Asymmetric-Registration discovery methodology

- **Lesson** (Track E): r2 Phase 1a's inventory pass identified 13 of the eventual 18 in-scope services. The 5 missed (Tier 1.5 LATENT) were caught by Phase 1c iteration + Step 9.5 latent-bug scan.
- **Codified in**: `bff-extensions.md` § F.1 binding rule + procedure-doc §18.1 + ADR-030 §10 static-scan recipe.
- **Open methodology question**: should the static-scan recipe be automated as a build-time check? Currently it's reviewer-judgment. Phase 5 design (§5.5) explicitly rejected CI scripts; if r3 reverses this, codify the test infrastructure first.

### 7.2 Ledger-hypothesis correction rate

- **Lesson** (Track E): r1 ledger entries' recommended fixes were INCOMPLETE in 100% of the 3 r2 cases (010 / 011 / 012). Empirical-reproduction-FIRST protocol is now § F.3 binding.
- **r3 implication**: if r3 ever happens, expect ~50%+ of any inherited ledger entries to need re-investigation. Budget accordingly.

### 7.3 Fixture-config gaps masked as "subsumed by upstream cluster fix"

- **Lesson** (tasks 025 + 037): BOTH "verify-subsumed-by-011" tasks turned out to be fixture-config gaps the cluster fix had unmasked.
- **Codified in**: `bff-extensions.md` § F.2 + procedure-doc §18.2 + new `docs/procedures/test-fixture-contracts.md`.

---

## 8. Pure technical debt (informational; not r3-scope)

### 8.1 Probabilistic-flake-detection pattern (RB-T013-01)

- **Description**: `TrackingIdGeneratorTests.Generate_ProducesUniqueIdsAcrossMultipleCalls` previously asserted exact uniqueness (`HaveCount(100)`) on 100 4-char IDs from a 30-char alphabet — birthday-paradox collision ~0.6% per run. Fixed in r2 to `HaveCountGreaterThanOrEqualTo(99)`.
- **Generalizable pattern**: assertions on random output MUST use deterministic seeds OR document explicit probabilistic tolerance with math justification.
- **Recommendation**: codify as a sibling pattern to procedure-doc §18.4 (TestClock) when next refreshing the procedure doc — maybe §18.5 "Deterministic Test Data: probabilistic assertions". Low priority.

### 8.2 Per-namespace coverage gaps (informational)

- **Source**: Track D Coverlet baseline
- **Lowest-coverage namespaces in Sprk.Bff.Api** (Debug, CI): Workers 12.8%, Endpoints 18.2%.
- These are NOT regressions from r2; they reflect long-standing patterns. Address only if a coverage threshold is added in 3.1 above.

---

## 9. How to use this backlog

1. **Treat as project memory**, not as a binding task list. r2 closed comprehensively per D-06.
2. **If r3 is ever commissioned**: use this as the input for r3's design phase. Each § above maps to a candidate r3 work stream.
3. **If individual items are picked up ad-hoc**: file the work as a new ledger entry citing this document § for context.
4. **Updates**: revise this document if (a) any open ledger entry transitions, (b) r3 is commissioned, or (c) a new r2-derived lesson surfaces during ongoing maintenance.

---

*Authored 2026-06-01 as part of r2 closure. Aggregates Phase 4 Track A/B/C/D findings + Track E lessons + RB-T053-01a residual + doc-drift audit recommendations.*
