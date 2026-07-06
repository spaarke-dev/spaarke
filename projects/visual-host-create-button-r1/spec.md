# Visual Host "+" Create Button — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-05
> **Source**: `design.md` (rev 2)
> **Project**: `visual-host-create-button-r1`

## Executive Summary

Add a maker-configurable **"+" toolbar button** to the Visual Host PCF that opens the appropriate Create wizard for the entity a visual represents, launched from and **auto-associated to the host record**. Wizards follow the **standard Spaarke wizard template** (Associate To → Add Files → Enter Info → Next Steps), use the **ADR-024 polymorphic resolver** for parent association, **dual-bind uploaded documents** to both host and child, and offer **Send Email / Add To Do / Assign Work** follow-ons. The project also **consolidates the duplicated Next-Steps/follow-on UI into one shared `WizardFollowOns` module** across all four wizard families. AI prefill ships as an **inert seam** (no BFF work this release).

## Scope

### In Scope

- Read two **already-existing** columns on `sprk_chartdefinition`: `sprk_createwizardenabled` (bool), `sprk_createwizardkey` (text). (No schema creation — columns confirmed present.)
- Visual Host "+" toolbar button in `CardChrome.tsx` + legacy `VisualHostRoot.tsx` path, gated on `createWizardEnabled`.
- `WizardRegistry` dispatcher (lazy-loaded, key → wizard component) + `WizardHostProps` contract.
- **Migrate `CreateEventWizard`/`eventService` onto `applyResolverFields`** (ADR-024 compliance fix) and wire it to the `event` key.
- Two new wizards on the standard template: **`CreateInvoiceWizard`** (`sprk_invoice`) and **`CreateKPIAssessmentWizard`** (`sprk_kpiassessment`), each with a service.
- **Auto-association** from the host record: `initialAssociation` seed + `lockAssociation` (hides step 1 when launched from Visual Host).
- **Polymorphic association** via `PolymorphicResolverService.applyResolverFields` (entity-specific lookup + all 4 resolver fields).
- **Schema delta** (Dataverse): **none.** All resolver fields (KPI created by owner; Invoice pre-existing) and chart-def columns already exist. KPI has no files step, so no `sprk_document` KPI lookup is needed.
- **File dual-bind (Event + Invoice only)**: extend `EntityCreationService.createDocumentRecords` for a second `@odata.bind` so one `sprk_document` links to both host and child. **KPI Assessment has no files step** (owner: KPI needs no documents).
- **`WizardFollowOns` shared module** (`FollowOnGrid` + reusable follow-on steps incl. net-new `AddTodoFollowOnStep`); **migrate all four wizard families** (`CreateRecordWizard`, `CreateMatterWizard`, `CreateWorkAssignmentWizard`, `SummarizeFilesWizard`) onto it and delete duplicate copies.
- **AI prefill inert seam**: `useAiPrefill` wired in Enter Info behind `prefillEnabled = false`.
- **Per-wizard field manifests** drafted from live schema (Phase 0), owner-refined.
- PCF version bump + deploy.

### Out of Scope

- AI prefill BFF endpoints and JPS prefill Actions (separate follow-on project; only the client seam ships).
- Making KPI Assessment polymorphic beyond Matter + Project.
- Wizard support for visual types other than Event / Invoice / KPI Assessment.
- New modal shell (reuses Fluent Dialog hosting).
- Any BFF (`Sprk.Bff.Api`) change — hot-path stays BFF=N.

### Affected Areas

- `src/client/pcf/VisualHost/control/components/CardChrome.tsx` — "+" button in `iconSlots`.
- `src/client/pcf/VisualHost/control/VisualHostRoot.tsx` — legacy toolbar path.
- `src/client/pcf/VisualHost/control/services/ConfigurationLoader.ts` + `ChartDefinition` type — read new columns.
- `src/client/shared/Spaarke.UI.Components/src/components/WizardRegistry/` — **new**.
- `src/client/shared/Spaarke.UI.Components/src/components/WizardFollowOns/` — **new** (shared follow-ons).
- `src/client/shared/Spaarke.UI.Components/src/components/CreateInvoiceWizard/`, `CreateKPIAssessmentWizard/` — **new**.
- `src/client/shared/Spaarke.UI.Components/src/components/CreateEventWizard/eventService.ts` — resolver migration.
- `src/client/shared/Spaarke.UI.Components/src/components/{CreateRecordWizard,CreateMatterWizard,CreateWorkAssignmentWizard,SummarizeFilesWizard}/` — follow-on migration + delete duplicates.
- `src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts` — multi-bind.
- `src/client/shared/Spaarke.UI.Components/src/index.ts` — export new modules.
- Dataverse schema: **none required.** (`sprk_chartdefinition` columns, `sprk_kpiassessment` resolver fields, and `sprk_invoice` resolver fields all already exist; KPI has no files step.)

## Requirements

### Functional Requirements

1. **FR-01** — Read the **existing** `sprk_createwizardenabled` (Yes/No, default No) and `sprk_createwizardkey` (Text, 100) columns on `sprk_chartdefinition` into the `ChartDefinition` type. Valid key set is dev-defined (registry keys); maker enters the value per record; no Dataverse-side validation. *Acceptance*: existing chart-def records render unchanged (no "+" button) with columns null/false; a set value drives the button.
2. **FR-02** — Render a "+" `Button` (`AddRegular`) between spaarkle and open in `CardChrome` and the legacy `VisualHostRoot` toolbar, only when `chartDefinition.createWizardEnabled === true`. *Acceptance*: toggling the column shows/hides the button with no redeploy.
3. **FR-03** — `WizardRegistry.resolveWizard(key, fallbackEntity)` maps a key to a `lazy()`-loaded wizard component; falls back to normalized `sprk_entitylogicalname` when key empty; returns `null` for unknown keys. *Acceptance*: unknown key shows a toast, no crash; wizard chunk loads on first "+" click only.
4. **FR-04** — Clicking "+" opens the resolved wizard in a Fluent Dialog, injected with `WizardHostProps` (dataService, authenticatedFetch, bffBaseUrl, navigationService, resolveSpeContainerId, tenantId, `initialAssociation`, `lockAssociation`). *Acceptance*: wizard mounts modally; second "+" queues behind the first.
5. **FR-05** — Each wizard implements the standard template built by wrapping `CreateRecordWizard`. **Event/Invoice**: Associate To → Add Files → Enter Info → Next Steps. **KPI Assessment**: Associate To → Enter Info → Next Steps (no files step — owner: KPI needs no documents). *Acceptance*: step order/nav match the CreateWorkAssignment reference; KPI omits Add Files via config, not a fork.
6. **FR-06** — When launched from Visual Host, the wizard receives `initialAssociation = {host entity, id, name}` and `lockAssociation = true`, and **hides step 1**. *Acceptance*: created child is regarding the host record; the user never sees/edits the association step in the Visual Host flow.
7. **FR-07** — All child associations write via `PolymorphicResolverService.applyResolverFields` — the entity-specific `@odata.bind` **and** all 4 resolver fields (`sprk_regardingrecordtype`→`sprk_recordtype_ref`, `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordurl`). *Acceptance*: created record has one specific lookup + 4 populated resolver fields; verified in Dataverse.
8. **FR-08** — Migrate `eventService.createEvent` off its ad-hoc matter/project map onto `applyResolverFields`. *Acceptance*: Events created from all existing surfaces still link correctly, now with resolver fields populated (regression-verified).
9. **FR-09** — **No Dataverse schema delta.** Resolver fields on `sprk_kpiassessment` were created by owner (2026-07-05); Invoice already resolver-ready; chart-def columns exist; KPI has no files (no `sprk_document` KPI lookup needed). *Acceptance*: `applyResolverFields` succeeds for Matter and Project parents on Event/Invoice/KPI; resolver fields verified present.
10. **FR-10** — `CreateInvoiceWizard` + `invoiceService` create a valid `sprk_invoice` regarding the host record; `CreateKPIAssessmentWizard` + `kpiAssessmentService` create a valid `sprk_kpiassessment` regarding the host Matter/Project. *Acceptance*: submit produces a saved record with correct association and field values from the manifest.
11. **FR-11** — AI prefill: wire `useAiPrefill` inside the **Event and Invoice** Enter Info steps (file-driven), gated by `prefillEnabled` default **false**. **KPI has no files → no prefill.** *Acceptance*: with the flag off, Enter Info renders with no spinner and no network call; the hook contract is present for the follow-on project.
12. **FR-12** — Extend `EntityCreationService.createDocumentRecords` to accept optional additional binds `{entitySet, id, navProp}`; the **Event and Invoice** wizards discover both host and child `sprk_document` nav-props (`sprk_matter`/`sprk_project` + `sprk_Event`/`sprk_invoice`, all pre-existing) and bind both. *Acceptance*: one uploaded file → one `sprk_document` appearing in both the host and child Documents subgrids. (KPI not applicable — no files.)
13. **FR-13** — Build `WizardFollowOns`: config-driven `FollowOnGrid` (cards → `addDynamicStep`) + reusable steps `SendEmailFollowOnStep`, `AddTodoFollowOnStep` (**net-new**, calls `todoService.createTodo`), `AssignWorkFollowOnStep`, `CreateEventFollowOnStep`, `DraftSummaryFollowOnStep`; export from the shared-lib root. *Acceptance*: a wizard renders its declared `FollowOnCardConfig[]`; zero selection = early finish.
14. **FR-14** — Migrate `CreateRecordWizard`, `CreateMatterWizard`, `CreateWorkAssignmentWizard`, `SummarizeFilesWizard` onto `WizardFollowOns`; delete their local `NextStepsStep`/`NextStepsSelectionStep`/`SummaryNextStepsStep`/`SendEmailStep` copies. *Acceptance*: no duplicate follow-on implementations remain (grep); each migrated wizard passes a smoke/regression pass.
15. **FR-15** — The three project wizards offer cards **Send Email, Add To Do, Assign Work**; each follow-on record is created regarding the just-created child via `applyResolverFields`. *Acceptance*: selecting each card adds its step and persists the follow-on with correct regarding.
16. **FR-16** — Enter Info steps are driven by the **owner-provided field manifests** (below); Phase 0 validates exact logical names/types/required/lookup targets against live schema and writes the validated manifest to `notes/field-manifests/{entity}.md`. *Acceptance*: each wizard collects exactly the manifest fields; all schema-required fields present; Invoice `sprk_invoicedate` defaults to today.

    **KPI Assessment**: `sprk_kpiname` (text), `sprk_performancearea` (choice), `sprk_kpigradescore` (choice), `sprk_assessmentcriteria` (multiline), `sprk_assessmentnotes` (multiline).

    **Invoice**: `sprk_invoicenumber` (text), `sprk_name` (text), `sprk_description` (text), `sprk_vendororg` (lookup → `sprk_organization`; relationship `sprk_sprk_organization_sprk_invoice_sprk_vendororg`), `sprk_invoicedate` (date, default today).

    **Event**: uses the fields already collected by the existing `CreateEventStep` (no new manifest).
17. **FR-17** — PCF version bump + solution deploy for Visual Host. *Acceptance*: deployed control renders the "+" button per config.

### Non-Functional Requirements

- **NFR-01** — Visual Host bundle delta < 5 KB gzipped (registry only; wizards code-split via `lazy()`).
- **NFR-02** — **No BFF changes.** `git diff --stat` shows no `src/server/api/Sprk.Bff.Api/**` files; hot-path declaration stays BFF=N.
- **NFR-03** — No regression in spaarkle/open buttons, existing Event creation, or the three migrated wizards (Matter, WorkAssignment, SummarizeFiles).
- **NFR-04** — PCF/React/Fluent v9 conventions per ADR-022/ADR-021.
- **NFR-05** — Backward compatible: existing `sprk_chartdefinition` records need no migration (new columns default off/null).
- **NFR-06** — Dialog accessibility: keyboard nav + focus trap via Fluent v9 Dialog (inherited).

## Technical Constraints

### Applicable ADRs

- **ADR-024 (Polymorphic Resolver Pattern)** — CENTRAL. Association MUST use the shared resolver + all 4 resolver fields.
- **ADR-022 (PCF Platform Libraries)** — React/Fluent versions for PCF.
- **ADR-021 (Fluent Design System)** — Fluent v9 UI.
- **ADR-007 (SPE storage)** — file upload path (`/api/obo/containers/...`).
- **ADR-028 (Auth)** — `authenticatedFetch` bearer for any BFF call (email/SPE).
- **ADR-011 (dataset PCF over subgrids)** — context for Documents display.

### MUST Rules

- ✅ MUST write associations via `PolymorphicResolverService.applyResolverFields` (both specific lookup + 4 resolver fields).
- ✅ MUST populate only ONE entity-specific lookup at a time (mutual exclusion; naturally satisfied on create-from-host).
- ✅ MUST reference `sprk_recordtype_ref` for the resolver-type discriminator (not choice fields).
- ✅ MUST keep record creation client-side via `IDataService.createRecord` (`Xrm.WebApi` adapter) — no new BFF endpoints.
- ❌ MUST NOT use the native "Regarding" lookup.
- ❌ MUST NOT ship a new duplicate Next-Steps/SendEmail implementation (consolidate into `WizardFollowOns`).
- ❌ MUST NOT add to `Sprk.Bff.Api` in this project.

### Existing Patterns to Follow

- `src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/workAssignmentService.ts` — the ADR-024-compliant service template (nav-prop discovery → payload → BU defaults → `applyResolverFields` → createRecord → file pipeline → warnings).
- `src/client/shared/Spaarke.UI.Components/src/components/CreateEventWizard/CreateEventWizard.tsx` — thin `CreateRecordWizard` wrapper shape.
- `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts` (`applyResolverFields`, `findNavProp`).
- `src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts` (SPE upload, `createDocumentRecords`, `sendEmail`, BU defaults).
- `src/client/shared/Spaarke.UI.Components/src/hooks/useAiPrefill.ts`.
- `src/client/shared/Spaarke.UI.Components/src/components/AssociateToStep/` (+ `TODO_REGARDING_TARGETS`).
- `src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/todoService.ts` (`createTodo` for the Add-To-Do follow-on).

## ADR Tensions (per CLAUDE.md §6.5)

> No blocking ADR tensions surfaced at design time. All listed ADRs apply without exception.

Two items worth explicit note (neither requires an exception/amendment):

| ADR | Rule | Note | Path |
|---|---|---|---|
| ADR-024 | "MUST use the shared PolymorphicResolverService for all client-side programmatic record creation" | `CreateEventWizard` **currently violates** this (ad-hoc matter/project map, no resolver fields). This project **fixes** it (FR-08). This is a latent pre-existing violation being remediated, not a new deviation. | C (comply) |
| ADR-024 | Documents use typed lookups, not the resolver | `sprk_document` has no `sprk_regardingrecordtype` discriminator, so file dual-bind (FR-12) uses typed lookups per the ADR's own document guidance. Consistent, not in tension. | C (comply) |

## Success Criteria

1. [ ] Maker enables "+" via `sprk_createwizardenabled = Yes`, no PCF redeploy — *Verify*: toggle column, reload visual.
2. [ ] "+" on an Event visual (on a Matter) opens `CreateEventWizard`, step 1 hidden, created Event regarding the host Matter with 4 resolver fields populated — *Verify*: create + inspect record in Dataverse.
3. [ ] "+" on an Invoice visual creates a valid `sprk_invoice` regarding the host via `applyResolverFields` — *Verify*: create + inspect.
4. [ ] "+" on a KPI visual creates a valid `sprk_kpiassessment` regarding the host Matter/Project — *Verify*: create + inspect.
5. [ ] A file uploaded in the **Event or Invoice** wizard yields one `sprk_document` in both the host and child Documents subgrids — *Verify*: upload + check both subgrids. (KPI has no files step.)
6. [ ] Next Steps offers Send Email / Add To Do / Assign Work; each creates a follow-on regarding the child — *Verify*: select each card, submit, inspect follow-on record.
7. [ ] One shared `WizardFollowOns` backs all wizard families; duplicate copies deleted; migrated wizards show no regression — *Verify*: grep for removed components + smoke-test Matter/WorkAssignment/SummarizeFiles.
8. [ ] AI prefill seam present but inert; no BFF files in the diff — *Verify*: `git diff --stat` + confirm no network call with flag off.
9. [ ] No regression in spaarkle/open buttons — *Verify*: exercise both on a Visual Host visual.
10. [ ] Visual Host bundle delta < 5 KB gzipped — *Verify*: build size report before/after.

## Dependencies

### Prerequisites

- **🚧 BLOCKED ON PR #549 (`set-regarding-and-field-mapping-resolver-r1`)** — hard prerequisite (owner decision 2026-07-05). It refactors `PolymorphicResolverService` (`applyResolverFields`), **extracts the polymorphic picker** (overlaps `AssociateToStep`), and edits ADR-024. Wizard tasks MUST consume the **post-#549** resolver API + extracted picker. Do not start resolver-dependent implementation until #549 merges.
- **⚠️ COORDINATE WITH PR #525 (`feat/pcf-visualhost-uat-tracking-field-trio`)** — direct file collision on `CardChrome.tsx`, `VisualHostRoot.tsx`, `ControlManifest.Input.xml`, `bundle.js`, VisualHost solution files. Sequence to rebase on #525; PCF version-bump must account for #525's bump.
- Pipeline re-run (`/project-pipeline`) is **paused** until #549 (and ideally #525) merge — see `notes/pipeline-paused.md`.
- Phase 0 discovery: live-schema read of `sprk_document` (confirm host + Event/Invoice child lookups); validate owner-provided manifests (required flags, option-set values).
- Dataverse schema: **none.** All resolver fields, chart-def columns, and document lookups already exist.

### External Dependencies

- Existing `sprk_recordtype_ref` catalog rows for `sprk_matter` + `sprk_project` (already present — used by Todo/WorkAssignment).
- SPE container resolution for the host record (`resolveSpeContainerId`) for file upload.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| AI prefill scope | Build prefill BFF endpoints now, or stub? | **Stub only; back-fill later** | No BFF this project; `prefillEnabled=false` seam only (FR-11); hot-path stays BFF=N. |
| KPI polymorphism | Which parent types for KPI? | **Matter + Project** | KPI already has both lookups; delta = 4 resolver fields only (FR-09). |
| 3rd Next Step | Third follow-on card beyond Send Email + Add To Do? | **Assign Work** | Cards = Send Email, Add To Do, Assign Work (FR-15). |
| Prefill UX | Prefill as its own visible step, or inside Enter Info? | **Inside Enter Info** | No separate PrefillStep; hook runs in Enter Info (FR-11). |
| Follow-on consolidation | How far to consolidate the duplicated Next-Steps? | **Full migration** (all 4 families, delete duplicates) | FR-14 migrates + deletes; R7 tracks regression risk. |
| Field manifests | Who defines the wizard fields? | **Draft from schema, owner refines** | Phase 0 drafts manifests; owner sign-off gates B/C (FR-16). |

## Assumptions

- **`lockAssociation` = hide** (not merely lock/read-only) step 1 in the Visual Host flow; the step remains available when `lockAssociation` is false (other launch contexts).
- **`prefillEnabled` default false** ships in code (not a Dataverse flag) this release.
- **`sprk_recordtype_ref` rows** for `sprk_matter` and `sprk_project` exist (used by Todo/WorkAssignment) — required for KPI/Invoice resolver-type discriminator.

## Unresolved Questions

- **None remaining.** All schema and contract unknowns are resolved.

*(Resolved since rev 1: chart-def columns exist; KPI resolver fields created by owner; `applyResolverFields`/`findNavProp` already exist and are reused; Invoice is fully resolver-ready — no schema delta; vendor-org field = `sprk_vendororg`; Invoice targets = Matter + Project; field manifests owner-provided; **KPI drops the files step — no document handling, no `sprk_document` KPI lookup, no schema delta**.)*

---
*AI-optimized specification. Original design: `design.md` (rev 2).*
