# Code Quality and Assurance R3 — Design Document

> **Status**: Draft
> **Created**: 2026-03-15
> **Predecessor**: code-quality-and-assurance-r2 (feature/code-quality-and-assurance-r2)

---

## Current State Assessment

After R1 and R2, the Spaarke repository sits at **A (95/100)** quality grade. R1 established tooling (Prettier, ESLint, pre-commit hooks, CI gates) and R2 addressed structural issues (God classes, memory leaks, interface bloat, dead code, ADR violations, deprecated PCF controls).

### What's Already Done

| Area | R1 Achievement | R2 Achievement |
|------|---------------|----------------|
| Tooling | Prettier, ESLint, pre-commit hooks | — |
| CI | dotnet build/test gates | 4,176 tests passing |
| Backend | Program.cs decomposition | OfficeService, AnalysisOrchestration, IDataverseService decomposed |
| Frontend | TODO resolution, formatting | AnalysisWorkspaceApp decomposed (7 hooks), 10 deprecated controls deleted |
| Architecture | ADR documentation | ADR-022 fixes (3 controls), BaseProxyPlugin assessed |
| Memory | — | 3 unbounded dictionaries fixed, HttpClient fixed |

### What Remains (5 Points to A+)

The remaining 5 points come from items that were out of R2 scope or only partially addressed:

1. **OfficeService.cs still 1,951 lines** (target was <500) — needs deeper API redesign
2. **3 ADR-022 violations remain** — ReactControl-based controls (different fix pattern)
3. **117 console.log statements** — no shared logger adopted across PCF controls
4. **181 ESLint warnings** — pre-existing, primarily `no-unused-vars` and `no-explicit-any`
5. **138 integration test failures** — pre-existing, require Azure Key Vault configuration
6. **@spaarke/auth not a proper workspace package** — causes build resolution errors
7. **PCF build infrastructure fragile** — Node.js memory limits, no CI for individual control builds

---

## Problem Statement

R2 achieved the major structural wins. R3 targets the "long tail" of quality issues — items that individually seem minor but collectively prevent reaching A+ (98+/100). These fall into three categories:

1. **Completion of partial R2 work** — OfficeService decomposition, remaining ADR-022 fixes
2. **Developer experience improvements** — ESLint warnings, console.log cleanup, build infrastructure
3. **Test infrastructure** — integration test reliability, coverage gaps

---

## Recommended Work Items

### High Priority — Production & Architecture Impact

#### 1. Complete OfficeService.cs Decomposition

**Current**: 1,951 lines with 6 remaining responsibility groups.
**Target**: ~400-500 lines (orchestrator core only).

Extract:
- `OfficeSearchService` — entity and document search methods
- `OfficeShareService` — share link creation and invitations
- `OfficeRecentService` — recent documents
- `OfficeJobStatusService` — SSE streaming for job status

**Why**: The largest remaining God class. Merge conflicts in this file are the #1 developer friction point for Office-related work.

**Effort**: ~6-8 hours
**Impact**: +1 point (completes the partial R2 criterion)

#### 2. Fix Remaining ADR-022 Violations (ReactControl Pattern)

**Current**: 3 controls still import `react-dom/client`: AnalysisBuilder, AnalysisWorkspace, UniversalQuickCreate.
**Target**: 0 violations.

These controls use `ReactControl` (platform-managed rendering), not `StandardControl` (manual DOM). The fix is different — need to understand the ReactControl lifecycle where the platform calls `updateView()` and the control returns a `React.ReactElement`.

**Why**: ADR compliance. While functionally working, these violations create confusion about which React API to use.

**Effort**: ~3-4 hours
**Impact**: +0.5 points

#### 3. Create @spaarke/auth Workspace Package

**Current**: `@spaarke/auth` is a local file reference (`file:../../shared/Spaarke.Auth`) without a built `dist/` directory. PCF controls that import it fail until manually built.
**Target**: Proper workspace package with build automation.

- Add to npm workspace configuration
- Ensure `dist/` is built as part of root build process
- Resolve pre-existing module resolution errors in SpeFileViewer, SpeDocumentViewer (now deleted, but pattern needed for future controls)

**Why**: Build reliability. Every developer who clones the repo hits this issue.

**Effort**: ~2-3 hours
**Impact**: +0.5 points

#### 4. Integration Test Infrastructure

**Current**: 138 integration tests fail because they require Azure Key Vault credentials not available in CI.
**Target**: Either fix test infrastructure or clearly separate integration tests from unit tests.

Options:
- A: Configure Key Vault access in CI pipeline (preferred)
- B: Mark integration tests with `[Category("Integration")]` and exclude from default `dotnet test`
- C: Create test doubles for Key Vault dependencies

**Why**: 138 failing tests mask real regressions. Developers ignore test results.

**Effort**: ~4-6 hours (Option A) or ~2-3 hours (Option B)
**Impact**: +1 point

### Medium Priority — Developer Velocity

#### 5. ESLint Warning Reduction Campaign

**Current**: 181 pre-existing warnings across 16 PCF controls.
**Breakdown**: Primarily `no-unused-vars` (~60%), `no-explicit-any` (~25%), other (~15%).

Approach:
- Phase A: Auto-fix `no-unused-vars` (prefix unused params with `_`, remove unused imports)
- Phase B: Replace `any` with proper types where straightforward
- Phase C: Enable `--max-warnings 0` in CI to prevent regression

**Why**: Warnings mask real issues. Clean ESLint output makes new errors immediately visible.

**Effort**: ~4-5 hours
**Impact**: +0.5 points

#### 6. Console.log Cleanup → Shared Logger

**Current**: 117 console.log/warn/error statements across 13 active PCF controls.
**Target**: 0 raw console calls; all logging via shared utility.

Approach:
- Create `createLogger(prefix)` utility in `@spaarke/ui-components` (based on existing AnalysisWorkspace `logger.ts` pattern)
- Migrate top-10 worst offender files
- Add ESLint rule `no-console` set to `warn` initially, then `error`

**Why**: Raw console.log pollutes browser console in production. Structured logging enables filtering and severity levels.

**Effort**: ~3-4 hours
**Impact**: +0.5 points

#### 7. PCF Build Infrastructure

**Current**: Root `npm run build` in `src/client/pcf/` hits Node.js memory limits and only produces bundles for some controls. Individual builds work but require manual dependency setup.

Approach:
- Create `build-all.sh` script that builds shared libs first, then each control individually
- Add `--max-old-space-size=8192` to webpack invocation
- Document the build dependency chain in PCF-DEPLOYMENT-GUIDE.md (partially done in R2)

**Why**: Build reliability for CI/CD and developer workflow.

**Effort**: ~3-4 hours
**Impact**: +0.5 points

#### 8. God Class Audit — Identify Remaining Candidates

**Current**: After R2 decomposition, need to verify no other services exceed thresholds.

Approach:
- Run line-count audit across all `.cs` files in `src/server/`
- Flag any service > 800 lines or > 12 constructor dependencies
- Prioritize based on change frequency (git log)

**Why**: Proactive quality maintenance. Prevents new God classes from forming.

**Effort**: ~2 hours (audit only, fixes are separate)
**Impact**: Informational (feeds future R4 if needed)

### Lower Priority — Architecture Polish

#### 9. BaseProxyPlugin Full Inversion

**Current**: Marked `[Obsolete]` in R2 with ADR-002 violations documented. Plugin makes HTTP calls, uses `Thread.Sleep`, 5+ Dataverse round-trips per execution.
**Target**: Either fully invert to comply with ADR-002 or formally decommission.

**Why**: Only relevant if the plugin is reactivated for production use.

**Effort**: ~8-10 hours (full inversion) or ~1 hour (formal decommission)
**Impact**: +0.5 points if decommissioned, +1 point if properly inverted

#### 10. Two Parallel Dataverse Implementations Assessment

**Current**: Both `DataverseServiceClientImpl` (SDK-based) and `DataverseWebApiService` (Web API-based) exist and implement `IDataverseService`.
**Target**: Document the strategy — is the intent to migrate to one? Or maintain both?

**Why**: Having two implementations of a 63-method interface is unusual. If intentional, document why. If accidental, plan migration.

**Effort**: ~2-3 hours (assessment and documentation)
**Impact**: Informational

#### 11. Unit Test Coverage Improvement

**Current**: 4,176 tests pass but coverage is uneven — some critical services have minimal test coverage.
**Target**: Identify coverage gaps in R2-decomposed services.

**Why**: The new focused services (OfficeEmailEnricher, AnalysisDocumentLoader, etc.) were extracted from tested code but may have gaps in their individual test coverage.

**Effort**: ~6-8 hours
**Impact**: +0.5 points

#### 12. Bundle Size Optimization

**Current**: PCF bundles range from 145 KB to 13.2 MB. Some controls bundle entire libraries that could be shared.
**Target**: Assess tree-shaking opportunities and shared bundle strategies.

**Note**: Blocked by `pcf-scripts` tooling — the PCF build system doesn't support tree-shaking. This may need to wait for Microsoft tooling updates.

**Why**: Smaller bundles = faster form loads. But limited by current tooling.

**Effort**: ~4-6 hours (assessment) + unknown (implementation, depends on tooling)
**Impact**: Performance improvement, no grade impact

---

## Proposed Phasing

| Phase | Items | Estimated Hours | Grade Impact |
|-------|-------|-----------------|-------------|
| Phase 1: Complete R2 | #1 (OfficeService), #2 (ADR-022) | ~10h | +1.5 points |
| Phase 2: Developer Experience | #5 (ESLint), #6 (Console.log), #3 (@spaarke/auth) | ~10h | +1.5 points |
| Phase 3: Test Infrastructure | #4 (Integration tests), #8 (God class audit) | ~8h | +1 point |
| Phase 4: Polish | #7 (PCF build), #9-12 (assessments) | ~10h | +1 point |
| **Total** | **12 items** | **~38h** | **A+ (99-100/100)** |

---

## Success Criteria

| # | Metric | Current | Target |
|---|--------|---------|--------|
| 1 | OfficeService.cs lines | 1,951 | <500 |
| 2 | ADR-022 violations | 3 | 0 |
| 3 | Raw console.log in PCF controls | 117 | 0 |
| 4 | ESLint warnings | 181 | 0 (with --max-warnings CI gate) |
| 5 | Integration test failures | 138 | 0 (either fixed or properly categorized) |
| 6 | @spaarke/auth build failures | Manual workaround needed | Zero-config builds |
| 7 | Overall quality grade | A (95/100) | A+ (98+/100) |

---

## Out of Scope

| Item | Why Excluded |
|------|-------------|
| PowerShell script remediation | Scripts are well-structured. PSScriptAnalyzer warnings are cosmetic. |
| Accessibility audit | Fluent UI v9 handles most ARIA. Not a code quality issue. |
| ConfigureAwait(false) | ASP.NET Core has no sync context. Not needed. |
| Base64 triple allocation | Graph SDK forces buffering. Can't control. |
| N+1 Dataverse patterns | Intentional throttling. Revisit when batching API available. |
| Full Dataverse implementation consolidation | Architectural decision, not quality issue. Separate project. |
| PCF bundle tree-shaking | Blocked by pcf-scripts tooling limitations. |

---

## Dependencies

- R2 PR must be merged before R3 branch is created
- Integration test fix (item #4) requires Azure DevOps pipeline access
- @spaarke/auth workspace (item #3) may affect other in-progress PCF work

---

*Design document for R3 quality improvement sprint. Transform to spec.md via `/design-to-spec` when ready to execute.*
