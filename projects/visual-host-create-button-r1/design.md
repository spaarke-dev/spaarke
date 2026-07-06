# Visual Host "+" Create Button — Design

> **Project**: `visual-host-create-button-r1`
> **Worktree**: `c:\code_files\spaarke-wt-visual-host-create-button-r1`
> **Branch**: `work/visual-host-create-button-r1`
> **Owner**: ralph.schroeder@hotmail.com
> **Date**: 2026-07-05 (rev 2 — standard wizard template, polymorphic association, AI-prefill stub, file dual-bind)

---

## 1. Problem Statement

The Visual Host PCF (`src/client/pcf/VisualHost/`) currently exposes two toolbar buttons per rendered visual:

- **spaarkle** — opens the AI Summary popover
- **open** — launches the corresponding entity view (dataset grid code page) inside a modal, filtered to the visual's context

There is no fast path for a user viewing a visual to **create a new record of the type that visual represents**. For example, when a user is looking at an Events calendar visual on a Matter form and wants to add a new event, they must leave the visual, navigate to the Events subgrid or command bar, and launch the wizard from there.

This project adds a third toolbar button — **"+"** — that opens the appropriate Create wizard for the entity the visual represents. The wizard follows the **standard Spaarke wizard template**, and because it is launched *from a host record*, the created child record is **automatically associated to that host record**.

## 2. Goals

1. Add a **"+" toolbar icon** to the Visual Host PCF's `CardChrome` toolbar, between spaarkle and open.
2. Make the button **maker-configurable** via the existing `sprk_chartdefinition` Dataverse config record (no PCF redeploy needed to enable per visual).
3. Introduce a **registry-based wizard dispatcher** that maps a wizard key → wizard React component, so future visuals can bind to new wizards without touching Visual Host source.
4. **Wire the existing `CreateEventWizard`** to the `event` key — and, in doing so, **migrate it onto the ADR-024 polymorphic resolver** (`applyResolverFields`), which it currently does not use (see §5.6).
5. Build **two new wizards** — `CreateInvoiceWizard` and `CreateKPIAssessmentWizard` — following the **standard wizard template** (§5.4) using the established shared-lib building blocks (`WizardShell`, `AssociateToStep`, `FileUploadZone`, `useAiPrefill`, `NextStepsStep`, `EntityCreationService`, `PolymorphicResolverService`).
6. **Auto-associate the created child to the host record** using the polymorphic resolver, so an Event/Invoice/KPI Assessment created from a Matter visual is correctly regarding that Matter (§5.5, §5.6).
7. **Dual-bind uploaded files** so a document created in the wizard is linked to **both** the host (parent) record and the newly-created child record (§5.8).
8. **Consolidate the Next Steps follow-ons into a single shared `WizardFollowOns` module** (§5.9) and migrate all four existing wizard families onto it (deleting the duplicate copies). Cards wired for the new wizards: **Send Email**, **Add To Do**, **Assign Work**.
9. **Stub the AI prefill seam** (§5.7) — the wizards wire `useAiPrefill` inside Enter Info but it is feature-gated OFF this release; the BFF prefill endpoints + JPS prefill actions are a follow-on project. **No BFF changes ship in this project.**

## 3. Non-Goals

- Not changing the behavior of the existing spaarkle or open buttons.
- Not adding wizard support for all 13+ visual types — only Event/Invoice/KPI Assessment in this release; the registry accommodates future additions.
- **Not building the AI prefill BFF endpoints or JPS prefill actions.** The prefill step is wired as an inert seam (§5.7) and back-filled by a separate BFF project. This keeps the project's hot-path declaration at BFF=N.
- Not introducing a new modal shell. Reuses the wizard-in-Fluent-Dialog invocation pattern already used by every `Create*Wizard`.
- Not making KPI Assessment fully polymorphic across all 11 entity types — KPI gets **Matter + Project** targets only this release (§5.6).
- Not versioning any schema migration for existing `sprk_chartdefinition` records — new columns default to disabled/null so existing configs continue to render with no "+" button until a maker opts in.

## 4. Users

- **End users** — Legal ops / matter workers viewing dashboards or record forms with embedded Visual Host visuals; primary benefit is one-click creation of related records without leaving the current context.
- **Solution makers** — Configure the "+" button per visual by setting two fields on the `sprk_chartdefinition` record; no code required.
- **Developers** — Add a new wizard by registering it in the `WizardRegistry` (single line) and building the wizard following the standard template (§5.4).

## 5. Solution Overview

### 5.1 Dataverse columns on `sprk_chartdefinition` (already created)

| Column | Type | Purpose |
|---|---|---|
| `sprk_createwizardenabled` | Yes/No (bool), default = No | Shows/hides the "+" button on this visual's toolbar. When No (default), existing behavior is preserved — no "+" button appears. |
| `sprk_createwizardkey` | Single Line of Text (100 chars), optional | Registry key selecting which wizard to open. When empty, falls back to `sprk_entitylogicalname`. Explicit key allows one entity → multiple wizard variants in the future. |

**Both columns already exist in Dataverse** (confirmed by owner 2026-07-05) — this project **reads** them, no schema creation here.

**Value provenance**: the **valid key set is dev-defined** — the keys registered in `wizardRegistry.ts` (`event`, `invoice`, `kpi-assessment`). The **maker enters one of those keys** on the chart-definition record (or leaves it blank to fall back to `sprk_entitylogicalname`). The field is free text with no Dataverse-side validation; an unknown/typo'd key surfaces as the FR-03 toast. Deliverable includes a short maker-facing list of valid keys.

### 5.2 Visual Host UI change

`ConfigurationLoader.ts` reads the two new columns into the `ChartDefinition` TypeScript type. `CardChrome.tsx` renders a new `Button` with `AddRegular` icon between the existing spaarkle and open icons in `iconSlots` (lines 144–167), conditionally rendered on `chartDefinition.createWizardEnabled === true`. Click handler resolves the wizard key, looks up the component in `WizardRegistry`, and opens it in a Fluent Dialog **seeded with the host record as the initial association** (§5.5). Legacy toolbar path in `VisualHostRoot.tsx` (lines 729–764) receives the same treatment.

### 5.3 Wizard registry

New file: `src/client/shared/Spaarke.UI.Components/src/components/WizardRegistry/wizardRegistry.ts`.

```ts
export type WizardComponent = React.LazyExoticComponent<React.ComponentType<WizardHostProps>>;

export const wizardRegistry: Record<string, WizardComponent> = {
  'event':          lazy(() => import('../CreateEventWizard/CreateEventWizard')),
  'invoice':        lazy(() => import('../CreateInvoiceWizard/CreateInvoiceWizard')),
  'kpi-assessment': lazy(() => import('../CreateKPIAssessmentWizard/CreateKPIAssessmentWizard')),
};

export function resolveWizard(key: string | null | undefined, fallbackEntity: string | null): WizardComponent | null { ... }
```

- Uses React `lazy()` so wizard bundles aren't loaded until first "+" click on a matching visual.
- Central registry — one file to touch to add a new wizard binding.
- Fallback: if `sprk_createwizardkey` is empty, use `sprk_entitylogicalname` normalized.
- Returns `null` for unknown keys — caller shows a toast rather than crashing.

`WizardHostProps` is the common injection contract every registered wizard accepts. Modeled on `IWorkAssignmentWizardDialogProps` (`WorkAssignmentWizardDialog.tsx:61`):

```ts
interface WizardHostProps {
  open: boolean;
  onClose: () => void;
  dataService: IDataService;
  authenticatedFetch: (input: RequestInfo, init?: RequestInit) => Promise<Response>;
  bffBaseUrl: string;
  navigationService?: INavigationService;
  resolveSpeContainerId?: (recordId: string) => Promise<string | null>;
  tenantId?: string;
  embedded?: boolean;
  // Launch-context seed (§5.5) — the host record the wizard was opened from:
  initialAssociation?: AssociationResult;   // { entityType, recordId, recordName }
  lockAssociation?: boolean;                 // true when launched from Visual Host → hide/lock step 1
}
```

### 5.4 The standard wizard template

Both new wizards (and the migrated Event wizard) follow the **standard Spaarke wizard template**, built on the generic `WizardShell` (`src/client/shared/Spaarke.UI.Components/src/components/Wizard/`). The canonical live reference is **`CreateWorkAssignmentWizard`** — it is the closest existing implementation *and* the ADR-024-compliant one.

**Visible steps (4)** — mapping to your 5-item spec:

| # | Your spec item | Step component | Notes |
|---|---|---|---|
| 1 | **Associate To** (record type + lookup) | `AssociateToStep` (shared, `components/AssociateToStep/`) | Pre-filled + hidden/locked when launched from Visual Host (§5.5). |
| 2 | **Add files** | `FileUploadZone` + `UploadedFileList` (`components/FileUpload/`) via an `AddFilesStep` | Collects `IUploadedFile[]`; upload happens at finish. **Omitted for KPI Assessment** (owner: KPI needs no documents). |
| 3 | **Run Prefill AI process** | *folded into step 4* via `useAiPrefill` (stubbed — §5.7) | Not a separate visible step; runs inside Enter Info as a spinner when enabled. The seam ships OFF. **N/A for KPI** (prefill is file-driven; KPI has no files). |
| 4 | **Enter Info** | entity-specific step (`CreateInvoiceStep`, `CreateKpiAssessmentStep`) | Invoice/Event host the `useAiPrefill` hook (currently inert); KPI does not. |
| 5 | **Next steps** | `NextStepsStep` card grid (`components/CreateRecordWizard/FollowOnSteps.tsx`) | Cards: Send Email, Add To Do, Assign Work (§5.9). |

The template supports omitting steps: **KPI Assessment runs Associate To → Enter Info → Next Steps** (no files, no prefill). Event and Invoice run the full set. `CreateRecordWizard` already makes the Add-files step conditional, so this is configuration, not a fork.

**Build approach**: wrap the config-driven **`CreateRecordWizard`** orchestrator (as `CreateEventWizard`/`CreateProjectWizard` do) rather than hand-rolling on `WizardShell`. `CreateRecordWizard` already delivers Associate To → Add files → Enter Info → Next Steps + dynamic follow-ons; each new wizard supplies only an `infoStep`, search callbacks, and an `onFinish` service call. This is the smallest-surface path and inherits the follow-on plumbing for free.

**Persistence** goes through the injected `IDataService.createRecord(logicalName, payload)` abstraction (backed by `Xrm.WebApi` in the PCF/Code-Page adapter) — not `Xrm.WebApi` directly. Each wizard gets a service (`invoiceService.ts`, `kpiAssessmentService.ts`) mirroring `workAssignmentService.ts`: nav-prop discovery → build payload → BU-defaults cascade (`EntityCreationService.applyUserBuDefaults`) → `applyResolverFields` (§5.6) → `createRecord` → file pipeline (§5.8) → warnings array (never throws for non-fatal side-effects).

### 5.5 Auto-association from the host record

When launched from a Visual Host visual, the created child must be regarding the **host record** (the record the visual is embedded on — e.g. the Matter). The dispatcher passes:

- `initialAssociation = { entityType: hostEntityLogicalName, recordId: hostRecordId, recordName: hostRecordName }` — read from `context.mode.contextInfo`.
- `lockAssociation = true` — the wizard **hides step 1** (Associate To) and treats the host record as fixed. (Per owner: either hiding or auto-setting is acceptable; we hide because from Visual Host the parent is always unambiguous. The step remains visible for other launch contexts where `lockAssociation` is false.)

`CreateRecordWizard`'s `IAssociateToStepConfig.initialAssociation` already supports launch-context pre-fill; we add the `lockAssociation` behavior to suppress/lock the step.

### 5.6 Polymorphic association per entity (ADR-024)

Association is written via the **shared `PolymorphicResolverService.applyResolverFields(...)`** (`src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts`), which is the ADR-024-mandated primitive. It writes the entity-specific lookup (`@odata.bind`) **and** all four resolver fields (`sprk_regardingrecordtype` → `sprk_recordtype_ref`, `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordurl`). The canonical reference call is `CreateTodoWizard`/`workAssignmentService.ts`.

Per-entity status and delta:

| Entity | Today | This project |
|---|---|---|
| **`sprk_event`** | Already fully polymorphic (11 lookups + resolver fields). **But `CreateEventWizard` bypasses the resolver** — it uses an ad-hoc matter/project-only map and skips the 4 resolver fields (`eventService.ts:315`). | **Migrate `eventService.createEvent` to `applyResolverFields`.** No schema change; brings Event into ADR-024 compliance and unblocks all host types. |
| **`sprk_invoice`** | **Already fully resolver-ready** (confirmed by owner): has `sprk_matter`, `sprk_project`, and all resolver fields (`sprk_regardingrecordid`/`number`/`name`/`type`/`url`) + vendor org + `sprk_recordtype_ref`. The `docs/data-model/sprk_invoice.md` file is stale; the ER model (lines 120–124) confirms. | **No schema delta.** Create via `applyResolverFields` (Matter + Project). |
| **`sprk_kpiassessment`** | **Now fully resolver-ready** — has `sprk_matter` + `sprk_project` lookups and the **4 resolver fields were created by owner (2026-07-05)**. | **No schema delta.** Create via `applyResolverFields` (Matter + Project). `sprk_recordtype_ref` rows for matter/project already exist (used by Todo). |

Schema deltas (KPI, and Invoice if needed) are authored via the `dataverse-create-schema` skill. These are Dataverse-only changes — **not** a BFF hot-path.

> **ADR-024 mutual-exclusion note**: at most one entity-specific lookup may be populated at a time. On create from Visual Host there is exactly one host record, so this is naturally satisfied.

### 5.7 AI prefill — stubbed seam (no BFF this release)

Per owner decision, the prefill capability is **wired but inert** this project:

- Each wizard's Enter Info step wires the shipped **`useAiPrefill`** hook (`src/client/shared/Spaarke.UI.Components/src/hooks/useAiPrefill.ts`), which POSTs uploaded files to `POST /api/workspace/{entity}/pre-fill` and applies structured field-value pairs.
- A `prefillEnabled` flag (default **false**) gates the hook. With it off, Enter Info renders normally with no spinner and **no BFF dependency**.
- The seam (hook wiring, `fieldExtractor`, `lookupResolvers`) is left in place so the follow-on BFF project only needs to: (1) add the BFF pre-fill endpoint per entity, (2) author a JPS **Action** (prompt + `output.fields[]` schema) + a `sprk_playbookconsumer` routing row (Linear AI Consumer path, single LLM call, constrained JSON), and (3) flip `prefillEnabled = true`.

**This is why the project's hot-path declaration remains BFF=N** (§9). Reference architecture for the follow-on: `docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md`; existing shipped example: `MatterPreFillService` + `POST /api/workspace/matters/pre-fill`.

### 5.8 File → document dual-bind (parent + child)

Uploaded files become `sprk_document` rows via the shared pipeline: `EntityCreationService.uploadFilesToSpe` (SPE OBO `PUT /api/obo/containers/{containerId}/files/{name}`) → `createDocumentRecords` (one `sprk_document` per file, client-side Web API) → `indexUploadedFiles` (RAG).

**Applies to Event and Invoice only** — KPI Assessment has no files step (owner decision), so no document handling for KPI.

`sprk_document` exposes **separate typed lookups** for each parent type (`sprk_matter`, `sprk_project`, `sprk_Event`, work-assignment lookup, `sprk_invoice`, …). A single document can therefore bind to **both** the host record and the new child record natively — **no intersect table, no schema change**. The only code change:

- **Extend `EntityCreationService.createDocumentRecords`** to accept an optional array of **additional binds** `{ entitySet, id, navProp }` and emit extra `@odata.bind` entries alongside the primary one (single seam at `EntityCreationService.ts:588`).
- In each wizard's `onFinish`, discover both nav-props on `sprk_document` (`findNavProp(navProps, childEntity)` and `findNavProp(navProps, hostEntity)`) and pass both. Result: one SPE file, one `sprk_document`, appearing in **both** the host record's and the child record's Documents subgrid.

Both child lookups already exist on `sprk_document`: `sprk_Event` (used today by `CreateEventWizard`) and `sprk_invoice` (ER model lines 81/195). Phase 0 confirms the host lookup (`sprk_matter`/`sprk_project`) presence, since `field-mapping-reference.md` under-documents them.

> **Note**: `sprk_document`'s "regarding" is only a text field with no `sprk_regardingrecordtype` discriminator — so documents use the **typed lookups**, not `applyResolverFields`. This is intentional and distinct from the child-record association in §5.6.

### 5.9 Next Steps follow-ons — shared `WizardFollowOns` module (consolidation)

**Current state (tech debt)**: the Next Steps grid + follow-on steps are **duplicated across four wizard families** — only `EmailStep/` is a genuine shared component:

| Location | Grid | Follow-on steps |
|---|---|---|
| `CreateRecordWizard/FollowOnSteps.tsx` | `NextStepsStep` | `steps/SendEmailStep`, `AssignWorkFollowOnStep`, `CreateEventFollowOnStep`, `DraftSummaryStep`, `AssignResourcesStep` |
| `CreateWorkAssignmentWizard/` | `NextStepsSelectionStep` | `AssignWorkStep`, `CreateFollowOnEventStep` |
| `CreateMatterWizard/` | `NextStepsStep` (own copy) | `SendEmailStep` (own copy) |
| `SummarizeFilesWizard/` | `SummaryNextStepsStep` | `SummarizeSendEmailStep` |

**This project consolidates all four into one shared, config-driven module** (owner decision: full migration).

New: `src/client/shared/Spaarke.UI.Components/src/components/WizardFollowOns/`
- `FollowOnGrid.tsx` — the reusable card grid. Takes `cards: FollowOnCardConfig[]`, current selection, and the shell's `addDynamicStep`/`removeDynamicStep` handle. Zero selection → step is an early finish. Domain-free.
- `steps/` — the reusable follow-on step components, each self-contained: `SendEmailFollowOnStep`, `AddTodoFollowOnStep` (**net-new**), `AssignWorkFollowOnStep`, `CreateEventFollowOnStep`, `DraftSummaryFollowOnStep`.
- `followOnTypes.ts` — `FollowOnCardConfig` (`{ id, label, icon, description, renderStep }`), plus `FOLLOW_ON_ID_MAP` / `LABEL_MAP` / `CANONICAL_ORDER`.
- `index.ts` — barrel; **exported from the shared-lib root `src/index.ts`**.

Each wizard passes the card set it offers. The three new wizards offer:

| Card | Follow-on step | Notes |
|---|---|---|
| **Send Email** | `SendEmailFollowOnStep` | Wraps the already-shared `EmailStep`; sends via `EntityCreationService.sendEmail` (existing endpoint — no new BFF surface). |
| **Add To Do** | `AddTodoFollowOnStep` (**net-new**) | Collects To Do fields, calls the ADR-024-compliant `todoService.createTodo`, seeding the To Do's regarding to the just-created child record. |
| **Assign Work** | `AssignWorkFollowOnStep` | Migrated from `CreateRecordWizard/steps`. |

**Migration (deletes duplicates)**: `CreateRecordWizard`, `CreateWorkAssignmentWizard`, `CreateMatterWizard`, and `SummarizeFilesWizard` all switch to `WizardFollowOns`; their local `NextStepsStep`/`NextStepsSelectionStep`/`SummaryNextStepsStep`/`SendEmailStep` copies are removed. Because this touches three existing working wizards, each migrated wizard gets a smoke/regression pass (see R7, §8 Phase D). The grid is config-driven so wizard-specific card sets (e.g. SummarizeFiles' summary-oriented cards) are expressed as `FollowOnCardConfig[]`, not forks.

Any follow-on record (To Do, assigned work, event) inherits the created child as its regarding via `applyResolverFields`.

### 5.10 Per-wizard field manifests

Each wizard's Enter Info step is driven by a **field manifest** — the authoritative list of fields the wizard collects and writes. The manifest is captured in the spec as a table and drives three build artifacts: the Enter Info step component, the service create-payload, and (later) the JPS prefill `output.fields[]` schema.

Manifest columns:

| Field (logical name) | Label | Control | Required | Default | Lookup target / filter | Prefill target? |
|---|---|---|---|---|---|---|

**Owner-provided manifests (2026-07-05)** — the field sets below are authoritative; Phase 0 only **validates** exact logical names, types, required flags, and lookup targets against live schema (it does not re-author them). Event uses the fields already collected by the existing `CreateEventStep` (no new manifest).

**KPI Assessment (`sprk_kpiassessment`)**

| Field (logical) | Control | Notes |
|---|---|---|
| `sprk_kpiname` | Text | |
| `sprk_performancearea` | Choice | option set from schema |
| `sprk_kpigradescore` | Choice | option set from schema |
| `sprk_assessmentcriteria` | Multiline text | |
| `sprk_assessmentnotes` | Multiline text | |

**Invoice (`sprk_invoice`)**

| Field (logical) | Control | Notes |
|---|---|---|
| `sprk_invoicenumber` | Text | Phase 0: confirm logical name |
| `sprk_name` | Text | |
| `sprk_description` | Text | |
| `sprk_vendororg` | Lookup → `sprk_organization` | relationship `sprk_sprk_organization_sprk_invoice_sprk_vendororg` |
| `sprk_invoicedate` | Date | **default = today** |

Phase 0 writes the validated manifests to `projects/visual-host-create-button-r1/notes/field-manifests/{entity}.md`, flagging any logical-name mismatch or additional schema-required field for owner sign-off.

## 6. Alternatives Considered

### 6.1 Config via PCF manifest properties instead of `sprk_chartdefinition`
Rejected — would require a PCF version bump + solution redeploy per visual. `sprk_chartdefinition` is already the maker-facing config surface.

### 6.2 Hardcoded switch on entity logical name
Rejected — every new wizard would require editing Visual Host source. Registry isolates Visual Host from wizard specifics.

### 6.3 Hand-roll each wizard directly on `WizardShell` (WorkAssignment style)
Rejected as the default. `CreateRecordWizard` already wires the canonical steps + follow-ons; wrapping it is less code than hand-rolling. We hand-roll only if a wizard needs a genuinely non-standard step order (none do). WorkAssignment remains the *reference for service-layer patterns* (`applyResolverFields`, BU cascade, file pipeline).

### 6.4 Make the AI prefill its own visible step
Rejected per owner — prefill stays inside Enter Info (the shipped pattern), and is stubbed OFF this release regardless.

### 6.5 Make KPI Assessment fully polymorphic (all 11 targets) now
Rejected per owner — KPI gets Matter + Project only. Broader participation is deferred until a non-Matter/Project KPI host actually appears.

### 6.6 Intersect table / polymorphic-regarding for dual document linking
Rejected — `sprk_document`'s existing typed lookups already support binding to multiple parents on one row. An intersect table would fight the subgrid UX and add schema for no benefit.

## 7. Reused Components

Per §11 Component Justification — this project heavily reuses existing surface:

| Component | Justification |
|---|---|
| `WizardShell` + `CreateRecordWizard` orchestrator | The generic wizard framework; every record-creation wizard sits on it. Extending. |
| `AssociateToStep` + `TODO_REGARDING_TARGETS` | Reusable Associate-To picker; Invoice already in the catalog. |
| `FileUploadZone` / `UploadedFileList` / `AddFilesStep` | Existing Add-files UI. |
| `useAiPrefill` hook | Shipped prefill hook (wired inert this release). |
| `EmailStep` (already shared) | Wrapped by the new `SendEmailFollowOnStep`. |
| `AssignWorkFollowOnStep` / `CreateEventFollowOnStep` / `DraftSummaryStep` logic | Migrated into `WizardFollowOns/steps`. |
| `PolymorphicResolverService.applyResolverFields` | ADR-024 primitive for regarding association. |
| `EntityCreationService` | SPE upload + `createDocumentRecords` + RAG index + `sendEmail` + BU defaults. |
| `todoService.createTodo` | ADR-024-compliant To Do creation, for the Add-To-Do follow-on. |
| `CardChrome.tsx` `iconSlots` div | Existing toolbar; adding a third icon is a ~5-line change. |
| `sprk_chartdefinition` table | Existing maker config surface; extending schema not creating a table. |

**New components (each justified)**:
- `WizardRegistry` module — REQUIRED so Visual Host stays agnostic to specific wizard implementations. Cost-of-doing-nothing: hardcoded dispatch scattered across CardChrome/VisualHostRoot, unmaintainable as wizards proliferate.
- `CreateInvoiceWizard` — REQUIRED; no existing invoice-creation surface (0 hits for `CreateInvoice*`). Cost-of-doing-nothing: users cannot create invoices from a visual.
- `CreateKPIAssessmentWizard` — same; no existing surface.
- `WizardFollowOns` shared module — REQUIRED to end the 4-way duplication of the Next Steps grid/follow-on steps (§5.9). Cost-of-doing-nothing: every new wizard forks yet another `NextStepsStep`/`SendEmailStep` copy; a bug fix must be applied 4+ times. Justified as consolidation, not net-new surface.
- `AddTodoFollowOnStep` — REQUIRED (inside `WizardFollowOns`); no existing follow-on wires To Do creation. Cost-of-doing-nothing: the third Next Step (per owner) has no implementation.
- **Modifications** (not new): `EntityCreationService.createDocumentRecords` gains optional multi-bind; `eventService.createEvent` migrates to `applyResolverFields`; `CreateRecordWizard` / `CreateMatterWizard` / `CreateWorkAssignmentWizard` / `SummarizeFilesWizard` migrate to `WizardFollowOns` and delete their local follow-on copies.

## 8. Phased Implementation

| Phase | Deliverable | Depends on |
|---|---|---|
| **0** | **Discovery**: live-schema read of `sprk_document` (confirm host + Event/Invoice child lookups) and **validate the owner-provided manifests** (§5.10) required flags/option-set values → `notes/field-manifests/`. KPI/Invoice resolver fields + chart-def columns already confirmed (no action). | — |
| **A** | Schema: **none** (all resolver fields + columns already exist). Visual Host: `ConfigurationLoader` + `CardChrome` + `VisualHostRoot` "+" button + `WizardRegistry`. Shared: `EntityCreationService` multi-bind; `eventService` → `applyResolverFields`; `lockAssociation` support. Wire Event. PCF version bump + deploy. | 0 |
| **D** | **`WizardFollowOns` shared module** (§5.9): build `FollowOnGrid` + reusable follow-on steps incl. net-new `AddTodoFollowOnStep`; **migrate `CreateRecordWizard`, `CreateMatterWizard`, `CreateWorkAssignmentWizard`, `SummarizeFilesWizard`** onto it; delete duplicate copies; regression-pass each migrated wizard. | A |
| **B** | `CreateInvoiceWizard` + `invoiceService` + registry entry (offers Send Email / Add To Do / Assign Work) + smoke test on an Invoice-bound Visual Host (created from a Matter). Uses refined field manifest. | A, D |
| **C** | `CreateKPIAssessmentWizard` + `kpiAssessmentService` + registry entry + smoke test on a KPI-bound Visual Host. Uses refined field manifest. | A, D |

Phase D (follow-on consolidation) runs after A and is a prerequisite for B/C so the new wizards consume the shared module rather than a soon-to-be-deleted copy. B and C are independent and can run in parallel after A + D. AI prefill (BFF endpoints + JPS actions) is **out of scope** — tracked as a follow-on project (§5.7).

## 9. Hot-Path Declaration (per §10 BFF Hygiene + FR-C04)

```xml
<hot-path-declaration>
  <bff>N</bff>
  <spaarkeai>N</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-CLAUDE-md>N</root-CLAUDE-md>
</hot-path-declaration>
```

**Rationale**: This project modifies (a) the Visual Host PCF, (b) shared UI components (`Spaarke.UI.Components`), and (c) Dataverse schema (`sprk_chartdefinition` columns + KPI/Invoice resolver fields). **No BFF endpoints, services, or DI** — the AI prefill seam is inert and its BFF work is a separate project (§5.7). Follow-on cards (Send Email) reuse existing BFF email endpoints, adding no new BFF surface. Confirm via `git diff --stat` at PR time; if any `src/server/api/Sprk.Bff.Api/**` file changes, this declaration must flip and §10 (Placement Justification + publish-size verification) applies.

## 10. ADR Compliance Snapshot

- **ADR-024 (Polymorphic Resolver Pattern)** — CENTRAL. All child associations MUST go through `applyResolverFields` (both entity-specific lookup + 4 resolver fields). This includes **migrating `CreateEventWizard` off its non-compliant ad-hoc map**. New KPI/Invoice resolver fields follow the dual-field strategy; `sprk_recordtype_ref` rows (not choice fields) back the discriminator.
- **ADR-022 (PCF Platform Libraries)** — Visual Host is a PCF; React + Fluent v9 per convention.
- **ADR-032 (Null-Object kill-switch)** — N/A (no BFF service registration this project).

The Event-wizard migration is a **correctness fix**, not a new deviation — the current design's "reuse as-is" would have shipped an ADR-024 violation. No anticipated conflicts requiring §6.5 escalation; if `adr-check` surfaces one, resolve per §6.5.

## 11. Risks + Open Questions

| # | Risk / Question | Mitigation / Follow-up |
|---|---|---|
| R1 | Schema-delta scope. Confirmed by owner: **KPI and Invoice are both fully resolver-ready** (KPI resolver fields created 2026-07-05); chart-def columns already exist; KPI has no files step. | **No Dataverse schema delta remains.** Phase 0 is validation-only (manifests + document lookups for Event/Invoice). |
| R2 | `CreateEventWizard` currently violates ADR-024 (matter/project-only, no resolver fields). Reusing it as-is would propagate the violation. | **Phase A migrates `eventService.createEvent` to `applyResolverFields`.** Regression-test that Events created from existing surfaces still link correctly. |
| R3 | `sprk_document` dual-bind assumes both host and child typed lookups exist on the document. | Phase 0 verifies the concrete columns (`sprk_matter`, `sprk_Event`, `sprk_invoice`, KPI lookup) exist; `field-mapping-reference.md` under-documents them, so verify against live schema, not docs. |
| R4 | Maker enables `sprk_createwizardenabled = Yes` but key doesn't resolve. | Toast on unknown key: "No create wizard is registered for `{key}`." |
| R5 | AI prefill seam ships inert; a later BFF project must not break the wizard when `prefillEnabled` flips. | Keep the hook contract stable; the follow-on only adds endpoints + JPS actions + flips the flag. Document the seam in each wizard's Enter Info step. |
| R6 | Multiple Visual Host instances on one page — "+" on visual A while B's wizard is open. | Fluent Dialog is modal; second click queues. Standard behavior. |
| R7 | **Follow-on consolidation (Phase D) touches 3 existing working wizards** (Matter, WorkAssignment, SummarizeFiles). Migration could regress their Next Steps / email / assign-work flows. | Config-driven grid keeps each wizard's card set intact; each migrated wizard gets a smoke/regression pass before its duplicate is deleted. Migrate + delete per-wizard (not big-bang) so a regression is isolatable. |
| Q1 | KPI reuses existing `sprk_matter` lookup vs. adding `sprk_regardingmatter` for strict ADR-024 naming. | Recommend reuse — `applyResolverFields` discovers nav-props by referenced entity, so the existing `sprk_matter` column works; only add `sprk_regardingproject` + resolver fields. Confirm at Phase 0. |
| Q2 | Field manifests: owner must refine the Phase 0 drafts before B/C build. | Drafts land in `notes/field-manifests/`; B/C are gated on owner sign-off of the corresponding manifest. |

## 12. Success Criteria

1. A maker can enable the "+" button on a visual by toggling `sprk_createwizardenabled = Yes`, no PCF redeploy.
2. Clicking "+" on an Event calendar visual (on a Matter) opens `CreateEventWizard`, the Associate-To step is hidden, and the created Event is regarding the host Matter **with all 4 resolver fields populated** (ADR-024).
3. Clicking "+" on an Invoice-bound visual opens `CreateInvoiceWizard`; submitting creates a valid `sprk_invoice` regarding the host record via `applyResolverFields`.
4. Clicking "+" on a KPI-bound visual opens `CreateKPIAssessmentWizard`; submitting creates a valid `sprk_kpiassessment` regarding the host Matter/Project.
5. Files uploaded in the **Event and Invoice** wizards produce a single `sprk_document` bound to **both** the host record and the created child (visible in both Documents subgrids). KPI Assessment has no files step.
6. The Next Steps step offers Send Email, Add To Do, Assign Work; selecting each adds its follow-on step and creates the follow-on record regarding the child.
6a. A single shared `WizardFollowOns` module backs the Next Steps for all wizard families; the duplicate `NextStepsStep`/`SendEmailStep` copies in CreateRecordWizard, CreateMatterWizard, CreateWorkAssignmentWizard, and SummarizeFilesWizard are deleted, and those wizards show no regression.
7. The AI prefill seam is present but inert (`prefillEnabled = false`); no BFF changes in the diff (`git diff --stat` shows no `Sprk.Bff.Api` files).
8. No regression in spaarkle or open button behavior; no regression in existing Event creation surfaces after the resolver migration.
9. Visual Host bundle delta < 5 KB gzipped (registry only; wizards code-split).

---

## 13. References

- Visual Host PCF: [src/client/pcf/VisualHost/](../../src/client/pcf/VisualHost/)
- CardChrome (button host): [src/client/pcf/VisualHost/control/components/CardChrome.tsx](../../src/client/pcf/VisualHost/control/components/CardChrome.tsx)
- ConfigurationLoader: [src/client/pcf/VisualHost/control/services/ConfigurationLoader.ts](../../src/client/pcf/VisualHost/control/services/ConfigurationLoader.ts)
- Generic wizard shell: [src/client/shared/Spaarke.UI.Components/src/components/Wizard/](../../src/client/shared/Spaarke.UI.Components/src/components/Wizard/)
- CreateRecordWizard orchestrator: [src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/](../../src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/)
- Reference 5-step wizard (WorkAssignment): [src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/](../../src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/)
- CreateEventWizard (to migrate): [src/client/shared/Spaarke.UI.Components/src/components/CreateEventWizard/](../../src/client/shared/Spaarke.UI.Components/src/components/CreateEventWizard/)
- AssociateToStep + regarding catalog: [src/client/shared/Spaarke.UI.Components/src/components/AssociateToStep/](../../src/client/shared/Spaarke.UI.Components/src/components/AssociateToStep/)
- Polymorphic resolver: [src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts](../../src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts)
- EntityCreationService (files/docs/email/BU): [src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts](../../src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts)
- AI prefill hook: [src/client/shared/Spaarke.UI.Components/src/hooks/useAiPrefill.ts](../../src/client/shared/Spaarke.UI.Components/src/hooks/useAiPrefill.ts)
- Prefill reference (BFF, follow-on): [src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs](../../src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs) · [docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md](../../docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md)
- ADR-024 (polymorphic resolver): [.claude/adr/ADR-024-polymorphic-resolver-pattern.md](../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md)
- To Do creation (follow-on): [src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/](../../src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/)
- Component Justification rule: root [CLAUDE.md §11](../../CLAUDE.md)
- Hot-Path Declaration rule: root [CLAUDE.md §10](../../CLAUDE.md)
