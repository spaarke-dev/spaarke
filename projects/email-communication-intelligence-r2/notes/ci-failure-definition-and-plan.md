# CI Failure — Definition & Remediation Plan

> **Author**: email-communication-intelligence-r2 (during worktree-sync + deploy prep, 2026-08-12)
> **Trigger**: operator asked "how do we get these defined and a plan to address them (not just keep them hanging out there)"
> **Scope**: the red **"SDAP CI"** workflow on master HEAD `3107c679`. NONE of these are email-intelligence-r2 code.

---

## TL;DR

There are **two** CI systems on this repo:

| Workflow | File | Status on master HEAD | Role |
|---|---|---|---|
| **CI** (Tier 1 blocking + Tier 2 advisory) | `ci-router.yml` → `ci-tier1-blocking.yml` / `ci-tier2-advisory.yml` | ✅ **GREEN** | The **new, real gate** (from `github-actions-rationalization-r1`). Changed-surface-scoped. |
| **SDAP CI** | `sdap-ci.yml` | ❌ RED (225 "deterministic failures") | **Legacy**, already made `continue-on-error` / informational (PR #449). Being retired. |

**There is no branch protection on this repo** (`repos/.../branches/master/protection` → 404). So **SDAP CI red blocks nothing** — that is exactly why external-access-r2 (#761) and assistant-r3 merged over it. The green Tier-1 CI is the gate that matters.

**The deploy is NOT blocked** by any of this: the real gate (Tier 1) is green, `deploy-*.yml` workflows are independent of SDAP CI, my BFF builds clean (0 err), my ingest tests pass 15/15, publish size 47.11 MB (< 60 MB ceiling).

The 225 "failures" are **not one bug** — they are **4 distinct classes with 3 different owners**:

---

## The four failure classes

### Class A — Compose corpus tests (~200 of the 225) — CI-CONFIG bug, NOT a product bug
- **Symptom**: `ComposeCitationResolverSeamTests`, `ComposeAlignmentApplierSeamTests`, `ComposeHeadingListApplierSeamTests`, `ComposeNumberingCanonicalModelSeamTests`, `ComposeReadFidelityHarnessSeamTests`, `ComposeRevisionReconciliationSeamTests`, `Nfr09RealTemplateHardeningTests`, etc. — all corpus/golden-`.docx`-driven.
- **Root cause**: `sdap-ci.yml` **`build-test` job** checkout (line ~101) uses `fetch-depth: 0` but **no `lfs: true`**. The `.docx` corpus (`tests/fixtures/compose-corpus/*.docx`) is **git-LFS-tracked** (`git check-attr filter` → `filter: lfs`). Without `lfs: true` the fixtures check out as ~130-byte pointer files → `ComposeCorpusFixtureLocator`'s PK-signature guard fails loudly → every corpus test throws.
- **Proof it is environmental, not a product defect**:
  1. **Pass locally**: `dotnet test …Seam.Compose…` → **85/85 green** (LFS materialized on my Windows checkout).
  2. **Same commit, sibling job passes**: in the *same* failing run, **"Compose Fidelity Gate (Corpus Round-Trip)" = SUCCESS** — and that job (line ~755) is the *only* one with `lfs: true`. Same tests, LFS present → pass; LFS absent → fail.
  3. New Tier-1 CI is green (it's changed-surface-scoped; doesn't run the full corpus suite unless compose changed).
- **Fix**: add `lfs: true` to the `build-test` job's `actions/checkout` in `sdap-ci.yml` (**one line**), mirroring the `compose-fidelity-gate` job. The in-file comment even says the fidelity-gate used "the same mechanism as PR #690's build-test fix" — the build-test job currently lacks it (never applied, or dropped).
- **Owner**: `github-actions-rationalization-r1` (owns ci-workflows hygiene; `.github/workflows/**` is their hot-path).
- **Blocks**: nothing (SDAP CI is informational).

### Class B — `ExternalParticipationServiceInvalidationTests` (3) — STALE TEST (corrected 2026-08-12 after deeper investigation)
- **Symptom (failed locally AND in CI, 3/4)**: `RemoveAsync(..., 2, ...)` "0 times" + stale `{1,2,3}` survives.
- **Corrected root cause**: **cache-version drift in the TESTS, not a product bug.** Production `ExternalParticipationService.CacheVersion = 3` (bumped 2→3 by task 073 #7 to reflect the widened org-grant shape, deliberately orphaning pre-org-grant v2 entries). Reads/writes/invalidation are all internally consistent at v3. The tests re-declared a hardcoded `CacheVersion = 2` (+ `ExternalAccessResource` string) instead of referencing the production public const, so the v3 removal never matched the v2 key. The production const's own comment mandates consumers reference the shared const so a bump auto-propagates — the tests were exactly the drift it warns against.
- **Fix applied (tests only; production is CORRECT and untouched)**: `ExternalParticipationServiceInvalidationTests.cs` (lines 26–33) + `StandingGrantRuntimeUnionSeamTests.cs` (lines 36–38) now reference `ExternalParticipationService.ExternalAccessResource` / `.CacheVersion` (drift-proof). Result: invalidation filter **4/4**, whole `~ExternalAccess` namespace **240/240**.
- **Owner**: fixed here (test hygiene), not a product defect. Reverting production 3→2 would have been a correctness regression.

### Class C — `SemanticSearchControl` ESLint (5 errors) — static lint debt
- **Symptom** (reproduces everywhere; `Client Quality (Prettier + ESLint)` job): `@typescript-eslint/consistent-type-definitions` "Use an `interface` instead of a `type`" ×3, `react/no-children-prop` "Do not pass children as props" ×2. (+104 warnings, non-blocking.)
- **Root cause**: lint debt in `src/client/pcf/SemanticSearchControl/**`. 3 of 5 are `--fix`-able.
- **Owner**: SemanticSearchControl owner.

### Class D — `ReAnalysisFlowTests` (Spe.Integration.Tests) — env-gated integration
- `tests/integration/Spe.Integration.Tests/Api/Ai/ReAnalysisFlowTests.cs` — needs SPE/Graph emulator or live creds; env-conditional, not a unit regression. Lowest priority; confirm it's meant to be skipped without the emulator.

---

## Why they've been "hanging out there"

`github-actions-rationalization-r1`'s own design.md states it plainly: *"sdap-ci.yml had a bug that silently broke the CI workflow for weeks. Nobody noticed because failures had become noise. When CI/CD fails consistently, it gets ignored. When it gets ignored, real regressions hide in the noise."* That project is the standing owner of this cleanup. Class A is literally an instance of the noise it exists to kill; Class B is the exact "real regression hiding in the noise" the thesis warns about.

---

## The plan (routed to owners; nothing silently cross-committed from this worktree)

| # | Action | Owner | Who does it |
|---|---|---|---|
| 1 | **Class A**: add `lfs: true` to `sdap-ci.yml` `build-test` checkout (one line). Kills ~200 red. | `github-actions-rationalization-r1` (ci-workflows hot-path) | Hand them the exact patch / file issue. **Do NOT edit `.github/workflows/**` from email-intelligence-r2** (would clobber their in-flight work — the "don't clobber" boundary). |
| 2 | **Class B**: fix cache-invalidation so `RemoveAsync`/version-bump fires; make the 3 tests green. | `SPA-external-access-platform-r2` | File issue against their project with the two assertion messages. Real defect — their fix. |
| 3 | **Class C**: `npm run lint -- --fix` for the 3 auto-fixable + hand-fix `react/no-children-prop` ×2 in SemanticSearchControl. | SemanticSearchControl owner | File issue; trivial, ~15 min. |
| 4 | **Class D**: confirm `ReAnalysisFlowTests` is emulator-gated (skip when no SPE emulator); no product action if so. | AI platform / rationalization | Note in rationalization backlog. |
| 5 | **Meta**: decide SDAP CI's fate — fix-then-keep vs retire-in-favor-of-Tier1. `github-actions-rationalization-r1` already owns this decision. | rationalization | Their call; this doc feeds it. |

**Deploy decoupling**: none of 1–5 blocks the R2 deploy (BFF → 059 → 044). The real gate (Tier-1 CI) is green; deploy workflows are independent. Recommend proceeding with the deploy in parallel with routing these to owners.

---

## Evidence appendix (commands)
- No branch protection: `gh api repos/spaarke-dev/spaarke/branches/master/protection` → 404 "Branch protection has been disabled".
- LFS-tracked fixtures: `git check-attr filter -- "tests/**/*.docx"` → `filter: lfs`.
- Local pass: `dotnet test …ComposeCitationResolverSeamTests|…Alignment|…HeadingList|…RevisionReconciliation` → 85/85.
- Sibling job proof: run `31661103915` — `Compose Fidelity Gate (Corpus Round-Trip)` = success (has `lfs: true`), `Build & Test (Debug)` = failure (no `lfs: true`).
- Class B local repro: `dotnet test …ExternalParticipationServiceInvalidationTests` → 3 failed / 1 passed.
- Classifier verdict: run `31661103915`, "Classify pass-1 failures" step → `summary=225 deterministic failure(s); 1 retry-eligible — failing build`.
- SDAP CI is informational: commit `64b40a107 ci(hotfix): make all sdap-ci.yml jobs continue-on-error (informational only) (#449)`.
