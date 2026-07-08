# Visual Host "+" Create Button — Implementation Plan

> **Source**: `spec.md` · **Design**: `design.md` (rev 2) · **Created**: 2026-07-08
> **Status**: Ready for task decomposition

## 1. Executive Summary

**Purpose**: Give users a one-click path to create a related record (Event / Invoice / KPI Assessment) directly from a Visual Host visual, auto-associated to the host record, using the standard Spaarke wizard template and the ADR-024 polymorphic resolver.

**Scope**: Visual Host PCF toolbar + `Spaarke.UI.Components` shared library (registry, two new wizards, follow-on consolidation, resolver migration, file dual-bind). No BFF, no SpaarkeAi, no CI-workflow changes. Dataverse schema: none confirmed (contingent Phase-0 `sprk_regardingrecordnumber` check on Event/KPI only).

**Estimated effort**: ~10–14 focused days across 6 phases; Phases B and C parallelizable after A + D.

## 2. Architecture Context

**Key constraints**
- **ADR-024 (Polymorphic Resolver Pattern)** — CENTRAL. All child association via `applyResolverFields` (entity-specific lookup + all resolver fields, now 5 incl. `sprk_regardingrecordnumber` per #549). ADR-024 was amended by #549 — re-read before implementation.
- **ADR-022 / ADR-021** — PCF React + Fluent v9 conventions.
- **ADR-007** — SPE file upload (`/api/obo/containers/...`).
- **ADR-028** — `authenticatedFetch` for any BFF call (email/SPE side-effects).
- **ADR-012** — Shared components context-agnostic.
- **CLAUDE.md §11** — Component justification (reuse-first); §10 BFF hygiene (N/A here — BFF=N).

**Tech stack**: TypeScript, React 18, Fluent UI v9, PCF (virtual control), `Xrm.WebApi` via `IDataService`, SharePoint Embedded.

**Integration points**: `sprk_chartdefinition` config; `PolymorphicResolverService.applyResolverFields` (post-#549); `EntityCreationService` (SPE + documents + email + BU defaults); `WizardShell` / `CreateRecordWizard`; `AssociateToStep`; `useAiPrefill` (inert); `todoService.createTodo` (Add-To-Do follow-on).

**Canonical reference implementation**: `CreateWorkAssignmentWizard` (`workAssignmentService.ts`) — the ADR-024-compliant service template (nav-prop discovery → payload → BU defaults → `applyResolverFields` → createRecord → file pipeline → warnings).

## 3. Implementation Approach

**Critical path**: Phase 0 (discovery) → Phase A (button + registry + Event migration) → Phase D (WizardFollowOns consolidation) → Phases B & C (new wizards, parallel) → Phase E (wrap-up).

Phase D is a prerequisite for B/C so the new wizards consume the shared follow-on module rather than a soon-to-be-deleted copy.

## 4. Work Breakdown Structure (WBS)

### Phase 0 — Discovery & Schema Verification
- **Objectives**: Ground the build in live schema; produce validated field manifests.
- **Deliverables**:
  - Live-schema read: `sprk_document` (host + Event/Invoice child lookups), `sprk_event`/`sprk_kpiassessment`/`sprk_invoice` (resolver fields incl. `sprk_regardingrecordnumber`), option-set values for KPI choices.
  - Validated field manifests → `notes/field-manifests/{event,invoice,kpi}.md` (owner-provided lists confirmed against schema; flag any missing required field / logical-name mismatch).
  - Re-read post-#549 `applyResolverFields` signature + `PolymorphicPicker` contract + amended ADR-024.
- **Outputs**: manifests, schema-delta decision (expected: none, or one additive `sprk_regardingrecordnumber` column on Event/KPI).
- **Dependencies**: none.

### Phase A — Visual Host "+" Button, Registry, Event Migration
- **Objectives**: Ship the button + dispatch + Event wizard end-to-end.
- **Deliverables**:
  - `ConfigurationLoader.ts` + `ChartDefinition` type read `sprk_createwizardenabled` / `sprk_createwizardkey`.
  - `CardChrome.tsx` + `VisualHostRoot.tsx` "+" button (gated), Fluent Dialog host, seeded `initialAssociation` + `lockAssociation`.
  - `WizardRegistry` module + `WizardHostProps` contract + unknown-key toast.
  - `EntityCreationService.createDocumentRecords` multi-bind (additional binds).
  - **Migrate `eventService.createEvent` → `applyResolverFields`** (ADR-024 fix); wire `event` key.
  - `lockAssociation` support in `CreateRecordWizard`/`AssociateToStep`.
  - PCF version bump + deploy; smoke test Event-from-Matter.
- **Dependencies**: Phase 0.

### Phase D — Shared `WizardFollowOns` Module (Consolidation)
- **Objectives**: One config-driven follow-on module; delete 4-way duplication.
- **Deliverables**:
  - `components/WizardFollowOns/`: `FollowOnGrid` + `followOnTypes` + reusable steps (`SendEmailFollowOnStep`, net-new `AddTodoFollowOnStep`, `AssignWorkFollowOnStep`, `CreateEventFollowOnStep`, `DraftSummaryFollowOnStep`); export from lib root.
  - Migrate `CreateRecordWizard`, `CreateMatterWizard`, `CreateWorkAssignmentWizard`, `SummarizeFilesWizard` onto it; delete local `NextStepsStep`/`NextStepsSelectionStep`/`SummaryNextStepsStep`/`SendEmailStep` copies.
  - Per-wizard smoke/regression pass.
- **Dependencies**: Phase A.

### Phase B — CreateInvoiceWizard
- **Objectives**: New invoice wizard on the standard template.
- **Deliverables**: `CreateInvoiceWizard` + `invoiceService` (Associate To → Add Files → Enter Info → Next Steps); manifest fields (`sprk_invoicenumber`, `sprk_name`, `sprk_description`, `sprk_vendororg`, `sprk_invoicedate` default today); registry entry; dual-bind; cards Send Email / Add To Do / Assign Work; smoke test Invoice-from-Matter.
- **Dependencies**: Phase A + D.

### Phase C — CreateKPIAssessmentWizard
- **Objectives**: New KPI wizard (no files step).
- **Deliverables**: `CreateKPIAssessmentWizard` + `kpiAssessmentService` (Associate To → Enter Info → Next Steps); manifest fields (`sprk_kpiname`, `sprk_performancearea`, `sprk_kpigradescore`, `sprk_assessmentcriteria`, `sprk_assessmentnotes`); registry entry; smoke test KPI-from-Matter/Project.
- **Dependencies**: Phase A + D.

### Phase E — Wrap-up
- **Objectives**: Close out cleanly.
- **Deliverables**: `/test-diet` reconciliation; maker-facing valid-keys note; README status → Complete; `lessons-learned.md`; PR description with hot-path declaration + `git diff --stat` (confirm no `Sprk.Bff.Api`).
- **Dependencies**: B + C.

## 5. Dependencies

**External**: `sprk_recordtype_ref` catalog rows for matter/project (present); SPE container resolution per host record.
**Internal**: PR #549 (✅ merged — post-#549 resolver API + `PolymorphicPicker`); PR #525 (✅ merged — VisualHost files).

## 6. Testing Strategy

- **Unit**: services (`invoiceService`, `kpiAssessmentService`, `eventService` migration) — nav-prop discovery, payload build, `applyResolverFields` invocation; `WizardFollowOns` grid selection → dynamic step; `createDocumentRecords` multi-bind. Follow ADR-038 (integration-heavy; no `Mock<HttpMessageHandler>` / DI-registration / ctor-null tests).
- **Regression**: the three migrated wizards (Matter, WorkAssignment, SummarizeFiles) + existing Event creation surfaces post-resolver-migration.
- **UI/E2E smoke**: "+" on Event/Invoice/KPI visuals (create → inspect record + resolver fields + Documents subgrids). Fluent v9 dark-mode check (ADR-021).

## 7. Acceptance Criteria

Per README graduation criteria + spec §Success Criteria (SC-1…SC-10). Each verified by: maker toggle, create-and-inspect in Dataverse, both-subgrid document check, grep for deleted duplicates, and `git diff --stat` (no BFF).

## 8. Risk Register

| Risk | Impact | Likelihood | Mitigation |
|------|--------|-----------|------------|
| Follow-on migration regresses Matter/WA/SummarizeFiles | Med | Med | Config-driven grid; per-wizard regression pass; migrate+delete per wizard, not big-bang |
| Event resolver migration breaks existing links | Med | Low | Regression-test existing Event surfaces; keep entity-specific lookup semantics |
| `sprk_regardingrecordnumber` absent on Event/KPI | Low | Med | Phase 0 verifies; additive column if needed |
| Bundle size regression in VisualHost | Low | Low | Registry-only delta; wizards code-split via `lazy()`; measure < 5 KB |

## 9. Next Steps

1. Run `/task-create projects/visual-host-create-button-r1` to decompose this plan into numbered POML tasks with TASK-INDEX + parallel groups.
2. Execute Phase 0 first (discovery gates B/C manifests).
3. Owner sign-off on validated field manifests before B/C build.
