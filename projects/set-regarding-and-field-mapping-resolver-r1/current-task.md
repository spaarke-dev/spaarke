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
| **Task** | — (SRFR-035 complete, v1.3.2 deployed) |
| **Step** | — |
| **Status** | idle |
| **Next Action** | Wave 8 remaining: **SRFR-084 (UAT Matter→Event end-to-end STANDARD)**. Or Wave 9 SRFR-090 (project wrap-up). Or SRFR-036 (sprk_todo form config) if still running in parallel. |

## Session Notes / Key Learnings (SRFR-035)

- **CREATE-mode gate is load-bearing** — the SRFR-032 presave bridge (`__sprk_regarding_pending__`) stages the resolver payload into the form's pending-attribute buffer for the INSERT transaction. `formType === 1` early-return in `autoRefreshForm` prevents `data.refresh(true)` from clobbering the form buffer during CREATE.
- **Manual refresh button (SRFR-034) preserved** — auto-refresh is ADDITIVE, not a replacement. If auto-refresh silently fails (Xrm-unavailable or save reject), the user still has the manual button as escape hatch.
- **Fire-and-forget pattern** — `void autoRefreshForm(getFormType())` after successful `applyRegardingSelection` completes the handler promptly for good UX; auto-refresh runs asynchronously without blocking the picker-select finally block.
- **7-anchor version discipline preserved** — same list as SRFR-033/034, all bumped to 1.3.2.

## Applicable ADRs (session-level)

- **ADR-022** — PCF platform libraries (virtual pattern preserved; bundle 1.57 MiB parity).
- **ADR-012** — Shared component library (PolymorphicPicker consumed unchanged).
- **ADR-024** — Polymorphic resolver pattern (applyResolverFields is still the write path; auto-refresh is display-side).

## Files Modified This Session

- MODIFIED: `src/client/pcf/RegardingResolver/RegardingResolver/RegardingResolverApp.tsx` (autoRefreshForm + getFormType helpers + wire-up in handlePickerSelect + docstring header)
- MODIFIED: `src/client/pcf/RegardingResolver/RegardingResolver/ControlManifest.Input.xml` (version)
- MODIFIED: `src/client/pcf/RegardingResolver/RegardingResolver/index.ts` (CONTROL_VERSION)
- MODIFIED: `src/client/pcf/RegardingResolver/package.json` (version)
- MODIFIED: `src/client/pcf/RegardingResolver/Solution/solution.xml` (Version)
- MODIFIED: `src/client/pcf/RegardingResolver/Solution/Controls/sprk_Spaarke.Controls.RegardingResolver/ControlManifest.xml` (version)
- MODIFIED: `src/client/pcf/RegardingResolver/Solution/pack.ps1` ($version)
- MODIFIED: `src/client/pcf/RegardingResolver/__tests__/RegardingResolverApp.test.tsx` (bulk 1.3.1→1.3.2 + 3 new tests)
- BUILT: `src/client/pcf/RegardingResolver/out/controls/RegardingResolver/bundle.js` (1.57 MiB)
- BUILT: `src/client/pcf/RegardingResolver/Solution/bin/RegardingResolverSolution_v1.3.2.zip`
- DEPLOYED: RegardingResolverSolution v1.3.2 to spaarkedev1
- CREATED: `projects/set-regarding-and-field-mapping-resolver-r1/tasks/035-regarding-resolver-auto-refresh-v1.3.2.poml` (status: completed)
- CREATED: `projects/set-regarding-and-field-mapping-resolver-r1/notes/task-035-regarding-resolver-auto-refresh.log`
- UPDATED: `projects/set-regarding-and-field-mapping-resolver-r1/tasks/TASK-INDEX.md` (SRFR-035 added, total 29 → 30)
