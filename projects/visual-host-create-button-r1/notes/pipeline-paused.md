# Pipeline Paused — Blocked on In-Flight PRs  ✅ RESOLVED 2026-07-08

> **RESOLUTION (2026-07-08)**: Both blockers merged and pulled into this worktree via master merge — **#525 merged 2026-07-07**, **#549 merged 2026-07-08**. Spec/design realigned to the post-#549 resolver API (backward-compatible `applyResolverFields` now returning `IApplyResolverFieldsResult`; 5th resolver field `sprk_regardingrecordnumber`; new optional `PolymorphicPicker`; `FieldMappingHandler` inheritance left out of scope). Pipeline resumed via `/project-pipeline`. The pause history below is retained for the record.
>
> **Date**: 2026-07-05
> **State**: spec.md + design.md complete and committed (commit `cc53ed77a`, branch `work/visual-host-create-button-r1`). `/project-pipeline` was run but **halted at overlap detection (before Step 2)** — NO README/PLAN/CLAUDE.md/tasks generated yet.
> **Decision**: Owner chose to **pause the pipeline entirely** until the blocking PRs merge, and to treat **#549 as a hard prerequisite**.

## Why paused — two active-PR collisions

### 🚧 PR #549 — `set-regarding-and-field-mapping-resolver-r1` (HARD PREREQUISITE)
Refactors the foundation this project builds on:
- `PolymorphicResolverService` / `applyResolverFields` (task `020-extend-polymorphic-resolver-service`)
- **Extracts the polymorphic picker** (task `021-extract-polymorphic-picker`) — overlaps our `AssociateToStep` reuse
- Edits `.claude/adr/ADR-024-polymorphic-resolver-pattern.md`
- Adds `sprk_regardingrecordnumber` column (task `010`)

**Action on resume**: plan wizard tasks against the **post-#549** resolver API + extracted picker, not today's.

### ⚠️ PR #525 — `feat/pcf-visualhost-uat-tracking-field-trio` (DIRECT FILE COLLISION)
Modifies the exact VisualHost files our Phase A touches:
- `src/client/pcf/VisualHost/control/components/CardChrome.tsx`
- `src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx`
- `ControlManifest.Input.xml`, `bundle.js`, VisualHost solution/version files

**Action on resume**: rebase on #525; ensure our PCF version-bump accounts for #525's bump; avoid concurrent edits to those files.

> Note: neither project is listed in `projects/INDEX.md` (registry stale) — the live `gh pr list` scan caught both. Consider registering this project + #525/#549 in INDEX.md when work resumes.

## Resume trigger

When **#549 has merged to master** (and ideally #525):
1. `git fetch origin && git pull origin master` into this worktree (or rebase) to pick up the settled resolver API + picker + any VisualHost changes.
2. Re-verify against live code: `applyResolverFields` signature, the extracted picker component name/location, `AssociateToStep` status, and the VisualHost `CardChrome`/`VisualHostRoot` structure.
3. Re-run `/project-pipeline projects/visual-host-create-button-r1` — it will resume from Step 0 (spec is ready).
4. During task generation, mark resolver/picker-dependent tasks as consuming the post-#549 API and VisualHost tasks as rebased-on-#525.

## Current project state (ready for resume)
- `spec.md` — complete (rev reflects: no schema deltas, KPI no files, WizardFollowOns consolidation, prefill stub, dual-bind Event/Invoice, field manifests owner-provided).
- `design.md` — complete (rev 2).
- No tasks / plan / README yet — those are Step 2/3 of the paused pipeline.
