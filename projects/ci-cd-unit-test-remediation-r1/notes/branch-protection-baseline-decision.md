# Branch Protection Baseline — Reuse-vs-Refresh Decision

> **Task**: CICD-001 (`projects/ci-cd-unit-test-remediation-r1/tasks/001-branch-protection-baseline-reuse-decision.poml`)
> **Decided by**: Claude Code (autonomous; task `001`)
> **Decided on**: 2026-06-26
> **Scope**: Decide whether Phase 1/2 work in `ci-cd-unit-test-remediation-r1` references the 24-day-old baseline JSON from `github-actions-rationalization-r1`, OR captures a fresh interim snapshot now.

---

## TL;DR

**Decision**: Option (a) — **REUSE** the existing baseline (`projects/github-actions-rationalization-r1/baseline/branch-protection-2026-06-01.json`) as the Phase 1/2 reference for the *intended* protected configuration. The authoritative pre-cutover snapshot will be re-captured at task `070` per spec FR-A06 (binding).

**Rationale**: The "current state" diff is unusable as a baseline because branch protection is currently DISABLED on `master` (HTTP 404 from `/branches/master/protection`; rulesets array empty). There is no live protection config to snapshot in any meaningful sense. The Jun-1 baseline is the only artifact that describes the intended/restorable protected configuration, so Phase 1/2 work must reference that intent regardless. Taking an "interim snapshot of nothing" adds no information.

---

## Inputs

| Artifact | Path | Date |
|---|---|---|
| Existing baseline (intended config) | `projects/github-actions-rationalization-r1/baseline/branch-protection-2026-06-01.json` | 2026-06-01 |
| Current state JSON (this task's capture) | `projects/ci-cd-unit-test-remediation-r1/notes/branch-protection-current.json` | 2026-06-26 |

---

## Current state (2026-06-26) — captured commands and results

```
$ gh api repos/spaarke-dev/spaarke/branches/master/protection
HTTP 404 — "Branch protection has been disabled on this repository."

$ gh api repos/spaarke-dev/spaarke/branches/master | jq '.protected'
false

$ gh api repos/spaarke-dev/spaarke/rulesets
[]   # no rulesets active
```

**Note on the deprecated shadow field**: `/repos/.../branches/master` still returns an embedded `protection` object with `enabled: true` and one check (`Build & Test (Debug)`). This is the legacy v3 representation and does NOT reflect live enforcement — the top-level `protected: false` and the 404 on the protection endpoint are the canonical signals. Treat protection as DISABLED.

---

## Diff: current vs Jun-1 baseline

| Setting | 2026-06-01 baseline | 2026-06-26 current | Diff |
|---|---|---|---|
| Protection enabled | true | **false** (404) | **MATERIAL** — protection removed entirely |
| `required_status_checks.strict` | true | n/a | n/a (protection off) |
| `required_status_checks.contexts` | `["Build & Test (Debug)", "Build & Test (Release)", "Code Quality"]` | n/a | n/a (protection off) |
| `required_pull_request_reviews.dismiss_stale_reviews` | true | n/a | n/a |
| `required_pull_request_reviews.required_approving_review_count` | 0 | n/a | n/a |
| `enforce_admins.enabled` | true | n/a | n/a |
| `allow_force_pushes.enabled` | false | n/a (defaults to true when unprotected) | n/a |
| `allow_deletions.enabled` | false | n/a (defaults to true when unprotected) | n/a |
| `required_signatures.enabled` | false | n/a | unchanged-in-intent |
| `required_linear_history.enabled` | false | n/a | unchanged-in-intent |
| `required_conversation_resolution.enabled` | false | n/a | unchanged-in-intent |
| `block_creations.enabled` | false | n/a | unchanged-in-intent |
| `lock_branch.enabled` | false | n/a | unchanged-in-intent |
| Rulesets | (not captured in baseline) | `[]` (empty) | unchanged-in-intent |

**Summary**: The only diff is binary — protection went from fully configured to fully off. Every individual setting in the Jun-1 baseline is now "n/a (protection disabled)". There are no per-setting drifts to reconcile.

---

## Why protection is currently disabled — context

Per `projects/github-actions-rationalization-r1/CLAUDE.md`, the predecessor project noted operational use of brief `enforce_admins` bypass windows for actionlint-fix PRs (NFR-03). It is consistent with prior practice that protection was temporarily disabled during one of those windows or during another DevOps operation between 2026-06-01 and today, and not re-enabled. Investigating *why* protection is off and re-enabling it is **OUT OF SCOPE for this task** — it is properly the concern of task `070` (FR-A06 authoritative pre-cutover snapshot + restoration plan) and task `077` (post-cutover branch-protection flip after the 14-day stability window).

If the maintainer wants protection restored before task `070`, that is a separate operational decision that should be tracked in the project's `decisions/` folder, not silently fixed here. This task's job is documentation and decision recording only.

---

## Decision: Option (a) — REUSE the existing baseline

**Choice**: Reuse `projects/github-actions-rationalization-r1/baseline/branch-protection-2026-06-01.json` as the Phase 1/2 reference for the *intended* protected configuration. Do NOT take an interim snapshot now.

**Why (a) over (b)**:

1. **There is no meaningful "current state" to interim-snapshot.** Protection is off; the live API returns 404. An interim snapshot would just preserve the "off" state, which is not useful as a Phase 1/2 reference for any of these tasks:
   - Task `040`–`044` (workflow YAML changes referencing required checks)
   - Task `070` (authoritative pre-cutover snapshot — the one that matters per FR-A06)
   - Task `077` (post-cutover branch-protection flip + restore command)
2. **The Jun-1 baseline IS the intended-state document.** Phase 1/2 design work needs to know "what protected configuration are we targeting after the Tier 1/Tier 2 cutover?" — and that's exactly the baseline. The fact that protection happens to be temporarily off today doesn't change the target.
3. **Spec FR-A06 binds the authoritative snapshot to task `070`.** That snapshot MUST be re-captured immediately before the cutover (i.e., right before protection is flipped to require the new Tier 1 / Tier 2 contexts). Anything captured today is automatically superseded by that re-capture and is therefore not "authoritative" — making the term "interim" appropriate but the information content low.
4. **Lower coordination cost.** Reusing the existing artifact avoids fragmenting the baseline reference across two project directories. Phase 1/2 tasks (040–044, 070, 077) cite ONE path; if drift becomes a concern later, task `070` will re-capture from scratch.

**What this means in practice for downstream tasks**:

| Task | What it references |
|---|---|
| `040`, `041`, `042`, `043`, `044` (workflow YAML edits) | `projects/github-actions-rationalization-r1/baseline/branch-protection-2026-06-01.json` for the names of the 3 currently-required contexts (`Build & Test (Debug)`, `Build & Test (Release)`, `Code Quality`) when planning Tier 1/Tier 2 context naming + the eventual replacement set |
| `070` (authoritative pre-cutover snapshot) | Re-capture fresh from `gh api repos/spaarke-dev/spaarke/branches/master/protection` immediately before cutover; supersedes the Jun-1 baseline; saves to `projects/ci-cd-unit-test-remediation-r1/baseline/branch-protection-pre-cutover-YYYY-MM-DD.json` |
| `077` (post-cutover protection flip) | Uses the task-`070` snapshot's structure + new Tier 1/Tier 2 context names to construct the restore PUT body; the restore command below is the per-Jun-1-baseline reference shape |

---

## Restore command (reference — uses Jun-1 baseline shape)

To restore the 2026-06-01 baseline protection state (or to use as the template for the post-cutover protection in task `077` with updated context names):

```bash
# Reads the baseline JSON, builds the PUT body, and applies it.
# Confirm via: gh api repos/spaarke-dev/spaarke/branches/master/protection | jq '.required_status_checks.contexts'

gh api -X PUT repos/spaarke-dev/spaarke/branches/master/protection \
  --input - <<'JSON'
{
  "required_status_checks": {
    "strict": true,
    "contexts": [
      "Build & Test (Debug)",
      "Build & Test (Release)",
      "Code Quality"
    ]
  },
  "required_pull_request_reviews": {
    "dismiss_stale_reviews": true,
    "require_code_owner_reviews": false,
    "require_last_push_approval": false,
    "required_approving_review_count": 0
  },
  "enforce_admins": true,
  "required_linear_history": false,
  "allow_force_pushes": false,
  "allow_deletions": false,
  "block_creations": false,
  "required_conversation_resolution": false,
  "lock_branch": false,
  "allow_fork_syncing": false,
  "restrictions": null
}
JSON
```

**Note**: The GitHub API's `PUT /repos/.../branches/master/protection` accepts a slightly different shape than the `GET` response returns (notably: `enforce_admins`, `allow_force_pushes`, etc. are booleans on PUT, but objects with `.enabled` on GET; `restrictions` must be `null` for no user/team restrictions). The body above is the correctly-shaped PUT body, derived from the GET baseline.

For task `077`, the `required_status_checks.contexts` array will be replaced with the new Tier 1 / Tier 2 context names (TBD by tasks `040`/`041`). All other settings should be preserved unless explicitly re-evaluated.

---

## Verification of acceptance criteria

- [x] Decision document exists with explicit choice + rationale — this file
- [x] Current state captured as JSON — `notes/branch-protection-current.json` (includes the 404 response, the deprecated shadow field, rulesets emptiness, and interpretation metadata)

---

*This decision is reversible. If task `070` discovers that the protection settings were intentionally changed between Jun-1 and the cutover (e.g., to add a new required check that we should preserve), task `070` will reconcile and document the delta. Per spec FR-A06, `070`'s snapshot is authoritative regardless of what `001` decided.*
