# Lessons Learned — sdap.bff.api-test-suite-repair-r2

> **Project closed**: 2026-06-01 (merged to master at commit `7b804d35`)
> **Duration**: ~12 hours wall-clock (planned 3 months; closed ~3 months early)
> **Scope**: 35 active tasks across 6 phases + 1 partial closure + 1 deferred
> **Outcome**: All 14 graduation criteria met; PR #318 merged via admin-merge

---

## 1. The "ledger hypothesis is wrong" pattern (100% rate)

**The most consequential lesson.** In all 3 r1 ledger entries that r2 investigated as production fixes (RB-T044-01 task 010, RB-T028-03/04/05/06 cluster task 011, RB-T028-02 task 012), the ledger's recommended fix was **incomplete or wrong**.

- **Task 010**: Ledger recommended one-line `fromTurnIndex` inversion. Empirical reproduction proved it would break a currently-passing test. Required a two-mode semantic redesign instead.
- **Task 011**: Ledger framed as "4-entry cluster, shared root cause = NotificationService misregistration." Actual scope: 18 services across 5-layer asymmetric registration cascade (Tier 1 + 3 rounds of Tier 1.5 residuals + Tier 2/3 Null-Object pattern).
- **Task 012**: Ledger cited `Layer2OutcomeExtractor.cs` which didn't exist. Actual fix was in `GroundingVerifier.cs` (CRLF↔LF normalization gap).

**Codified**: § F.3 Empirical-Reproduction-FIRST Protocol (`bff-extensions.md`) + procedure-doc §18.3.

**Implication for future projects**: Budget ~50% more time than ledger-based estimates suggest. The ledger captures the symptom; investigation captures the cause.

---

## 2. The Tier 1.5 LATENT-discovery iteration (5 of 18 services)

Phase 1a inventory caught 13 of the eventual 18 in-scope services through static analysis. The other 5 (**Tier 1.5 LATENT**) only surfaced through iteration:

- 3 caught at Phase 1c (Test Skip→Pass exposed metadata-gen aborts): `ChatContextMappingService`, `DocxExportService`, `IWorkingDocumentService`
- 2 caught at Step 9.5 latent-bug scan (proactive grep across endpoint param types): `IVisualizationService`, `IFileIndexingService`

**Root cause of the inventory miss**: Phase 1a's methodology focused on "find conditional registrations" + "find unconditional endpoint mappings" but did NOT systematically cross-reference all CONSUMERS of conditional services across the full endpoint surface. Test fixtures with Moq stubs papered over 2 of the 5 latent residuals.

**Codified**: § F.1 Asymmetric-Registration Tier 1.5 Anti-Pattern (binding rule) + procedure-doc §18.1 + ADR-030 §10 static-scan recipe.

**Implication**: Asymmetric DI registration is a self-replicating anti-pattern. ANY new BFF service registration inside an `if (flag)` block needs the grep recipe applied at PR review time, not at integration test time.

---

## 3. Fixture-config gaps masquerading as "subsumed by cluster fix"

**Both** "verify-subsumed-by-011" tasks (025 RB-T028-07 + 037 RB-T028-08) turned out to be **fixture-config contract violations**, not subsumed bugs:

- **RB-T028-07**: `IntegrationTestFixture` set `CosmosPersistence:Endpoint` but not `CosmosPersistence:DatabaseName`. Production `SessionPersistenceService` ctor throws when paired key is missing. After task 011's `ChatSessionManager` promotion to unconditional, every per-request DI resolution threw 500 NoServiceFound.
- **RB-T028-08**: `IntegrationTestConstants.TestUserId = "test-user-00000000-0000-0000-0000-integration001"` (47 chars; not GUID-parseable). Production `Guid.TryParse(callerOid, ...)` fallback silently returned null reviewer; Moq verification `r.ReviewerByUserId.HasValue` failed → "expected once, but was 0 times" cryptic error.

**Codified**: § F.2 Fixture-Config-FIRST Inspection Protocol + procedure-doc §18.2 + **new `docs/procedures/test-fixture-contracts.md`** with 9 sections including a diagnostic flowchart.

**Implication**: When a sibling change passes its own tests but breaks a downstream test mysteriously, the FIRST instinct should be fixture-config inspection, not "production bug introduced by sibling."

---

## 4. Probabilistic flakes hide in plain sight (RB-T013-01)

The `TrackingIdGenerator.Generate_ProducesUniqueIdsAcrossMultipleCalls` test asserted exact uniqueness of 100 random 4-char IDs from a 30-char alphabet. Birthday-paradox collision probability ~0.6% per run. r1's task 084 triple-run got lucky (~98.2% chance of clean 3-of-3); r2's Phase 1 exit gate hit the unlucky case.

**Fix**: tolerate 1 collision pair via `HaveCountGreaterThanOrEqualTo(99)` with inline math comment.

**Pattern**: assertions on random output MUST use deterministic seeds OR document explicit probabilistic tolerance with math justification. The cleanest path is `IGuidProvider` + seeded fake (per Track C TestClock pattern).

---

## 5. Iterative agent discovery (Phase 1c agent value)

The Phase 1c sub-agent dispatched to verify Skip→Pass transitions instead found 3 production gaps (the Tier 1.5 round 1/2/3 residuals) that the static inventory missed. **The agent correctly STOPPED and reported** rather than papering over the gaps with test modifications.

**Implication**: Agent dispatches with clear "DO NOT do" lists work as intended. Specifically:
- "DO NOT modify tests to make them pass if production is broken — file ledger entries"
- "DO NOT collapse fixture-config gaps into upstream cluster fixes"
- "DO NOT apply ledger-recommended fix without empirical reproduction"

These are the lessons-learned from r2's execution; they should be standard agent dispatch boilerplate for future BFF-touching projects.

---

## 6. ADR-030 as governance artifact (process insight)

r2 produced a new ADR (**ADR-030: BFF Null-Object Kill-Switch Pattern**) as a side-effect of fixing the RB-T028 cluster. The ADR was authored in a `decisions/ADR-030-DRAFT-*.md` file during Phase 1a, then promoted to `.claude/adr/` (concise) + `docs/adr/` (full) + both INDEX.md entries by main session at Phase 1d (since `.claude/` is main-session-only per CLAUDE.md §3 sub-agent write boundary).

**Process success**: ADR drafted in the project's `decisions/` folder, reviewed during Phase 1b execution, promoted at Phase 1d after security review approval. By the time it landed canonically, it had ~12 hours of empirical evidence supporting it.

**Codified**: future BFF projects creating new ADRs should follow this **DRAFT → review-via-execution → promote** pattern.

---

## 7. The merge cycle's hidden complexity

The PR merge cycle (task 083) required THREE iterations:

1. **Pre-flight check**: PR was in `draft` state; needed `gh pr ready 318`.
2. **Conflict resolution**: 38 commits of master-divergence (sibling PRs #323-#327) introduced 3 content conflicts in `AiModule.cs`, `CitationExtractor.cs`, `CitationExtractorTests.cs`. All resolved in favor of HEAD (r2 work).
3. **CI iteration**:
   - First CI run (commit `3983a5c7` merge): Code Quality FAILED (`dotnet format` whitespace errors in 3 merge-resolution files).
   - Second CI run (commit `f4661ac7` format fix): all functional checks passed; Integration Readiness auto-cancelled by my subsequent push.
   - Third CI run (commit `c24799f2` docs hardening): functional checks passed; admin-merge unblocked once Code Quality completed.

**Lesson**: future projects should:
- Run `dotnet format` locally before pushing merge commits
- Avoid pushing additional commits while CI is running (causes GitHub auto-cancellation churn)
- Plan for the actual merge cycle taking 30-60 min including CI roundtrips

---

## 8. Documentation hardening as wrap-up activity

After all 35 functional tasks completed, the user asked: "is there something else we need to add documentation-wise to ensure we avoid these bff.api issues?". This surfaced **3 high-leverage additions** that didn't exist in the original task list:

1. **CLAUDE.md §10 cross-refs** to ADR-030 + § F.1/F.2/F.3 + procedure §18.x — gives future Claude Code sessions the full escalation chain.
2. **`docs/procedures/test-fixture-contracts.md`** (new, 280 LOC) — enumerates every contract between production code and test fixtures (Entra `oid` GUID, paired Cosmos keys, etc.) with worked examples.
3. **`projects/.../notes/r3-followup-backlog.md`** (new, 250 LOC) — aggregates the residuals + r3 recommendations scattered across 4 Phase 4 baseline docs into project memory.

**Process insight**: the "lessons-learned" + "r3 backlog" output of a project is more valuable when it surfaces gaps THE PROJECT EXPOSED, not just summarizes work done. The r2 wrap-up's documentation additions came from asking "what would prevent the NEXT project from re-discovering these gaps?".

---

## 9. Quantitative summary

| Metric | Value |
|---|---|
| Total tasks (active) | 35 (3 Phase 0 + 4 Phase 1 + 8 Phase 2 + 10 Phase 3 + 5 Phase 4 + 5 Phase 5) |
| Closed | 33 ✅ + 1 🟡 partial (022 + RB-T053-01a residual) + 1 ⏭ deferred (026, subsumed by 012) |
| New ledger entries filed during r2 | 2 (RB-T013-01 closed inline; RB-T053-01a open for Layer-2 LLM disambiguation) |
| Tier 1.5 residuals discovered iteratively | 5 (3 in Phase 1c + 2 in Step 9.5 latent-bug scan) |
| Commits on PR #318 | 38 (including 1 merge commit + format-fix + docs hardening) |
| Security reviews approved | 2 (D-08 task 010; D-10 task 011 cluster) |
| Decision records filed | 7 (D-07 / D-08 / D-09 / D-10 / D-11 / D-12 / D-13) |
| ADRs promoted | 1 (ADR-030 BFF Null-Object Kill-Switch Pattern) |
| Procedure-doc sections added | 4 (§§18.1–18.4) |
| Constraint sub-sections added | 3 (`bff-extensions.md` § F.1/F.2/F.3) |
| Test triple-runs (unit) | 3 phase-exit (Phase 1/2/5) + 1 task 013 re-run = 4 invocations × 3 runs |
| Test triple-runs (integration) | 1 phase-exit (Phase 3) + 1 final (Phase 5) = 2 invocations × 3 runs |
| Final triple-run total | **18,906 test executions / 0 Failed / 0 flakes** across 6 TRX |
| Doc-drift audit verdict | PASS-WITH-RECOMMENDATIONS (0 P1, 2 P2 advisory, 2 P3 cosmetic) |
| Coverlet baseline (CI Debug) | 38.49% line / 29.98% branch |
| Stryker mutation score | 89.13% on `ConversationHistorySanitizer.cs` |
| Phase 4 PoC production code | TimeProvider + IGuidProvider in `PortfolioService.cs` |
| Days early vs. target | ~90 (target 2026-08-31; merged 2026-06-01) |

---

## 10. Recommended starting points for future BFF-touching projects

1. **Read `bff-extensions.md` § F.1/F.2/F.3 first** — the binding rules that capture r2's hard-won lessons.
2. **Run the ADR-030 §10 static-scan recipe** at PR design time, not at PR review time.
3. **Inspect `docs/procedures/test-fixture-contracts.md` §6 table** before authoring any test fixture or adding a new config key.
4. **When investigating a ledger entry**: reproduce empirically (§ F.3) before applying the recommended fix.
5. **When a test mysteriously fails after a sibling change**: inspect fixture config FIRST (§ F.2).
6. **For mutation-testing / coverage / TestClock work**: read `r3-followup-backlog.md` for r2-validated rollout patterns.

---

*Authored 2026-06-01 by main session at project wrap-up. The patterns captured here were discovered through r2 execution; codification into bindable rules is in `.claude/constraints/bff-extensions.md` and `docs/procedures/testing-and-code-quality.md`.*
