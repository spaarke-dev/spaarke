# Implementation Plan — Set-Regarding and Field-Mapping Resolver R2

> **Source**: [spec.md](spec.md) · [design.md](design.md)
> **Status**: Ready for task decomposition

## 1. Executive Summary

**Purpose**: Restore automatic field inheritance at child-record creation time via a context-agnostic client engine that reads existing field-mapping profiles and applies all four mapping types, wired into all 7 wizard services. Close the BFF contract + rule schema so the capability never reopens.

**Scope**: Client engine rewrite + additive BFF DTO extension + one additive Dataverse column + 7-service wiring + config seed + docs. No new BFF endpoint/service/package; no Dataverse plugins; no new PCF.

**Estimated effort**: 10-14 tasks across 6 phases. Long pole = the engine (4 mapping types + lookup binding). Most tasks are well-specified brownfield edits against verified reference implementations.

## 2. Architecture Context

**Key constraints**:
- **ADR-012** — engine MUST be context-agnostic (no `ComponentFramework.WebApi`).
- **ADR-024** — polymorphic resolver / `sprk_recordtype_ref` authoritative; matter-as-parent via polymorphic regarding (basis for same-entity support).
- **ADR-001/008/010/019** — additive BFF change stays within Minimal API / DI-minimalism / ProblemDetails patterns.
- **§10 BFF Hygiene** — additive DTO change requires `bff-extensions.md` checklist + placement justification + publish-size verification.
- **Owner constraint (absolute)** — no Dataverse plugins / form scripts; client-only mechanism.

**Tech stack / integration points**:
- `@spaarke/ui-components` — engine home (`services/FieldMappingService.ts`) + 7 wizard services.
- Reuses live BFF `GET /api/v1/field-mappings/profiles/{source}/{target}` (single call, rules `$expand`-ed).
- `WizardHostProps`/`WizardRegistry` inject `dataService`/`authenticatedFetch`/`bffBaseUrl`; engine hooks adjacent to `applyResolverFields` (`PolymorphicResolverService`).
- Dataverse: `sprk_fieldmappingrule` (+ new `sprk_expression`), `sprk_fieldmappingprofile`; spaarkedev1 seed.
- BFF: `FieldMappingRuleDto`, `FieldMappingEndpoints`, `FieldMappingRuleEntity`, `DataverseWebApiService`.

## 3. Implementation Approach

**Critical path**: Phase 0 (BFF contract + schema) unblocks Phase 1 (engine, needs the extended DTO). Phase 1 unblocks Phase 2 (wiring). Phase 3 (seed) needs Phase 0 schema + can parallel Phase 2. Phases 4-5 (docs, wrap-up) last.

**Sequence**: 0 → 1 → 2, with 3 after 0 (parallel to 2), then 4 → 5.

## 4. Work Breakdown Structure

### Phase 0 — BFF Contract + Schema Extension (foundation)
*Objective: make the endpoint return everything the four engines need; add the Concat/Template config column. Additive only.*
- **D0.1** Add `sprk_expression` (`NVARCHAR(2000)`, nullable) to `sprk_fieldmappingrule` via `dataverse-create-schema`. `sprk_defaultvalue` unchanged.
- **D0.2** Extend `FieldMappingRuleEntity` (Models.cs) + `DataverseWebApiService` rule `$select` + `MapToFieldMappingRuleEntity` to read `sprk_mapping_type` + `sprk_expression`.
- **D0.3** Extend `FieldMappingRuleDto` + `MapRuleEntityToDto` to surface `mappingType`, `defaultValue`, `expression`, `isRequired`, `compatibilityMode`.
- **D0.4** BFF tests (rule DTO projection) + publish-size measurement + CVE scan + `UpdateRelatedButton`/`push` regression smoke (additive fields don't break existing deserialization).

*Tags: bff-api, dataverse, testing. Gate: FULL (BFF + tests).*

### Phase 1 — Client Engine (core deliverable)
*Objective: rewrite the stub into a working, context-agnostic four-type engine.*
- **D1.1** Rewrite `FieldMappingService.ts` shell — context-agnostic (`IDataService`/`authenticatedFetch`), single BFF call, graceful 404 → `{ profileFound: false }`, `{ profileFound, fieldsMapped, warnings }` result, never throws. Align `FieldMappingTypes.ts` to the BFF DTO + four engines.
- **D1.2** Consolidate nav-prop discovery — move the duplicated per-service `_discoverNavProps` into a shared util alongside `PolymorphicResolverService.findNavProp`; engine + services consume it.
- **D1.3** Copy engine — scalar + **lookup `@odata.bind`** (read source `_value` + `@…lookuplogicalname` annotation → pluralize referent → target nav-prop).
- **D1.4** Default/Constant + Concat/Template engines — literal from `sprk_defaultvalue`; `{sprk_field}` placeholder resolver over `sprk_expression` (shared by Concat + Template).
- **D1.5** Same-entity support — ensure no `source === target` guard; field self-mapping applies.
- **D1.6** Engine unit tests — all four types, lookup binding, same-entity (positive self-map + negative guard), graceful degradation (no profile / missing field / unresolved placeholder → warning, no throw).

*Tags: pcf (shared-lib), frontend, testing. Gate: FULL. Depends: Phase 0.*

### Phase 2 — Wire All 7 Wizard Services
*Objective: call the engine adjacent to `applyResolverFields`, before `createRecord`, in each service.*
- **D2.1** Wire `eventService`, `matterService`, `projectService`, `todoService`, `workAssignmentService`.
- **D2.2** Wire `invoiceService`, `reportCardService`.
- **D2.3** Per-service lookup-binding verification (payload shapes differ); graceful no-op when no profile.

*Tags: frontend, integration. Gate: FULL. Depends: Phase 1.*

### Phase 3 — Configuration Seed (config data, per-pair)
*Objective: seed the attorney matrix so re-test succeeds out-of-the-box.*
- **D3.1** Delete orphaned empty `sprk_fieldmappingrule`; deactivate/repurpose the two stale "SRFR-084 UAT" profiles.
- **D3.2** Seed Matter→Event/Invoice/Report Card profiles + Copy rules against **`describe`-verified** target names (Event identical 8; Invoice renamed + no law-firm; Report Card lawfirm1 renamed). Resolve `sprk_assignedto1/2` question at authoring.

*Tags: dataverse, data-seed. Gate: STANDARD. Depends: Phase 0 (schema).*

### Phase 4 — Documentation
*Objective: make the framework + extensibility discoverable.*
- **D4.1** Field Mapping Framework architecture doc (`docs-architecture`) — tables, BFF contract, engine, four types, `sprk_expression` extensibility, creation-vs-update boundary, same-entity note.
- **D4.2** Admin authoring guide (`docs-guide`) — native MDA form, mapping types, `sprk_expression` templates, attorney-seed worked example. Update root `CLAUDE.md` §17 pointer.

*Tags: documentation. Gate: MINIMAL. Depends: Phases 1-3 (describe the shipped behavior).*

### Phase 5 — Wrap-up
- **D5.1** Integration/UI verification of the end-to-end inheritance (one live record per mapping type + same-entity); `/test-diet`; README → Complete; lessons-learned; project archive.

*Tags: wrap-up, testing. Depends: all.*

## 5. Dependencies

**External**: Live BFF `/api/v1/field-mappings/*` (deployed); `dataverse-create-schema` access to spaarkedev1. **Invoice/Report Card wizards — MERGED to master 2026-07-09 (dependency satisfied).**

**Internal**: `PolymorphicResolverService` (nav-prop + `applyResolverFields`), `WizardHostProps`/`WizardRegistry`, `EntityCreationService`.

## 6. Testing Strategy
- **Unit**: engine (four types, lookup, same-entity, graceful degradation); BFF rule-DTO projection.
- **Integration/UI**: one live wizard-created record per mapping type verified in Dataverse; same-entity matter→matter; no-profile no-op; `push` regression.
- Per ADR-038: integration-heavy; new tests classified MAINTAIN/SCAFFOLDING at `/test-diet` wrap-up.

## 7. Acceptance Criteria
See [README.md Graduation Criteria](README.md) + [spec.md Success Criteria](spec.md). Each maps to a phase deliverable above.

## 8. Risk Register
| Risk | Mitigation |
|---|---|
| Lookup `@odata.bind` complexity underestimated | D1.3 isolated; reuse proven `applyResolverFields`/`findNavProp` binding pattern |
| BFF DTO extension breaks `push` deserialization | D0.4 explicit regression smoke; fields are additive/nullable |
| Per-target field-name divergence causes silent no-map | D3.2 authored against `describe` output; per-pair, not assumed |
| BFF=Y publish-size creep | D0.4 measures delta (expected ≈0, DTO-only); ≤60 MB ceiling |
| Same-entity guard silently foreclosed | D1.5 negative test asserts absence of `source === target` guard |

## 9. Next Steps
Run `/task-create projects/set-regarding-and-field-mapping-resolver-r2` to decompose this WBS into numbered POML task files, then execute via `task-execute`.
