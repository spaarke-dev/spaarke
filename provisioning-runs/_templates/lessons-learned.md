# Lessons Learned — {customerId}-{runId}

> **Created by**: `customer-provisioning-orchestration-r1` task 203a per punch list row A06 (template).
> **MANDATORY** — Written by `/provision-environment` skill Step 7 BEFORE run is marked complete or folder is committed.
> Cross-run audit (planned; `/audit-provisioning-lessons` slash command per task 203-followup) periodically rolls up recurring themes into `PROVISIONING-PREREQUISITES.md` or `.claude/patterns/provisioning/` updates.

## What went right

- 3-5 bullets describing what worked as designed. Concrete evidence — cite handler + timestamp.

## What went wrong

Each surprise (unexpected error, timing gap, silent failure, config drift, gate deadlock) → normalized shape:

### Lesson `{L01}`

- **Symptom**: what the operator or automation observed
- **Root cause**: what actually caused it (verify — do NOT stop at first plausible cause)
- **Fix applied**: `{yes-here-in-this-run | yes-in-parallel-worktree | no-deferred | no-worked-around}`
  - If fix applied: cite file + line (or handler + runId if code-level fix in a parallel worktree)
  - If deferred: file as GitHub issue + link; annotate expected owner
  - If worked-around: describe the workaround + why the underlying fix is deferred
- **Landing spot**: where does the fix belong long-term?
  - `.claude/patterns/provisioning/{pattern}.md` (add new / extend existing)
  - `docs/guides/PROVISIONING-PREREQUISITES.md` (add new prereq)
  - `.claude/skills/provision-environment/SKILL.md` (skill step change)
  - `scripts/{script}.ps1` (script hardening)
  - `src/server/services/**` (handler code fix)
  - `infrastructure/bicep/**` (Bicep hardening)
  - Another project's `notes/` (route out — per §10 BFF Hygiene / bff-extensions.md § F)
- **Blocks future runs**: `{yes | no | conditional-on-X}`
- **Punch-list class**: `{A | B | C}` (mirrors task-202 punch-list convention: A=provisioning-owned, B=BFF-owned, C=shared/coordination)

### Lesson `{L02}`

- (same shape)

## New prereqs to codify

Any newly-discovered manual step or external-dependency check that Step 0.5 should verify — propose an entry for [`docs/guides/PROVISIONING-PREREQUISITES.md`](../../docs/guides/PROVISIONING-PREREQUISITES.md) + machine-parseable [`scripts/provisioning-prereqs/prereqs.yaml`](../../scripts/provisioning-prereqs/prereqs.yaml):

| proposed_id | name | scope | tenancyModel | check_recipe | consequence-if-absent |
|---|---|---|---|---|---|
| — | — | — | — | — | — |

## New patterns to add

Any recurring shape worth extracting to `.claude/patterns/provisioning/` (skeleton with `When → Read These Files → Constraints → Key Rules`):

- `{pattern-name.md}` — {2-sentence rationale}

## Recommendations for next run

Concrete, actionable items (do NOT ship as vague aspirations):

- {Specific script hardening: `Grant-ControlPlaneIdentity.ps1` — add pre-flight check for existing RBAC to avoid PUT-409}
- {Specific Bicep tweak: `platform-controlplane.bicep` — expose `logRetentionDays` as parameter}
- {Specific skill improvement: Step 4 execute loop — reduce polling interval from 30s to 15s during handler-active window}

## Cross-run pattern (if this lesson recurs)

- **First observed in run**: `{runId}` (this run) or `{prior-runId}` if second+ occurrence
- **Occurrence count across runs**: {N} (populated by `/audit-provisioning-lessons` when it runs)
- **Recommended promotion**: if count ≥ 3 → promote to `PROVISIONING-PREREQUISITES.md` OR `.claude/patterns/provisioning/` (owner: platform-ops)

## Sign-off

- Author: `{operatorUpn}` @ `{ts}`
- Reviewer (if separate): `{reviewerUpn}` @ `{ts}`
- Committed to git: `{git-sha}` (post-Step-7)
- INDEX.md `lessons-count` field updated to reflect this file's entry count
