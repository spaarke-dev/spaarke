# AI SpaarkeAi Workspace UI R2 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-01
> **Source**: [`design.md`](design.md)
> **Predecessor**: [`../ai-spaarke-ai-workspace-UI-r1/design.md`](../ai-spaarke-ai-workspace-UI-r1/design.md) (established `DataverseEntityViewWidget` foundation)
> **PR strategy**: **3 phased PRs** (per owner clarification 2026-07-01) — see §Phasing

---

## Executive Summary

R2 standardizes how records open from SpaarkeAi workspace widgets by adopting a two-layout modal standard (Layout 1 canonical / Layout 2 justified exception), backfilling the missing Communications workspace widget, and retiring the contractually-unsupported iframe-hosted OOB `main.aspx` pattern currently in `SmartTodoModal`. All 6 workspace widgets (5 existing + 1 new) row-click through `Xrm.Navigation.navigateTo` (Layout 1); Documents preview retains `RichFilePreviewDialog` (Layout 2 reference case).

---

## Phasing

R2 ships as **3 phased PRs**, ordered by risk containment:

| Phase | PR contents | FRs |
|---|---|---|
| **PR 1 — Framework + widget migration** | `sprk_gridconfiguration.configjson.rowOpen` schema extension + DataGrid framework changes + 5 existing widgets adopt Layout 1 | FR-01 → FR-08 |
| **PR 2 — Communications widget** | Add `sprk_communication` section + direct widget + config record | FR-09 → FR-11 |
| **PR 3 — SmartTodoModal retirement + documentation** | Retire iframe pattern for To Do + 5 documentation updates | FR-12 → FR-19 |

Each PR is independently verifiable, mergeable, and reversible. Ordering: DataGrid framework changes precede widget migrations; Communications widget precedes SmartTodoModal retirement to avoid one merge dominating review attention.

---

## Scope

### In Scope

- Extend `sprk_gridconfiguration.configjson.rowOpen` JSON schema to accept optional `formId` field (backward-compatible)
- Rewrite Spaarke DataGrid framework's `defaultRecordOpen` behavior: any row-click routes through `Xrm.Navigation.navigateTo` (Layout 1); prior `window.open('_blank')` branch retired
- Migrate 5 existing workspace widgets (Documents, Matters, Projects, Invoices, Work Assignments) — sections + direct widgets — to Layout 1 row-click via the framework change
- Add Communications workspace widget (`sprk_communication` entity, all types — Email/Teams/SMS/other) as both section registration AND direct widget
- Create Communications `sprk_gridconfiguration` record with default columns per design §5.4
- Retire `SmartTodoModal` iframe-hosted-OOB pattern; every callsite converts to `Xrm.Navigation.navigateTo` (Layout 1)
- Delete `SmartTodoModal.tsx` and associated iframe-hosting wiring; retain `RecordNavigationModalShell` shared library component (still consumed by `RichFilePreviewDialog`)
- Sharpen 5 documentation surfaces to reflect the two-layout standard and the Microsoft Learn iframe anti-pattern citations

### Out of Scope

- Authoring per-entity "Workspace" simplified main form variants in the Power Apps form designer (separate future maker project; R2 delivers only the `formId` capability)
- Retiring or refactoring `RichFilePreviewDialog`, `RecordNavigationModalShell`, or Layout 2 itself
- Non-workspace surfaces (ribbon-launched dialogs, form-embedded subgrid row-open, Quick Create dialogs) — deferred to a follow-on project
- Retiring the Spaarke DataGrid framework or altering its public `onRecordOpen` prop contract (only the default handler behavior changes)
- New form authoring for Communications (default main form already exists per owner clarification 2026-07-01)
- BFF changes (no BFF work anticipated; project scope is client-side)
- Removing the cross-frame dirty-check protocol from `RecordNavigationModalShell` (kept as cheap insurance)

### Affected Areas

- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx` — extend default `onRecordOpen`
- `src/solutions/LegalWorkspace/src/sections/documents.registration.ts` — verify config record `rowOpen.type: 'formDialog'`
- `src/solutions/LegalWorkspace/src/sections/matters.registration.ts` — verify
- `src/solutions/LegalWorkspace/src/sections/projects.registration.ts` — verify
- `src/solutions/LegalWorkspace/src/sections/invoices.registration.ts` — verify
- `src/solutions/LegalWorkspace/src/sections/workAssignments.registration.ts` — verify
- `src/solutions/LegalWorkspace/src/sections/communications.registration.ts` — NEW
- `src/solutions/LegalWorkspace/src/sectionRegistry.ts` — add Communications registration
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` — add `communications-list` direct widget
- `src/solutions/SmartTodo/**` — delete iframe-hosting wiring, convert callsites to `navigateTo`
- `docs/standards/MODAL-DECISION-CRITERIA.md` — sharpen
- `.claude/patterns/ui/record-modal-selection.md` — update
- `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` — add row-click section
- `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` — document `onRecordOpen` + `formId`
- `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` — cross-ref modal standard
- Dataverse: 1 new `sprk_gridconfiguration` record (Communications); 5 existing records verified

---

## Requirements

### Functional Requirements

**Phase 1 — Framework + widget migration (PR 1)**

1. **FR-01**: `sprk_gridconfiguration.configjson.rowOpen` schema accepts optional `formId: string` field. Field is a Dataverse form GUID; absence means "use user's default main form."
   *Acceptance*: A `sprk_gridconfiguration` record with `rowOpen: { type: 'formDialog', formId: '<guid>' }` deserializes without error; record without `formId` still deserializes with identical behavior to today.

2. **FR-02**: DataGrid framework's `defaultRecordOpen` reads `rowOpen.formId` from configjson and passes it to `entityFormOptions.formId` on the `Xrm.Navigation.navigateTo` call when present.
   *Acceptance*: Given config record with `formId`, clicking a row opens the specified form variant; given config record without `formId`, clicking opens the user's default main form.

3. **FR-03**: DataGrid framework's `defaultRecordOpen` rewrites the `type !== 'formDialog'` branch (previously `window.open('_blank')`) to call `Xrm.Navigation.navigateTo` (Layout 1). All row-clicks from Spaarke DataGrids now open as OOB modals, regardless of `type` value.
   *Acceptance*: For any `sprk_gridconfiguration` record with any `rowOpen.type` value, clicking a row opens an OOB modal (target: 2) at **85% × 85% centered** (Layout 1 standard size — see FR-20) via `Xrm.Navigation.navigateTo`. Verified via manual QA against at least one config record with legacy `type: 'newTab'` value if any exist.

4. **FR-04**: Documents widget row-click opens Layout 1 (OOB modal + OOB default main form for `sprk_document`).
   *Acceptance*: Click a document row in the Documents section OR the `documents-list` direct widget → OOB modal opens at **85% × 85%** (Layout 1 standard) showing the Document main form; ESC or Save & Close returns to the workspace.

5. **FR-05**: Matters widget row-click opens Layout 1 for `sprk_matter`.
   *Acceptance*: As FR-04 but for `sprk_matter`.

6. **FR-06**: Projects widget row-click opens Layout 1 for `sprk_project`.
   *Acceptance*: As FR-04 but for `sprk_project`.

7. **FR-07**: Invoices widget row-click opens Layout 1 for `sprk_invoice`.
   *Acceptance*: As FR-04 but for `sprk_invoice`.

8. **FR-08**: Work Assignments widget row-click opens Layout 1 for `sprk_workassignment`.
   *Acceptance*: As FR-04 but for `sprk_workassignment`.

**Phase 2 — Communications widget (PR 2)**

9. **FR-09**: A Communications section registration is added at `src/solutions/LegalWorkspace/src/sections/communications.registration.ts` and included in `sectionRegistry.ts`. Section mounts `<DataverseEntityViewWidget entityName="sprk_communication" configId="<new-guid>" />`.
   *Acceptance*: A user-authored LegalWorkspace layout can include the Communications section; it renders a list of communications with correct columns per FR-11.

10. **FR-10**: A Communications direct widget (`communications-list`) is registered in `WorkspaceWidgetRegistry` via `register-workspace-widgets.ts`. Widget type is a standalone workspace tab dispatch target.
    *Acceptance*: `PaneEventBus` `widget_load` dispatches for `type: 'communications-list'` render the widget in a Workspace tab.

11. **FR-11**: A new `sprk_gridconfiguration` record is created for the Communications workspace view. Suggested columns: subject/summary, `sprk_communicationtype`, direction (inbound/outbound), sender, recipient(s), sent-on datetime, regarding record, status. `rowOpen.type: 'formDialog'`, `formId` omitted.
    *Acceptance*: Communications widget renders the configured columns; row-click opens Layout 1 with `sprk_communication`'s default main form.

**Phase 3 — SmartTodoModal retirement + documentation (PR 3)**

12. **FR-12**: SmartTodoModal callsites are fully enumerated. Preliminary count: 2–3 (per design §9.2); final list produced at task-execute time.
    *Acceptance*: A documented callsite list is committed to `notes/smart-todo-modal-callsites.md` or the task file; grep of `SmartTodoModal` returns only historical (retired) references after this phase completes.

13. **FR-13**: Every SmartTodoModal callsite converts to `Xrm.Navigation.navigateTo({ pageType: "entityrecord", entityName: "sprk_todo", entityId }, { target: 2, position: 1, width: {value: 85, unit: '%'}, height: {value: 85, unit: '%'} })` (Layout 1 standard size per FR-20).
    *Acceptance*: Each callsite opens the To Do OOB main form via Layout 1; no callsite iframes `main.aspx`.

14. **FR-14**: `SmartTodoModal.tsx` and iframe-hosting wiring (e.g. `useLaunchContext.ts` if solely for this pattern) are deleted from the repository. `RecordNavigationModalShell` remains in `@spaarke/ui-components` unchanged.
    *Acceptance*: Files no longer present in `src/`; imports resolve cleanly; type check passes; `RichFilePreviewDialog` still functions (Documents preview unchanged).

15. **FR-15**: [`docs/standards/MODAL-DECISION-CRITERIA.md`](../../docs/standards/MODAL-DECISION-CRITERIA.md) is sharpened to lead with "Layout 1 canonical default; Layout 2 justified exception" framing. Adds verbatim Microsoft Learn quote (*"Displaying a form within an IFrame embedded in another form is not supported"* — 2025-05-07), CSP direction-of-travel fact, and the 3-criteria Layout 2 justification gate. Removes the "canonical hybrid" framing of SmartTodoModal.
    *Acceptance*: Doc reflects two-layout standard; MS Learn citations present with dates; no reference to SmartTodoModal as a positive pattern.

16. **FR-16**: [`.claude/patterns/ui/record-modal-selection.md`](../../.claude/patterns/ui/record-modal-selection.md) pattern pointer is updated for the two-layout framing.
    *Acceptance*: Pointer file (≤25 lines) uses Layout 1 / Layout 2 language; cross-links to updated `MODAL-DECISION-CRITERIA.md`.

17. **FR-17**: [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) gains a section on row-click behavior. Every new list widget defaults to Layout 1 via `sprk_gridconfiguration.configjson.rowOpen: { type: 'formDialog' }`. Communications widget cited as reference example.
    *Acceptance*: New section added; example present; Layout 2 exception criteria referenced.

18. **FR-18**: [`docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../../docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md) documents the `onRecordOpen` override contract and the new `configjson.rowOpen.formId` field.
    *Acceptance*: Docs reflect the framework's actual behavior post-FR-01/FR-02/FR-03.

19. **FR-19**: [`docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) adds a modal-UX section cross-referencing `MODAL-DECISION-CRITERIA.md`.
    *Acceptance*: Doc has an anchor to the modal standard; framing of two-wrapper model aligns with two-layout language.

**Cross-cutting**

20. **FR-20 (Modal size standard — binding)**: Every Layout 1 open in R2 uses **`width: { value: 85, unit: '%' }, height: { value: 85, unit: '%' }, position: 1`**. One size for every entity (Documents, Matters, Projects, Invoices, Work Assignments, Communications, To Do). Do not vary per-entity. Applies to FR-03 through FR-13. Documented in the `MODAL-DECISION-CRITERIA.md` sharpening (FR-15) and the DataGrid framework doc (FR-18).
    *Acceptance*: Grep verification — every `Xrm.Navigation.navigateTo` callsite touched by R2 uses the exact size options above; deviations (if any) are documented as Path A exceptions with rationale per CLAUDE.md §6.5.

21. **FR-21 (Layout 2 dimension retention — binding)**: `RichFilePreviewDialog` retains its current portrait-oriented dimensions (`max-width: 1280px`, `height: 85vh`). **Do NOT resize Layout 2 to match the Layout 1 85% × 85% standard.** The portrait shape fits document / PDF / Word paper aspect ratio without wasted horizontal margin. Future Layout 2 consumers (media / chart previews) size to their content type; the shell imposes no fixed dimension.
    *Acceptance*: `RichFilePreviewDialog` rendered dimensions before R2 == after R2 (measured via DevTools or screenshot compare); no code change to the dialog's size props.

### Non-Functional Requirements

- **NFR-01**: **Zero regression** in existing workspace widget UX after each PR merges. Existing rendering, filtering, sorting, and view configuration behavior unchanged.
- **NFR-02**: **Only supported Client API** used for record open — `Xrm.Navigation.navigateTo` per current Microsoft Learn reference (2026-04-09). No `Xrm.Navigation.openForm` alternative unless required for backward compatibility, and if so, documented as exception.
- **NFR-03**: **No new iframe of `main.aspx`** introduced anywhere in `src/`. Grep verification at project close: `grep -r "main\.aspx" src/solutions/ src/client/` returns zero matches outside archived/test-fixture paths.
- **NFR-04**: **Fluent v9 conformance** for any new UI (ADR-021 binding). No `@fluentui/react` (v8) imports; semantic tokens only for any styling touches.
- **NFR-05**: **Shared library boundary respected** (ADR-012). No PCF-specific dependencies imported into `@spaarke/ui-components`. Communications widget in `@spaarke/ai-widgets` uses `Xrm.WebApi` via `XrmDataverseClient` (matches R1 `DataverseEntityViewWidget` pattern).
- **NFR-06**: **No token snapshots** in modal props (ADR-028). If any modal launch reaches BFF, pass `authenticatedFetch` as function dependency.
- **NFR-07**: **`RecordNavigationModalShell` interface unchanged** — public props contract stays identical; `RichFilePreviewDialog` continues to work without modification.
- **NFR-08**: **DataGrid framework public API unchanged** — the `onRecordOpen` prop contract remains as-is; only the DEFAULT handler behavior changes. Consumers that pass their own `onRecordOpen` are unaffected.
- **NFR-09**: **Each PR independently mergeable** — PR 1 (framework + migration) can ship without PR 2 (Communications) or PR 3 (SmartTodoModal retirement); PR 2 can ship without PR 3.

---

## Technical Constraints

### Applicable ADRs

- **ADR-006** — PCF over web resources. SmartTodo retirement retires an iframe pattern, not a PCF; no ADR-006 change.
- **ADR-012** — Shared component library. Communications widget's list rendering lives in `@spaarke/ai-widgets` (via `DataverseEntityViewWidget`); section registration lives in `LegalWorkspace` solution (thin shim, per Pattern D).
- **ADR-021** — Fluent UI v9 exclusive. All new UI code Fluent v9 semantic tokens only.
- **ADR-028** — Spaarke Auth v2. No token snapshots. Modal launches via `Xrm.Navigation` do not require BFF token acquisition (host-context call).

### MUST Rules (from ADRs and this project's design)

- ✅ MUST use `Xrm.Navigation.navigateTo` for entity-record modal launch (Layout 1)
- ✅ MUST NOT introduce any new `<iframe src="…main.aspx?pagetype=entityrecord…">` in `src/` (NFR-03)
- ✅ MUST preserve `RecordNavigationModalShell` in `@spaarke/ui-components` (Layout 2 dependency)
- ✅ MUST preserve `RichFilePreviewDialog` behavior for document preview (NFR-01, NFR-07)
- ✅ MUST NOT break the DataGrid framework's public `onRecordOpen` prop contract (NFR-08)
- ✅ MUST keep every PR independently mergeable (NFR-09)
- ✅ MUST cite Microsoft Learn iframe anti-pattern statement verbatim in `MODAL-DECISION-CRITERIA.md` sharpening (FR-15)

### Existing Patterns to Follow

- **`DataverseEntityViewWidget`** (established in R1) — canonical shared-lib widget pattern for entity-list workspace widgets. Communications widget consumes this component, not a bespoke implementation.
- **Pattern D dual-use** (per [`SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) §4) — every workspace widget registers both as a section (LegalWorkspace registry) AND as a direct widget (`WorkspaceWidgetRegistry`). Communications follows this pattern.
- **Spaarke DataGrid framework** — `<DataGrid configId="…" />` composition, config resolution from `sprk_gridconfiguration`, `onRecordOpen` escape hatch. See [`SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../../docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md).
- **`RichFilePreviewDialog` + `RecordNavigationModalShell`** — Layout 2 reference case for document preview. See [`RecordNavigationModalShell/README.md`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/README.md).
- **Section registration shape** — `documents.registration.ts` and siblings show the current pattern. `communications.registration.ts` mirrors this exactly.

---

## ADR Tensions (per CLAUDE.md §6.5)

> **No ADR tensions surfaced at design time.**
>
> All listed ADRs apply without exception. The R2 direction (Layout 1 canonical + Layout 2 justified exception + retire iframe pattern) resolves an internal standards drift rather than a formal ADR conflict:
>
> - `SmartTodoModal` was previously described as "canonical hybrid" reference in `MODAL-DECISION-CRITERIA.md`. The researcher note ([`notes/researcher-iframe-main-aspx-2026-07-01.md`](notes/researcher-iframe-main-aspx-2026-07-01.md)) established that the iframe pattern is contractually unsupported per Microsoft Learn. R2 sharpens the standard (FR-15) and retires the pattern (FR-12 → FR-14) in one motion.
> - No Path A (project-scoped exception) is invoked because the pattern is being fully retired, not accepted.
> - No Path B (ADR amendment) is needed because no ADR asserts that iframe-hosted OOB is required.
> - Path C (pivot to comply) applies to the whole project by construction — every deliverable aligns to the two-layout standard.
>
> This section may be updated if tensions emerge during implementation.

---

## Success Criteria

Verifiable pass/fail conditions. Each criterion has an explicit verification method.

1. [ ] **Every workspace widget row-click uses Layout 1** — Verify by: manual QA per widget (Documents, Matters, Projects, Invoices, Work Assignments, Communications); grep `src/` for `window.open.*_blank` in DataGrid framework returns zero matches.
2. [ ] **`formId` capability is honored when present** — Verify by: create a test `sprk_gridconfiguration` record with `formId` GUID; click a row; confirm the specified form variant renders.
3. [ ] **Absent `formId` falls back to user default** — Verify by: existing config records (no `formId`) still open the default main form; A/B compare pre-R2 vs post-R2 behavior on Documents widget.
4. [ ] **Communications widget renders correctly** — Verify by: mount Communications direct widget in a Workspace tab; verify columns per FR-11; click a row and confirm Layout 1 opens `sprk_communication` main form.
5. [ ] **Communications section usable in LegalWorkspace layouts** — Verify by: use `WorkspaceLayoutWizard` to author a layout including Communications; render and verify.
6. [ ] **SmartTodoModal retired** — Verify by: `grep -r "SmartTodoModal" src/` returns zero matches (excluding retired-callsite comments); every To Do launcher opens Layout 1.
7. [ ] **Documents preview unchanged** — Verify by: click a document, confirm `RichFilePreviewDialog` opens with prev/next browse; verify against pre-R2 screenshot / recording.
8. [ ] **`RecordNavigationModalShell` retained + unchanged** — Verify by: grep confirms one production consumer (`RichFilePreviewDialog`); public props contract unchanged; type check passes for external consumers.
9. [ ] **All 5 doc surfaces updated** — Verify by: manual read of each; MS Learn citations present with dates; two-layout language consistent across.
10. [ ] **No iframe of `main.aspx` in production code** — Verify by: `grep -r "main\.aspx" src/solutions/ src/client/` returns zero matches outside archived/test-fixture paths.
11. [ ] **CI green after each PR** — Verify by: build + unit tests pass; no type errors; no lint regressions; existing DataGrid tests + widget tests pass.
12. [ ] **Each PR independently mergeable** — Verify by: PR 1 branch can merge to master without PR 2 or PR 3; likewise for PR 2 without PR 3.
13. [ ] **Layout 1 modal size is 85% × 85% everywhere** — Verify by: `grep -rE "width.*value.*(85|80).*unit.*%.*height.*value.*(85|80).*unit.*%" src/` returns only the standard `85, 85` combination for R2-touched callsites; no `80% × 85%` or other variants remain.
14. [ ] **Layout 2 dimensions preserved** — Verify by: `RichFilePreviewDialog` still renders at `max-width: 1280px, height: 85vh`; A/B compare before/after screenshots show identical document-preview dimensions.

---

## Dependencies

### Prerequisites

- **`DataverseEntityViewWidget`** (from R1) exists and functions — canonical widget for Documents/Matters/Projects/Invoices/Work Assignments; Communications will consume it too
- **`RecordNavigationModalShell`** exists in `@spaarke/ui-components` — kept as-is, no changes required
- **`RichFilePreviewDialog`** exists and consumes the shell for document preview browse — kept as-is
- **`sprk_communication` entity** exists with a working default main form (per owner clarification 2026-07-01) — R2 does not author or modify this form
- **Spaarke DataGrid framework** exposes the `onRecordOpen` prop and default handler — public API stays unchanged; only default handler behavior evolves
- **5 existing `sprk_gridconfiguration` records** (Documents, Matters, Projects, Invoices, Work Assignments) — verified during Phase 1 that `rowOpen.type: 'formDialog'` is set; per-record fixes if needed

### External Dependencies

- **None**. No BFF endpoint changes, no Azure resource changes, no external API integrations, no NuGet or npm package additions anticipated. All work is client-side + one Dataverse configuration record.

---

## Owner Clarifications

Answers captured during the `/design-to-spec` interview on 2026-07-01:

| Topic | Question | Answer | Impact |
|---|---|---|---|
| DataGrid `newTab` branch (§9.1) | Which resolution for R2? | **Rewrite to Layout 1** | FR-03 unifies row-click behavior; all row-clicks go through `Xrm.Navigation.navigateTo` regardless of legacy `rowOpen.type` value; escape hatch is OOB Expand button |
| Communications entity readiness | Does `sprk_communication` have a working default main form? | **Yes — has working main form** | No form-authoring task added to R2 scope; Communications widget opens the existing default form via Layout 1 |
| PR strategy | Single PR or phased? | **Phased — 3 PRs** | Scope decomposed into 3 sequential PRs (framework/migration → Communications → SmartTodoModal + docs); each independently verifiable and mergeable per NFR-09 |

---

## Assumptions

Items where the owner did not explicitly specify — proceeding with these assumptions. Flag any that turn out to be incorrect during task execution and adjust scope.

- **SmartTodoModal callsite count**: Assuming 2–3 callsites (per design §9.2 preliminary inventory). Final enumeration happens at task-execute time; if actual count is >5 or if any callsite has substantive business logic that resists trivial replacement, project scope re-evaluated. Affects FR-12, FR-13.
- **Communications config record columns**: Assuming the suggested columns from design §5.4 (subject/summary, communication type, direction, sender, recipient(s), sent-on, regarding, status). Adjust per maker feedback at task time. Affects FR-11.
- **Communications config record deployment mechanism**: Assuming the [`dataverse-create-schema`](../../.claude/skills/dataverse-create-schema/SKILL.md) skill or equivalent Web API + PowerShell approach for creating the `sprk_gridconfiguration` row (not a full solution import). Affects FR-11 task shape.
- **Existing config records already have `type: 'formDialog'`**: Assuming the 5 existing widget config records (Documents, Matters, Projects, Invoices, Work Assignments) already have `rowOpen.type: 'formDialog'` set. Task 001 verifies; if any are `'newTab'`, they are updated to `'formDialog'` as part of Phase 1. FR-03 renders this moot regardless (all types resolve to Layout 1) — but explicit correction still worth doing for clarity.
- **No new test framework required**: Assuming existing test infrastructure (Vitest + Testing Library for shared lib; existing DataGrid tests) is sufficient; regression tests added to touched code where tests already exist; no new test framework introduced.
- **QA is manual + smoke test per widget**: Assuming project-close QA is a manual walkthrough of each widget per success criteria §1–7, not an automated E2E suite. Consistent with R1 project close.
- **PR review gates**: Assuming standard code-review + adr-check per CLAUDE.md §4 rigor rules; no special approvals needed beyond normal workflow.

---

## Unresolved Questions

Items still open at spec close. None block spec completion; each is resolved during task-execute at the noted task boundary.

- [ ] **Exact SmartTodoModal callsite list** — Blocks: Phase 3 task decomposition. Resolved: at start of Phase 3 (task PR3-01 enumerates callsites).
- [ ] **Config-record audit results** — Blocks: FR-04 through FR-08 verification. Resolved: at start of Phase 1 (task PR1-01 lists all 5 records + confirms `rowOpen.type`).
- [ ] **Communications config record columns — final confirmation** — Blocks: FR-11. Resolved: at task PR2-03 (or earlier if a maker specifies via Discord/comment before then).
- [ ] **`useLaunchContext.ts` retention decision** — Blocks: FR-14. Resolved: at task PR3-03 (inspect file; if solely for iframe pattern → delete; if has non-iframe concerns → refactor to preserve those, remove iframe pieces).

---

## Cross-references

- **Design source**: [`design.md`](design.md)
- **Research evidence**: [`notes/researcher-iframe-main-aspx-2026-07-01.md`](notes/researcher-iframe-main-aspx-2026-07-01.md)
- **R1 predecessor**: [`../ai-spaarke-ai-workspace-UI-r1/design.md`](../ai-spaarke-ai-workspace-UI-r1/design.md)
- **Standards being sharpened**: [`../../docs/standards/MODAL-DECISION-CRITERIA.md`](../../docs/standards/MODAL-DECISION-CRITERIA.md)
- **Related architecture**: [`../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) · [`../../docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../../docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md)
- **CLAUDE.md protocols**: [§6.5 ADR Conflict Resolution](../../CLAUDE.md#65-adr-conflict-resolution-protocol-binding--added-2026-06-29) · [§10 BFF Hygiene](../../CLAUDE.md#10-bff-hygiene--binding-governance-read-before-adding-to-sprkbffapi) (not applicable — no BFF work) · [§11 Component Justification](../../CLAUDE.md#11-component-justification--default-to-reuse-binding) (Communications widget CONSUMES existing `DataverseEntityViewWidget`; no new component authored)

---

*AI-optimized specification. Original design: [`design.md`](design.md). Generated 2026-07-01 via `/design-to-spec`.*
