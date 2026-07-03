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
| **Task** | — (SRFR-037 complete, v1.3.3 deployed) |
| **Step** | — |
| **Status** | idle |
| **Next Action** | Wave 8 remaining: **SRFR-084 (UAT Matter→Event end-to-end STANDARD)**. Or Wave 9 SRFR-090 (project wrap-up). Or await owner UAT feedback on v1.3.3 title styling fix. |

## Session Notes / Key Learnings (SRFR-037)

- **Path A exception is a living document** — SRFR-034 §4 originally recorded font-weight=400 based on a briefing. Owner DevTools inspection revealed OOB actually uses 600. Path A exceptions must be validated against ACTUAL OOB output, not documentation. Tightening the exception with real observed values is legitimate; loosening it would require re-invoking §6.5.
- **Griffel directional padding vs shorthand** — Fluent v9's `makeStyles` accepts individual `paddingTop`/`paddingRight`/`paddingBottom`/`paddingLeft` alongside shorthand `padding`. Directional override is the clean way to reduce ONE side while preserving others (avoiding the shorthand-vs-longhand conflict warnings Griffel emits).
- **`window.getComputedStyle` works in jsdom for Griffel atomic classes** — Griffel injects atomic class rules into `<style>` tags; jsdom's cascade resolves them for `computed.fontWeight` assertions. Robust way to assert on styling in unit tests without brittle className grep.
- **7-anchor version discipline preserved** — same list as SRFR-033/034/035, all bumped to 1.3.3.

## Applicable ADRs (session-level)

- **ADR-021** — Fluent v9 tokens preferred. Path A exception (SRFR-034 §4) for OOB parity styling extended by SRFR-037 with corrected weight 600.
- **ADR-022** — PCF platform libraries (virtual pattern preserved; bundle 1.57 MiB parity).
- **ADR-012** — Shared component library (PolymorphicPicker consumed unchanged).
- **ADR-024** — Polymorphic resolver pattern (unchanged).

## Files Modified This Session (SRFR-037)

- CREATED: `projects/set-regarding-and-field-mapping-resolver-r1/tasks/037-regarding-resolver-title-styling-v1.3.3.poml` (status: completed)
- MODIFIED: `src/client/pcf/RegardingResolver/RegardingResolver/RegardingResolverApp.tsx` (title fontWeight 400→600, container paddingTop 0, docstring)
- MODIFIED: `src/client/pcf/RegardingResolver/RegardingResolver/ControlManifest.Input.xml` (v1.3.3)
- MODIFIED: `src/client/pcf/RegardingResolver/RegardingResolver/index.ts` (CONTROL_VERSION v1.3.3)
- MODIFIED: `src/client/pcf/RegardingResolver/package.json` (v1.3.3)
- MODIFIED: `src/client/pcf/RegardingResolver/Solution/solution.xml` (v1.3.3)
- MODIFIED: `src/client/pcf/RegardingResolver/Solution/Controls/sprk_Spaarke.Controls.RegardingResolver/ControlManifest.xml` (v1.3.3)
- MODIFIED: `src/client/pcf/RegardingResolver/Solution/pack.ps1` (v1.3.3)
- MODIFIED: `src/client/pcf/RegardingResolver/__tests__/RegardingResolverApp.test.tsx` (bulk v1.3.3 + 1 new fontWeight test)
- BUILT: `src/client/pcf/RegardingResolver/out/controls/RegardingResolver/bundle.js` (1.57 MiB)
- BUILT: `src/client/pcf/RegardingResolver/Solution/bin/RegardingResolverSolution_v1.3.3.zip` (442,485 bytes)
- DEPLOYED: RegardingResolverSolution v1.3.3 to spaarkedev1 (verified via `pac solution list`)
- CREATED: `projects/set-regarding-and-field-mapping-resolver-r1/notes/task-037-regarding-resolver-title-styling.log`
- UPDATED: `projects/set-regarding-and-field-mapping-resolver-r1/tasks/TASK-INDEX.md` (SRFR-037 row; totals 32 → 33)
