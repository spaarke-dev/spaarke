# AI SpaarkeAi Workspace UI R2 — Implementation Plan

> **Status**: Ready for execution
> **Created**: 2026-07-01
> **Source**: [`design.md`](design.md) · [`spec.md`](spec.md)

## Architecture Context

R2 sits on top of the R1 `DataverseEntityViewWidget` foundation and modifies the Spaarke DataGrid framework's default `onRecordOpen` handler. No new architectural layer introduced. Three PRs deliver in sequence: framework + widget migrations → new Communications widget → SmartTodoModal retirement + doc sharpening.

### Discovered Resources

**Applicable ADRs** (loaded during Step 2):

- **ADR-006** — PCF over web resources (relevant for `SmartTodo` retirement; not a PCF change)
- **ADR-012** — Shared component library boundary (Communications widget's UI in `@spaarke/ai-widgets`; section shim in LegalWorkspace solution)
- **ADR-021** — Fluent UI v9 exclusive (all new UI code)
- **ADR-028** — Spaarke Auth v2 (no token snapshots; host-context `Xrm.Navigation` needs no BFF token)

**Relevant skills**:

- **`dataverse-create-schema`** — for creating the Communications `sprk_gridconfiguration` record
- **`task-execute`** — required for every task per [CLAUDE.md §4](../../CLAUDE.md#4--mandatory-task-execution-protocol)
- **`code-review`** + **`adr-check`** — quality gates at Step 9.5 of every FULL-rigor task

**Knowledge references**:

- [`MODAL-DECISION-CRITERIA.md`](../../docs/standards/MODAL-DECISION-CRITERIA.md) — the standard being sharpened in Phase 3
- [`SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../../docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md) — the framework being extended in Phase 1
- [`SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — dual-use Pattern D reference
- [`RecordNavigationModalShell/README.md`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/README.md) — Layout 2 component reference (unchanged in R2)

**Canonical implementations to follow**:

- Existing section registrations: `documents.registration.ts`, `matters.registration.ts` — template for Communications
- Existing direct widget registrations: `documents-list`, `matters-list` — template for `communications-list`
- `DataverseEntityViewWidget` (R1 delivery) — Communications consumes this component, no new list widget authored

## Phase Breakdown

### Phase 1 — Framework + widget migration (PR 1)

**Objective**: extend `sprk_gridconfiguration.configjson.rowOpen` schema with optional `formId`; rewrite DataGrid framework's `defaultRecordOpen` to route every row-click through `Xrm.Navigation.navigateTo` at 85% × 85%; migrate 5 existing widgets to Layout 1 via the framework change.

**Deliverables**:

- FR-01, FR-02, FR-03 — DataGrid framework extension
- FR-04..FR-08 — 5 widget migrations verified

**Tasks**: 001 — 004 (see [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md))

**Effort estimate**: 1–2 days

**Exit criteria**: PR 1 green in CI; manual QA per widget passing; behind-the-scenes verification via grep (no `window.open('_blank')` for row navigation).

### Phase 2 — Communications widget (PR 2)

**Objective**: add missing Communications workspace widget as both section AND direct widget (Pattern D dual-use); create backing `sprk_gridconfiguration` record.

**Deliverables**:

- FR-09 — section registration
- FR-10 — direct widget registration
- FR-11 — config record

**Tasks**: 010 — 013

**Effort estimate**: 1 day

**Exit criteria**: Communications visible in `LegalWorkspaceApp` layout options + as standalone workspace tab; renders configured columns; row-click opens Layout 1 with `sprk_communication` default main form.

### Phase 3 — SmartTodoModal retirement + documentation (PR 3)

**Objective**: retire the contractually-unsupported iframe-hosted OOB pattern; migrate all callsites to Layout 1; sharpen 5 documentation surfaces.

**Deliverables**:

- FR-12 — callsite enumeration
- FR-13 — callsite migration to `navigateTo`
- FR-14 — component deletion
- FR-15..FR-19 — 5 documentation updates
- FR-20, FR-21 — cross-cutting size standard + Layout 2 dimension retention verified

**Tasks**: 020 — 024

**Effort estimate**: 1 day

**Exit criteria**: `SmartTodoModal` deleted; no `iframe.*main\.aspx` in `src/`; docs reflect two-layout standard; `RichFilePreviewDialog` unchanged.

### Wrap-up

**Task 090** — project wrap-up per skill mandate:

- Update README status to Complete
- Create `notes/lessons-learned.md`
- Run `/test-diet` (build-vs-maintain classifier per CLAUDE.md §7)
- Archive artifacts

## Timeline

**Total estimated effort**: 3–4 days across 3 sequential PRs.

**Dependencies**: PR 1 → PR 2 → PR 3 (sequential, each independently mergeable).

**Critical path**: DataGrid framework change (Phase 1 task 002) → all widget migrations flow from this.

## References

- [`spec.md`](spec.md)
- [`design.md`](design.md)
- [`notes/researcher-iframe-main-aspx-2026-07-01.md`](notes/researcher-iframe-main-aspx-2026-07-01.md)
- [CLAUDE.md §6.5 — ADR Conflict Resolution](../../CLAUDE.md#65-adr-conflict-resolution-protocol-binding--added-2026-06-29)
