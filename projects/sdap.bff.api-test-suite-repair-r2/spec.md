# sdap.bff.api-test-suite-repair-r2 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-01
> **Source**: `design.md` (drafted 2026-06-01)
> **Predecessor**: `sdap-bff.api-test-suite-repair` (closed 2026-06-01)
> **Target end date**: **2026-08-31**

---

## Executive Summary

r1 closed at `Failed: 0` across both BFF test suites but explicitly deferred production code changes (NFR-01 in r1). r1 surfaced 20 real-bug-pending-fix entries in `ledgers/real-bug-ledger.md` (5 HIGH, 7 MED, 8 LOW) plus 5 structural test-quality gaps. r2 is the closure project: it inverts r1's NFR-01 (production code IS in scope), fixes all 20 entries, validates integration suite stability, completes sibling sign-offs, measures anti-drift effectiveness, and pilots 4 forward-looking quality improvements (PCF audit, mutation testing, TestClock PoC, coverage measurement). Hard deadline 2026-08-31 beats the September fix-by cliff.

---

## Scope

### In Scope

- **20 real-bug production fixes** per r1's `real-bug-ledger.md` — each fix changes `src/` code; tests flip Skip → Pass
- **Insights Layer 2 HOLD resolution** (RB-T028-02; sibling project coordination)
- **`Spe.Integration.Tests` runtime stability** validated via triple-run + flake-quarantine (mirrors r1 task 084 for unit)
- **Sibling-project sign-off completion** — populate TBD slots in `projects/sdap-bff.api-test-suite-repair/priority-order.md` for Action Engine, Insights, Communications owners
- **Anti-drift effectiveness measurement** — analyze BFF-touching PRs from 2026-06-01 → r2 close; report compliance with test-update obligation
- **PCF/Code Pages test rot audit** (read-only; recommendation only — no fixes applied)
- **Mutation testing pilot** for `Services/Ai/Safety/*` via Stryker.NET
- **TestClock + seeded Guid PoC** in `Services/Workspace/*` test surface
- **Coverlet baseline measurement** per project (no threshold enforcement)
- **Ledger lifecycle documentation** in `docs/procedures/testing-and-code-quality.md`
- **`bff-extensions.md` § F extension** with the unconditional-service-registration rule from RB-T028-03/04/05/06 root cause (if Phase 5 finds it warranted)

### Out of Scope

- Full PCF/Code Pages test suite repair (r2 audits + recommends only; full execution = r3)
- Full mutation testing remediation across all AI services (r2 pilots one area; r3 expands)
- Full deterministic test data migration (r2 proves in `Services/Workspace/*`; r3 generalizes)
- Coverage gate as required-status-check (waits for `github-actions-rationalization-r1` first)
- New test types (property-based, fuzzing)
- Integration-test surface reduction (separate project)
- Feature work, refactors unrelated to ledger entries

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Services/`, `Api/`, `Infrastructure/`, `Filters/` — production fixes per ledger
- `tests/unit/Sprk.Bff.Api.Tests/` — Skip → Pass transitions; TestClock PoC in `Services/Workspace/*`
- `tests/integration/Spe.Integration.Tests/` — triple-run validation; potential flaky quarantine
- `src/client/pcf/`, `src/client/code-pages/`, `src/client/shared/` — **read-only** for audit (item E)
- `projects/sdap-bff.api-test-suite-repair/ledgers/*` — entries transition status
- `projects/sdap.bff.api-test-suite-repair-r2/` — this project's artifacts
- `.github/workflows/sdap-ci.yml` — Coverlet measurement enable
- `docs/procedures/testing-and-code-quality.md` — lifecycle + TestClock pattern
- `.claude/constraints/bff-extensions.md` § F — extension if warranted (Phase 5)

---

## Functional Requirements

### Real-bug production fixes (FR-01 through FR-04)

1. **FR-01**: All 20 real-bug-pending-fix entries in `projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md` are closed by 2026-08-31, each with a Status transition (`open` → `assigned-to-r2` → `in-progress` → `repaired` / `transferred-to-sibling` / `archived-as-dead-target`). Acceptance: query against ledger shows 0 entries in `open`, `assigned-to-r2`, or `in-progress` status.

2. **FR-02**: All 5 HIGH severity entries closed in Phase 1 (3 weeks). Sub-items:
   - **RB-T044-01**: `ConversationHistorySanitizer.StripRetrievedContent` `fromTurnIndex` inverted slicing logic — fixed; cross-matter regression test added; security review approval recorded in PR
   - **RB-T028-03/04/05/06** (4-entry cluster, shared root cause): endpoint metadata aborts because `INotificationService` (and similar) registered conditionally but endpoints map unconditionally. Production fix: either make registration unconditional OR make endpoint mapping conditional on the same flag. Decision per-case based on whether the flag is product-meaningful.
   - Acceptance: 5 entries marked `repaired`; 5 production PRs merged; 37 tests flip Skip → Pass; security review documented for RB-T044-01.

3. **FR-03**: All 7 MEDIUM severity entries closed in Phase 2 (3 weeks). Per-entry production fix in the noted code path:
   - RB-T044-02 (CitationExtractor NormalizeCaseLaw), RB-T044-04 (NormalizePatent EP/WO), RB-T053-01 (CapabilityRouter Layer-1 classifier — owner decision required on 3 ranked fix options), RB-T070-03 (AnalysisChatContextResolver — restore or delete dead path), RB-T028-01 (AnalysisContextBuilder sort-stability), RB-T028-02 (Insights Layer 2 HOLD — resolved via FR-05), RB-T028-07 (Upload endpoint binding gap).
   - Acceptance: 7 entries marked `repaired` or `transferred-to-sibling`; 30-40 tests flip Skip → Pass.

4. **FR-04**: All 8 LOW severity entries closed in Phase 3 (2 weeks). Entries: RB-T012-01, RB-T034-01, RB-T044-03, RB-T044-05, RB-T050-01, RB-T070-01, RB-T070-02, RB-T028-08. Each is a focused 1-line-to-1-method production fix. Acceptance: 8 entries marked `repaired`; remaining tests flip Skip → Pass.

### Sibling coordination + governance (FR-05 through FR-09)

5. **FR-05**: RB-T028-02 (Insights Layer 2 HOLD) resolved with `ai-spaarke-insights-engine-r1` owner. One of: (a) sibling project takes the bug — 3 tests transferred to their backlog; T028-02 closed with cross-reference; (b) r2 takes the bug — production fix here; 3 tests flip Skip → Pass; (c) entries `archived-pending-sibling-engagement` if sibling unresponsive after 1 week. Acceptance: documented decision in `decisions/D-07-insights-layer2-resolution.md`.

6. **FR-06**: `projects/sdap-bff.api-test-suite-repair/priority-order.md` TBD sibling-owner sign-off slots populated. 3 owners contacted (Action Engine, Insights, Communications); responses captured in `decisions/`. Acceptance: priority-order.md shows owner names + sign-off dates (or "no in-flight overlap" annotation) for all 3 sibling slots.

7. **FR-07**: `docs/procedures/testing-and-code-quality.md` updated with ledger lifecycle documentation. Sections: real-bug state transitions, when to file vs when to fix, owner assignment rules, fix-by date logic. Acceptance: file diff shows new section "Real-Bug Ledger Lifecycle"; reviewed.

8. **FR-08**: `.claude/constraints/bff-extensions.md` § F extended IF Phase 5 finds the unconditional-service-registration rule warranted (based on Phase 1 RB-T028-03/04/05/06 work). If extended: new bullet under § F with rule + cross-reference to RB-T028 cluster. Acceptance: either extension exists OR decision record explains why not.

9. **FR-09**: Anti-drift effectiveness report published. Analysis: BFF-touching PRs from 2026-06-01 → 2026-08-15. Per PR: test-update obligation checkbox state, actual test addition/update presence, reviewer comment presence. Report includes compliance rate and (if <80%) corrective-action proposals. Acceptance: `baseline/anti-drift-effectiveness-2026-08-XX.md` exists with measured data and analysis.

### Integration stability (FR-10)

10. **FR-10**: `Spe.Integration.Tests` triple-run validation completes Phase 3. Same protocol as r1 task 084. Any flake (test that passes ≥1 run and fails ≥1 run): `[Trait("status","flaky-quarantined")]` + Skip + entry in `ledgers/flaky-ledger.md` with fix-by date. Acceptance: 3 consecutive `Spe.Integration.Tests` runs documented; ≤2 flakes; flaky-ledger entries for any quarantined tests.

### Phase 4 pilots (FR-11 through FR-14)

11. **FR-11**: PCF/Code Pages test rot audit document published. Apply r1's diagnostic playbook (factory config keys, sibling fixtures, endpoint vs service registration alignment) to `src/client/pcf/*` and `src/client/code-pages/*`. **Read-only**; no fixes. Acceptance: `audits/pcf-codepages-test-rot-2026-08-XX.md` exists with per-control disposition + r3 scope recommendation.

12. **FR-12**: Mutation testing pilot completed. Stryker.NET against `Services/Ai/Safety/*`. Acceptance: `baseline/mutation-testing-Services-Ai-Safety-2026-08-XX.md` exists with mutation score, top-10 weak assertions list, and recommendation on r3 expansion to `Services/Ai/Capabilities/*` + `Services/Ai/Chat/*`.

13. **FR-13**: TestClock + seeded Guid PoC working in `Services/Workspace/*` test surface. Pattern documented in `docs/procedures/testing-and-code-quality.md`. Acceptance: at least 1 `Workspace` test class using the pattern; pattern doc reviewed; r3 migration plan referenced.

14. **FR-14**: Coverlet baseline % per project published. Enable Coverlet output in `sdap-ci.yml` (minimal change). Capture baseline at r2 close. **No threshold enforcement.** Acceptance: r2 exit-ledger includes baseline coverage % for `Sprk.Bff.Api.Tests` and `Spe.Integration.Tests`.

### Validation + close (FR-15 through FR-16)

15. **FR-15**: Final triple-run validation. Both test projects, 3 consecutive runs each, all `Failed: 0`. Mirrors r1 task 084 pattern. Acceptance: 6 TRX artifacts; zero variance reported.

16. **FR-16**: Project closes by 2026-08-31 with PR + admin-merge cycle. Cumulative project state captured in `ledgers/exit-ledger.md`; lessons-learned authored. Acceptance: PR merged to master on or before 2026-08-31; final commit references this FR.

---

## Non-Functional Requirements

- **NFR-01 (r2)**: Production code changes ARE in scope. Tests modified ONLY for Skip → Pass transitions associated with closed ledger entries OR for the TestClock PoC (FR-13). No "while we're here" test repairs.
- **NFR-02**: Each production code change <50% line replacement per file. >50% escalates via `escalations/rewrite-request-T-XXX-{FileName}.md`.
- **NFR-03**: HIGH severity entries (FR-02) require security review approval recorded in the PR before merge.
- **NFR-04**: Every ledger entry closure commit cites the entry ID + resolution mode (`repaired` / `transferred-to-sibling` / `archived-as-dead-target`).
- **NFR-05**: Triple-run validation (FR-10 for integration, FR-15 for both) is mandatory before each named phase's exit gate.
- **NFR-06**: Each phase produces a delta artifact in `baseline/` so progress is reproducible.
- **NFR-07**: Anti-drift effectiveness report (FR-09) is published whether findings are favorable or not — no burying inconvenient data.
- **NFR-08**: Project CLAUDE.md is loaded by every task agent (predecessor pattern).
- **NFR-09**: Task POMLs declare `<repair-not-rewrite>true</repair-not-rewrite>` for test changes; `<production-fix-per-ledger>true</production-fix-per-ledger>` for production changes (NEW metadata field for r2).
- **NFR-10**: Per-file disjoint sets for parallel agent dispatch (predecessor pattern; 6-agent cap per wave).
- **NFR-11**: No test may end in `Failed` state at any phase exit (predecessor §4.3 pattern continues).

---

## Technical Constraints

### Applicable ADRs

- **ADR-001** (Minimal API + Workers) — endpoint patterns; binding when fixing endpoint registration in RB-T028 cluster
- **ADR-007** (SpeFileStore facade) — relevant if any LOW entry touches file operations
- **ADR-008** (endpoint filters) — authorization patterns
- **ADR-010** (DI minimalism, NFR-03 in BFF) — **DIRECTLY binding for RB-T028-03/04/05/06**: the conditional-registration root cause is an ADR-010 application question
- **ADR-013 refined** (AI extends BFF, 2026-05-20) — relevant for RB-T044-* (AI/Safety) and RB-T053-01 (CapabilityRouter)
- **ADR-018** (kill switches) — **DIRECTLY binding for RB-T028 cluster**: the conditional-registration pattern is partly an ADR-018 application question
- **ADR-028** (Spaarke Auth v2) — relevant if any entry touches auth (RB-T028-06 Auth integration is the candidate)
- **ADR-029** (BFF Publish Hygiene) — Coverlet enablement (FR-14) must preserve publish size baseline

### MUST Rules

- ✅ MUST cite ledger entry ID in production code change commits (NFR-04)
- ✅ MUST follow the test-update obligation per `.claude/constraints/bff-extensions.md` § F (codified by r1) when modifying production code
- ✅ MUST run triple-run validation before each phase exit (NFR-05)
- ✅ MUST get security review for HIGH severity entries (NFR-03)
- ❌ MUST NOT modify tests outside the resolved ledger entries' Skip → Pass scope or the TestClock PoC (NFR-01 — inverts r1)
- ❌ MUST NOT bypass `enforce_admins` outside the admin-merge window of a specific PR (NFR-03 pattern from r1)
- ❌ MUST NOT delete tests; archive via `*.cs.archived-YYYY-MM-DD` rename (predecessor NFR-06)
- ❌ MUST NOT add Coverlet thresholds to required-status-checks (defer to r3 per D-04)

### Existing Patterns

- Wave-based parallel dispatch per phase (6-agent cap; disjoint files)
- task-execute skill invocation for every task (mandatory per root CLAUDE.md)
- ledger lifecycle: `open` → `assigned-to-r2` → `in-progress` → `repaired`/`transferred`/`archived` → `closed`
- Phase-gap task numbering (001-090 with gaps; same as r1)
- Triple-run validation pattern (r1 task 084)
- Cluster-level real-bug classification (r1 task 028 pattern; one fix can close multiple entries)

---

## Success Criteria

1. [ ] All 20 ledger entries closed (FR-01) — Verify: ledger query
2. [ ] 5 HIGH closed in Phase 1 incl. RB-T044-01 with security review (FR-02) — Verify: PR records
3. [ ] 7 MED closed in Phase 2 (FR-03) — Verify: ledger
4. [ ] 8 LOW closed in Phase 3 (FR-04) — Verify: ledger
5. [ ] RB-T028-02 Insights HOLD resolved (FR-05) — Verify: D-07 decision record
6. [ ] priority-order.md sibling sign-offs populated (FR-06) — Verify: file diff
7. [ ] Spe.Integration.Tests triple-run validated; ≤2 flakes (FR-10) — Verify: 3 TRX
8. [ ] Anti-drift effectiveness report published (FR-09) — Verify: baseline doc
9. [ ] PCF/Code Pages audit document published with r3 recommendation (FR-11) — Verify: audit doc
10. [ ] Mutation testing pilot report published (FR-12) — Verify: baseline doc
11. [ ] TestClock PoC working in Services/Workspace (FR-13) — Verify: at least 1 test class
12. [ ] Coverlet baseline % published per project (FR-14) — Verify: exit-ledger
13. [ ] Final triple-run on both projects: Failed: 0 (FR-15) — Verify: 6 TRX
14. [ ] Project closed via PR merge by 2026-08-31 (FR-16) — Verify: merge commit date

---

## Dependencies

### Prerequisites

- r1 predecessor merged on master (DONE 2026-06-01 via PR #314)
- r1 `real-bug-ledger.md` accessible (in `projects/sdap-bff.api-test-suite-repair/ledgers/`)
- `enforce_admins: true` restored after PR #314 merge (DONE 2026-06-01)

### External Dependencies

- `github-actions-rationalization-r1` (PR #315 in flight) — coordinate any CI workflow changes through that project; FR-14 (Coverlet) waits for it
- `ai-spaarke-insights-engine-r1` (sibling project) — FR-05 dependent on owner response
- Owner availability for security review (NFR-03 for HIGH severity)
- Stryker.NET package availability (NuGet) — for FR-12

---

## Owner Clarifications

| Topic | Question | Owner answer | Impact |
|---|---|---|---|
| Sequencing | Run r2 first before other BFF-modifying projects? | "we should just run this full project and get it out of the way" (2026-06-01) | r2 Phase 1 leads; Phases 2-5 can run parallel under code-path discipline |
| Scope coverage | Include all near/mid/long-term items from the post-r1 synopsis? | "single project so that we can address these issues once and for all" (2026-06-01) | All 12 synopsis items in scope; long-term ones are pilot-grade in r2 (D-04) |
| Deadline | When? | "not drag it out until September 2026" (2026-06-01) | Hard deadline 2026-08-31 (D-01 / FR-16) |

---

## Assumptions

- **CI sequencing**: Assuming `github-actions-rationalization-r1` will land its Phase 0 + Phase 1 (CI workflow fixes) before r2 Phase 4 Track D begins. If not, FR-14 may need to defer Coverlet enable.
- **Insights sibling project state**: Assuming `ai-spaarke-insights-engine-r1` is still active and has a reachable owner. If not, RB-T028-02 takes the `archived-pending-sibling-engagement` path.
- **Mutation testing tooling**: Assuming Stryker.NET v3.x is compatible with the test project's csproj structure. If not, FR-12 may require version pinning or test-project restructure (out of scope).
- **Security reviewer identity**: Assuming the owner identifies the security reviewer at Phase 1 start; not pre-named here.
- **PCF audit access**: Assuming `src/client/pcf/` and `src/client/code-pages/` are readable. If client surfaces are in separate repos, FR-11 narrows to repo-local audit only.

---

## Unresolved Questions

- [ ] **Security reviewer named?** NFR-03 binding requires a security review for HIGH severity. Reviewer identity needed before Phase 1 starts. Blocks: Phase 1 merge gates.
- [ ] **`ai-spaarke-insights-engine-r1` owner contact** — needed for FR-05. Blocks: RB-T028-02 disposition path decision.
- [ ] **Phase 4 staffing** — 5 parallel tracks. Run as 5 sequential single-agent tasks (safe, slow) OR 2-3 parallel agents per track (fast, context-heavy). Owner decides based on coordination capacity. Blocks: Phase 4 dispatch model in `/project-pipeline`.
- [ ] **`github-actions-rationalization-r1` Phase 1 completion date** — affects whether FR-14 (Coverlet) ships in Phase 4 or slips to Phase 5. Blocks: Phase 4 Track D start date.
- [ ] **r3 commitment** — D-06 explicitly does NOT pre-commit r3. Is owner OK with that, or should r2 reserve r3 budget? Blocks: nothing in r2; affects roadmap discussions.

---

*AI-optimized specification. Original design: `design.md`. Predecessor: `projects/sdap-bff.api-test-suite-repair/`.*
