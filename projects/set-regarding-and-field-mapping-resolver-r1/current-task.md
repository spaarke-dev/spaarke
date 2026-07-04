# Current Task

**Project**: set-regarding-and-field-mapping-resolver-r1
**Wave**: Wave 8 remaining (SRFR-084 UAT); Wave 9 wrap-up (SRFR-090)
**Task**: none active
**Status**: idle
**Started**: —
**Rigor**: —

## Quick Recovery

| Field | Value |
|-------|-------|
| **Task** | — (SRFR-039 + SRFR-041 complete; v1.3.4 deployed; OOB fields hidden) |
| **Step** | — |
| **Status** | idle |
| **Next Action** | Wave 8 remaining: **SRFR-084 (UAT Matter→Event end-to-end STANDARD)**. Or Wave 9 SRFR-090 (project wrap-up). Or await owner UAT feedback on v1.3.4. |

## Session Notes / Key Learnings (SRFR-039 + SRFR-041)

- **Owner corrections are common on visible UI polish** — SRFR-034 §6 removed the Name cell based on a briefing; owner post-v1.3.3 clarified they wanted it inline. Similarly SRFR-038 misinterpreted the hidden OOB fields as a regression; owner clarified they were intentionally hidden. Lesson: for visible-UI changes, prefer to verify via screenshot/DevTools BEFORE making permanent structural decisions.
- **Path A exceptions can be extended** — SRFR-034 §4 (title OOB parity) was extended by SRFR-037 (weight 400 → 600) and now SRFR-039 (Row 2 field labels 12px/400/#616161 + name text 14px/400/#242424). All extensions stay bounded to OOB-parity within the same file. This is a legitimate pattern per CLAUDE.md §6.5.
- **Preserving companion wiring on revert** — SRFR-041 revert keeps the OnLoad handler + `<events>` block intact so owner can re-enable OOB fields via maker portal without re-registering the webresource. Reverts should preserve wiring, not tear everything down.
- **7-anchor version discipline preserved** — same list as SRFR-033/034/035/037, all bumped to 1.3.4.

## Applicable ADRs (session-level)

- **ADR-021** — Fluent v9 tokens preferred. Path A exception (SRFR-034 §4) extended by SRFR-037 (title weight) and SRFR-039 (Row 2 labels + name text). All hardcoded values match observed OOB DevTools output.
- **ADR-022** — PCF platform libraries (virtual pattern preserved; bundle 1.58 MiB parity).
- **ADR-012** — Shared component library (PolymorphicPicker consumed unchanged).
- **ADR-024** — Polymorphic resolver pattern (unchanged).
- **ADR-006** — Form config only for W2 (SRFR-041).

## Files Modified This Session (SRFR-039 + SRFR-041)

- CREATED: `projects/set-regarding-and-field-mapping-resolver-r1/tasks/039-regarding-resolver-restore-name-cell-v1.3.4.poml` (status: completed)
- CREATED: `projects/set-regarding-and-field-mapping-resolver-r1/tasks/041-sprk-todo-form-revert-hide-oob-fields.poml` (status: completed)
- MODIFIED: `src/client/pcf/RegardingResolver/RegardingResolver/RegardingResolverApp.tsx` (Row 2 grid + labels + Griffel styles; +81 net lines)
- MODIFIED: `src/client/pcf/RegardingResolver/RegardingResolver/ControlManifest.Input.xml` (v1.3.4)
- MODIFIED: `src/client/pcf/RegardingResolver/RegardingResolver/index.ts` (CONTROL_VERSION v1.3.4)
- MODIFIED: `src/client/pcf/RegardingResolver/package.json` (v1.3.4)
- MODIFIED: `src/client/pcf/RegardingResolver/Solution/solution.xml` (v1.3.4)
- MODIFIED: `src/client/pcf/RegardingResolver/Solution/Controls/sprk_Spaarke.Controls.RegardingResolver/ControlManifest.xml` (v1.3.4)
- MODIFIED: `src/client/pcf/RegardingResolver/Solution/pack.ps1` (v1.3.4)
- MODIFIED: `src/client/pcf/RegardingResolver/__tests__/RegardingResolverApp.test.tsx` (bulk v1.3.4 + 3 replaced + 6 new SRFR-039 tests; +164 net lines)
- BUILT: `src/client/pcf/RegardingResolver/out/controls/RegardingResolver/bundle.js` (1.58 MiB)
- BUILT: `src/client/pcf/RegardingResolver/Solution/bin/RegardingResolverSolution_v1.3.4.zip` (442,670 bytes)
- DEPLOYED: RegardingResolverSolution v1.3.4 to spaarkedev1 (verified via `pac solution list`)
- CREATED: `c:/tmp/deploy-sprktodo-form-041-revert.ps1`
- PATCHED: sprk_todo main form (formid eca59df4-...); both OOB cells reverted to `visible="false"`; PublishXml executed
- CREATED: `projects/set-regarding-and-field-mapping-resolver-r1/notes/task-039-restore-name-cell.log`
- CREATED: `projects/set-regarding-and-field-mapping-resolver-r1/notes/task-041-sprk-todo-form-revert.log`
- UPDATED: `projects/set-regarding-and-field-mapping-resolver-r1/tasks/TASK-INDEX.md` (SRFR-039 + SRFR-041 rows; totals 33 → 35)
