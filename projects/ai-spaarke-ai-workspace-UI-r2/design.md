# AI SpaarkeAi Workspace UI — R2

> **Status**: DRAFT — design document. Not yet a committed spec.
> **Project ID**: `ai-spaarke-ai-workspace-UI-r2`
> **Positioning**: Modal UX standardization + missing entity widget + retiring the iframe-hosted OOB pattern
> **Owner**: Ralph Schroeder
> **Created**: 2026-07-01
> **R1 reference**: [`../ai-spaarke-ai-workspace-UI-r1/design.md`](../ai-spaarke-ai-workspace-UI-r1/design.md) — established the `DataverseEntityViewWidget` foundation this project extends

<hot-path-declaration>
  <bff>N</bff>
  <spaarke-ai>Y</spaarke-ai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-CLAUDE-md>Y</root-CLAUDE-md>
</hot-path-declaration>

<!--
Hot-path declaration rationale (per CICD-061 / bff-extensions.md §G):
- bff=N: no `src/server/api/Sprk.Bff.Api/**` touches; all work client-side + one Dataverse config record
- spaarke-ai=Y: new Communications direct widget in @spaarke/ai-widgets; DataGrid framework change in @spaarke/ui-components consumed by SpaarkeAi widgets
- ci-workflows=N: no `.github/workflows/**` touches
- skill-directives=N: adds .claude/patterns/ui/record-modal-selection.md (pattern pointer) but no skill files modified
- root-CLAUDE-md=Y: added §17 pointer row in preparatory commit 367bdb4a1; no further changes expected during task execution
-->

---

## 1. Purpose

**Standardize how records open from SpaarkeAi workspace widgets, retire an unsupported iframe pattern, and fill the last missing entity-list widget.**

R1 established the reusable `DataverseEntityViewWidget` (Documents, Matters, Projects, Invoices, Work Assignments — dual-use Pattern D sections + direct widgets). R1 did NOT settle the row-click UX: today, clicking a row either opens an OOB dialog (`formDialog` config) OR opens a new browser tab. Neither behavior is intentional; both are inherited from the DataGrid framework's default `onRecordOpen`. Independently, `SmartTodoModal` iframe-embeds the OOB To Do main form inside `RecordNavigationModalShell` — a pattern Microsoft has explicitly stated is not supported (see §3.3).

R2 replaces both accidents with a deliberate two-layout standard, backfills the missing Communications widget, and locks the row-click behavior for every current and future workspace widget.

---

## 2. Product Statement

Every record opened from a Spaarke workspace widget follows one of two supported layouts. **Layout 1 is the canonical default; Layout 2 is a justified exception for preview-heavy, collection-browse content.** No third pattern (custom iframe of OOB `main.aspx`) ships in production. Configuration is maker-driven via `sprk_gridconfiguration`; the code adapters are one-time infrastructure.

---

## 3. Architecture — The Two Modal Layouts

### 3.1 Layout 1 (canonical default) — OOB modal + OOB main form

**Mechanism**: `Xrm.Navigation.navigateTo({ pageType: "entityrecord", entityName, entityId, formId? }, { target: 2, position: 1, width: {value: 80, unit: '%'}, height: {value: 85, unit: '%'} })`.

**What the user sees**: Native Power Apps modal, sized **85% × 85% centered** (the R2 standard for every Layout 1 open — Matter, Project, Invoice, Work Assignment, Communication, To Do), rendering the OOB main form with full business rules, subgrids, native ribbon, and Save / Save & Close.

**Modal size standard (binding)**: All Layout 1 opens use `width: { value: 85, unit: '%' }, height: { value: 85, unit: '%' }, position: 1`. One size for every entity; consistent workspace UX. Do not vary per-entity. Matches the OOB entity-list dialog precedent (e.g. the "All Active Events" page).

**Optional `formId`**: If `configjson.rowOpen.formId` is set on the grid configuration record, `navigateTo` receives it and Dataverse opens THAT specific main form variant instead of the user's default. Enables a future "Workspace" simplified form variant per entity without code change. This project ships the `formId` **capability**; the variant authoring is a separate future project.

**No cross-record browse**. This is a deliberate trade — user gets full form fidelity and platform-native editing in exchange for the browse UX.

**Support posture**: Fully supported. `Xrm.Navigation.navigateTo` is a documented Client API surface with a stable contract.

### 3.2 Layout 2 (justified exception) — proprietary modal + browse + proprietary content

**Mechanism**: `RecordNavigationModalShell` (from `@spaarke/ui-components`) wrapped in a Fluent v9 `<Dialog>`, hosting proprietary React content, with `currentIndex` + `navigationTotal` + `onNavigate` supplied to enable prev/next browse.

**What the user sees**: Fluent v9 modal with `<` / `>` chevrons + "N of M" counter at top, proprietary content in the body (e.g. `RichFilePreview` for documents), close via ✕ or ESC.

**Modal size**: Layout 2 sizing is **content-driven, not standardized to the Layout 1 85% × 85% value**. `RichFilePreviewDialog` today uses `max-width: 1280px` + `height: 85vh` — portrait-shaped to fit paper/PDF/Word document aspect ratio without wasted horizontal margin. Future Layout 2 consumers (media previews, chart viewers) size to their content type; the shell does not impose a fixed dimension. **Do not resize `RichFilePreviewDialog` to match the Layout 1 dimensions** — the portrait shape is deliberate.

**Justified exception criteria — ALL three must hold**:

1. Content is fundamentally NOT a form — it's preview / media / document / image / chart. OOB main form has no fidelity advantage because there is no form to render.
2. User workflow inherently involves paging a collection — "review the 25 documents on this matter", not "look at one record then close".
3. We have (or plan) a proprietary content component — we are NOT maintaining the exception by iframing OOB.

**Reference case**: `RichFilePreviewDialog` for document preview. Passes all three criteria. Ships unchanged in R2.

**Support posture**: The shell + its proprietary React content = fully supported (Fluent v9 in a Custom Page). The iframe of `main.aspx` is what's unsupported; Layout 2 does NOT iframe `main.aspx`.

### 3.3 The retired pattern — iframe-hosted OOB `main.aspx` inside the shell

`SmartTodoModal` today wraps `RecordNavigationModalShell` around an `<iframe src="…main.aspx?pagetype=entityrecord&…">` of the OOB To Do form, adding a cross-frame `postMessage` dirty-check protocol. This pattern:

- Is contractually unsupported per [Microsoft Learn (2025-05-07)](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/use-iframe-and-web-resource-controls-on-a-form): *"Displaying a form within an IFrame embedded in another form is not supported."*
- Works today because default `frame-ancestors 'self'` passes for same-origin same-tenant embedding
- Sits within the direction of platform tightening ([CSP strict-mode enforcement, late Jan 2026](https://learn.microsoft.com/en-us/power-platform/admin/content-security-policy); no relaxation in 2025/2026 wave plans)
- Is discouraged by community consensus (no MVP endorsement 2025–2026 found in [research 2026-07-01](../ai-spaarke-ai-workspace-UI-r2/notes/researcher-iframe-main-aspx-2026-07-01.md))

R2 retires this pattern. To Do moves to Layout 1. No new callers of the iframe-hosted OOB pattern are introduced. `RecordNavigationModalShell` and its cross-frame dirty-check protocol stay in the shared library (Layout 2 still uses the shell; the dirty-check protocol is cheap-to-maintain insurance for a hypothetical future BFF-hosted editable preview).

---

## 4. Scope Summary

### In scope

| # | Deliverable | Notes |
|---|---|---|
| 4.1 | Extend `sprk_gridconfiguration.configjson.rowOpen` schema with optional `formId` field | Backward-compatible; absence = existing behavior |
| 4.2 | Extend the `DataGrid` framework's default `onRecordOpen` to consume `formId` when present | Uses `Xrm.Navigation.navigateTo` (Layout 1) with `entityFormOptions.formId` |
| 4.3 | Migrate 5 existing workspace widgets to Layout 1 row-click behavior | Documents, Matters, Projects, Invoices, Work Assignments — sections + direct widgets |
| 4.4 | Add Communications workspace widget | New — entity `sprk_communication`; shows all communication types (Email / Teams / SMS / other); registered as both section (via `communications.registration.ts`) and direct widget (`communications-list`) |
| 4.5 | Retire `SmartTodoModal` iframe pattern | Convert every callsite to `Xrm.Navigation.navigateTo` (Layout 1); delete the modal component; retire the `useLaunchContext` shell-hosting wiring; keep `RecordNavigationModalShell` shared library component |
| 4.6 | Standards + pattern doc updates | See §7 |
| 4.7 | Verification / QA plan | See §8 |

### Out of scope

- **Authoring the Workspace form variants** in the Power Apps form designer (per-entity simplified main forms). Separate future maker project. R2 delivers only the config capability; all widgets ship using default main forms (i.e. omit `formId`).
- **Non-workspace surfaces** — ribbon-launched dialogs, form-embedded subgrid row-open behavior, Quick Create dialogs, model-driven app main-form row-open. Follow-on project after R2 ships.
- **Retiring or changing** `RichFilePreviewDialog`, Layout 2, or `RecordNavigationModalShell`. Layout 2 stays.
- **Retiring or changing** the Spaarke DataGrid framework itself. Only the row-click adapter path changes.
- **New form variants for Communications** — Communications ships opening its default OOB main form via Layout 1, same as every other entity.

### Follow-on work (out of scope for R2, tracked for future)

- **Authoring Workspace main form variants** (per entity, in the form designer) — high UX payoff once the `formId` capability from 4.1 exists
- **Non-workspace surface modal standardization** — apply the two-layout model to ribbon dialogs, subgrids, Quick Create, etc.
- **Reference the two-layout standard in `BUILD-A-NEW-WORKSPACE-WIDGET.md` archetype table** — done as part of §7.4 doc updates in R2 but the archetype table itself may benefit from expansion

---

## 5. Work Breakdown

### 5.1 `sprk_gridconfiguration.configjson.rowOpen` — add optional `formId`

**Schema change**: `configjson.rowOpen` today may hold `{ type: 'formDialog' | 'newTab' }`. Extend to `{ type: 'formDialog' | 'newTab', formId?: string }` (GUID of the specific form to open when `type === 'formDialog'`).

**Config-record migration**: Existing records unchanged (no `formId` field, behavior identical). New records / edited records can specify a `formId`.

**Documentation**: Update the `sprk_gridconfiguration` maker documentation to describe the new field and the "Workspace form variant" pattern this enables.

**Data-model change**: No column change to the Dataverse table — the field lives inside the existing `configjson` JSON column.

### 5.2 `<DataGrid>` framework — extend default `onRecordOpen`

**File**: [`src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx)

**Change**: The `defaultRecordOpen` callback (currently ~line 885–919) dispatches on `rowOpen.type`. When `type === 'formDialog'` and `rowOpen.formId` is present, pass `formId` in the `entityFormOptions` bag to `Xrm.Navigation.navigateTo`. When `type === 'newTab'` — decision point: R2 default is **to change** `type === 'newTab'` behavior to also call `Xrm.Navigation.navigateTo` (Layout 1) rather than `window.open`. Rationale: `newTab` was the accidental default, not a deliberate design choice; new-tab escape hatch is available at the OOB form's "Expand to full page" ⤴ button.

**Open question — see §9**: Should we hard-remove the `newTab` branch or leave it as a discouraged legacy path? R2 lean: keep the branch for one release, deprecate it in comments + docs, remove in a future project.

### 5.3 Migrate 5 existing widgets to Layout 1 row-click

**Files** (per widget, sections + registrations):

| Widget | Section registration | Direct widget registration |
|---|---|---|
| Documents | [`src/solutions/LegalWorkspace/src/sections/documents.registration.ts`](../../src/solutions/LegalWorkspace/src/sections/documents.registration.ts) | (existing in `WorkspaceWidgetRegistry`) |
| Matters | [`src/solutions/LegalWorkspace/src/sections/matters.registration.ts`](../../src/solutions/LegalWorkspace/src/sections/matters.registration.ts) | (existing) |
| Projects | [`src/solutions/LegalWorkspace/src/sections/projects.registration.ts`](../../src/solutions/LegalWorkspace/src/sections/projects.registration.ts) | (existing) |
| Invoices | [`src/solutions/LegalWorkspace/src/sections/invoices.registration.ts`](../../src/solutions/LegalWorkspace/src/sections/invoices.registration.ts) | (existing) |
| Work Assignments | [`src/solutions/LegalWorkspace/src/sections/workAssignments.registration.ts`](../../src/solutions/LegalWorkspace/src/sections/workAssignments.registration.ts) | (existing) |

**Change per widget**: None to the section / registration files themselves. Behavior change flows from the DataGrid default-handler update in §5.2 + `sprk_gridconfiguration` records having `rowOpen.type: 'formDialog'` set (already the case for most, needs verification per record).

**Config-record audit**: enumerate all 5 grid configuration records; confirm `rowOpen.type` is `'formDialog'` for each. If any are `'newTab'`, decide per-record whether they should switch. (Layout 1 is the standard, so almost certainly yes.)

### 5.4 Add Communications widget

**Entity**: `sprk_communication` (Spaarke custom table). Widget shows all communication types (Email / Teams / SMS / other). Type discrimination via `sprk_communicationtype` column shown in the grid, not per-widget filtering.

**Files to add**:

1. `src/solutions/LegalWorkspace/src/sections/communications.registration.ts` — section registration (mirrors invoices/projects/etc. shape)
2. Update `src/solutions/LegalWorkspace/src/sectionRegistry.ts` to include the new section
3. Register `communications-list` direct widget in `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts`

**Config record**: create a new `sprk_gridconfiguration` record for the Communications workspace view. Suggested columns: subject / summary, communication type, direction (in/out), sender, recipient(s), sent-on datetime, regarding record (matter/project/etc.), status. Query = "Active Communications" saved view or a synthesized fetchxml.

**Row-click**: Layout 1 — opens `sprk_communication`'s default OOB main form via `navigateTo`.

**Deployment note**: adding a new section + widget + config record. The config record is a Dataverse row — deploys via solution import or `dataverse-create-schema` skill.

### 5.5 Retire `SmartTodoModal` iframe pattern → Layout 1

**Callsite inventory**: enumerate every place that opens `SmartTodoModal` today. Preliminary: [`src/solutions/SmartTodo/src/hooks/useLaunchContext.ts`](../../src/solutions/SmartTodo/src/hooks/useLaunchContext.ts), the SmartTodo Code Page main entry, and any consumer in LegalWorkspace's `todo.registration.ts` / SmartToDo widget code path.

**Change per callsite**: Replace the `SmartTodoModal` open call with `Xrm.Navigation.navigateTo({ pageType: "entityrecord", entityName: "sprk_todo", entityId }, { target: 2, position: 1, width: {value: 85, unit: '%'}, height: {value: 85, unit: '%'} })` (Layout 1 standard size per §3.1).

**Delete**:

- [`src/solutions/SmartTodo/src/SmartTodoModal.tsx`](../../src/solutions/SmartTodo/src/SmartTodoModal.tsx) (or `components/Modal/SmartTodoModal.tsx` per audit)
- [`src/solutions/SmartTodo/src/hooks/useLaunchContext.ts`](../../src/solutions/SmartTodo/src/hooks/useLaunchContext.ts) if it existed solely for the iframe-hosting wiring (verify — some launch-context concerns may be legitimate and warrant preservation)
- Any test files that only tested the iframe pattern

**Retain**:

- `RecordNavigationModalShell` in `@spaarke/ui-components` — still consumed by `RichFilePreviewDialog` for Layout 2
- The cross-frame dirty-check protocol infrastructure inside the shell — cheap insurance for future BFF-hosted editable previews; no active consumer after this project
- The `sprk_todo` OOB main form itself — unchanged; users still open it, just via `navigateTo` instead of iframe

**Path A exception documentation** (per [CLAUDE.md §6.5](../../CLAUDE.md#65-adr-conflict-resolution-protocol-binding--added-2026-06-29)): No exception needed at project close — the iframe pattern is fully retired, not accepted as an ongoing exception. If any callsite proves technically unable to migrate, document as a Path A exception at THAT callsite with rationale and migration plan.

### 5.6 Documents preview — verification-only (no change)

`RichFilePreviewDialog` for document preview stays exactly as-is. It's the canonical Layout 2 reference case. Verify at project close that:

- Its rendering surface still iframes ONLY the BFF-served preview URL (which is Spaarke-owned, not `main.aspx`)
- `RecordNavigationModalShell` composition still receives correct `navigationTotal` + `currentIndex` + `onNavigate` props from callers (LegalWorkspace DocumentCard, SpaarkeAi DocumentViewerWidget, etc.)
- No regression in the preview / browse experience

---

## 6. Verification the two-layout standard is applied consistently

After all 5 widgets migrate + Communications adds + SmartTodoModal retires, verify:

- Every workspace widget row-click uses `Xrm.Navigation.navigateTo` (Layout 1) except Documents preview (Layout 2)
- No callsite in `src/solutions/**` or `src/client/**` remains that iframe-embeds `main.aspx?pagetype=entityrecord`
- `RecordNavigationModalShell` is still consumed by exactly ONE production caller: `RichFilePreviewDialog` (verified by grep)
- The DataGrid `defaultRecordOpen` no longer uses `window.open('_blank')` for any `sprk_gridconfiguration` record in production (see §5.2 open question)

---

## 7. Documentation updates

R2 clarifies the standard approach across five documentation surfaces. These updates ship as part of the project, not as follow-on work.

### 7.1 [`docs/standards/MODAL-DECISION-CRITERIA.md`](../../docs/standards/MODAL-DECISION-CRITERIA.md)

**Change**: Sharpen the decision tree to lead with the two-layout framing. Add:

- Explicit "Layout 1 is canonical default; Layout 2 is justified exception" language at the top
- The three-criteria gate for Layout 2 justification (from §3.2)
- Verbatim Microsoft Learn citation for the iframe anti-pattern: *"Displaying a form within an IFrame embedded in another form is not supported."* — [source, updated 2025-05-07](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/use-iframe-and-web-resource-controls-on-a-form)
- CSP direction-of-travel fact: default `frame-ancestors 'self' https://*.powerapps.com`; strict mode rolled out late Jan 2026 (see `notes/researcher-iframe-main-aspx-2026-07-01.md`)
- Remove the "canonical hybrid" framing of `SmartTodoModal` (it's now retired)

### 7.2 [`.claude/patterns/ui/record-modal-selection.md`](../../.claude/patterns/ui/record-modal-selection.md)

**Change**: Update the 25-line pointer file to lead with "Layout 1 default, Layout 2 exception" phrasing. Add pointer to `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` for the `onRecordOpen` + `formId` convention.

### 7.3 [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md)

**Change**: Add a section on row-click behavior for entity-list widgets. Every new list widget defaults to Layout 1 via `sprk_gridconfiguration.configjson.rowOpen: { type: 'formDialog' }`. Cite Communications widget as the reference example added in R2.

### 7.4 [`docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../../docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md)

**Change**: Document the `onRecordOpen` override contract and the new `configjson.rowOpen.formId` field. Note that `formId` is optional (absence = user default main form) and that authoring form variants is a separate maker task.

### 7.5 [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md)

**Change**: Add a short section on modal UX for widgets that open records: reference the two-layout standard, cite `MODAL-DECISION-CRITERIA.md`.

---

## 8. Success criteria

R2 ships successfully when ALL of these are true:

1. **Layout 1** — every workspace widget row-click opens the OOB record in an OOB modal at 80×85% via `Xrm.Navigation.navigateTo`. Exception: Documents preview (Layout 2).
2. **`formId` capability** — `sprk_gridconfiguration.configjson.rowOpen.formId` is honored when present; absence falls back to user's default main form.
3. **Communications widget** — visible as both a section (in `LegalWorkspaceApp` layouts) and a direct widget (in `WorkspaceWidgetRegistry`); shows all `sprk_communication` types; opens records via Layout 1.
4. **SmartTodoModal retired** — component deleted; no callsite iframes `main.aspx`; To Do opens via Layout 1; existing To Do UX (Kanban, list, filters) unchanged.
5. **Documents preview unchanged** — `RichFilePreviewDialog` still shows document preview with browse across matter documents; Layout 2 reference case still works.
6. **Documentation updates** — all five docs in §7 reflect the two-layout standard.
7. **Repo grep** — no `iframe.*main\.aspx` in `src/solutions/` or `src/client/` outside archived / test-fixture paths.
8. **CI green** — no test regressions; type checks clean.
9. **Manual QA** — smoke test per widget: click a row, confirm OOB modal opens, confirm form fidelity is intact, confirm Save & Close works.

---

## 9. Open questions

### 9.1 The `newTab` branch in `DataGrid.defaultRecordOpen`

Per §5.2: today the DataGrid falls back to `window.open(url, '_blank')` when `rowOpen.type !== 'formDialog'`. R2 could:

- **(a)** Keep the branch, mark deprecated, remove in a future project
- **(b)** Rewrite the branch to also call `Xrm.Navigation.navigateTo` (Layout 1) — effectively "any row click is Layout 1"
- **(c)** Delete the branch entirely; require `type: 'formDialog'` on every config record

Recommendation: **(b)**. The DataGrid should never new-tab from a workspace widget — that breaks the workspace UX by ejecting the user to a new browser tab. If a config record has `type: 'newTab'` (probably none in production, verify), the new default reinterprets it as Layout 1. Escape hatch to full page is available via the OOB form's ⤴ Expand button. Confirm at task-execute time or defer.

### 9.2 `SmartTodoModal` callsite count

Preliminary inventory identified 2–3 callsites; final enumeration happens at task time. If any callsite has business logic beyond "open the To Do" (e.g. context menus, kanban card modals with extended behavior), migration may need a small proprietary UX preserved outside the modal (still Layout 1 for the record open itself). Flag any such case for Path A exception documentation.

### 9.3 `RecordNavigationModalShell` dirty-check protocol — retain?

R2 default: **retain** (see §3.3 and §5.5). Cheap to maintain, provides insurance for future BFF-hosted editable preview. If a code-review or repo hygiene pass in a future project shows the protocol is genuinely dead weight, retire it then.

---

## 10. Cross-references

### Architectural authorities

- [`docs/standards/MODAL-DECISION-CRITERIA.md`](../../docs/standards/MODAL-DECISION-CRITERIA.md) — the standard being sharpened in §7.1
- [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — the two-wrapper dual-use Pattern D model
- [`docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../../docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md) — the DataGrid framework being extended in §5.2
- [`src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/README.md`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/README.md) — Layout 2 component authoritative reference

### Related ADRs and constraints

- [ADR-012 — Shared component library](../../.claude/adr/ADR-012-shared-component-library.md) — shared library boundary rules
- [ADR-021 — Fluent UI v9](../../.claude/adr/ADR-021-fluent-ui-v9.md) — Fluent v9 exclusivity
- [ADR-028 — Spaarke Auth v2](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — no token snapshots in modal props
- [CLAUDE.md §6.5 — ADR Conflict Resolution Protocol](../../CLAUDE.md#65-adr-conflict-resolution-protocol-binding--added-2026-06-29) — Path A exception documentation for callsites that can't migrate
- [CLAUDE.md §10 — BFF Hygiene](../../CLAUDE.md#10-bff-hygiene--binding-governance-read-before-adding-to-sprkbffapi) — not touched in this project (no BFF changes anticipated)

### R1 predecessor

- [`../ai-spaarke-ai-workspace-UI-r1/design.md`](../ai-spaarke-ai-workspace-UI-r1/design.md) — established `DataverseEntityViewWidget` foundation; row-click behavior was NOT settled in R1

### Research artifact

- `notes/researcher-iframe-main-aspx-2026-07-01.md` (to be created) — evidence trail for the "iframe `main.aspx` not supported" finding that drives §3.3

---

## 11. What this document does NOT contain

Following the R1 precedent (ad-hoc UX project, no `/project-pipeline`), this document is a **design document**, not a spec. A `spec.md` and task decomposition can follow via `/design-to-spec` + `/project-pipeline` if the scope requires it — but given the discrete, well-bounded work here, direct task execution from this design doc may be sufficient. Operator decision at project start.

---

*Design document. Updates trigger a re-review of §4 (scope), §7 (doc updates), and §8 (success criteria) at minimum.*
