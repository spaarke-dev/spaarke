# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-19
> **Phase**: Pre-pipeline. design.md rewritten + spec.md generated; `/project-pipeline` NOT yet run (no tasks exist).

## State

- **design.md**: rewritten 2026-10-19 after re-verification against master `49279def1`. Owner decisions Q1–Q12 are
  settled (§7). Committed and pushed as `556d3bb2f`.
- **notes/design-review-2026-10-19.md**: evidence base (file:line facts) for the spec and tasks. Committed in
  `556d3bb2f`.
- **spec.md**: generated 2026-10-19 by `/design-to-spec`. 28 FRs, 8 NFRs, one ADR-008 tension (path A). **Not yet
  committed.**
- **Portfolio**: issue #1164 board Status set to Active (was wrongly Completed).
- **Worktree**: branch `work/field-mapping-server-write-path-r1`, synced with master on 2026-10-18.

## Owner decisions captured during design-to-spec (in spec.md Owner Clarifications)

- A mapped value that fails AppendTo or field security is **skipped with a warning**; the create proceeds.
- **No backfill**: forward-only. No ownership or mapping fix for rows created before go-live.
- Rollout: per-wizard cutover (Matter first); rollback is redeploying the previous bundle; no runtime flag.
- Matter number comes from the platform autonumber (task 076 decision); the wizard stops generating
  `{typeCode}-{random6}`.

## Open items (spec "Unresolved Questions")

- Repair marker: verify `createdby` ≠ BFF app user (impersonated creates?).
- Live quick-create flags for Project, Matter and Invoice Line Item.
- Exception assessment: notification outbox; `DecisionRecordWriter` communication-owner case.
- UAC-r2 timing for the `OwnedChildWrite` refactor.

## Exact next action

1. The user reviews spec.md (design-to-spec Step 5: y → `/project-pipeline` / edit / done).
2. Commit spec.md + current-task.md.
3. If y: run `/project-pipeline projects/field-mapping-server-write-path-r1`. It registers the project in
   `projects/INDEX.md` with the hot-path declaration (BFF=Y, root-CLAUDE=Y) and creates the tasks.
