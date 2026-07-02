# CLAUDE.md — AI SpaarkeAi Workspace UI R2

> **Loaded automatically** at task-execute time for every task in this project.
> **Purpose**: Project-specific AI context — WHAT this project is doing, WHAT rules to follow.

## Project

Standardize workspace widget modal UX to Layout 1 (OOB `Xrm.Navigation.navigateTo` at 85% × 85%); retire the contractually-unsupported `SmartTodoModal` iframe pattern; add missing Communications workspace widget. See [`design.md`](design.md), [`spec.md`](spec.md).

## PR strategy

**Three phased PRs**:

1. Framework + widget migration (Phase 1, tasks 001–004)
2. Communications widget (Phase 2, tasks 010–013)
3. SmartTodoModal retirement + doc updates (Phase 3, tasks 020–024)

Each PR is independently mergeable. Do NOT batch phases into a single PR — coordination hurts review quality.

## Two-layout standard (binding for R2)

- **Layout 1 (canonical default)**: `Xrm.Navigation.navigateTo({ pageType: "entityrecord", entityName, entityId, formId? }, { target: 2, position: 1, width: {value: 85, unit: '%'}, height: {value: 85, unit: '%'} })`. Used for every entity record row-click. ONE size for every entity — do not vary.
- **Layout 2 (justified exception)**: `RecordNavigationModalShell` + proprietary Fluent v9 content (documents preview via `RichFilePreviewDialog`). Dimensions are content-driven (portrait for documents: `max-width: 1280px, height: 85vh`). **Do not resize to match Layout 1.**
- **Retired**: iframe-hosted OOB `main.aspx` inside the shell. Anti-pattern per Microsoft Learn 2025-05-07.

## Applicable ADRs

- **ADR-006** — PCF over web resources
- **ADR-012** — Shared component library boundary; `@spaarke/ai-widgets` for Communications direct widget; solution folder for section shim
- **ADR-021** — Fluent UI v9 exclusive, semantic tokens only, no v8 imports
- **ADR-028** — Spaarke Auth v2; no token snapshots in modal props (`Xrm.Navigation` is host-context, no BFF token needed)

## MUST rules

- ✅ MUST use `Xrm.Navigation.navigateTo` for Layout 1 opens
- ✅ MUST use 85% × 85% for every Layout 1 open (see FR-20)
- ✅ MUST NOT introduce any new iframe of `main.aspx` in `src/`
- ✅ MUST preserve `RecordNavigationModalShell` in `@spaarke/ui-components` (Layout 2 dependency)
- ✅ MUST preserve `RichFilePreviewDialog` behavior + dimensions (FR-21)
- ✅ MUST NOT break the DataGrid `onRecordOpen` public prop contract
- ✅ MUST keep every PR independently mergeable

## Existing patterns to follow

- **`DataverseEntityViewWidget`** — Communications consumes this component (per §11 component justification — extend, don't rebuild)
- **`documents.registration.ts` / `matters.registration.ts`** — template shape for `communications.registration.ts`
- **Pattern D dual-use** — section + direct widget for every entity list surface

## Files most likely to touch

**Phase 1**:
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx` (extend `defaultRecordOpen`)
- 5 `sprk_gridconfiguration` records (audit + verify `rowOpen.type`)

**Phase 2**:
- `src/solutions/LegalWorkspace/src/sections/communications.registration.ts` (new)
- `src/solutions/LegalWorkspace/src/sectionRegistry.ts` (add row)
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` (add `communications-list`)
- 1 new `sprk_gridconfiguration` record for Communications

**Phase 3**:
- `src/solutions/SmartTodo/**` (delete iframe wiring; migrate callsites)
- `docs/standards/MODAL-DECISION-CRITERIA.md` (sharpen)
- `.claude/patterns/ui/record-modal-selection.md` (update)
- `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` (add row-click section)
- `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` (document `formId`)
- `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` (cross-ref modal standard)

## Files NOT to touch

- `@spaarke/ui-components/src/components/RecordNavigationModalShell/**` — Layout 2 component; interface stays unchanged
- `@spaarke/ui-components/src/components/FilePreview/RichFilePreviewDialog.tsx` — Documents preview reference case; dimensions preserved (FR-21)
- `Sprk.Bff.Api/**` — no BFF work in R2
- Public prop contracts on `<DataGrid>` — only the DEFAULT handler behavior changes

## References

- [`spec.md`](spec.md) — 21 FRs, 9 NFRs, 14 success criteria
- [`design.md`](design.md) — architecture context
- [`plan.md`](plan.md) — phase breakdown + effort
- [`notes/researcher-iframe-main-aspx-2026-07-01.md`](notes/researcher-iframe-main-aspx-2026-07-01.md) — evidence trail
- [Root CLAUDE.md](../../CLAUDE.md) — repo-wide operational rules
- [CLAUDE.md §4 — Task Execution Protocol](../../CLAUDE.md#4--mandatory-task-execution-protocol) — MUST invoke `task-execute` for every task
- [CLAUDE.md §6.5 — ADR Conflict Resolution](../../CLAUDE.md#65-adr-conflict-resolution-protocol-binding--added-2026-06-29) — if implementation surfaces an ADR conflict, use the A/B/C protocol
- [CLAUDE.md §7 — Test diet gate](../../CLAUDE.md#7-task-completion--transition) — `090-project-wrap-up` runs `/test-diet` before closing
