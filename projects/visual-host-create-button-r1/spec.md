# Visual Host "+" Create Button — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-05 · **Regenerated**: 2026-07-08 (clean pass from design.md rev 2, post-#549)
> **Source**: `design.md` (rev 2)
> **Project**: `visual-host-create-button-r1`
>
> **⚠️ Post-Phase-D amendment (2026-07-08, owner decision)**: every reference below to "KPI Assessment" / `sprk_kpiassessment` as the third wizard's TARGET entity is superseded. The third wizard now creates `sprk_reportcard` (the parent review artifact `sprk_kpiassessment` line-items belong to), via `CreateReportCardWizard`/`reportCardService`, registry key `report-card`. Enter Info also now includes 8 assigned-resource lookups (owner decision) that weren't in the original KPI manifest. See `notes/field-manifests/reportcard.md` (authoritative) and `tasks/040-create-reportcard-wizard.poml`. Everything else in this spec (Event, Invoice, WizardFollowOns, resolver pattern, AI-prefill-inert-seam) is unaffected.

## Executive Summary

Add a maker-configurable **"+" toolbar button** to the Visual Host PCF that opens the appropriate Create wizard for the entity a visual represents — launched from and **auto-associated to the host record**. Wizards follow the **standard Spaarke wizard template** (Associate To → Add Files → Enter Info → Next Steps), write parent association via the **ADR-024 polymorphic resolver** (`applyResolverFields`), **dual-bind uploaded documents** to both host and child, and offer **Send Email / Add To Do / Assign Work** follow-ons from a new shared **`WizardFollowOns`** module that also consolidates today's four duplicated Next-Steps implementations. AI prefill ships as an **inert seam** (no BFF work this release).

## Scope

### In Scope

- Read two **already-existing** columns on `sprk_chartdefinition` (`sprk_createwizardenabled` bool, `sprk_createwizardkey` text) into the Visual Host config model.
- Visual Host "+" toolbar button (`CardChrome.tsx` + legacy `VisualHostRoot.tsx`), gated on `sprk_createwizardenabled`.
- `WizardRegistry` dispatcher (lazy-loaded, key → wizard component) + `WizardHostProps` injection contract.
- **Migrate `CreateEventWizard`/`eventService` onto `applyResolverFields`** (ADR-024 compliance fix) and wire it to the `event` key.
- Two new wizards on the standard template: **`CreateInvoiceWizard`** (`sprk_invoice`) and **`CreateKPIAssessmentWizard`** (`sprk_kpiassessment`), each with a service.
- **Auto-association** from the host record: `initialAssociation` seed + `lockAssociation` (hides step 1 from Visual Host).
- **Polymorphic association** via `PolymorphicResolverService.applyResolverFields` (entity-specific lookup + all resolver fields).
- **File dual-bind (Event + Invoice)**: extend `EntityCreationService.createDocumentRecords` for a second `@odata.bind` so one `sprk_document` links to both host and child. **KPI has no files step.**
- **`WizardFollowOns` shared module** (`FollowOnGrid` + reusable follow-on steps incl. net-new `AddTodoFollowOnStep`); **migrate all four wizard families** (`CreateRecordWizard`, `CreateMatterWizard`, `CreateWorkAssignmentWizard`, `SummarizeFilesWizard`) onto it and delete duplicate copies.
- **AI prefill inert seam**: `useAiPrefill` wired in Enter Info (Event/Invoice) behind `prefillEnabled = false`.
- Per-wizard field manifests (owner-provided; validated against live schema in Phase 0).
- PCF version bump + deploy.

### Out of Scope

- AI prefill BFF endpoints + JPS prefill actions (separate follow-on project; only the client seam ships).
- KPI polymorphism beyond Matter + Project.
- Field-mapping inheritance (`FieldMappingHandler` / `sprk_fieldmappingprofile`) in the wizards — available post-#549 but a later enhancement.
- Wizard support for visual types other than Event / Invoice / KPI Assessment.
- New modal shell (reuses Fluent Dialog hosting).
- Any BFF (`Sprk.Bff.Api`) change — hot-path stays BFF=N.

### Affected Areas

- `src/client/pcf/VisualHost/control/components/CardChrome.tsx` — "+" button in `iconSlots`.
- `src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx` — legacy toolbar path.
- `src/client/pcf/VisualHost/control/services/ConfigurationLoader.ts` + `ChartDefinition` type — read the two columns.
- `src/client/shared/Spaarke.UI.Components/src/components/WizardRegistry/` — **new**.
- `src/client/shared/Spaarke.UI.Components/src/components/WizardFollowOns/` — **new** (shared follow-ons).
- `src/client/shared/Spaarke.UI.Components/src/components/CreateInvoiceWizard/`, `CreateKPIAssessmentWizard/` — **new**.
- `src/client/shared/Spaarke.UI.Components/src/components/CreateEventWizard/eventService.ts` — resolver migration.
- `src/client/shared/Spaarke.UI.Components/src/components/{CreateRecordWizard,CreateMatterWizard,CreateWorkAssignmentWizard,SummarizeFilesWizard}/` — follow-on migration + delete duplicates.
- `src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts` — document multi-bind.
- `src/client/shared/Spaarke.UI.Components/src/index.ts` — export new modules.
- Dataverse: **no confirmed schema delta** (contingent only on the Phase-0 `sprk_regardingrecordnumber` check for Event/KPI).

## Requirements

### Functional Requirements

1. **FR-01** — Read the **existing** `sprk_createwizardenabled` (Yes/No, default No) and `sprk_createwizardkey` (Text, 100) columns into `ChartDefinition`. Valid key set is dev-defined (registry keys); maker enters the value per record; no Dataverse-side validation. *Acceptance*: existing chart-def records render unchanged (no button) when null/false; a set value drives the button.
2. **FR-02** — Render a "+" `Button` (`AddRegular`) between spaarkle and open in `CardChrome` and the legacy `VisualHostRoot` toolbar, only when `chartDefinition.createWizardEnabled === true`. *Acceptance*: toggling the column shows/hides the button with no redeploy.
3. **FR-03** — `WizardRegistry.resolveWizard(key, fallbackEntity)` maps a key to a `lazy()`-loaded wizard; falls back to normalized `sprk_entitylogicalname` when key empty; returns `null` for unknown keys. *Acceptance*: unknown key shows a toast (no crash); wizard chunk loads on first "+" click only.
4. **FR-04** — Clicking "+" opens the resolved wizard in a Fluent Dialog with `WizardHostProps` (dataService, authenticatedFetch, bffBaseUrl, navigationService, resolveSpeContainerId, tenantId, `initialAssociation`, `lockAssociation`). *Acceptance*: wizard mounts modally; a second "+" queues behind the first.
5. **FR-05** — Each wizard is built by wrapping `CreateRecordWizard`. **Event/Invoice**: Associate To → Add Files → Enter Info → Next Steps. **KPI**: Associate To → Enter Info → Next Steps (no files step). *Acceptance*: step order/nav match the `CreateWorkAssignment` reference; KPI omits Add Files via config, not a fork.
6. **FR-06** — When launched from Visual Host, the wizard receives `initialAssociation = {host entity, id, name}` + `lockAssociation = true` and **hides step 1**. *Acceptance*: created child is regarding the host; the user never sees/edits the association step in the Visual Host flow.
7. **FR-07** — All child associations write via `PolymorphicResolverService.applyResolverFields` — the entity-specific `@odata.bind` **and** all resolver fields (`sprk_regardingrecordtype`→`sprk_recordtype_ref`, `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordurl`, and — post-#549 — `sprk_regardingrecordnumber` where the target has it). *Acceptance*: created record has one specific lookup + populated resolver fields; verified in Dataverse.
8. **FR-08** — Migrate `eventService.createEvent` off its ad-hoc matter/project map onto `applyResolverFields`. *Acceptance*: Events created from all existing surfaces still link correctly, now with resolver fields populated (regression-verified).
9. **FR-09** — **No confirmed Dataverse schema delta.** KPI resolver fields created by owner (2026-07-05); Invoice already resolver-ready; chart-def columns exist; KPI has no files (no `sprk_document` KPI lookup). *Contingency*: Phase 0 verifies `sprk_regardingrecordnumber` on Event/KPI; add the single additive column if absent. *Acceptance*: `applyResolverFields` succeeds for Matter and Project parents on Event/Invoice/KPI.
10. **FR-10** — `CreateInvoiceWizard` + `invoiceService` create a valid `sprk_invoice` regarding the host; `CreateKPIAssessmentWizard` + `kpiAssessmentService` create a valid `sprk_kpiassessment` regarding the host Matter/Project. *Acceptance*: submit produces a saved record with correct association + manifest field values.
11. **FR-11** — Wire `useAiPrefill` inside the **Event and Invoice** Enter Info steps, gated by `prefillEnabled` default **false**. **KPI has no files → no prefill.** *Acceptance*: with the flag off, Enter Info renders with no spinner and no network call; the seam is present for the follow-on project.
12. **FR-12** — Extend `EntityCreationService.createDocumentRecords` to accept optional additional binds `{entitySet, id, navProp}`; the **Event and Invoice** wizards discover both host and child `sprk_document` nav-props and bind both. *Acceptance*: one uploaded file → one `sprk_document` in both host and child Documents subgrids. (KPI n/a — no files.)
13. **FR-13** — Build `WizardFollowOns`: config-driven `FollowOnGrid` (cards → `addDynamicStep`) + reusable steps `SendEmailFollowOnStep`, `AddTodoFollowOnStep` (**net-new**, calls `todoService.createTodo`), `AssignWorkFollowOnStep`, `CreateEventFollowOnStep`, `DraftSummaryFollowOnStep`; export from the shared-lib root. *Acceptance*: a wizard renders its declared `FollowOnCardConfig[]`; zero selection = early finish.
14. **FR-14** — Migrate `CreateRecordWizard`, `CreateMatterWizard`, `CreateWorkAssignmentWizard`, `SummarizeFilesWizard` onto `WizardFollowOns`; delete their local `NextStepsStep`/`NextStepsSelectionStep`/`SummaryNextStepsStep`/`SendEmailStep` copies. *Acceptance*: no duplicate follow-on implementations remain (grep); each migrated wizard passes a smoke/regression pass.
15. **FR-15** — The three project wizards offer cards **Send Email, Add To Do, Assign Work**; each follow-on record is created regarding the just-created child via `applyResolverFields`. *Acceptance*: selecting each card adds its step and persists the follow-on with correct regarding.
16. **FR-16** — Enter Info steps are driven by the **owner-provided field manifests** (below); Phase 0 validates exact logical names/types/required/lookup targets against live schema → `notes/field-manifests/{entity}.md`. *Acceptance*: each wizard collects exactly the manifest fields; all schema-required fields present; Invoice `sprk_invoicedate` defaults to today.

    **KPI Assessment**: `sprk_kpiname` (text), `sprk_performancearea` (choice), `sprk_kpigradescore` (choice), `sprk_assessmentcriteria` (multiline), `sprk_assessmentnotes` (multiline).

    **Invoice**: `sprk_invoicenumber` (text), `sprk_name` (text), `sprk_description` (text), `sprk_vendororg` (lookup → `sprk_organization`; relationship `sprk_sprk_organization_sprk_invoice_sprk_vendororg`), `sprk_invoicedate` (date, default today).

    **Event**: uses the fields already collected by the existing `CreateEventStep` (no new manifest).

17. **FR-17** — PCF version bump + solution deploy for Visual Host. *Acceptance*: deployed control renders the "+" button per config.

### Non-Functional Requirements

- **NFR-01** — Visual Host bundle delta < 5 KB gzipped (registry only; wizards code-split via `lazy()`).
- **NFR-02** — **No BFF changes.** `git diff --stat` shows no `src/server/api/Sprk.Bff.Api/**`; hot-path stays BFF=N.
- **NFR-03** — No regression in spaarkle/open buttons, existing Event creation, or the three migrated wizards (Matter, WorkAssignment, SummarizeFiles).
- **NFR-04** — PCF/React/Fluent v9 conventions per ADR-022/ADR-021; dark-mode via semantic tokens.
- **NFR-05** — Backward compatible: existing `sprk_chartdefinition` records need no migration (columns default off/null).
- **NFR-06** — Dialog accessibility: keyboard nav + focus trap via Fluent v9 Dialog (inherited).

## Technical Constraints

### Applicable ADRs

- **ADR-024 (Polymorphic Resolver Pattern)** — CENTRAL; **amended by #549** (re-read before implementation). Association MUST use the shared resolver + all resolver fields.
- **ADR-022 (PCF Platform Libraries)** / **ADR-021 (Fluent v9)** — PCF React/Fluent conventions.
- **ADR-012 (Shared Component Library)** — shared components stay context-agnostic.
- **ADR-007 (SPE storage)** / **ADR-028 (Auth v2)** — file upload via `/api/obo/...` with `authenticatedFetch`.
- **ADR-011 (dataset PCF over subgrids)** — context for Documents display.
- **ADR-038 (Testing strategy)** — integration-heavy; no `Mock<HttpMessageHandler>` / DI-registration / ctor-null tests.

### MUST Rules

- ✅ MUST write associations via `applyResolverFields` (entity-specific lookup + all resolver fields).
- ✅ MUST populate only ONE entity-specific lookup at a time (mutual exclusion; satisfied on create-from-host).
- ✅ MUST reference `sprk_recordtype_ref` for the resolver-type discriminator (not choice fields).
- ✅ MUST keep record creation client-side via `IDataService.createRecord` (`Xrm.WebApi` adapter) — no new BFF endpoints.
- ✅ MUST consume the **post-#549** resolver API (`applyResolverFields` now returns `IApplyResolverFieldsResult`; 5th field `sprk_regardingrecordnumber`).
- ❌ MUST NOT use the native "Regarding" lookup.
- ❌ MUST NOT ship a new duplicate Next-Steps/SendEmail implementation (consolidate into `WizardFollowOns`).
- ❌ MUST NOT add to `Sprk.Bff.Api` in this project.

### Existing Patterns to Follow

- `CreateWorkAssignmentWizard/workAssignmentService.ts` — the ADR-024-compliant service template (nav-prop discovery → payload → BU defaults → `applyResolverFields` → createRecord → file pipeline → warnings).
- `CreateEventWizard/CreateEventWizard.tsx` — thin `CreateRecordWizard` wrapper shape.
- `services/PolymorphicResolverService.ts` (`applyResolverFields`, `findNavProp`, `resolveRecordNumberFieldName`) — post-#549.
- `services/EntityCreationService.ts` (SPE upload, `createDocumentRecords`, `sendEmail`, BU defaults).
- `hooks/useAiPrefill.ts`; `components/AssociateToStep/` (+ `TODO_REGARDING_TARGETS`).
- `components/PolymorphicPicker/` — new shared picker (available; `AssociateToStep` remains the wizard step).
- `components/CreateTodoWizard/todoService.ts` (`createTodo` for the Add-To-Do follow-on).

## ADR Tensions (per CLAUDE.md §6.5)

> No blocking ADR tensions surfaced at design time. All listed ADRs apply without exception.

Two items noted (neither requires an exception/amendment):

| ADR | Rule | Note | Path |
|---|---|---|---|
| ADR-024 | "MUST use the shared PolymorphicResolverService for all client-side programmatic record creation" | `CreateEventWizard` **currently violates** this (ad-hoc matter/project map, no resolver fields). This project **fixes** it (FR-08) — remediation of a latent pre-existing violation, not a new deviation. | C (comply) |
| ADR-024 | Documents use typed lookups, not the resolver | `sprk_document` has no `sprk_regardingrecordtype` discriminator, so file dual-bind (FR-12) uses typed lookups per the ADR's own document guidance. Consistent, not in tension. | C (comply) |

## Success Criteria

1. [ ] Maker enables "+" via `sprk_createwizardenabled = Yes`, no PCF redeploy — *Verify*: toggle column, reload visual.
2. [ ] "+" on an Event visual (on a Matter) opens `CreateEventWizard`, step 1 hidden, created Event regarding the host Matter with all resolver fields populated — *Verify*: create + inspect in Dataverse.
3. [ ] "+" on an Invoice visual creates a valid `sprk_invoice` regarding the host via `applyResolverFields` — *Verify*: create + inspect.
4. [ ] "+" on a KPI visual creates a valid `sprk_kpiassessment` regarding the host Matter/Project — *Verify*: create + inspect.
5. [ ] A file uploaded in the **Event or Invoice** wizard yields one `sprk_document` in both host and child Documents subgrids — *Verify*: upload + check both subgrids. (KPI has no files step.)
6. [ ] Next Steps offers Send Email / Add To Do / Assign Work; each creates a follow-on regarding the child — *Verify*: select each card, submit, inspect follow-on record.
7. [ ] One shared `WizardFollowOns` backs all wizard families; duplicate copies deleted; migrated wizards show no regression — *Verify*: grep for removed components + smoke-test Matter/WorkAssignment/SummarizeFiles.
8. [ ] AI prefill seam present but inert; no BFF files in the diff — *Verify*: `git diff --stat` + confirm no network call with the flag off.
9. [ ] No regression in spaarkle/open buttons — *Verify*: exercise both on a Visual Host visual.
10. [ ] Visual Host bundle delta < 5 KB gzipped — *Verify*: build size report before/after.

## Dependencies

### Prerequisites

- ✅ **PR #549 (`set-regarding-and-field-mapping-resolver-r1`) MERGED 2026-07-08** — wizards build on the post-#549 resolver API (`applyResolverFields` backward-compatible + returns `IApplyResolverFieldsResult`; 5th resolver field `sprk_regardingrecordnumber`; new `PolymorphicPicker`; `FieldMappingHandler` inheritance out of scope).
- ✅ **PR #525 (`feat/pcf-visualhost-uat-tracking-field-trio`) MERGED 2026-07-07** — VisualHost file base rebased in via master merge.
- Phase 0 discovery: live-schema read of `sprk_document` (host + Event/Invoice child lookups) and `sprk_regardingrecordnumber` presence on Event/KPI; validate owner-provided manifests.
- Owner sign-off on validated field manifests before Phase B/C build.

### External Dependencies

- `sprk_recordtype_ref` catalog rows for `sprk_matter` + `sprk_project` (present — used by Todo/WorkAssignment).
- SPE container resolution for the host record (`resolveSpeContainerId`) for file upload.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| AI prefill scope | Build prefill BFF now, or stub? | **Stub only; back-fill later** | No BFF this project; `prefillEnabled=false` seam (FR-11); BFF=N. |
| KPI polymorphism | Which parent types for KPI? | **Matter + Project** | KPI already has both lookups; resolver fields created by owner (FR-09). |
| KPI documents | Does KPI need file uploads? | **No — remove the files step** | KPI wizard omits Add Files; no dual-bind, no prefill (FR-05). |
| 3rd Next Step | Third follow-on card? | **Assign Work** | Cards = Send Email, Add To Do, Assign Work (FR-15). |
| Prefill UX | Its own step, or inside Enter Info? | **Inside Enter Info** | No separate PrefillStep (FR-11). |
| Follow-on consolidation | How far to consolidate? | **Full migration** (all 4 families) | FR-14 migrates + deletes; NFR-03 regression risk. |
| Field manifests | Who defines wizard fields? | **Owner-provided + Phase-0 validation** | Manifests in FR-16; Phase 0 validates logical names. |
| Chart-def columns | Create the two columns? | **Already exist** | FR-01 reads them; no schema creation. |
| Vendor org field | Logical name? | **`sprk_vendororg` → `sprk_organization`** | Invoice manifest (FR-16). |

## Assumptions

- **`lockAssociation` = hide** (not merely lock/read-only) step 1 in the Visual Host flow; the step remains available when `lockAssociation` is false (other launch contexts).
- **`prefillEnabled` default false** ships in code (not a Dataverse flag) this release.
- **Event/Invoice `sprk_document` child lookups** (`sprk_Event`, `sprk_invoice`) exist per the ER model + `CreateEventWizard` usage; Phase 0 confirms host lookups.

## Unresolved Questions

- [ ] **`sprk_regardingrecordnumber` on `sprk_event` / `sprk_kpiassessment`** — present or add? *Blocks*: nothing (additive if needed). Resolve in Phase 0.

*(All prior-round unknowns resolved: chart-def columns exist; KPI resolver fields created; `applyResolverFields`/`findNavProp` reused; Invoice fully resolver-ready — no delta; vendor-org = `sprk_vendororg`; Invoice targets = Matter + Project; field manifests owner-provided; KPI has no files step; #549/#525 merged.)*

---
*AI-optimized specification. Original design: `design.md` (rev 2).*
