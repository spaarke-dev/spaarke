# D-10 — auto-add-to-project.yml Disposition

> **Date**: 2026-06-01
> **Project**: github-actions-rationalization-r1
> **Phase**: 2 (Rationalization) — task 020
> **Related**: spec FR-06 (≤8 workflow target), NFR-04 (`git rm` for deletes), NFR-06 (per-decision record), design.md D-03 (delete-by-default)
> **Recommendation**: **DELETE**

## Context

`auto-add-to-project.yml` is an 18-line workflow triggered by `issues: [opened, reopened]`. Its declared purpose is to add newly-opened or reopened GitHub issues to the org-level project board `PVT_kwHODW0Pv84BEgWu` via `actions/add-to-project@v0.5.0`, authenticated with secret `GH_TOKEN_PROJECT`. It is a low-value triage automation — when working, it saves the manual step of dragging an issue onto the project board.

## Evidence

**Wave A inventory data**:
- Last-30-day runs: 29 total / 0 success / 29 failure → **0% success**
- Triggers: `issues: [opened, reopened]`
- Pattern observed: NOT loader-failure. The job runs to the `Add to org project` step, where it fails on every invocation. Likely root cause is **expired `GH_TOKEN_PROJECT` secret** (PAT tokens for GraphQL Projects v2 access expire and require manual rotation) OR `actions/add-to-project@v0.5.0` is outdated (current is `@v1.x`).
- Last meaningful commit: `d650b9f2` 2025-10-01 (>8 months ago) — "feat: Complete Sprint 3 Phase 1-3 (Tasks 1.1-3.2)"

**Recent contributor check (live)**: `git log --since='90 days ago' -- .github/workflows/auto-add-to-project.yml` returns NO commits in the last 90 days (the last commit `d650b9f2` predates the lookup window by ~5 months).

**Most recent runs (live `gh run list --limit 10`)**: 10 runs since 2025-10-06; ALL `conclusion: failure`. The 7 most recent runs (2026-03-13) are all failures clustered in a single day — likely a burst of issue-opening events that all hit the same broken token/action-version. The workflow has been **broken continuously since at least 2025-10-06** (the oldest observable failure in the live list).

**Value assessment**: The capability — auto-add issues to a project board — has a small upside (saves ~5 seconds per issue triage) and a measurable downside (29 consecutive failure records in the last 30 days, generating notification noise that trains people to ignore CI). Even if the secret were renewed AND the action version were bumped, the steady-state value is low (the team's issue volume does not require automated triage at the cadence required to justify ongoing maintenance of the secret + action-version + permissions for a single-step workflow). The workflow has had 0 successful runs across the full observable history.

This is the most acute D-03 candidate among the 7 — it has been broken longest, has the lowest realized value, and the fix cost (secret rotation + action-version bump + retest) exceeds the value of the automation it provides.

## Recommendation

**DELETE** per design.md D-03 (delete-by-default for never-used workflows). The "never used" criterion is over-satisfied: 29/29 fails in 30 days, 0 successful runs across the full observable history (≥6 months), last touched 2025-10-01 (>8 months ago). Per NFR-04 the deletion MUST be `git rm`, not comment-out. The decision record commit message should explicitly cite this finding so that any future automation work on issue triage has a documented precedent.

If issue-board automation becomes desired in the future, recommend implementing as a GitHub Actions native workflow using the org-level PAT pattern from the project documentation (NOT a personal PAT in `GH_TOKEN_PROJECT`) — this is a follow-up engineering concern, not in scope for this disposition record.

## Execution plan (for task 021 or 022)

**Task 022 (non-deploy dispositions)** executes:

```bash
git rm .github/workflows/auto-add-to-project.yml
git commit -m "$(cat <<'EOF'
chore(workflows): delete auto-add-to-project.yml (D-10)

29/29 failures in last 30 days; broken continuously since at least
2025-10-06 (likely expired GH_TOKEN_PROJECT secret or outdated
actions/add-to-project@v0.5.0). Last meaningful commit 2025-10-01
(>8 months ago). Low-value automation (auto-add issues to org
project board) — even renewed/upgraded, ongoing maintenance cost
exceeds the value.

If issue-board automation becomes desired again, recommend rebuild
with org-level PAT pattern from scratch — not worth repairing.

Per design.md D-03 (delete-by-default) + NFR-04 (git rm, not
comment-out).

See projects/github-actions-rationalization-r1/decisions/D-10-disposition-auto-add-to-project.md
EOF
)"
```

Net workflow count change: -1. No consolidation.

## Owner sign-off note

Recent contributors (last 90 days): NONE. Last commit (`d650b9f2`) is from 2025-10-01, which is >8 months before this disposition. No "speak now" trigger window applies — D-03 handles the default-delete case explicitly. The 29 consecutive failures in the past 30 days are themselves the strongest evidence that no one is monitoring this workflow's health; if anyone were, they would have rotated the secret or bumped the action version months ago.
