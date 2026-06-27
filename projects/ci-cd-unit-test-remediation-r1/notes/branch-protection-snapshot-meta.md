# Branch Protection Pre-Cutover Snapshot Meta (task CICD-070)

> **Captured**: 2026-06-26 12:55:00 UTC
> **HEAD SHA at capture**: `be07090bc6ebc57286d26eb439486d203dcfad18` (PR #459 merge commit)
> **Authority**: spec FR-A06 ("pre-cutover state exported to `notes/branch-protection-pre-cutover.json`") + spec §152 rollback procedure

## What the snapshot captured

**Pre-cutover state = `disabled`**. Branch protection on master is OFF (no required checks, no enforce_admins, no restrictions, no rulesets). This was confirmed by task CICD-001 earlier (Phase 1 finding) and re-verified here immediately before cutover.

## Rollback procedure (per spec §152)

If any rollback trigger fires within 24-48h post-cutover:
- Tier 1 flake rate >2% sustained over 24h
- Master green rate <90% over 24h
- >3 false-positive blocks within 24h
- Merge queue stalls >2h with >4 PRs

**Restore command**:
```bash
gh api -X DELETE repos/spaarke-dev/spaarke/branches/master/protection
```

This returns the repository to the pre-cutover state (branch protection disabled). Merge queue must also be disabled via the UI (Settings → Branches → master → uncheck "Require merge queue") since merge queue is a separate setting from branch protection rules.

## Forward target (NOT the rollback target — for task 071)

Task 071 cutover will configure branch protection as follows:
- `required_status_checks.contexts = ["CI / Router"]` (the single composite required check from ci-router.yml)
- `required_status_checks.strict = true` (must be up-to-date with base branch)
- `enforce_admins = true` (admins must follow rules)
- `required_pull_request_reviews = null` (solo developer; no reviewer requirement per spec)
- `restrictions = null` (no push restrictions)
- `allow_force_pushes = false`
- `allow_deletions = false`
- Merge queue enabled (batch=1, no speculative, queue timeout 30min) per spec FR-C01

The Jun-1 baseline at `projects/github-actions-rationalization-r1/baseline/branch-protection-2026-06-01.json` documents a similar intended-protected configuration with 3 required checks. The cutover REPLACES that historical baseline's required-check list with the single `CI / Router` check.

## Round-trip verifiability

This snapshot file plus the rollback command above constitute a complete round-trip restore path. Tested logically: rollback returns to "disabled" state (matching this snapshot); apply cutover returns to "enabled with CI / Router" state.

## Cross-references

- `notes/branch-protection-current.json` (task CICD-001) — earlier capture of same "disabled" state
- `notes/branch-protection-baseline-decision.md` (task CICD-001) — decision to retain Jun-1 baseline as reference but take THIS snapshot as the authoritative rollback target
- `projects/github-actions-rationalization-r1/baseline/branch-protection-2026-06-01.json` — historical intended-protected configuration (forward reference for task 071, NOT rollback)
