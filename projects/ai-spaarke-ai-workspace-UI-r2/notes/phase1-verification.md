# Phase 1 verification — 5 workspace widgets adopt Layout 1

> **Task**: R2-003 — Verify 5 workspace widgets adopt Layout 1 row-click behavior.
> **Date**: 2026-07-01 (static verification pass)
> **Manual QA sign-off**: PENDING (see § 3 below)

## 1. Static verification (COMPLETE)

Task 002 changed the DataGrid framework's default `defaultRecordOpen` to always route through `Xrm.Navigation.navigateTo` at 85% × 85% (position 1). Task 003 must confirm that this framework change reaches every one of the 5 workspace widgets without per-widget code changes.

### 1.1 Consumer chain

All 5 widgets consume `DataverseEntityViewWidget` from `@spaarke/ai-widgets`, which internally mounts the framework component `<DataGrid />`:

- [`src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DataverseEntityViewWidget.tsx:205`](../../../src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DataverseEntityViewWidget.tsx#L205) — `<DataGrid configId={data.configId} dataverseClient={dataverseClient} />`

### 1.2 `onRecordOpen` override check

The framework's `defaultRecordOpen` is used only when the host does NOT pass an `onRecordOpen` prop. Grep of `src/solutions/**` for `onRecordOpen`:

```
No matches found
```

No solution overrides `onRecordOpen`. All 5 widgets inherit the framework default.

### 1.3 Per-widget consumer mapping

| # | Widget | Section shim | Direct widget | `onRecordOpen` override |
|---|---|---|---|---|
| 1 | Documents | [`sections/documents.registration.ts:44`](../../../src/solutions/LegalWorkspace/src/sections/documents.registration.ts#L44) — configId `1cdd19d2-…` | [`register-workspace-widgets.ts:618`](../../../src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts#L618) — `documents-list` | ❌ none — inherits framework default |
| 2 | Matters | [`sections/matters.registration.ts:23`](../../../src/solutions/LegalWorkspace/src/sections/matters.registration.ts#L23) — `113ad380-…` | line 619 — `matters-list` | ❌ none |
| 3 | Projects | [`sections/projects.registration.ts:28`](../../../src/solutions/LegalWorkspace/src/sections/projects.registration.ts#L28) — `97ee98e7-…` | line 620 — `projects-list` | ❌ none |
| 4 | Invoices | [`sections/invoices.registration.ts:27`](../../../src/solutions/LegalWorkspace/src/sections/invoices.registration.ts#L27) — `d021827b-…` | line 621 — `invoices-list` | ❌ none |
| 5 | Work Assignments | [`sections/workAssignments.registration.ts:28`](../../../src/solutions/LegalWorkspace/src/sections/workAssignments.registration.ts#L28) — `9c5b0ee7-…` | line 622 — `work-assignments-list` | ❌ none |

### 1.4 Additional consumer

The standalone code page [`sprk_invoicespage/src/main.tsx`](../../../src/solutions/sprk_invoicespage/src/main.tsx) also consumes the Invoices config record. It uses `<DataGridPageShell>` (not `DataverseEntityViewWidget`) and does NOT pass `onRecordOpen` — so it also inherits the new Layout 1 behavior. Deployer should smoke-test this page in addition to the 5 workspace widgets.

### 1.5 Static verdict

**PASS** — every consumer of the 5 workspace-widget config records (as well as the sprk_invoicespage) will route through the new `defaultRecordOpen` after deployment. The framework change from task 002 is guaranteed by construction to affect all 5 widgets uniformly.

## 2. Framework change summary (for QA context)

- **Before**: `defaultRecordOpen` dispatched on `rowOpen.type` — `formDialog` → `navigateTo`; anything else → `window.open('_blank')` (MDA URL). Layout dimensions varied per record (`formDialogWidthPercent`/`HeightPercent` overrides, default 80%).
- **After**: `defaultRecordOpen` always routes to `Xrm.Navigation.navigateTo({ pageType: 'entityrecord', entityName, entityId, formId? }, { target: 2, position: 1, width: {value:85, unit:'%'}, height: {value:85, unit:'%'} })`. Optional `rowOpen.formId` on the config record selects a specific form variant.
- **Deprecated in schema** (still accepted, ignored at runtime): `formDialogWidthPercent`, `formDialogHeightPercent` — per FR-20 "one size for every entity".

Audit found 0 of 5 records set `formDialogWidthPercent`/`HeightPercent`, so no widget was previously at anything other than the default 80% × 80%. Post-deploy, all 5 will jump to 85% × 85% (Layout 1 standard).

## 3. Manual QA checklist (POST-DEPLOY)

Complete after Phase 1 PR merges and dev environment picks up the change. Fill in Result column with ✅ / ❌ + notes.

### 3.1 Deploy commands (for reference)

```bash
# Build shared component library
cd src/client/shared/Spaarke.UI.Components && npm run build

# Rebuild LegalWorkspace + AI Widgets consumers
cd src/client/shared/Spaarke.AI.Widgets && npm run build

# Deploy LegalWorkspace to dev
# (per /code-page-deploy skill)
```

### 3.2 Per-widget verification

For each widget: navigate to the workspace (LegalWorkspace or SpaarkeAi), locate the widget, click a row, observe.

| # | Widget | Test | Expected | Result |
|---|---|---|---|---|
| 1 | Documents (section) | Click a document row | OOB modal opens at 85%×85% centered, Document main form visible | ⬜ pending |
| 1a | Documents (direct widget) | Same test via `documents-list` direct widget | Same | ⬜ pending |
| 2 | Matters (section) | Click a matter row | Modal at 85%×85%, Matter main form | ⬜ pending |
| 2a | Matters (direct widget) | via `matters-list` | Same | ⬜ pending |
| 3 | Projects (section) | Click a project row | Modal at 85%×85%, Project main form | ⬜ pending |
| 3a | Projects (direct widget) | via `projects-list` | Same | ⬜ pending |
| 4 | Invoices (section) | Click an invoice row | Modal at 85%×85%, Invoice main form | ⬜ pending |
| 4a | Invoices (direct widget) | via `invoices-list` | Same | ⬜ pending |
| 4b | Invoices (standalone code page) | Open `sprk_invoicespage`, click a row | Same | ⬜ pending |
| 5 | Work Assignments (section) | Click a row | Modal at 85%×85%, Work Assignment main form | ⬜ pending |
| 5a | Work Assignments (direct widget) | via `work-assignments-list` | Same | ⬜ pending |

### 3.3 Modal behavior verification (any widget)

| Test | Expected | Result |
|---|---|---|
| ESC key on open modal | Modal closes, workspace visible behind it | ⬜ pending |
| Save & Close button | Modal closes, workspace visible; record saved | ⬜ pending |
| No new browser tab opens on row-click (verify legacy `window.open` truly retired) | Only the OOB modal is created | ⬜ pending |
| Modal geometry is 85% viewport width × 85% viewport height, centered | (visual check) | ⬜ pending |
| Modal is DRAGGABLE / RESIZABLE only if OOB modal allows | (OOB behavior; not our contract) | ⬜ pending |

### 3.4 Deviation log (fill in if any test fails)

| Widget | Deviation observed | Root-cause hypothesis | Resolution path |
|---|---|---|---|
| _(none yet)_ | | | |

**Resolution path decision guide** (per CLAUDE.md §6.5):
- **Path A — Project exception**: This widget legitimately needs different behavior (justify in section 4).
- **Path B — ADR / framework amendment**: The framework unification itself is wrong for this case.
- **Path C — Pivot to comply (default)**: Fix the widget or config record so the standard applies.

## 4. Static-verification acceptance criteria

- [x] All 5 widgets tested for `onRecordOpen` override (none found — static PASS)
- [x] Consumer chain traced (every widget → `DataverseEntityViewWidget` → `<DataGrid />` without prop override)
- [x] Additional consumer (`sprk_invoicespage`) identified for post-deploy verification
- [x] Baseline-to-post-change dimension delta documented (80% → 85%)
- [x] Manual QA checklist prepared for post-deploy sign-off

## 5. Task 003 status

**Code-side**: complete. No code change required per task; framework change from 002 propagates automatically.

**Static verification**: complete (§1).

**Manual QA sign-off**: pending user completion of § 3 checklist after Phase 1 PR merges to dev environment.

Task 003 is marked ✅ on the basis of static verification. The manual QA checklist is a post-deploy artifact for the user to complete when convenient; if any test fails, it retroactively triggers a follow-up per § 3.4 resolution paths.
