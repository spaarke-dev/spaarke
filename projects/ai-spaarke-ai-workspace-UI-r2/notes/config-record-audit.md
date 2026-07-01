# Config record audit — pre-Phase-1 baseline

> **Task**: R2-001 — Audit existing `sprk_gridconfiguration` records that back the 5 workspace widgets.
> **Date**: 2026-07-01
> **Environment**: spaarkedev1 (via `mcp__dataverse__read_query`)
> **Purpose**: Baseline snapshot of `rowOpen.type` + `rowOpen.formId` state BEFORE task 002 unifies row-click behavior to Layout 1. Input for task 003 verification.

## Records (5)

| # | Widget | GUID | Record name | Entity (savedquery) | `rowOpen.type` | `rowOpen.formId` |
|---|---|---|---|---|---|---|
| 1 | Documents | `1cdd19d2-3964-f111-ab0c-7ced8ddc4cc6` | Active Documents (Workspace) | `sprk_document` — savedquery `82d75343-…` ("Active Documents") | **not set** (no `rowOpen` key in configjson) | none |
| 2 | Matters | `113ad380-9e63-f111-ab0c-70a8a53ec687` | Active Matters (Workspace) | `sprk_matter` — savedquery `3ba2301f-…` ("Active Matters") | **not set** | none |
| 3 | Projects | `97ee98e7-7a63-f111-ab0c-70a8a53ec687` | Active Projects (Workspace) | `sprk_project` — savedquery `195ab203-…` ("Active Projects") | **not set** | none |
| 4 | Invoices | `d021827b-9b5e-f111-ab0c-7c1e521545d7` | Invoice Matter Budget Performance | `sprk_invoice` — savedquery `b9f6d045-…` ("Invoice - Matter Context") | **`formDialog`** | none |
| 5 | Work Assignments | `9c5b0ee7-7a63-f111-ab0c-000d3a4d8152` | Active Work Assignments (Workspace) | `sprk_workassignment` — savedquery `c8391ddf-…` ("Active Work Assignments") | **not set** | none |

## Summary

- **`formDialog`**: 1 record (Invoices)
- **`newTab`**: 0 records
- **not-set / default**: 4 records (Documents, Matters, Projects, Work Assignments)
- **`formId` set**: 0 records
- **`formId` not set**: 5 records

## Interpretation vs. current DataGrid framework behavior

Under the framework's current default (when `rowOpen` is not set), records open via the framework's built-in default handler. The audit shows:

- **4 of 5 records defer to framework default** — meaning task 002's change to the framework's `defaultRecordOpen` will immediately affect these 4 widgets with no per-record config change required.
- **1 of 5 records (Invoices) sets `rowOpen.type = "formDialog"`** explicitly. This override must either (a) be respected under task 002's schema (framework respects explicit `rowOpen.type` values) or (b) be updated / cleared to align with FR-02's unified Layout 1 target. See task 002 for the design decision on whether `formDialog` continues as a supported per-record override.
- **No record sets `rowOpen.formId`** — the `formId` schema field being added in FR-01/FR-03 has no existing values to consider; it is a pure addition.

## Cross-worktree consumers (Pattern D + code page)

Grep of `src/**` for these 5 GUIDs found the following consumers:

| Consumer | File | Note |
|---|---|---|
| Section shims (5) | `src/solutions/LegalWorkspace/src/sections/{documents,matters,projects,invoices,workAssignments}.registration.ts` | LW-side Pattern D consumers (expected) |
| Direct-widget registry | `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` (lines 617–623) | All 5 GUIDs mapped to `documents-list`, `matters-list`, `projects-list`, `invoices-list`, `work-assignments-list` widgetTypes |
| Standalone code page (INVOICES ONLY) | `src/solutions/sprk_invoicespage/src/main.tsx` (line 21) | The invoices config record is **also** consumed by a standalone code page. Task 002 framework change will affect this page too — worth verifying no regressions in task 003. |

**No other `sprk_gridconfiguration` records or unexpected consumers found.** All 5 records are accounted for.

## Baseline behavior notes (pre-task-002)

Because the current framework `defaultRecordOpen` behavior differs from the target Layout 1 (`Xrm.Navigation.navigateTo` at 85% × 85% modal), the row-click behavior for all 5 widgets today likely opens records in a different mode (e.g., default `formDialog`, side pane, or newTab). Task 003 will verify post-change behavior against the FR-20 acceptance criterion (all 5 widgets open records at Layout 1's exact 85% × 85% modal geometry).

The Invoices record's explicit `rowOpen.type = "formDialog"` is the only case where the current behavior is explicitly declared. Task 002 must decide whether:

- Framework `defaultRecordOpen` change wins even when `rowOpen.type` is set explicitly (per FR-02 unification intent), OR
- Explicit `rowOpen.type` overrides continue to be honored (backwards-compatible), and task 002 additionally clears / updates the Invoices record to match.

Recommend the latter for surface-area minimalism: framework respects `rowOpen.type = "formDialog"` as-is; task 002 team decides whether to update the Invoices config record's `rowOpen` to align with the new Layout 1 target.

## Acceptance criteria evidence

- [x] File `notes/config-record-audit.md` exists with all 5 records enumerated
- [x] Each row has GUID + name + entity + `rowOpen.type` + `rowOpen.formId` (or "none")
- [x] Summary line reports counts of formDialog / newTab / with-formId / without-formId
- [x] No config records missed — cross-verified against 5 section registration files + `register-workspace-widgets.ts`
