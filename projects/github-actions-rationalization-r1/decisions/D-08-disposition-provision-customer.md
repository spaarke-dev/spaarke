# D-08 — provision-customer.yml Disposition

> **Date**: 2026-06-01
> **Project**: github-actions-rationalization-r1
> **Phase**: 2 (Rationalization) — task 020
> **Related**: spec FR-06 (≤8 workflow target), NFR-04 (`git rm` for deletes), NFR-06 (per-decision record), design.md D-03 (delete-by-default)
> **Recommendation**: **DELETE**

## Context

`provision-customer.yml` is a 279-line manual-only (`workflow_dispatch`) wrapper around `./scripts/Provision-Customer.ps1`. Its declared purpose is to provision a new customer-dedicated stack (Model 2 customer tenant), with 4 jobs: validate-inputs (customerId regex check: lowercase alphanumeric 3-10 chars) → provision → verify (`Test-Deployment.ps1`) → summary. It uploads 90-day-retention audit logs as artifacts. Created 2026-03-13 alongside `deploy-bff-api.yml` and `deploy-platform.yml` as the initial CI/CD scaffolding.

## Evidence

**Wave A inventory data**:
- Last-30-day runs: **0 total** — never invoked in last 30 days
- Triggers: `workflow_dispatch` only (no auto-triggers)
- Pattern observed: N/A (no runs to analyze)
- Last meaningful commit: `8fa46c60` 2026-03-13 (spaarke-dev) — "feat(ci): add CI/CD workflow files for platform, BFF API, and customer provisioning"

**Recent contributor check (live)**: `git log --since='90 days ago' -- .github/workflows/provision-customer.yml` returns the single commit `8fa46c60` from 2026-03-13 (>2 months ago). No edits in the last 90 days.

**Most recent runs (live `gh run list --limit 10`)**: empty array `[]` — confirms 0 runs throughout the full lookup window (longer than 30 days).

**Value assessment**: Identical structural pattern to `deploy-platform.yml` (D-05): a manual-only workflow wrapping a PowerShell script that the team runs locally per `docs/procedures/production-release.md`. The workflow adds (a) 90-day audit-log retention via artifact upload, and (b) customerId regex validation enforcement. Neither has been realized: zero invocations means zero audit-trail records and zero validated provisioning runs via CI. The Provision-Customer.ps1 script itself implements the same validation inline (per ADR-027 subscription isolation). Customer provisioning is currently a low-frequency operator-driven activity; if multi-tenant scale-out increases, the workflow can be recreated.

## Recommendation

**DELETE** per design.md D-03 (delete-by-default for never-used workflows). Zero invocations in 30+ days, last touched 2026-03-13 (>2 months ago), functional equivalence available via `Provision-Customer.ps1` locally. Per NFR-04 the deletion MUST be `git rm`, not comment-out. The 90-day audit-log retention is currently a theoretical capability with no actual records — if regulated-compliance audit-trail becomes a hard requirement in the future, this workflow can be recreated with explicit requirements basis from git history (`git show 8fa46c60:.github/workflows/provision-customer.yml`).

This deletion is part of the strategic D-03 cluster (auto-add-to-project + deploy-platform + provision-customer + insights-eval) — four "scaffolded but never invoked" workflows that collectively represent the "every project added without holistic consideration" pattern called out in CLAUDE.md §10 (BFF hygiene rule). Removing them collapses CI surface to the workflows that actually run.

## Execution plan (for task 021 or 022)

**Task 021 (deploy-* dispositions)** executes (provision-customer is deploy-adjacent — manual provisioning):

```bash
git rm .github/workflows/provision-customer.yml
git commit -m "$(cat <<'EOF'
chore(workflows): delete provision-customer.yml (D-08)

Zero invocations in last 30 days (also confirmed zero across full lookup
window). Functionality fully covered by ./scripts/Provision-Customer.ps1
run locally per docs/procedures/production-release.md. 90-day audit-log
retention was a theoretical capability with no actual records.

Per design.md D-03 (delete-by-default for never-used workflows) +
NFR-04 (git rm, not comment-out).

See projects/github-actions-rationalization-r1/decisions/D-08-disposition-provision-customer.md
EOF
)"
```

Net workflow count change: -1. No consolidation.

## Owner sign-off note

Recent contributors (last 90 days): NONE. The only commit is `8fa46c60` 2026-03-13 (>2 months ago, same scaffolding commit as deploy-platform.yml). The original author (spaarke-dev) has had ≥2 months without invocation — strong signal that the workflow is not load-bearing. No "speak now" trigger window applies (D-03 handles the default-delete case).
