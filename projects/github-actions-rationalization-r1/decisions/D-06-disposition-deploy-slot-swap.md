# D-06 — deploy-slot-swap.yml Disposition

> **Date**: 2026-06-01
> **Project**: github-actions-rationalization-r1
> **Phase**: 2 (Rationalization) — task 020
> **Related**: spec FR-06 (≤8 workflow target), NFR-04 (`git rm` for deletes), NFR-06 (per-decision record), design.md D-04 (consolidate liberally), ADR-029 (BFF Publish Hygiene), D-04 (deploy-bff-api KEEP — consolidation target)
> **Recommendation**: **CONSOLIDATE → deploy-bff-api.yml** (then DELETE this file)

## Context

`deploy-slot-swap.yml` is a 365-line BFF deployment pipeline that uses the Azure App Service staging-slot pattern. Its 6 jobs are: Build & Test → Resolve Configuration → Deploy to Staging Slot → Health Check → Swap to Production → Pipeline Summary. It is triggered by (a) `workflow_dispatch` with `environment: dev | staging | prod` + `skip_tests: bool` inputs, and (b) `workflow_run` on completion of "SDAP CI" on master. Unlike `deploy-bff-api.yml`, it has a multi-env `resolve-config` step that picks `app_service_name` and `resource_group` from secrets based on the env input, and uses GitHub `environment: production` as the manual-approval gate (no explicit rollback job — relies on operator re-running the swap to reverse).

## Evidence

**Wave A inventory data**:
- Last-30-day runs: 8 total / 6 success / 2 failure → **75.0% success (misleading)**
- Triggers: `workflow_dispatch` + `workflow_run` (on SDAP CI completion on master)
- Pattern observed: the 75% success rate is **artificially inflated** by the `workflow_run` no-op pattern. When triggered by `workflow_run` and the upstream `sdap-ci.yml` failed (most pushes recently per D-01), the build job's `if: github.event.workflow_run.conclusion == 'success'` guard skips, ALL downstream jobs skip (`needs: build` chain), only the always-running `Pipeline Summary` job runs, and conclusion = "success" (skipped jobs don't fail). Inventory inspection of run 26755201586 (2026-06-01) confirmed all 5 real jobs were `skipped`; only Pipeline Summary ran. The workflow is effectively a no-op on every `workflow_run` trigger today.
- Last meaningful commit: `0bbebe73` 2026-05-26 (Spaarke Dev) — "sdap-bff-api-remediation: Phase 4 + 5 (demo) + Phase 6 docs + LegalWorkspace client /api fix (#295)"

**Recent contributor check (live)**: `git log --since='90 days ago' -- .github/workflows/deploy-slot-swap.yml` shows 1 commit:
- `0bbebe73` 2026-05-26 Spaarke Dev — sdap-bff-api-remediation Phase 4+5+6 (#295)

**Most recent runs (live `gh run list --limit 10`)**: 8 runs total since 2026-03-14; 2 successful runs on 2026-06-01 (12:34Z, 13:23Z, both `workflow_run` triggered — likely the no-op pattern from skipped upstream). Previous activity clustered 2026-03-14 (5 runs, 3 success / 2 failure shortly after creation), then dormant for ~2.5 months.

**Value assessment**: The workflow has high logical overlap with `deploy-bff-api.yml`:

| Capability | deploy-bff-api.yml | deploy-slot-swap.yml |
|---|---|---|
| Build + test | ✅ (separate jobs) | ✅ (combined job) |
| Deploy to staging slot | ✅ | ✅ |
| Health check (`/healthz` + `/ping`) | ✅ | ✅ |
| Slot swap to production | ✅ | ✅ |
| Production health verify | ✅ | ✅ |
| Rollback on failure | ✅ (explicit `rollback` job with `if: failure()`) | ❌ (operator re-runs swap manually) |
| Multi-env resolve-config | ❌ (hardcoded `spaarke-bff-prod`) | ✅ (resolves from secrets per env) |
| Auto trigger on master push | ✅ (`push: paths: src/server/api/**`) | ❌ (`workflow_run` no-op) |
| Manual env selection | ✅ (`dev` / `production`) | ✅ (`dev` / `staging` / `prod`) |

The unique features of `deploy-slot-swap.yml` are: (a) `resolve-config` job for multi-env secret resolution, (b) `staging` as a separate environment value, (c) `skip_tests` boolean for hotfix path. None of these are currently in use (only `prod` is actively deployed; `STAGING_*` secrets are not populated). The `workflow_run` trigger is a confirmed no-op due to upstream failure. Maintaining two BFF-deploy workflows violates design.md D-04 (consolidate liberally).

## Recommendation

**CONSOLIDATE → deploy-bff-api.yml** and DELETE this file. Per design.md D-04, two workflows doing similar things (build → test → staging-slot deploy → swap to prod) MUST be consolidated. `deploy-bff-api.yml` is selected as the surviving artifact (see D-04 rationale: explicit rollback, push-on-master trigger, ADR-029-compliant rollback semantics). The unique `resolve-config` multi-env capability is NOT preserved at this consolidation step because (a) the secrets it consumes (`STAGING_APP_NAME`, `STAGING_RESOURCE_GROUP`, `PROD_APP_NAME`, `PROD_RESOURCE_GROUP`) are not currently populated, (b) only the `prod` environment is actively deployed, and (c) preserving it would be speculative work without a real multi-env requirement. If future work needs multi-env support, the resolve-config pattern can be re-introduced from git history (`git show 0bbebe73:.github/workflows/deploy-slot-swap.yml`).

The `workflow_run` no-op trigger is the most acute reason to delete now: it generates an inflated success metric (75%) that **misrepresents** workflow health to anyone reading `gh run list` output. The FR-11 weekly health report will compute its rolling success rate over the remaining workflows; allowing this no-op success-skew to persist would poison the FR-14 ≥90% success-rate target.

## Execution plan (for task 021 or 022)

**Task 021 (deploy-* dispositions)** executes:

```bash
git rm .github/workflows/deploy-slot-swap.yml
git commit -m "$(cat <<'EOF'
chore(workflows): consolidate deploy-slot-swap.yml into deploy-bff-api.yml (D-06)

Two BFF-deploy workflows violated design.md D-04 (one workflow per concern).
deploy-bff-api.yml retained as canonical (explicit rollback job + push-on-master
trigger + ADR-029 hash-verify-ready). deploy-slot-swap.yml's workflow_run
trigger was a confirmed no-op (skipped upstream → all jobs skip → Pipeline
Summary runs → false "success"), inflating success metrics. The unique
multi-env resolve-config job is recoverable from git history if future
multi-env deploy work materializes (currently STAGING_* secrets are not
populated; only prod is in use).

Per design.md D-04 (consolidate liberally) + NFR-04 (git rm).

See projects/github-actions-rationalization-r1/decisions/D-06-disposition-deploy-slot-swap.md
See also D-04 (deploy-bff-api KEEP rationale).
EOF
)"
```

**Consolidation merge effort**: ZERO additional edits to `deploy-bff-api.yml` in this Phase. The two workflows already implement the same staging-slot swap pattern; `deploy-bff-api.yml` is functionally a superset of `deploy-slot-swap.yml` for the currently-in-use prod-only deploy path. The `resolve-config` multi-env capability is explicitly deferred (recoverable from git history). Net workflow count change: -1.

**ADR-029 preservation note**: The slot-swap pattern + `/healthz` health-check + rollback semantics MUST be preserved. All three are already present in `deploy-bff-api.yml`. No action required to preserve.

## Owner sign-off note

Recent contributors (last 90 days): Spaarke Dev (`0bbebe73` 2026-05-26 via PR #295 — sdap-bff-api-remediation Phase 4+5+6). The PR title indicates the workflow was touched as part of the predecessor remediation, not as a deliberate addition. The same author (Spaarke Dev / spaarke-dev — same identity) also owns `deploy-bff-api.yml`'s most recent edit (`36f10712` 2026-05-31). The consolidation toward `deploy-bff-api.yml` is consistent with the author's later (2026-05-31) intent. No "speak now" hold required.
