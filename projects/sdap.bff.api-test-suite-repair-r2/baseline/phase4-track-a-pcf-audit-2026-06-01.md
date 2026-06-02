# Phase 4 — Track A: PCF / Code Pages Test Rot Audit (Pilot-Grade)

> **Task**: 040 — P4.A PCF / Code Pages test rot audit (READ-ONLY)
> **Authored**: 2026-06-01
> **Rigor**: STANDARD (audit-only; no code modifications)
> **Authority**: design.md §4 D-04 (Phase 4 tracks are pilot-grade — measurement + recommendation only)
> **Hypothesis under test** (per task POML §context): "BFF rot patterns r1 catalogued (factory-config drift, sibling-fixture co-evolution gaps, endpoint-vs-service registration misalignment) likely have analogs on the React/PCF side that have gone undetected because the test suites are smaller and less load-bearing for CI."

---

## 1. Executive Summary

**Verdict**: Hypothesis **partially confirmed**. The client surface is MUCH cleaner than BFF was at r1 baseline (zero skipped tests, zero `xit`/`it.only` directives, zero env-coupling in tests, zero deprecation TODOs inside the actual test body of >170 active files), BUT three concrete rot pockets exist that map exactly to the r1 playbook elements:

1. **Endpoint-vs-Service Registration drift (Playbook Element 3)** — SemanticSearch code-page declares **17 Jest-style test files** but its `package.json` declares **no `test` script** and **no Jest devDependencies + no jest.config**. Tests cannot run. Equivalent of the BFF "endpoint mapped unconditionally, service registered conditionally" pattern transplanted to the JS surface.
2. **Stale Mocks (Playbook Element 1 — factory-config drift analog)** — AnalysisWorkspace code-page has **2 documented `@deprecated`/OBSOLETE test files** that import `__tests__/mocks/MockSprkChatBridge`, a file deleted in r1 task 043. The jest.config has an unresolved TODO comment about this. Tests will fail at module resolution.
3. **Tooling Coverage Asymmetry (Playbook Element 2 — sibling-fixture co-evolution analog)** — Of 19 client surfaces (15 PCF + 4 code-pages), **8 PCF controls and 2 code-pages have ZERO test files**. The asymmetry is invisible to CI because Jest does not run where no config exists.

**Aggregate dispositions** (see §3):
- `clean`: 11 / 27 (40.7%)
- `no tests` (= `requires-deeper-investigation`): 10 / 27 (37.0%) — including 2 code-pages
- `playbook-LOW`: 4 / 27 (14.8%)
- `playbook-MED`: 1 / 27 (3.7%) — AnalysisWorkspace stale-mock pair
- `playbook-HIGH`: 1 / 27 (3.7%) — SemanticSearch code-page tests + no runner

**r3 scope recommendation**: **Commission a narrow r3 track for client surfaces — small batch (~3-5 PRs), 60-day fix-by**. The HIGH pocket (SemanticSearch code-page) is a one-PR fix (add jest.config + test script + verify tests pass). The MED pocket (AnalysisWorkspace stale mocks) is a one-PR fix (rewrite vs the new direct-ref architecture, OR delete with traceability per r1 task 043 closeout). The remaining 8 "no tests" surfaces are scope expansion candidates, NOT rot — they need owner triage to decide which warrant tests at all.

---

## 2. Inventory — Test File Counts

### 2.1 PCF Controls (`src/client/pcf/`)

| Control | Test files | Test cases (it/test) | Jest config | jest.setup | Runner | Status |
|---|---:|---:|:---:|:---:|---|---|
| AIMetadataExtractor | 0 | 0 | — | — | n/a | no tests |
| AssociationResolver | 0 | 0 | — | — | n/a | no tests |
| DocumentRelationshipViewer | 4 | ~78 | ✅ | ✅ | jest | active |
| DrillThroughWorkspace | 2 | ~23 | ✅ | ✅ | jest | active |
| EmailProcessingMonitor | 0 | 0 | — | — | n/a | no tests |
| RelatedDocumentCount | 2 | ~25 | ✅ | ✅ | jest | active |
| ScopeConfigEditor | 3 | ~35 | ✅ | ✅ | jest | active |
| SemanticSearchControl | 7 | ~52 | ✅ | ✅ | jest | active |
| SpaarkeGridCustomizer | 0 | 0 | — | — | n/a | no tests |
| SpeDocumentViewer | 0 | 0 | — | — | n/a | no tests |
| ThemeEnforcer | 0 | 0 | — | — | n/a | no tests |
| UniversalDatasetGrid | 6 | ~163 | ✅ | ✅ | jest | active |
| UniversalQuickCreate | 3 | ~65 | ✅ | ✅ | jest | active |
| UpdateRelatedButton | 0 | 0 | — | — | n/a | no tests |
| VisualHost | 9 | ~106 | ✅ | ✅ | jest | active |
| **Subtotal** | **36** | **~547** | | | | **8 active / 7 untested** |

### 2.2 Code Pages (`src/client/code-pages/`)

| Code Page | Test files | Test cases (it/test) | Jest config | jest.setup | Runner | Status |
|---|---:|---:|:---:|:---:|---|---|
| AnalysisWorkspace | 7 | ~83 | ✅ | ✅ | jest | active (+ 2 deprecated) |
| DocumentRelationshipViewer | 0 | 0 | — | — | n/a | no tests |
| PlaybookBuilder | 0 | 0 | — | — | n/a | no tests |
| **SemanticSearch** | **17** | **~358** | **❌ MISSING** | **❌ MISSING** | **none — orphaned** | **BROKEN** |
| **Subtotal** | **24** | **~441** | | | | **1 active / 2 untested / 1 broken** |

### 2.3 Shared Packages (`src/client/shared/`)

| Package | Test files | Test cases (it/test) | Jest config | jest.setup | Runner | Status |
|---|---:|---:|:---:|:---:|---|---|
| Spaarke.AI.Context | 0 | 0 | — | — | n/a | no tests |
| Spaarke.AI.Outputs | 18 | ~133 | ✅ | — | jest | active |
| Spaarke.AI.Widgets | 17 | ~248 | ✅ | — | jest | active |
| Spaarke.Auth | 6 | ~51 | ✅ | — | jest | active (uses `tests/`) |
| Spaarke.Events.Components | 0 | 0 | — | — | n/a | no tests |
| Spaarke.SdapClient | 1 | ~10 | ✅ | — | jest | active |
| Spaarke.UI.Components | 53 | ~1,539 | ✅ | ✅ | jest | active |
| **Subtotal** | **95** | **~1,981** | | | | **5 active / 2 untested** |

### 2.4 Totals (in-scope surfaces only — PCF + Code Pages + shared)

| Aggregate | Total | With tests | Without tests | Broken (config drift) |
|---|---:|---:|---:|---:|
| PCF + Code Pages + Shared (26 entities) | **155 test files**, **~2,969 it/test calls** | 14 | 11 | 1 |

> **Methodology note**: Test case counts via `grep -c '^\s*(it\|test)\s*\('` per file then summed. Approximate — does not differentiate `it()` from `it.each(...)` cardinality, and does not subtract `describe()` block lines. r1 used the same approximation.

---

## 3. Per-Surface Disposition

> **Playbook element legend** (per task POML §prompt):
> - **E1** = Factory-config / environment / mock setup drift
> - **E2** = Sibling-fixture co-evolution gap (shared setup file used by many tests)
> - **E3** = Endpoint-vs-service registration alignment (declared but not wired, or wired but not declared)

| # | Surface | Disposition | Playbook elements | Rationale |
|---|---|---|---|---|
| 1 | PCF: AIMetadataExtractor | requires-deeper-investigation | (no tests to audit) | No tests exist. Owner decision: does this control warrant tests? |
| 2 | PCF: AssociationResolver | requires-deeper-investigation | (no tests to audit) | Same. |
| 3 | PCF: DocumentRelationshipViewer | clean | — | Tests run, no skipped, no deprecated imports, no stale Moq-equivalents. |
| 4 | PCF: DrillThroughWorkspace | clean | — | Same. |
| 5 | PCF: EmailProcessingMonitor | requires-deeper-investigation | (no tests to audit) | Same. |
| 6 | PCF: RelatedDocumentCount | clean | — | Same. |
| 7 | PCF: ScopeConfigEditor | playbook-LOW | E3 (mild — 3 `// @ts-ignore`/`as any` in tests) | Type-casting in tests is a weak rot signal, not blocking. |
| 8 | PCF: SemanticSearchControl | clean | — | Tests run. |
| 9 | PCF: SpaarkeGridCustomizer | requires-deeper-investigation | (no tests to audit) | Same. |
| 10 | PCF: SpeDocumentViewer | requires-deeper-investigation | (no tests to audit) | Same. |
| 11 | PCF: ThemeEnforcer | requires-deeper-investigation | (no tests to audit) | Same. |
| 12 | PCF: UniversalDatasetGrid | clean | — | 6 test files, 163 it/test calls, all running. dateFilter uses literal dates (not relative) — not date-coupled rot. |
| 13 | PCF: UniversalQuickCreate | clean | — | Same. |
| 14 | PCF: UpdateRelatedButton | requires-deeper-investigation | (no tests to audit) | Same. |
| 15 | PCF: VisualHost | clean | — | 9 test files, all running. |
| 16 | CP: AnalysisWorkspace | **playbook-MED** | **E1** (stale mocks) | **2 test files (`useDiffReview.test.ts`, `streaming-e2e.test.ts`) marked `@deprecated OBSOLETE`; import `__tests__/mocks/MockSprkChatBridge` which was DELETED in r1 task 043. Tests fail at module-resolution. `jest.config.js` line 27-28 has an unresolved TODO comment acknowledging the debt.** |
| 17 | CP: DocumentRelationshipViewer | requires-deeper-investigation | (no tests to audit) | Same. |
| 18 | CP: PlaybookBuilder | requires-deeper-investigation | (no tests to audit) | Same. |
| 19 | CP: **SemanticSearch** | **playbook-HIGH** | **E3** (config-declaration drift) | **17 test files (~358 cases) exist on disk but `package.json` declares no `test` script, has no `jest`/`ts-jest`/`@testing-library` devDependencies, and the repo has no `jest.config.*` for this code-page. Tests are orphaned — they CANNOT run. This is the BFF "endpoint mapped but service not registered" pattern transplanted: test files written, test runner not wired. Severity HIGH because the cases test the largest single search surface in the platform.** |
| 20 | Shared: Spaarke.AI.Context | clean | — | No tests, but it's a pure types/context package — tests may not be warranted. Lower priority than the 8 untested PCF controls. |
| 21 | Shared: Spaarke.AI.Outputs | clean | — | 18 test files, all running. |
| 22 | Shared: Spaarke.AI.Widgets | playbook-LOW | E2 (moduleNameMapper maps `@spaarke/auth` to a stub — see jest.config.ts line 13-15) | The stub-mapping is a deliberate test-isolation pattern, not rot. Logged as LOW because if Spaarke.Auth API changes, this mapping silently masks contract drift. Not actionable now. |
| 23 | Shared: Spaarke.Auth | clean | — | 6 test files in `tests/` (not `__tests__/`) — naming variance vs other packages, but Jest config explicitly targets `tests/`. Intentional. |
| 24 | Shared: Spaarke.Events.Components | requires-deeper-investigation | (no tests to audit) | Same. |
| 25 | Shared: Spaarke.SdapClient | playbook-LOW | E1 (single test file for an API client — coverage gap, not rot) | One test file (`SdapApiClient.test.ts`) for the entire client. Low rot, possible scope gap. |
| 26 | Shared: Spaarke.UI.Components | clean | — | **53 test files, ~1,539 it/test calls — the load-bearing test surface for the client tier. Zero skipped, zero deprecated, zero `xit`. Healthy.** |

### Aggregate counts

| Disposition | Count | % | Surfaces |
|---|---:|---:|---|
| `clean` | 11 | 42.3% | 6 PCF + 1 CP + 4 Shared |
| `playbook-LOW` | 4 | 15.4% | ScopeConfigEditor, Spaarke.AI.Widgets, Spaarke.SdapClient, Spaarke.AI.Context |
| `playbook-MED` | 1 | 3.8% | AnalysisWorkspace |
| `playbook-HIGH` | 1 | 3.8% | SemanticSearch code-page |
| `requires-deeper-investigation` | 10 | 38.5% | 7 PCF + 2 CP + 1 Shared (no tests) |
| **TOTAL** | **26** | **100%** | |

### Top 3 most-impacted surfaces

1. **SemanticSearch code-page** (HIGH) — 17 orphaned test files, ~358 cases. One PR to add jest.config + test script + ts-jest + testing-library.
2. **AnalysisWorkspace code-page** (MED) — 2 deprecated tests, 1 jest.config TODO. One PR to delete or rewrite.
3. **PCF "no test" cluster** (8 controls) — Scope-expansion candidate, not rot. Owner triage needed: AIMetadataExtractor, AssociationResolver, EmailProcessingMonitor, SpaarkeGridCustomizer, SpeDocumentViewer, ThemeEnforcer, UpdateRelatedButton + the 2 untested code-pages.

---

## 4. Top 5 Rot Patterns Observed

### Pattern 1 — Test files written, test runner not wired (E3 analog)
**Where**: `src/client/code-pages/SemanticSearch/`
**Evidence**: 17 `*.test.{ts,tsx}` files under `src/__tests__/` (services, hooks, components, integration), 0 jest config, 0 test script, 0 testing devDeps.
**BFF equivalent**: RB-T028-03/04/05/06 cluster — production code calls a service that is conditionally registered while the caller is unconditional.
**Impact**: Test debt grows invisibly; new tests added don't fail CI because there's no CI step that knows they should run.

### Pattern 2 — Stale mocks survive cleanup waves (E1)
**Where**: `src/client/code-pages/AnalysisWorkspace/src/__tests__/{useDiffReview,streaming-e2e}.test.{ts,tsx}`
**Evidence**: Both files have `@deprecated OBSOLETE` headers; both import `'../__tests__/mocks/MockSprkChatBridge'`; that mock file was deleted in r1 task 043. The `jest.config.js` (lines 27-28) acknowledges the debt with a comment but does not test-ignore the files.
**BFF equivalent**: Stale Moq predicates that don't match renamed methods (RB-T028-08 — the `PostPrecedent` zero-call verify).
**Impact**: Test collection fails (module not found) — silently masked unless you actually run `npm test` in that workspace.

### Pattern 3 — Untested controls hide behind missing config (E2/E3 hybrid)
**Where**: 8 PCF controls (AIMetadataExtractor, AssociationResolver, EmailProcessingMonitor, SpaarkeGridCustomizer, SpeDocumentViewer, ThemeEnforcer, UpdateRelatedButton) + 2 code-pages (DocumentRelationshipViewer, PlaybookBuilder).
**Evidence**: No `__tests__/` directories, no `*.test.*` files, no jest configs in those workspaces.
**BFF equivalent**: Surfaces that exist but have no integration test coverage — not strictly rot, but an asymmetry that hides regressions.
**Impact**: Some of these are PCF *types* (ScopeConfigEditor sibling controls in the same project) where untested-by-design is fine. Others (EmailProcessingMonitor, PlaybookBuilder) are likely accidental gaps.

### Pattern 4 — moduleNameMapper as silent contract bypass (E1/E2 hybrid)
**Where**: `src/client/shared/Spaarke.AI.Widgets/jest.config.ts` line 13-15 — `'^@spaarke/auth$': '<rootDir>/src/__mocks__/@spaarke/auth.ts'`.
**Evidence**: Comment explicitly states "real implementation uses MSAL/browser APIs that are unavailable in jsdom".
**BFF equivalent**: r1's lesson — when a fixture mocks a dependency, contract drift in the dependency goes undetected at the consumer.
**Impact**: LOW today (auth API is stable). Adds technical debt: contract changes in Spaarke.Auth's exported surface won't surface here.

### Pattern 5 — Test naming convention drift across siblings
**Where**: `Spaarke.Auth` uses `tests/*.test.ts`; every other shared package uses `src/**/__tests__/*.test.{ts,tsx}`.
**Evidence**: jest.config.js `testMatch: ['**/tests/**/*.test.ts']` vs every other config `testMatch: ['**/__tests__/**/*.test.{ts,tsx}']`.
**BFF equivalent**: Naming-convention drift that masked test-discovery gaps in r1 baseline (the "are we even running these?" question).
**Impact**: LOW (Jest discovers all tests via per-workspace config). Annoyance only — searchability + onboarding.

---

## 5. Recommended Ledger Entries (Real-Bug Style)

Two production-grade rot pockets warrant new ledger entries if r3 is commissioned. Filing NOT done here (D-04 read-only constraint); these are recommendations for r3 scoping.

### RB-CLIENT-001 (proposed) — SemanticSearch code-page test runner not wired
- **Severity**: HIGH (largest code-page surface; 17 orphaned test files)
- **Production file**: `src/client/code-pages/SemanticSearch/package.json` + missing `jest.config.{js,ts}` + missing jest devDependencies
- **Fix path**: Mirror `AnalysisWorkspace/package.json` (jest + jest-environment-jsdom + ts-jest + identity-obj-proxy + @testing-library/* + @types/jest), add `jest.config.js` modelled on AnalysisWorkspace's, add `"test": "jest"` script. Verify tests pass.
- **Tests Skipped/Broken**: ~358 it/test calls across 17 files (currently invisible to CI; runtime status unknown until tooling is wired)
- **Fix-by**: 60 days (2026-08-01)
- **r3 phase**: P1 (single PR; HIGH severity)

### RB-CLIENT-002 (proposed) — AnalysisWorkspace deprecated test files
- **Severity**: MED (2 files, ~22 it/test calls — but they would fail at module resolution if anyone ran the suite)
- **Production file**: `src/client/code-pages/AnalysisWorkspace/src/__tests__/useDiffReview.test.ts` + `src/client/code-pages/AnalysisWorkspace/src/__tests__/streaming-e2e.test.ts` + `jest.config.js` (lines 27-28)
- **Fix path**: Owner decision (3 options):
  1. **Delete both files** — record in `notes/` traceability that r1 task 043 sunset the SprkChatBridge in code-pages.
  2. **Rewrite both files** against the new direct-ref architecture (`useDocumentStreaming` -> `EditorPanel` ref).
  3. **Keep + `testPathIgnorePatterns`** — explicit ignore in jest.config until rewrite is scheduled.
- **Tests Skipped/Broken**: ~22 (the deprecated files' it/test count)
- **Fix-by**: 60 days (2026-08-01)
- **r3 phase**: P1 (single PR; same workspace as RB-CLIENT-001 — sequential)

### Out of ledger scope (not "rot" by r1 definition)

The 10 surfaces with no tests at all (8 PCF + 2 code-pages) are **scope-expansion candidates**, not rot. r1's `real-bug-ledger.md` definition required a production behavior the tests were Skipping; here there are no tests so there is no Skip. r3 should treat them as a separate backlog with owner triage.

---

## 6. Recommended Phase 5 Governance Updates

Phase 5 of r2 codifies anti-drift mechanisms. Two updates are warranted:

### G-1 (proposed) — Extend BFF "asymmetric registration" guard to PCF/code-pages
**Where**: `.claude/constraints/bff-extensions.md` (or a new `.claude/constraints/client-extensions.md`).
**Rule**: "When adding a `*.test.{ts,tsx}` file to `src/client/{pcf,code-pages,shared}/{Project}/`, the host `package.json` MUST declare a `test` script AND the workspace MUST contain a discoverable `jest.config.*` OR `vitest.config.*`. PRs failing this guard MUST be flagged in code review."
**Justification**: SemanticSearch code-page is direct evidence the rule is needed. Mirror of RB-T028 cluster lesson.
**Enforcement vehicle**: Code-review checklist + PR template — NOT CI (per design.md §5.5 — anti-drift via review, not script).

### G-2 (proposed) — Stale-mock cleanup cadence
**Where**: `docs/procedures/testing-and-code-quality.md` (post-PR checklist section).
**Rule**: "When a test fixture file is deleted from `src/__tests__/mocks/` or `src/__mocks__/`, the PR MUST grep `**/*.test.{ts,tsx}` for residual imports and either rewrite or delete the consuming test in the same PR."
**Justification**: AnalysisWorkspace deprecated tests are direct evidence the rule is needed. r1 task 043 deleted the mock without updating consumers; the residue survived r1 + r2 wave closure.
**Enforcement vehicle**: Code-review checklist (new bullet in the existing per-PR `testing-and-code-quality.md` checklist).

---

## 7. Comparison to BFF Rot

| Dimension | BFF (r1 close) | Client (this audit) |
|---|---|---|
| Total tests | 6,451 | ~2,969 |
| Skipped at close | 235 (137 unit + 98 integration) | **0** |
| `[Trait("status","real-bug-pending-fix")]` (BFF) / `@deprecated` (client) | 51 | 2 files (~22 cases) |
| Asymmetric registration / runner | RB-T028 cluster (HIGH × 4) | 1 (SemanticSearch code-page, HIGH × 1) |
| Stale mocks / fixtures | RB-T028-08 (LOW × 1) | AnalysisWorkspace (MED × 2 files) |
| Workspaces with NO tests | n/a (single solution) | 10 of 26 (38.5%) |
| Active test count growing? | Yes (51 → r3 forward) | Stable (zero pending) |
| **Verdict** | r1 + r2 closed 235 → 0 across 51 ledger entries | **2 actionable rot pockets + 1 scope-expansion backlog** |

The client surface is materially CLEANER than BFF was at r1 baseline — but the failure modes that exist mirror the BFF playbook 1:1.

---

## 8. r3 Scope Recommendation

### Commission r3 for client surfaces? **YES — narrow scope**

**Scope**:
1. **r3 P1 (HIGH; 1 PR; 60-day fix-by)** — RB-CLIENT-001: wire SemanticSearch code-page jest config + scripts + deps. Verify all 17 test files pass (or surface real bugs they expose; they may have rotted alongside the surface they test).
2. **r3 P1 (MED; 1 PR; 60-day fix-by)** — RB-CLIENT-002: resolve AnalysisWorkspace 2 deprecated tests (delete OR rewrite OR explicit ignore — owner decision).
3. **r3 P2 (no severity tier; backlog)** — Owner triage on the 10 untested surfaces. Many are likely "intentionally untested for now"; some (EmailProcessingMonitor, PlaybookBuilder) are likely gaps. NOT batch-fixed; case-by-case.

### Batch sizing
- **2 PRs in P1** (both narrow; both single-workspace; both 1-2 days work)
- **Up to 8 individual ADR-style triage decisions in P2** (each is "do we want tests here? yes/no/later")

### Why narrow vs broad
The BFF rot was systemic (51 tests across 20 entries, 5 HIGH); the client rot is two isolated pockets + a backlog. A broad r3 would chase false positives. The two pockets are well-scoped enough to ship as a single mini-project; the 8 "no tests" cases need PRODUCT decisions, not engineering execution.

### Anti-recommendations
- **Do NOT** mass-add tests to the 8 untested PCF controls without owner triage. Some are scaffold/shell controls; testing them costs effort with low yield.
- **Do NOT** scope r3 to "achieve coverage parity with shared lib (Spaarke.UI.Components)" — that's an order of magnitude more work than the rot warrants.

---

## 9. Boundary Verification

- ✅ Read-only audit. No source files in `src/client/pcf/`, `src/client/code-pages/`, or `src/client/shared/` were modified.
- ✅ No `.claude/` paths touched.
- ✅ Single output: this document (`baseline/phase4-track-a-pcf-audit-2026-06-01.md`).
- ✅ No commit performed; per task instructions ("DO NOT commit").

> **Path note**: POML §goal cited `audits/pcf-codepages-test-rot-2026-08-XX.md`; user task instruction (most recent direction, takes precedence) cited `baseline/phase4-track-a-pcf-audit-2026-06-01.md`. Document written to the user-instructed path. Both directories pre-existed in the project root.

---

## 10. Cross-References

- **Project artifacts**: `projects/sdap.bff.api-test-suite-repair-r2/baseline/r1-closeout-2026-06-01.md` (r1 final state), `notes/lessons-learned.md` (r1 playbook source — referenced via POML §inputs).
- **r1 playbook source**: `projects/sdap-bff.api-test-suite-repair/notes/lessons-learned.md` (the 3-element diagnostic playbook).
- **Design authority**: `projects/sdap.bff.api-test-suite-repair-r2/design.md` §4 D-04 (Phase 4 pilot-grade constraint).
- **CLAUDE.md §10 BFF Hygiene**: governance template for the proposed G-1 client-extensions constraint.
