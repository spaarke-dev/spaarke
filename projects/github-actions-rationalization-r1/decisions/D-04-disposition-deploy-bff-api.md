# D-04 — deploy-bff-api.yml Disposition

> **Date**: 2026-06-01
> **Project**: github-actions-rationalization-r1
> **Phase**: 2 (Rationalization) — task 020
> **Related**: spec FR-06 (≤8 workflow target), NFR-04 (`git rm` for deletes / no comment-out), NFR-06 (per-decision record), design.md D-03 (delete-by-default) / D-04 (consolidate liberally), ADR-029 (BFF Publish Hygiene)
> **Recommendation**: **KEEP** (and absorb deploy-slot-swap.yml functionality into it — see D-06)

## Context

`deploy-bff-api.yml` is the canonical Spaarke BFF API deployment pipeline. It triggers on push to `master` for paths `src/server/api/**` and on manual `workflow_dispatch` (env: `dev` | `production`). The workflow runs a 7-job pipeline: Build → Test → Deploy to Staging Slot → Verify Staging Health → Swap Staging → Production (with `environment: production` reviewer gate) → Verify Production → Rollback (`if: failure()` swap-back). It uses OIDC Azure login, a concurrency group `deploy-bff-api-${env}` (no cancel-in-progress), and targets `spaarke-bff-prod` app service. The `/healthz` and `/ping` endpoints are checked at both staging and production stages.

## Evidence

**Wave A inventory data**:
- Last-30-day runs: 58 total / 0 success / 58 failure → **0% success**
- Triggers: `push` (paths `src/server/api/**`) + `workflow_dispatch`
- Pattern observed: NOT loader-failure (0/5 sampled empty). The workflow actually executes; failures are at the `Test API → Run unit tests` step (`dotnet test tests/unit/Sprk.Bff.Api.Tests/`) — the same test-suite drift as Risk R1 / D-01.
- Last meaningful commit: `36f10712` 2026-05-31 (spaarke-dev) — "feat(tests): Phase 1 Wave 1.1b complete + CI gate restored"

**Recent contributor check**: `git log --since='90 days ago' -- .github/workflows/deploy-bff-api.yml` shows 2 commits:
- `36f10712` 2026-05-31 spaarke-dev — Phase 1 Wave 1.1b
- `8fa46c60` 2026-03-13 spaarke-dev — original creation

**Value assessment**: This is THE canonical BFF deployment workflow. ADR-029 (BFF Publish Hygiene) governs its behavior. It implements zero-downtime slot-swap deployment with full rollback semantics — the safest path to production. The 0% success rate is a SYMPTOM of the test-suite regression (D-01 scope, deferred per NFR-01), not a defect in the workflow itself. Once the test suite is repaired in a follow-up project, this workflow is expected to be fully functional. There is also significant logical overlap with `deploy-slot-swap.yml` (see D-06).

## Recommendation

**KEEP** as the canonical deployment workflow. Treat `deploy-slot-swap.yml` as redundant logic that should be consolidated INTO `deploy-bff-api.yml` (see D-06 for the consolidation merge plan). The 0% recent success rate is a downstream symptom of the test-suite regression tracked in D-01 (NFR-01 puts the fix out-of-scope for this project); the workflow itself is well-structured and ADR-029-compliant.

Per design.md D-04 (consolidate liberally), the two BFF-deploy workflows MUST not coexist. `deploy-bff-api.yml` is selected as the surviving artifact because it (a) has explicit rollback on `if: failure()` with `verify-production` dependency, (b) is triggered automatically on `master` pushes (the canonical deploy trigger), and (c) is the file most likely to receive future ADR-029 hash-verify enhancements (slot-swap is a deployment STRATEGY, not a separate workflow concern). The multi-env (`dev`/`staging`/`prod`) `resolve-config` pattern from `deploy-slot-swap.yml` is the one piece worth absorbing — see D-06 execution plan.

## Execution plan (for task 021 or 022)

**Task 021 (deploy-* dispositions)**: NO `git rm` for this file. Action items in priority order:
1. KEEP `.github/workflows/deploy-bff-api.yml` as-is in this Phase. Do not edit during this project (NFR-02 <50% replacement — and there is no compelling functional change to make).
2. The consolidation merge from `deploy-slot-swap.yml` is described in **D-06**. Task 021 SHOULD NOT merge `deploy-slot-swap.yml` logic in this phase unless there is a clear value-additive change (the existing rollback semantics already satisfy ADR-029).
3. Defer absorption of the multi-env `resolve-config` job to a follow-up project once the test suite is repaired and there is a real multi-env need.
4. The 0% success rate disposition is owned by D-01 (master CI failure deferred per NFR-01); no action in this project beyond preserving the workflow.

## Owner sign-off note

Recent contributors (last 90 days): spaarke-dev (`36f10712` 2026-05-31, `8fa46c60` 2026-03-13). The same `spaarke-dev` author touched both `deploy-bff-api.yml` and the related `deploy-slot-swap.yml`. No external contributor objections expected — the consolidation toward this workflow is consistent with the author's most recent (2026-05-31) Phase 1 / CI-gate-restoration intent. No "speak now" trigger because (a) KEEP is the recommendation and (b) the related consolidation (D-06) affects deploy-slot-swap, not this file.
