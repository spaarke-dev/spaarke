# Visual Host "+" Create Button — Implementation Plan

> **Source**: `spec.md` · **Design**: `design.md` (rev 2) · **Created**: 2026-07-08
> **Status**: Ready for execution (tasks generated)

## 1. Executive Summary

**Purpose**: One-click creation of a related record (Event / Invoice / KPI Assessment) from a Visual Host visual, auto-associated to the host record via the ADR-024 polymorphic resolver, using the standard Spaarke wizard template.

**Scope**: Visual Host PCF toolbar + `Spaarke.UI.Components` shared library. No BFF, no SpaarkeAi, no CI-workflow changes. Dataverse schema: none confirmed (contingent only on a Phase-0 `sprk_regardingrecordnumber` check on Event/KPI).

**Estimated effort**: ~10–14 focused days across 6 phases; Phases B and C parallelizable after A + D.

## 2. Architecture Context

**Constraints**: ADR-024 (Polymorphic Resolver — CENTRAL, amended by #549), ADR-022/021 (PCF React + Fluent v9), ADR-007/028 (SPE upload + auth), ADR-012 (context-agnostic shared components), ADR-011 (dataset PCF), ADR-038 (integration-heavy testing). CLAUDE.md §11 (reuse-first), BFF=N.

**Tech**: TypeScript, React 18, Fluent v9, PCF (virtual control), `Xrm.WebApi` via `IDataService`, SharePoint Embedded.

### Discovered Resources

- **Canonical reference (copy this)**: `CreateWorkAssignmentWizard/workAssignmentService.ts` — the ADR-024-compliant service template (nav-prop discovery → payload → BU defaults → `applyResolverFields` → createRecord → file pipeline → warnings).
- **Resolver (post-#549)**: `services/PolymorphicResolverService.ts` — `applyResolverFields` (now returns `IApplyResolverFieldsResult`), `findNavProp`, `resolveRecordNumberFieldName` (5th field). New `components/PolymorphicPicker/` (optional; `AssociateToStep` stays the wizard step).
- **Building blocks**: `components/Wizard/` (WizardShell), `components/CreateRecordWizard/` (orchestrator + `FollowOnSteps`), `components/AssociateToStep/` (+ `TODO_REGARDING_TARGETS`), `components/FileUpload/`, `hooks/useAiPrefill.ts`, `services/EntityCreationService.ts`, `components/CreateTodoWizard/todoService.ts`.
- **Migration targets (Phase D)**: `CreateRecordWizard`, `CreateMatterWizard`, `CreateWorkAssignmentWizard`, `SummarizeFilesWizard` (each has a duplicate NextSteps/SendEmail copy).
- **ADRs**: ADR-024, ADR-022, ADR-021, ADR-007, ADR-028, ADR-012, ADR-011, ADR-038.
- **Skills**: `dataverse-create-schema` (KPI resolver-field verify), `pcf-deploy`, `fluent-v9-component`, `code-review`, `adr-check`, `ui-test`.
- **Schema (verified)**: `sprk_chartdefinition` cols exist; `sprk_kpiassessment` has matter+project lookups + resolver fields (owner-created); `sprk_invoice` resolver-ready; `sprk_document` typed lookups (matter/project/Event/invoice); `sprk_recordtype_ref` matter/project rows present.
- **Patterns**: `.claude/patterns/dataverse/polymorphic-resolver.md`, `.claude/patterns/ui/record-modal-selection.md`. Standards: `DATA-ACCESS-DECISION-CRITERIA.md`, `MODAL-DECISION-CRITERIA.md`.

## 3. Implementation Approach

**Critical path**: Phase 0 (discovery) → Phase A (button + registry + Event migration) → Phase D (WizardFollowOns consolidation) → Phases B & C (new wizards, parallel) → Phase E (wrap-up). Phase D precedes B/C so the new wizards consume the shared module, not a soon-to-be-deleted copy.

## 4. Work Breakdown Structure (WBS)

### Phase 0 — Discovery & Schema Verification
- Live-schema read: `sprk_document` (host + Event/Invoice child lookups), `sprk_regardingrecordnumber` on Event/KPI, KPI/Invoice option-sets.
- Validate owner-provided field manifests → `notes/field-manifests/{event,invoice,kpi}.md`.
- Re-read post-#549 `applyResolverFields` signature + `PolymorphicPicker` contract + amended ADR-024.
- **Model tier**: sonnet (read-only discovery). **Dependencies**: none.

### Phase A — Visual Host "+" Button, Registry, Event Migration
- `ConfigurationLoader` + `ChartDefinition` read the two columns; `CardChrome` + `VisualHostRoot` "+" button (gated), Fluent Dialog host, `initialAssociation` + `lockAssociation`.
- `WizardRegistry` + `WizardHostProps` + unknown-key toast.
- `EntityCreationService.createDocumentRecords` multi-bind.
- **Migrate `eventService.createEvent` → `applyResolverFields`** (ADR-024 fix); wire `event` key. KPI resolver-field schema verify (dataverse-create-schema, only if Phase 0 flags absent).
- PCF version bump + deploy; smoke test Event-from-Matter.
- **Model tier**: mixed — Event migration = **opus** (ADR-compliance); button/registry/config = sonnet. **Dependencies**: Phase 0.

### Phase D — Shared `WizardFollowOns` Module (Consolidation)
- Build `components/WizardFollowOns/` (`FollowOnGrid` + reusable steps incl. net-new `AddTodoFollowOnStep`); export from lib root.
- Migrate `CreateRecordWizard`, `CreateMatterWizard`, `CreateWorkAssignmentWizard`, `SummarizeFilesWizard` onto it; delete duplicates; per-wizard regression pass.
- **Model tier**: **opus** (cross-cutting, 4-family blast radius). **Dependencies**: Phase A.

### Phase B — CreateInvoiceWizard
- `CreateInvoiceWizard` + `invoiceService` (Associate To → Add Files → Enter Info → Next Steps); manifest fields; registry entry; dual-bind; cards Send Email/Add To Do/Assign Work; smoke test.
- **Model tier**: sonnet (single-component, canonical reference). **Dependencies**: Phase A + D.

### Phase C — CreateKPIAssessmentWizard
- `CreateKPIAssessmentWizard` + `kpiAssessmentService` (Associate To → Enter Info → Next Steps, no files); manifest fields; registry entry; smoke test.
- **Model tier**: sonnet. **Dependencies**: Phase A + D.

### Phase E — Wrap-up
- `/test-diet`; maker-facing valid-keys note; README → Complete; `lessons-learned.md`; PR with hot-path declaration + `git diff --stat` (no `Sprk.Bff.Api`).
- **Model tier**: sonnet. **Dependencies**: B + C.

## 5. Dependencies

External: `sprk_recordtype_ref` matter/project rows (present); SPE container resolution per host record.
Internal: PR #549 (✅ merged — resolver API + picker); PR #525 (✅ merged — VisualHost files).

## 6. Testing Strategy

Unit: services (`invoiceService`, `kpiAssessmentService`, `eventService` migration) — nav-prop discovery, payload, `applyResolverFields`; `WizardFollowOns` grid selection; `createDocumentRecords` multi-bind. Per ADR-038 (integration-heavy; no `Mock<HttpMessageHandler>`/DI-registration/ctor-null tests). Regression: Matter/WorkAssignment/SummarizeFiles + existing Event surfaces. UI smoke: "+" on Event/Invoice/KPI visuals (create → inspect record + resolver fields + both Documents subgrids); Fluent v9 dark-mode (ADR-021).

## 7. Acceptance Criteria

Per README graduation criteria + spec §Success Criteria (SC-1…SC-10). Verified by maker toggle, create-and-inspect, both-subgrid check, grep for deleted duplicates, `git diff --stat` (no BFF).

## 8. Risk Register

| Risk | Impact | Likelihood | Mitigation |
|------|--------|-----------|------------|
| Follow-on migration regresses Matter/WA/SummarizeFiles | Med | Med | Config-driven grid; per-wizard regression; migrate+delete per wizard (opus tier) |
| Event resolver migration breaks existing links | Med | Low | Regression-test existing Event surfaces (opus tier) |
| `sprk_regardingrecordnumber` absent on Event/KPI | Low | Med | Phase 0 verifies; additive column if needed |
| Bundle-size regression in VisualHost | Low | Low | Registry-only delta; wizards code-split; measure < 5 KB |

## 9. Next Steps

1. Review generated tasks + TASK-INDEX (model-tier + parallel groups).
2. Execute Phase 0 first (gates B/C manifests); switch session to **Sonnet 5** for execution — opus-tagged tasks (Event migration, WizardFollowOns) auto-escalate.
