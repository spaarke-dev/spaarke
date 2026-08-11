# Task 058 — r5 coordination contract (FR-E6) — COMPLETE (2026-08-10)

**Rigor**: MINIMAL (documentation edit; no code). **Deliverable**: `projects/email-communication-solution-r5/notes/email-intelligence-r1-coordination.md` §10.

## What this task did
The **ownership statement** half of §10 was pre-written at project setup (2026-08-05) — it already recorded that R2 owns Pillar E states D/E/F + the Exceptions Queue (shared `Spaarke.Communication.Components`, superseding r5's deferral) + the `/conflict-check`-before-every-shared-lib-PR obligation (incl. `dataset-grid-framework-r2`). That satisfied the POML's three ACs at the ownership level, so TASK-INDEX carried 058 as ✅.

This task **completed the section as an as-built contract** now that R2's Pillar E build is code-complete through task 057 — folding the concrete details staged in `notes/defer-issues.md` **COORD-058-01** into §10 so r5 has an unambiguous consumption spec (not just an ownership boundary):

- **Updated the R2-delivery table** (State D/E/F + Exceptions Queue rows) from forward-looking ("R2 task 05x — will build") to as-built, naming the actual components (`RelatedToCell`, `FieldUpdateReconcileTab`/`Modal`, `TaskReconcileTab`/`Modal`, the grid + triage columns + browse/reader shell + citation nav + category→team routing) and the endpoint splits.
- **Added an "As-built consumption contract" subsection** enumerating exactly what r5 WIRES:
  - New BFF endpoints r5 calls: **055a** apply-`{overrideValue}`, **055b** dismiss, **056b** ad-hoc create-task.
  - Reconcile-tab exports r5 mounts (browse `renderTabs` slot + email form; host supplies `regarding` from 052's `onConfirmed`, re-supplies on override; citation→`activeCitation`).
  - The **056 Accept-routing** r5 must NOT re-implement (unchanged proposal→034 apply; edited-identity→056b ad-hoc+055b dismiss; +New task→056b).
  - The **057 routing config** (`Communication:CategoryRouting` → `ownerid` team + per-team `sprk_gridconfiguration` `membershipFilter`).

## AC check
1. R2 owns states D/E/F + Exceptions Queue (shared lib), superseding r5's deferral — ✅ (§10 statement + as-built table).
2. `/conflict-check` before every shared-lib PR incl. `dataset-grid-framework-r2` — ✅ (existing binding paragraph, unchanged).
3. No duplicate r5 build implied — ✅ ("r5 MUST NOT duplicate-build" + "r5 mounts them; it does NOT re-implement any of them").

## Scope note
Per the POML constraint, only §10 was touched (the ownership + as-built contract); the rest of the r5 coordination note is unchanged. `email-communication-solution-r5` is a closed project on master (memory: `closed-r5-projects-editable`), so the note was edited directly from this worktree.

## Remaining Pillar E
Only **059** (deploy — GATED/paused): seeds the Needs-review + per-team `sprk_gridconfiguration` records, sets the `Communication:CategoryRouting` app setting, and updates `NEEDS_REVIEW_CONFIG_ID` to the seeded record id.
